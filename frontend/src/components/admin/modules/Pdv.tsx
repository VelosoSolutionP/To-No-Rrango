"use client";

import { type FormEvent, useMemo, useState } from "react";
import { formatBRL, formatDateTime, type AdminHttp } from "@/lib/admin-api";
import type { AdminOrder, FulfillmentType, MenuItem, PaymentMethod, PublicMenu } from "@/lib/types";
import { Empty, ErrorMessage, ModuleHeading, SubTabs, text, toCents, useAction, useLoad } from "./shared";

interface CashMovement {
  id: string;
  type: "Supply" | "Withdrawal" | "Sale" | "SaleReversal";
  method: PaymentMethod;
  amountInCents: number;
  reason: string;
  operatorName: string;
  orderId: string | null;
  createdAt: string;
}

interface CashSession {
  id: string;
  status: "Open" | "Closed";
  openedBy: string;
  openedAt: string;
  openingAmountInCents: number;
  expectedCashInCents: number;
  suppliesInCents: number;
  withdrawalsInCents: number;
  totals: Array<{ method: PaymentMethod; amountInCents: number; sales: number }>;
  closedBy: string | null;
  closedAt: string | null;
  countedCashInCents: number | null;
  differenceInCents: number | null;
  closingNotes: string | null;
  movements: CashMovement[];
}

interface Overview {
  current: CashSession | null;
  history: CashSession[];
}

interface CartLine {
  key: string;
  item: MenuItem;
  quantity: number;
  optionIds: string[];
  optionNames: string[];
  unitInCents: number;
}

const methodLabels: Record<PaymentMethod, string> = { Cash: "Dinheiro", CardOnDelivery: "Cartão", Pix: "Pix", Online: "Online" };
const movementLabels: Record<CashMovement["type"], string> = { Supply: "Suprimento", Withdrawal: "Sangria", Sale: "Venda", SaleReversal: "Estorno" };
const counterMethods: PaymentMethod[] = ["Cash", "CardOnDelivery", "Pix"];

function plain(value: string) {
  return value.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
}

export function Pdv({
  http,
  establishmentId,
  menu,
  orders,
  canManage,
  onChanged,
}: {
  http: AdminHttp;
  establishmentId: string;
  menu: PublicMenu | null;
  orders: AdminOrder[];
  canManage: boolean;
  onChanged: () => Promise<void>;
}) {
  const base = `/establishments/${establishmentId}/pdv`;
  const overview = useLoad(() => http.get<Overview>(base), [base]);
  const action = useAction(async () => {
    await overview.reload();
    await onChanged();
  });
  const [view, setView] = useState<"sell" | "receive" | "cash" | "history">("sell");
  const session = overview.data?.current ?? null;

  if (overview.loading && !overview.data) {
    return <p className="muted">Carregando caixa…</p>;
  }

  return (
    <section className="admin-section pdv">
      <ModuleHeading eyebrow="Operação" title="PDV — Ponto de venda" hint="Vendas de balcão, mesa e retirada entram como pedido: vão para a cozinha, baixam o estoque e lançam no financeiro.">
        {session ? (
          <span className="pdv-status is-open">
            Caixa aberto por {session.openedBy} · {formatDateTime(session.openedAt)}
          </span>
        ) : (
          <span className="pdv-status">Caixa fechado</span>
        )}
      </ModuleHeading>
      <ErrorMessage message={overview.error ?? action.error} />

      {!session ? (
        <OpenCash busy={action.busy} onOpen={(cents) => action.run(() => http.post(`${base}/open`, { openingAmountInCents: cents }))} />
      ) : (
        <>
          <SubTabs
            onChange={setView}
            options={[
              { value: "sell", label: "Nova venda" },
              { value: "receive", label: "Receber pedidos" },
              { value: "cash", label: "Caixa" },
              { value: "history", label: "Fechamentos" },
            ]}
            value={view}
          />
          {view === "sell" ? (
            <CounterSale
              busy={action.busy}
              canManage={canManage}
              menu={menu}
              onSell={(payload) => action.run(() => http.post(`${base}/sales`, payload))}
            />
          ) : null}
          {view === "receive" ? (
            <ReceiveOrders
              busy={action.busy}
              onReceive={(orderId, method) => action.run(() => http.post(`${base}/orders/${orderId}/receive`, { paymentMethod: method }))}
              orders={orders}
            />
          ) : null}
          {view === "cash" ? (
            <CashPanel
              base={base}
              busy={action.busy}
              canManage={canManage}
              http={http}
              orders={orders}
              run={action.run}
              session={session}
            />
          ) : null}
        </>
      )}
      {view === "history" || !session ? <History sessions={overview.data?.history ?? []} /> : null}
    </section>
  );
}

function OpenCash({ busy, onOpen }: { busy: boolean; onOpen: (cents: number) => Promise<boolean> }) {
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void onOpen(toCents(new FormData(event.currentTarget).get("amount")));
  }

  return (
    <form className="card pdv-open" onSubmit={submit}>
      <h3>Abrir caixa</h3>
      <p className="muted">Conte o dinheiro do troco que está na gaveta e informe abaixo.</p>
      <label className="field">
        <span>Fundo de troco (R$)</span>
        <input defaultValue="0,00" inputMode="decimal" name="amount" required />
      </label>
      <button className="primary-button" disabled={busy} type="submit">
        Abrir caixa
      </button>
    </form>
  );
}

function CounterSale({
  menu,
  busy,
  canManage,
  onSell,
}: {
  menu: PublicMenu | null;
  busy: boolean;
  canManage: boolean;
  onSell: (payload: unknown) => Promise<boolean>;
}) {
  const [search, setSearch] = useState("");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [picking, setPicking] = useState<MenuItem | null>(null);
  const [fulfillment, setFulfillment] = useState<FulfillmentType>("Pickup");
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [discount, setDiscount] = useState("");
  const [received, setReceived] = useState("");
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());

  const items = useMemo(() => {
    const query = plain(search.trim());
    return (menu?.categories ?? [])
      .filter((category) => category.isActive)
      .flatMap((category) => category.items)
      .filter((item) => item.isActive && item.isAvailable)
      .filter((item) => !query || plain(item.name).includes(query));
  }, [menu, search]);

  const subtotal = cart.reduce((total, line) => total + line.unitInCents * line.quantity, 0);
  const discountCents = Math.min(subtotal, toCents(discount));
  const total = subtotal - discountCents;
  const change = method === "Cash" && received ? toCents(received) - total : null;

  function add(item: MenuItem, optionIds: string[] = []) {
    const options = item.optionGroups.flatMap((group) => group.options).filter((option) => optionIds.includes(option.id));
    const key = `${item.id}:${[...optionIds].sort().join(",")}`;
    const unit = item.priceInCents + options.reduce((sum, option) => sum + option.priceInCents, 0);

    setCart((current) =>
      current.some((line) => line.key === key)
        ? current.map((line) => (line.key === key ? { ...line, quantity: Math.min(99, line.quantity + 1) } : line))
        : [...current, { key, item, quantity: 1, optionIds, optionNames: options.map((option) => option.name), unitInCents: unit }],
    );
    setPicking(null);
  }

  function choose(item: MenuItem) {
    if (item.optionGroups.length > 0) {
      setPicking(item);
      return;
    }

    add(item);
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const element = event.currentTarget;
    const done = await onSell({
      customerName: text(form, "customerName"),
      customerPhone: text(form, "customerPhone"),
      fulfillmentType: fulfillment,
      paymentMethod: method,
      tableLabel: fulfillment === "DineIn" ? text(form, "table") : null,
      notes: text(form, "notes"),
      discountInCents: discountCents,
      discountReason: discountCents > 0 ? text(form, "discountReason") : null,
      deliveryAddress:
        fulfillment === "Delivery"
          ? {
              street: text(form, "street"),
              number: text(form, "number"),
              complement: text(form, "complement"),
              district: text(form, "district"),
              city: text(form, "city"),
              state: text(form, "state"),
              postalCode: text(form, "postalCode"),
              reference: text(form, "reference"),
            }
          : null,
      items: cart.map((line) => ({ menuItemId: line.item.id, quantity: line.quantity, notes: null, optionIds: line.optionIds })),
      idempotencyKey,
    });

    if (done) {
      setCart([]);
      setDiscount("");
      setReceived("");
      setIdempotencyKey(crypto.randomUUID());
      element.reset();
    }
  }

  return (
    <div className="pdv-sale">
      <div className="pdv-products">
        <input className="pdv-search" onChange={(event) => setSearch(event.target.value)} placeholder="Buscar produto" type="search" value={search} />
        <div className="pdv-grid">
          {items.map((item) => (
            <button className="pdv-product" key={item.id} onClick={() => choose(item)} type="button">
              <strong>{item.name}</strong>
              <span>{formatBRL(item.priceInCents)}</span>
            </button>
          ))}
          {items.length === 0 ? <Empty>Nenhum produto encontrado.</Empty> : null}
        </div>
        {picking ? <OptionPicker item={picking} onCancel={() => setPicking(null)} onPick={(ids) => add(picking, ids)} /> : null}
      </div>

      <form className="card pdv-cart" onSubmit={submit}>
        <h3>Venda</h3>
        {cart.length === 0 ? <p className="muted">Toque nos produtos para adicionar.</p> : null}
        <ul className="pdv-lines">
          {cart.map((line) => (
            <li key={line.key}>
              <div>
                <strong>{line.item.name}</strong>
                {line.optionNames.length > 0 ? <small>{line.optionNames.join(", ")}</small> : null}
              </div>
              <div className="pdv-qty">
                <button onClick={() => setCart((current) => current.flatMap((item) => (item.key !== line.key ? [item] : item.quantity > 1 ? [{ ...item, quantity: item.quantity - 1 }] : [])))} type="button">
                  −
                </button>
                <span>{line.quantity}</span>
                <button onClick={() => setCart((current) => current.map((item) => (item.key === line.key ? { ...item, quantity: Math.min(99, item.quantity + 1) } : item)))} type="button">
                  +
                </button>
              </div>
              <span>{formatBRL(line.unitInCents * line.quantity)}</span>
            </li>
          ))}
        </ul>

        <div className="pdv-choice" role="group" aria-label="Atendimento">
          {(["Pickup", "DineIn", "Delivery"] as FulfillmentType[]).map((value) => (
            <button className={fulfillment === value ? "is-active" : ""} key={value} onClick={() => setFulfillment(value)} type="button">
              {value === "Pickup" ? "Balcão / retirada" : value === "DineIn" ? "Mesa" : "Entrega"}
            </button>
          ))}
        </div>

        {fulfillment === "DineIn" ? (
          <label className="field">
            <span>Mesa</span>
            <input maxLength={20} name="table" required />
          </label>
        ) : null}

        <div className="pdv-fields">
          <label className="field">
            <span>Cliente {fulfillment === "Delivery" ? "*" : "(opcional)"}</span>
            <input maxLength={120} name="customerName" required={fulfillment === "Delivery"} />
          </label>
          <label className="field">
            <span>Telefone {fulfillment === "Delivery" ? "*" : "(opcional)"}</span>
            <input inputMode="tel" maxLength={30} name="customerPhone" required={fulfillment === "Delivery"} />
          </label>
        </div>

        {fulfillment === "Delivery" ? (
          <div className="pdv-fields">
            <label className="field"><span>Rua *</span><input name="street" required /></label>
            <label className="field"><span>Número *</span><input name="number" required /></label>
            <label className="field"><span>Bairro *</span><input name="district" required /></label>
            <label className="field"><span>Complemento</span><input name="complement" /></label>
            <label className="field"><span>Cidade *</span><input defaultValue={menu?.establishment.city ?? ""} name="city" required /></label>
            <label className="field"><span>UF *</span><input defaultValue={menu?.establishment.state ?? ""} maxLength={2} name="state" required /></label>
            <label className="field"><span>CEP *</span><input defaultValue={menu?.establishment.postalCode ?? ""} name="postalCode" required /></label>
            <label className="field"><span>Referência</span><input name="reference" /></label>
          </div>
        ) : null}

        <label className="field">
          <span>Observação</span>
          <input maxLength={500} name="notes" />
        </label>

        {canManage ? (
          <div className="pdv-fields">
            <label className="field">
              <span>Desconto (R$)</span>
              <input inputMode="decimal" onChange={(event) => setDiscount(event.target.value)} placeholder="0,00" value={discount} />
            </label>
            {discountCents > 0 ? (
              <label className="field">
                <span>Motivo do desconto *</span>
                <input maxLength={120} name="discountReason" required />
              </label>
            ) : null}
          </div>
        ) : null}

        <div className="pdv-choice" role="group" aria-label="Pagamento">
          {counterMethods.map((value) => (
            <button className={method === value ? "is-active" : ""} key={value} onClick={() => setMethod(value)} type="button">
              {methodLabels[value]}
            </button>
          ))}
        </div>

        {method === "Cash" ? (
          <label className="field">
            <span>Valor recebido (para calcular o troco)</span>
            <input inputMode="decimal" onChange={(event) => setReceived(event.target.value)} placeholder="0,00" value={received} />
          </label>
        ) : null}

        <div className="pdv-total">
          {discountCents > 0 ? <span>Subtotal {formatBRL(subtotal)} · desconto {formatBRL(discountCents)}</span> : null}
          <strong>Total {formatBRL(total)}</strong>
          {change !== null && change >= 0 ? <span className="pdv-change">Troco {formatBRL(change)}</span> : null}
          {change !== null && change < 0 ? <span className="form-error">Faltam {formatBRL(-change)}</span> : null}
        </div>

        <button className="primary-button primary-button-full pdv-finish" disabled={busy || cart.length === 0 || (change !== null && change < 0)} type="submit">
          {busy ? "Registrando…" : `Receber ${formatBRL(total)}`}
        </button>
      </form>
    </div>
  );
}

function OptionPicker({ item, onPick, onCancel }: { item: MenuItem; onPick: (ids: string[]) => void; onCancel: () => void }) {
  const [chosen, setChosen] = useState<Record<string, string[]>>({});
  const valid = item.optionGroups.every((group) => {
    const count = chosen[group.id]?.length ?? 0;
    return count >= group.minSelections && count <= group.maxSelections;
  });

  return (
    <div className="card pdv-options">
      <h4>{item.name}</h4>
      {item.optionGroups.map((group) => (
        <fieldset key={group.id}>
          <legend>
            {group.name} {group.minSelections > 0 ? "*" : ""} <small>(até {group.maxSelections})</small>
          </legend>
          {group.options.filter((option) => option.isAvailable).map((option) => {
            const selected = chosen[group.id]?.includes(option.id) ?? false;
            return (
              <button
                className={`pdv-option ${selected ? "is-active" : ""}`}
                key={option.id}
                onClick={() =>
                  setChosen((current) => {
                    const list = current[group.id] ?? [];
                    const next = selected ? list.filter((id) => id !== option.id) : group.maxSelections === 1 ? [option.id] : [...list, option.id].slice(0, group.maxSelections);
                    return { ...current, [group.id]: next };
                  })
                }
                type="button"
              >
                {option.name} {option.priceInCents > 0 ? `+${formatBRL(option.priceInCents)}` : ""}
              </button>
            );
          })}
        </fieldset>
      ))}
      <div className="pdv-option-actions">
        <button className="secondary-button" onClick={onCancel} type="button">Cancelar</button>
        <button className="primary-button" disabled={!valid} onClick={() => onPick(Object.values(chosen).flat())} type="button">Adicionar</button>
      </div>
    </div>
  );
}

function ReceiveOrders({ orders, busy, onReceive }: { orders: AdminOrder[]; busy: boolean; onReceive: (orderId: string, method: PaymentMethod) => Promise<boolean> }) {
  const pending = orders.filter((order) => order.paymentStatus === "Pending" && order.status !== "Cancelled" && order.channel !== "Pdv");

  return (
    <div className="pdv-receive">
      <p className="muted">Pedidos do cardápio e do WhatsApp que ainda não foram pagos. Receba aqui quando o cliente pagar no balcão ou o entregador acertar.</p>
      {pending.length === 0 ? <Empty>Nenhum pedido aguardando pagamento.</Empty> : null}
      {pending.map((order) => (
        <article className="card pdv-receive-card" key={order.id}>
          <div>
            <strong>{order.code.slice(-8)} · {order.customerName}</strong>
            <small>
              {formatDateTime(order.createdAt)} · escolheu {methodLabels[order.paymentMethod]}
            </small>
          </div>
          <strong>{formatBRL(order.totalInCents)}</strong>
          <div className="pdv-receive-actions">
            {counterMethods.map((method) => (
              <button className="secondary-button" disabled={busy} key={method} onClick={() => void onReceive(order.id, method)} type="button">
                {methodLabels[method]}
              </button>
            ))}
          </div>
        </article>
      ))}
    </div>
  );
}

function CashPanel({
  session,
  http,
  base,
  busy,
  canManage,
  orders,
  run,
}: {
  session: CashSession;
  http: AdminHttp;
  base: string;
  busy: boolean;
  canManage: boolean;
  orders: AdminOrder[];
  run: (action: () => Promise<unknown>) => Promise<boolean>;
}) {
  const [counted, setCounted] = useState("");
  const countedCents = counted ? toCents(counted) : null;
  const difference = countedCents === null ? null : countedCents - session.expectedCashInCents;
  const codeOf = (orderId: string | null) => orders.find((order) => order.id === orderId)?.code.slice(-8) ?? "";
  const reversed = new Set(session.movements.filter((movement) => movement.type === "SaleReversal").map((movement) => movement.orderId));

  function movement(kind: "supply" | "withdraw") {
    return async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      const element = event.currentTarget;
      const form = new FormData(element);
      const done = await run(() => http.post(`${base}/${kind}`, { amountInCents: toCents(form.get("amount")), reason: text(form, "reason") }));
      if (done) {
        element.reset();
      }
    };
  }

  async function close(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    await run(() => http.post(`${base}/close`, { countedCashInCents: countedCents ?? 0, notes: text(form, "notes") }));
  }

  async function cancelSale(orderId: string) {
    const reason = window.prompt("Motivo do cancelamento da venda:");
    if (reason && reason.trim()) {
      await run(() => http.post(`${base}/sales/${orderId}/cancel`, { reason: reason.trim() }));
    }
  }

  return (
    <div className="pdv-cash">
      <div className="pdv-summary">
        <div className="card"><small>Fundo de troco</small><strong>{formatBRL(session.openingAmountInCents)}</strong></div>
        {session.totals.map((total) => (
          <div className="card" key={total.method}>
            <small>{methodLabels[total.method]} · {total.sales} venda(s)</small>
            <strong>{formatBRL(total.amountInCents)}</strong>
          </div>
        ))}
        <div className="card"><small>Suprimentos</small><strong>{formatBRL(session.suppliesInCents)}</strong></div>
        <div className="card"><small>Sangrias</small><strong>{formatBRL(session.withdrawalsInCents)}</strong></div>
        <div className="card pdv-expected"><small>Dinheiro esperado na gaveta</small><strong>{formatBRL(session.expectedCashInCents)}</strong></div>
      </div>

      <div className="pdv-forms">
        <form className="card" onSubmit={movement("supply")}>
          <h4>Suprimento</h4>
          <input inputMode="decimal" name="amount" placeholder="Valor (R$)" required />
          <input maxLength={120} name="reason" placeholder="Motivo" required />
          <button className="secondary-button" disabled={busy} type="submit">Registrar suprimento</button>
        </form>
        <form className="card" onSubmit={movement("withdraw")}>
          <h4>Sangria</h4>
          <input inputMode="decimal" name="amount" placeholder="Valor (R$)" required />
          <input maxLength={120} name="reason" placeholder="Motivo" required />
          <button className="secondary-button" disabled={busy} type="submit">Registrar sangria</button>
        </form>
        <form className="card" onSubmit={close}>
          <h4>Fechar caixa</h4>
          <input inputMode="decimal" onChange={(event) => setCounted(event.target.value)} placeholder="Dinheiro contado (R$)" required value={counted} />
          {difference !== null ? (
            <p className={difference === 0 ? "pdv-ok" : "form-error"}>
              {difference === 0 ? "Confere com o esperado." : difference > 0 ? `Sobra de ${formatBRL(difference)}` : `Falta de ${formatBRL(-difference)}`}
            </p>
          ) : null}
          <input maxLength={300} name="notes" placeholder={difference ? "Explique a diferença *" : "Observação (opcional)"} required={Boolean(difference)} />
          <button className="primary-button" disabled={busy} type="submit">Fechar caixa</button>
        </form>
      </div>

      <h3>Movimentações</h3>
      {session.movements.length === 0 ? <Empty>Nenhuma movimentação ainda.</Empty> : null}
      <table className="admin-table pdv-table">
        <tbody>
          {session.movements.map((item) => (
            <tr key={item.id}>
              <td>{formatDateTime(item.createdAt)}</td>
              <td>{movementLabels[item.type]}</td>
              <td>{item.reason} {codeOf(item.orderId)}</td>
              <td>{methodLabels[item.method]}</td>
              <td>{item.operatorName}</td>
              <td className={item.type === "Withdrawal" || item.type === "SaleReversal" ? "is-negative" : ""}>{formatBRL(item.amountInCents)}</td>
              <td>
                {canManage && item.type === "Sale" && item.orderId && !reversed.has(item.orderId) ? (
                  <button className="link-button" disabled={busy} onClick={() => void cancelSale(item.orderId!)} type="button">Cancelar venda</button>
                ) : null}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function History({ sessions }: { sessions: CashSession[] }) {
  return (
    <div className="pdv-history">
      <h3>Últimos fechamentos</h3>
      {sessions.length === 0 ? <Empty>Nenhum caixa fechado ainda.</Empty> : null}
      <table className="admin-table">
        <tbody>
          {sessions.map((session) => (
            <tr key={session.id}>
              <td>{formatDateTime(session.openedAt)} → {session.closedAt ? formatDateTime(session.closedAt) : ""}</td>
              <td>{session.openedBy}{session.closedBy && session.closedBy !== session.openedBy ? ` / ${session.closedBy}` : ""}</td>
              <td>Esperado {formatBRL(session.expectedCashInCents)}</td>
              <td>Contado {formatBRL(session.countedCashInCents ?? 0)}</td>
              <td className={session.differenceInCents ? "is-negative" : ""}>
                {session.differenceInCents ? `Diferença ${formatBRL(session.differenceInCents)}` : "Sem diferença"}
              </td>
              <td>{session.closingNotes}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
