"use client";

import { type FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { formatBRL, formatDateTime, type AdminHttp } from "@/lib/admin-api";
import type { AdminOrder, MenuItem, PaymentMethod, PublicMenu } from "@/lib/types";
import { ErrorMessage, text, toCents, useAction, useLoad } from "./shared";

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

interface OpenTable {
  table: string;
  orders: number;
  totalInCents: number;
  openedAt: string;
  waiters: string[];
  allServed: boolean;
  items: AdminOrder[];
}

interface Line {
  key: string;
  item: MenuItem;
  quantity: number;
  optionIds: string[];
  optionNames: string[];
  unitInCents: number;
}

type Mode = "counter" | "tables" | "delivery" | "receive";

interface CustomerLite {
  id: string;
  name: string;
  phone: string;
  isActive?: boolean;
}

const methodLabels: Record<PaymentMethod, string> = { Cash: "Dinheiro", CardOnDelivery: "Cartão", Pix: "Pix", Online: "Online" };
const methodIcons: Record<string, string> = { Cash: "💵", CardOnDelivery: "💳", Pix: "⚡" };
const movementLabels: Record<CashMovement["type"], string> = { Supply: "Suprimento", Withdrawal: "Sangria", Sale: "Venda", SaleReversal: "Estorno" };
const counterMethods: PaymentMethod[] = ["Cash", "CardOnDelivery", "Pix"];
const palette = ["#8b7cf6", "#2dd4bf", "#f5b544", "#5aa9ff", "#f472b6", "#3ecf8e", "#fb923c", "#a3e635"];
const DEFAULT_TABLES = 20;

function plain(value: string) {
  return value.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
}

/** Two letters that tell products apart: "Coca-Cola lata" → CC, "X-Burger" → XB. */
function initials(name: string) {
  const words = name.replace(/[^\p{L}\p{N}\s-]/gu, "").split(/[\s-]+/).filter(Boolean);
  return (words.length > 1 ? words[0][0] + words[1][0] : name.slice(0, 2)).toUpperCase();
}

function minutesSince(value: string) {
  return Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60000));
}

/* ------------------------------------------------------------------------------------------------ */

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
  const base = `/establishments/${establishmentId}`;
  const overview = useLoad(() => http.get<Overview>(`${base}/pdv`), [base]);
  const tables = useLoad(() => http.get<OpenTable[]>(`${base}/tables`), [base]);
  const action = useAction(async () => {
    await Promise.all([overview.reload(), tables.reload(), onChanged()]);
  });
  const [mode, setMode] = useState<Mode>("counter");
  const [drawer, setDrawer] = useState<"cash" | "history" | null>(null);
  const [table, setTable] = useState<string | null>(null);
  const [comanda, setComanda] = useState<string | null>(null);
  const [receipt, setReceipt] = useState<AdminOrder | null>(null);
  const customers = useLoad(() => http.get<CustomerLite[]>(`${base}/customers`).catch(() => []), [base]);
  const session = overview.data?.current ?? null;

  useEffect(() => {
    const timer = window.setInterval(() => void tables.reload(), 8000);
    return () => window.clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [base]);

  const toReceive = orders.filter((order) => order.paymentStatus === "Pending" && order.status !== "Cancelled" && order.channel !== "Pdv" && !order.tableLabel);

  if (overview.loading && !overview.data) {
    return <p className="muted">Carregando caixa…</p>;
  }

  return (
    <section className="pos">
      <header className="pos-bar">
        <nav className="pos-modes" aria-label="Modo do PDV">
          {(
            [
              ["counter", "Balcão"],
              ["tables", `Mesas${tables.data?.length ? ` · ${tables.data.length}` : ""}`],
              ["delivery", "Delivery"],
              ["receive", `Receber${toReceive.length ? ` · ${toReceive.length}` : ""}`],
            ] as Array<[Mode, string]>
          ).map(([value, label]) => (
            <button
              className={mode === value ? "is-active" : ""}
              key={value}
              onClick={() => {
                setMode(value);
                setComanda(null);
              }}
              type="button"
            >
              {label}
            </button>
          ))}
        </nav>
        <div className="pos-cash">
          {session ? (
            <>
              <span className="pos-status is-open">● Caixa aberto · {session.openedBy.split("@")[0]} · {formatDateTime(session.openedAt)}</span>
              <span className="pos-drawer-amount">Gaveta {formatBRL(session.expectedCashInCents)}</span>
              <button className="secondary-button" onClick={() => setDrawer("cash")} type="button">Caixa</button>
            </>
          ) : (
            <span className="pos-status">● Caixa fechado</span>
          )}
          <button className="secondary-button" onClick={() => setDrawer("history")} type="button">Fechamentos</button>
        </div>
      </header>

      <ErrorMessage message={overview.error ?? tables.error ?? action.error} />

      {!session ? (
        <OpenCash busy={action.busy} onOpen={(cents) => action.run(() => http.post(`${base}/pdv/open`, { openingAmountInCents: cents }))} />
      ) : mode === "tables" && !comanda ? (
        <TablesView
          slug={menu?.establishment.slug ?? null}
          busy={action.busy}
          onAdd={(label) => setComanda(label)}
          onClose={(label, method) => action.run(() => http.post(`${base}/tables/${encodeURIComponent(label)}/close`, { paymentMethod: method }))}
          onSelect={setTable}
          selected={table}
          tables={tables.data ?? []}
        />
      ) : mode === "receive" ? (
        <ReceiveView
          busy={action.busy}
          onReceive={(orderId, method) => action.run(() => http.post(`${base}/pdv/orders/${orderId}/receive`, { paymentMethod: method }))}
          orders={toReceive}
        />
      ) : (
        <SaleView
          busy={action.busy}
          canManage={canManage}
          comanda={mode === "tables" ? comanda : null}
          customers={customers.data ?? []}
          delivery={mode === "delivery"}
          menu={menu}
          onBack={() => setComanda(null)}
          onSell={async (payload) => {
            if (mode === "tables" && comanda) {
              const done = await action.run(() => http.post(`${base}/tables/${encodeURIComponent(comanda)}/orders`, payload));
              if (done) {
                setTable(comanda);
                setComanda(null);
              }
              return done;
            }
            let sold: AdminOrder | null = null;
            const done = await action.run(async () => {
              sold = (await http.post<{ order: AdminOrder }>(`${base}/pdv/sales`, payload)).order;
            });
            if (done && sold) setReceipt(sold);
            return done;
          }}
        />
      )}

      {receipt ? <Receipt onClose={() => setReceipt(null)} order={receipt} storeName={menu?.establishment.name ?? ""} /> : null}

      {drawer === "cash" && session ? (
        <Drawer onClose={() => setDrawer(null)} title="Caixa">
          <CashPanel base={base} busy={action.busy} canManage={canManage} http={http} orders={orders} run={action.run} session={session} />
        </Drawer>
      ) : null}
      {drawer === "history" ? (
        <Drawer onClose={() => setDrawer(null)} title="Últimos fechamentos">
          <History sessions={overview.data?.history ?? []} />
        </Drawer>
      ) : null}
    </section>
  );
}

function Drawer({ title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode }) {
  return (
    <>
      <button aria-label="Fechar" className="drawer-backdrop" onClick={onClose} type="button" />
      <aside className="drawer drawer-wide">
        <header className="drawer-head">
          <h2>{title}</h2>
          <button className="drawer-close" onClick={onClose} type="button">×</button>
        </header>
        {children}
      </aside>
    </>
  );
}

/* ------------------------------------------------------------------------------------------------ */

function Keypad({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const press = (key: string) => {
    if (key === "⌫") {
      onChange(value.slice(0, -1));
      return;
    }
    if (key === "C") {
      onChange("");
      return;
    }
    const digits = (value + key).replace(/\D/g, "").replace(/^0+/, "").slice(0, 9);
    onChange(digits);
  };

  return (
    <div className="keypad">
      {["1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "⌫"].map((key) => (
        <button key={key} onClick={() => press(key)} type="button">{key}</button>
      ))}
    </div>
  );
}

const centsFromDigits = (digits: string) => Number(digits || "0");

/** Cédulas e moedas do real, em centavos. */
const denominations: Array<{ cents: number; label: string; kind: "note" | "coin" }> = [
  { cents: 20000, label: "200", kind: "note" },
  { cents: 10000, label: "100", kind: "note" },
  { cents: 5000, label: "50", kind: "note" },
  { cents: 2000, label: "20", kind: "note" },
  { cents: 1000, label: "10", kind: "note" },
  { cents: 500, label: "5", kind: "note" },
  { cents: 200, label: "2", kind: "note" },
  { cents: 100, label: "1", kind: "coin" },
  { cents: 50, label: "0,50", kind: "coin" },
  { cents: 25, label: "0,25", kind: "coin" },
  { cents: 10, label: "0,10", kind: "coin" },
  { cents: 5, label: "0,05", kind: "coin" },
];

/**
 * Contagem do caixa como o operador faz na gaveta: toca na cédula/moeda e o total soma sozinho.
 * Devolve o mesmo valor em centavos (string de dígitos) que o teclado devolvia, então quem usa
 * não muda. "Digitar valor" continua para quem prefere o teclado.
 */
function CashCounter({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const [mode, setMode] = useState<"count" | "type">("count");
  const [counts, setCounts] = useState<Record<number, number>>({});
  /* Quem usa zerou o valor (registrou a sangria, fechou o caixa): a contagem na tela zera junto. */
  useEffect(() => {
    if (!value) setCounts({});
  }, [value]);

  function bump(cents: number, delta: number) {
    const next = { ...counts, [cents]: Math.max(0, Math.min(999, (counts[cents] ?? 0) + delta)) };
    setCounts(next);
    onChange(String(denominations.reduce((sum, item) => sum + item.cents * (next[item.cents] ?? 0), 0)));
  }

  function switchTo(next: "count" | "type") {
    setMode(next);
    setCounts({});
    onChange("");
  }

  return (
    <div className="cash-counter">
      <div className="cash-counter-modes" role="tablist">
        <button aria-selected={mode === "count"} className={mode === "count" ? "is-active" : ""} onClick={() => switchTo("count")} role="tab" type="button">Contar cédulas</button>
        <button aria-selected={mode === "type"} className={mode === "type" ? "is-active" : ""} onClick={() => switchTo("type")} role="tab" type="button">Digitar valor</button>
      </div>
      {mode === "type" ? (
        <Keypad onChange={onChange} value={value} />
      ) : (
        <div className="cash-money">
          {denominations.map((item) => {
            const count = counts[item.cents] ?? 0;
            return (
              <div className={`cash-money-item is-${item.kind} cash-${item.label.replace(",", "")} ${count ? "has-count" : ""}`} key={item.cents}>
                <button aria-label={`Somar ${item.kind === "note" ? "cédula" : "moeda"} de R$ ${item.label}`} className="cash-money-face" onClick={() => bump(item.cents, 1)} type="button">
                  <small>R$</small>
                  <strong>{item.label}</strong>
                  {count ? <b>{count}×</b> : null}
                </button>
                {count ? (
                  <button aria-label={`Tirar uma de R$ ${item.label}`} className="cash-money-minus" onClick={() => bump(item.cents, -1)} type="button">−</button>
                ) : null}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

function OpenCash({ busy, onOpen }: { busy: boolean; onOpen: (cents: number) => Promise<boolean> }) {
  const [digits, setDigits] = useState("");

  return (
    <div className="pos-open">
      <div className="pos-open-card">
        <small>Abertura de caixa</small>
        <h2>Quanto tem de troco na gaveta?</h2>
        <strong className="pos-open-value">{formatBRL(centsFromDigits(digits))}</strong>
        <CashCounter onChange={setDigits} value={digits} />
        <button className="primary-button primary-button-full" disabled={busy} onClick={() => void onOpen(centsFromDigits(digits))} type="button">
          Abrir caixa
        </button>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------------------------------------ */

function SaleView({
  customers,
  menu,
  busy,
  canManage,
  delivery,
  comanda,
  onSell,
  onBack,
}: {
  menu: PublicMenu | null;
  busy: boolean;
  canManage: boolean;
  delivery: boolean;
  comanda: string | null;
  onSell: (payload: unknown) => Promise<boolean>;
  onBack: () => void;
  customers: CustomerLite[];
}) {
  const [category, setCategory] = useState<string>("all");
  const [search, setSearch] = useState("");
  const [lines, setLines] = useState<Line[]>([]);
  const [picking, setPicking] = useState<MenuItem | null>(null);
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [received, setReceived] = useState("");
  const [discount, setDiscount] = useState("");
  const [dineIn, setDineIn] = useState<string>("");
  const [notes, setNotes] = useState("");
  const [details, setDetails] = useState(false);
  const [keypad, setKeypad] = useState(false);
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());

  const categories = useMemo(() => (menu?.categories ?? []).filter((item) => item.isActive && item.items.some((product) => product.isActive)), [menu]);
  const colorOf = useMemo(() => Object.fromEntries(categories.map((item, index) => [item.id, palette[index % palette.length]])), [categories]);

  const products = useMemo(() => {
    const query = plain(search.trim());
    return categories
      .flatMap((item) => item.items.map((product) => ({ product, categoryId: item.id })))
      .filter(({ product }) => product.isActive)
      .filter(({ product, categoryId }) =>
        category === "all" ? true : category === "promo" ? (product.compareAtPriceInCents ?? 0) > product.priceInCents : categoryId === category,
      )
      .filter(({ product }) => !query || plain(`${product.name} ${product.description ?? ""} ${product.sku ?? ""} ${product.barcode ?? ""}`).includes(query));
  }, [categories, category, search]);

  const subtotal = lines.reduce((total, line) => total + line.unitInCents * line.quantity, 0);
  const discountCents = Math.min(subtotal, toCents(discount));
  const total = subtotal - discountCents;
  const receivedCents = centsFromDigits(received);
  const change = method === "Cash" && received ? receivedCents - total : null;
  const isComanda = comanda !== null;

  function add(item: MenuItem, optionIds: string[] = []) {
    const options = item.optionGroups.flatMap((group) => group.options).filter((option) => optionIds.includes(option.id));
    const key = `${item.id}:${[...optionIds].sort().join(",")}`;
    const unit = item.priceInCents + options.reduce((sum, option) => sum + option.priceInCents, 0);
    setLines((current) =>
      current.some((line) => line.key === key)
        ? current.map((line) => (line.key === key ? { ...line, quantity: Math.min(99, line.quantity + 1) } : line))
        : [...current, { key, item, quantity: 1, optionIds, optionNames: options.map((option) => option.name), unitInCents: unit }],
    );
    setPicking(null);
  }

  function change_(key: string, delta: number) {
    setLines((current) => current.flatMap((line) => (line.key !== key ? [line] : line.quantity + delta > 0 ? [{ ...line, quantity: Math.min(99, line.quantity + delta) }] : [])));
  }

  const searchRef = useRef<HTMLInputElement>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const [customerPhone, setCustomerPhone] = useState("");

  // Shortcuts at the counter: F2 search, F4 next payment method, F9 finish, Esc clear search.
  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key === "F2") {
        event.preventDefault();
        searchRef.current?.focus();
      } else if (event.key === "F4" && !isComanda) {
        event.preventDefault();
        setMethod((current) => counterMethods[(counterMethods.indexOf(current) + 1) % counterMethods.length]);
      } else if (event.key === "F9") {
        event.preventDefault();
        formRef.current?.requestSubmit();
      } else if (event.key === "Escape" && document.activeElement === searchRef.current) {
        setSearch("");
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [isComanda]);

  /** The barcode reader types the code and presses Enter: an exact code adds the product at once. */
  function onSearchEnter() {
    const code = search.trim();
    const all = categories.flatMap((item) => item.items).filter((product) => product.isActive && product.isAvailable);
    const exact = all.find((product) => product.barcode === code.replace(/\D/g, "") || (product.sku && product.sku.toLowerCase() === code.toLowerCase()));
    const target = exact ?? (products.length === 1 ? products[0].product : null);
    if (target) {
      if (target.optionGroups.length > 0) setPicking(target);
      else add(target);
      setSearch("");
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const element = event.currentTarget;
    const items = lines.map((line) => ({ menuItemId: line.item.id, quantity: line.quantity, notes: null, optionIds: line.optionIds }));

    const done = isComanda
      ? await onSell({ notes: notes.trim() || null, items, idempotencyKey })
      : await onSell({
          customerName: text(form, "customerName"),
          customerPhone: text(form, "customerPhone"),
          fulfillmentType: delivery ? "Delivery" : dineIn ? "DineIn" : "Pickup",
          paymentMethod: method,
          tableLabel: dineIn || null,
          notes: notes.trim() || null,
          discountInCents: discountCents,
          discountReason: discountCents > 0 ? text(form, "discountReason") : null,
          deliveryAddress: delivery
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
          items,
          idempotencyKey,
        });

    if (done) {
      setLines([]);
      setReceived("");
      setDiscount("");
      setNotes("");
      setDineIn("");
      setCustomerPhone("");
      setIdempotencyKey(crypto.randomUUID());
      searchRef.current?.focus();
      element.reset();
    }
  }

  return (
    <div className="pos-sale">
      <aside className="pos-rail" aria-label="Categorias">
        <button className={category === "all" ? "is-active" : ""} onClick={() => setCategory("all")} type="button">
          <i style={{ background: "#c9ccd4" }} />Tudo
        </button>
        {categories.some((item) => item.items.some((product) => (product.compareAtPriceInCents ?? 0) > product.priceInCents)) ? (
          <button className={category === "promo" ? "is-active" : ""} onClick={() => setCategory("promo")} type="button">
            <i style={{ background: "#ff6a13" }} />Promoções
          </button>
        ) : null}
        {categories.map((item) => (
          <button className={category === item.id ? "is-active" : ""} key={item.id} onClick={() => setCategory(item.id)} type="button">
            <i style={{ background: colorOf[item.id] }} />
            {item.name}
            <small>{item.items.filter((product) => product.isActive).length}</small>
          </button>
        ))}
      </aside>

      <div className="pos-catalog">
        <div className="pos-searchbar">
          <input
            autoFocus
            className="pos-search"
            onChange={(event) => setSearch(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.preventDefault();
                onSearchEnter();
              }
            }}
            placeholder="Nome, código ou leia o código de barras  (F2)"
            ref={searchRef}
            type="search"
            value={search}
          />
          <span className="pos-keys"><kbd>F4</kbd> pagamento <kbd>F9</kbd> finalizar</span>
        </div>
        <div className="pos-grid">
          {products.map(({ product, categoryId }) => {
            const promo = (product.compareAtPriceInCents ?? 0) > product.priceInCents;
            const count = lines.filter((line) => line.item.id === product.id).reduce((sum, line) => sum + line.quantity, 0);
            return (
              <button
                className={`pos-tile ${product.isAvailable ? "" : "is-off"}`}
                disabled={!product.isAvailable}
                key={product.id}
                onClick={() => (product.optionGroups.length > 0 ? setPicking(product) : add(product))}
                style={{ ["--tile" as string]: colorOf[categoryId] }}
                type="button"
              >
                {product.imageUrl ? <img alt="" src={product.imageUrl} /> : <span className="pos-tile-mark">{initials(product.name)}</span>}
                <span className="pos-tile-name">{product.name}</span>
                <span className="pos-tile-price">
                  {promo ? <s>{formatBRL(product.compareAtPriceInCents!)}</s> : null}
                  {formatBRL(product.priceInCents)}
                </span>
                {count > 0 ? <b className="pos-tile-count">{count}</b> : null}
                {!product.isAvailable ? <em>esgotado</em> : null}
              </button>
            );
          })}
          {products.length === 0 ? <p className="muted">Nenhum produto encontrado.</p> : null}
        </div>
      </div>

      <form className="pos-ticket" onSubmit={submit} ref={formRef}>
        <header>
          {isComanda ? (
            <>
              <button className="link-button" onClick={onBack} type="button">← Mesas</button>
              <h3>Comanda · Mesa {comanda}</h3>
            </>
          ) : (
            <h3>{delivery ? "Delivery" : "Venda no balcão"}</h3>
          )}
          {lines.length > 0 ? <button className="link-button danger" onClick={() => setLines([])} type="button">Limpar</button> : null}
        </header>

        <div aria-hidden="true" className={`pos-printer ${lines.length ? "is-printing" : ""}`}>
          <span className="pos-printer-led" />
          <span className="pos-printer-label">{isComanda ? "Comanda" : "Cupom"}</span>
          <span className="pos-printer-slot" />
        </div>
        <ul className="pos-lines pos-paper">
          <li className="pos-paper-head" aria-hidden="true">
            <span>{menu?.establishment.name ?? "Venda"}</span>
            <small>{isComanda ? `Comanda · mesa ${comanda}` : "Prévia do cupom"}</small>
          </li>
          {lines.length === 0 ? <li className="pos-empty">Toque nos produtos para montar o pedido.</li> : null}
          {lines.map((line) => (
            <li className="pos-paper-line" key={line.key}>
              <div>
                <strong>{line.item.name}</strong>
                {line.optionNames.length > 0 ? <small>{line.optionNames.join(", ")}</small> : null}
                <small>{formatBRL(line.unitInCents)} un.</small>
              </div>
              <div className="pos-qty">
                <button onClick={() => change_(line.key, -1)} type="button">−</button>
                <span>{line.quantity}</span>
                <button onClick={() => change_(line.key, 1)} type="button">+</button>
              </div>
              <b>{formatBRL(line.unitInCents * line.quantity)}</b>
            </li>
          ))}
        </ul>

        <div className="pos-ticket-body">
          {!isComanda && !delivery && details ? (
            <div className="pos-row">
              <input
                list="pos-customers"
                name="customerName"
                onChange={(event) => {
                  const found = customers.find((customer) => customer.name === event.target.value);
                  if (found) setCustomerPhone(found.phone);
                }}
                placeholder="Cliente (opcional)"
              />
              <input name="customerPhone" type="hidden" value={customerPhone} />
              <input inputMode="text" maxLength={20} onChange={(event) => setDineIn(event.target.value)} placeholder="Mesa (opcional)" value={dineIn} />
            </div>
          ) : null}
          {delivery ? (
            <div className="pos-address">
              <input
                list="pos-customers"
                name="customerName"
                onChange={(event) => {
                  const found = customers.find((customer) => customer.name === event.target.value);
                  if (found) setCustomerPhone(found.phone);
                }}
                placeholder="Cliente *"
                required
              />
              <input inputMode="tel" name="customerPhone" onChange={(event) => setCustomerPhone(event.target.value)} placeholder="Telefone *" required value={customerPhone} />
              <input className="wide" name="street" placeholder="Rua *" required />
              <input name="number" placeholder="Nº *" required />
              <input name="district" placeholder="Bairro *" required />
              <input name="complement" placeholder="Complemento" />
              <input name="reference" placeholder="Referência" />
              <input defaultValue={menu?.establishment.city ?? ""} name="city" placeholder="Cidade *" required />
              <input defaultValue={menu?.establishment.state ?? ""} maxLength={2} name="state" placeholder="UF *" required />
              <input defaultValue={menu?.establishment.postalCode ?? ""} name="postalCode" placeholder="CEP *" required />
            </div>
          ) : null}
          {isComanda || delivery || details ? (
            <input className="pos-notes" onChange={(event) => setNotes(event.target.value)} placeholder={isComanda ? "Observação para a cozinha" : "Observação"} value={notes} />
          ) : null}

          {!isComanda && !delivery ? (
            <button className="link-button pos-more" onClick={() => setDetails((value) => !value)} type="button">
              {details ? "− Menos detalhes" : "+ Cliente, mesa, observação e desconto"}
            </button>
          ) : null}

          {!isComanda && canManage && (details || delivery) ? (
            <div className="pos-row">
              <input inputMode="decimal" onChange={(event) => setDiscount(event.target.value)} placeholder="Desconto R$" value={discount} />
              {discountCents > 0 ? <input name="discountReason" placeholder="Motivo do desconto *" required /> : null}
            </div>
          ) : null}

          {!isComanda ? (
            <div className="pay-methods" role="group" aria-label="Pagamento">
              {counterMethods.map((value) => (
                <button className={method === value ? "is-active" : ""} key={value} onClick={() => setMethod(value)} type="button">
                  <span>{methodIcons[value]}</span>
                  {methodLabels[value]}
                </button>
              ))}
            </div>
          ) : null}

          {!isComanda && method === "Cash" && lines.length > 0 ? (
            <div className="cash-calc">
              <div className="cash-quick">
                <button onClick={() => setReceived(String(total))} type="button">Exato</button>
                {[1000, 2000, 5000, 10000, 20000].filter((bill) => bill >= total).slice(0, 3).map((bill) => (
                  <button key={bill} onClick={() => setReceived(String(bill))} type="button">{formatBRL(bill).replace(",00", "")}</button>
                ))}
                <button className={keypad ? "is-active" : ""} onClick={() => setKeypad((value) => !value)} type="button">Outro</button>
              </div>
              {received ? (
                <div className="cash-received">
                  <small>Recebido</small>
                  <strong>{formatBRL(receivedCents)}</strong>
                </div>
              ) : null}
              {keypad ? <Keypad onChange={setReceived} value={received} /> : null}
            </div>
          ) : null}
        </div>

        <footer className="pos-total">
          {discountCents > 0 ? <span>Subtotal {formatBRL(subtotal)} · desconto − {formatBRL(discountCents)}</span> : null}
          <div>
            <small>Total</small>
            <strong>{formatBRL(isComanda ? subtotal : total)}</strong>
          </div>
          {change !== null && change >= 0 ? <p className="pos-change">Troco {formatBRL(change)}</p> : null}
          {change !== null && change < 0 ? <p className="form-error">Faltam {formatBRL(-change)}</p> : null}
          <button
            className={`primary-button primary-button-full pos-finish ${isComanda ? "" : "money-button"}`}
            disabled={busy || lines.length === 0 || (change !== null && change < 0)}
            type="submit"
          >
            {busy ? "Registrando…" : isComanda ? "Enviar para a cozinha" : `Receber ${formatBRL(total)}`}
          </button>
        </footer>
      </form>

      {picking ? <OptionPicker item={picking} onCancel={() => setPicking(null)} onPick={(ids) => add(picking, ids)} /> : null}
      <datalist id="pos-customers">
        {customers.filter((customer) => customer.isActive !== false).slice(0, 300).map((customer) => (
          <option key={customer.id} value={customer.name}>{customer.phone}</option>
        ))}
      </datalist>
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
    <div className="modal-backdrop" onClick={(event) => event.target === event.currentTarget && onCancel()} role="presentation">
      <section className="modal pos-options" role="dialog">
        <h3>{item.name}</h3>
        {item.optionGroups.map((group) => (
          <fieldset key={group.id}>
            <legend>
              {group.name} {group.minSelections > 0 ? "*" : ""} <small>até {group.maxSelections}</small>
            </legend>
            {group.options.filter((option) => option.isAvailable).map((option) => {
              const selected = chosen[group.id]?.includes(option.id) ?? false;
              return (
                <button
                  className={`pos-option ${selected ? "is-active" : ""}`}
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
        <div className="pos-option-actions">
          <button className="secondary-button" onClick={onCancel} type="button">Cancelar</button>
          <button className="primary-button" disabled={!valid} onClick={() => onPick(Object.values(chosen).flat())} type="button">Adicionar</button>
        </div>
      </section>
    </div>
  );
}

/* ------------------------------------------------------------------------------------------------ */

function TablesView({
  slug,
  tables,
  selected,
  busy,
  onSelect,
  onAdd,
  onClose,
}: {
  slug: string | null;
  tables: OpenTable[];
  selected: string | null;
  busy: boolean;
  onSelect: (table: string | null) => void;
  onAdd: (table: string) => void;
  onClose: (table: string, method: PaymentMethod) => Promise<boolean>;
}) {
  const [custom, setCustom] = useState("");
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const open = new Map(tables.map((item) => [item.table, item]));
  const numbered = Math.max(DEFAULT_TABLES, ...tables.map((item) => Number(item.table)).filter((value) => Number.isFinite(value)));
  const labels = [
    ...Array.from({ length: numbered }, (_, index) => String(index + 1)),
    ...tables.map((item) => item.table).filter((label) => !/^\d+$/.test(label)),
  ];
  const current = selected ? open.get(selected) ?? null : null;
  const lines = current
    ? Object.values(
        current.items
          .flatMap((order) => order.items)
          .reduce<Record<string, { name: string; quantity: number; total: number }>>((acc, item) => {
            const entry = acc[item.name] ?? { name: item.name, quantity: 0, total: 0 };
            entry.quantity += item.quantity;
            entry.total += item.totalInCents;
            acc[item.name] = entry;
            return acc;
          }, {}),
      )
    : [];

  return (
    <div className="pos-tables">
      <div className="tables-map">
        <div className="tables-legend">
          <span><i className="is-free" />Livre</span>
          <span><i className="is-busy" />Ocupada</span>
          <span><i className="is-served" />Tudo servido</span>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              if (custom.trim()) {
                onSelect(custom.trim());
                onAdd(custom.trim());
                setCustom("");
              }
            }}
          >
            <input maxLength={20} onChange={(event) => setCustom(event.target.value)} placeholder="Outra mesa (ex.: Varanda 2)" value={custom} />
          </form>
        </div>
        <div className="tables-grid">
          {labels.map((label) => {
            const info = open.get(label);
            return (
              <button
                className={`table-island ${info ? (info.allServed ? "is-served" : "is-busy") : "is-free"} ${selected === label ? "is-selected" : ""}`}
                key={label}
                onClick={() => onSelect(label)}
                type="button"
              >
                <i aria-hidden="true" className="table-3d"><i className="table-top" /><i className="table-chairs" /></i>
                <strong>{label}</strong>
                {info ? (
                  <>
                    <span>{formatBRL(info.totalInCents)}</span>
                    <small>{minutesSince(info.openedAt)} min · {info.orders} pedido(s)</small>
                  </>
                ) : (
                  <small>livre</small>
                )}
              </button>
            );
          })}
        </div>
      </div>

      <aside className="table-panel">
        {!selected ? (
          <p className="pos-empty">Escolha uma mesa para lançar a comanda ou fechar a conta.</p>
        ) : (
          <>
            <header>
              <h3>Mesa {selected}</h3>
              {current ? <small>Aberta há {minutesSince(current.openedAt)} min{current.waiters.length ? ` · ${current.waiters.join(", ")}` : ""}</small> : <small>Livre</small>}
            </header>
            <ul className="pos-lines">
              {lines.length === 0 ? <li className="pos-empty">Nada lançado ainda.</li> : null}
              {lines.map((line) => (
                <li key={line.name}>
                  <div><strong>{line.quantity}× {line.name}</strong></div>
                  <span />
                  <b>{formatBRL(line.total)}</b>
                </li>
              ))}
            </ul>
            <button className="primary-button primary-button-full" onClick={() => onAdd(selected)} type="button">+ Lançar itens (comanda)</button>
            {slug ? (
              <div className="table-qr">
                <small>Link da mesa para o QR Code (o cliente pede pelo celular)</small>
                <code>{`${window.location.origin}/menu/${slug}?mesa=${encodeURIComponent(selected)}`}</code>
                <button className="link-button" onClick={() => void navigator.clipboard?.writeText(`${window.location.origin}/menu/${slug}?mesa=${encodeURIComponent(selected)}`)} type="button">Copiar link</button>
              </div>
            ) : null}
            {current ? (
              <div className="table-close">
                <div className="pos-total">
                  <div><small>Conta da mesa</small><strong>{formatBRL(current.totalInCents)}</strong></div>
                  <small>Com 10% de serviço: {formatBRL(Math.round(current.totalInCents * 1.1))} (informativo)</small>
                </div>
                <div className="pay-methods" role="group" aria-label="Pagamento">
                  {counterMethods.map((value) => (
                    <button className={method === value ? "is-active" : ""} key={value} onClick={() => setMethod(value)} type="button">
                      <span>{methodIcons[value]}</span>
                      {methodLabels[value]}
                    </button>
                  ))}
                </div>
                <button
                  className="primary-button primary-button-full money-button pos-finish"
                  disabled={busy}
                  onClick={() => window.confirm(`Fechar a conta da mesa ${selected} em ${methodLabels[method]}?`) && void onClose(selected, method).then((done) => done && onSelect(null))}
                  type="button"
                >
                  Fechar conta · {formatBRL(current.totalInCents)}
                </button>
              </div>
            ) : null}
          </>
        )}
      </aside>
    </div>
  );
}

/* ------------------------------------------------------------------------------------------------ */

function ReceiveView({ orders, busy, onReceive }: { orders: AdminOrder[]; busy: boolean; onReceive: (orderId: string, method: PaymentMethod) => Promise<boolean> }) {
  return (
    <div className="pos-receive">
      <p className="muted">Pedidos do cardápio e do WhatsApp ainda não pagos. Receba quando o cliente pagar no balcão ou o entregador acertar.</p>
      {orders.length === 0 ? <p className="pos-empty">Nenhum pedido aguardando pagamento.</p> : null}
      <div className="receive-grid">
        {orders.map((order) => (
          <article className="receive-card" key={order.id}>
            <header>
              <span className={`origin-tag origin-${order.channel === "WhatsApp" ? "whatsapp" : "menu"}`}>{order.channel === "WhatsApp" ? "WhatsApp" : "Cardápio"}</span>
              <small>#{order.code.slice(-6)} · {formatDateTime(order.createdAt)}</small>
            </header>
            <strong>{order.customerName}</strong>
            <span className="muted">{order.fulfillmentType === "Delivery" ? `Entrega${order.courierName ? ` · ${order.courierName}` : ""}` : "Retirada"} · escolheu {methodLabels[order.paymentMethod]}</span>
            <b className="receive-total">{formatBRL(order.totalInCents)}</b>
            <div className="pay-methods compact">
              {counterMethods.map((method) => (
                <button disabled={busy} key={method} onClick={() => void onReceive(order.id, method)} type="button">
                  <span>{methodIcons[method]}</span>
                  {methodLabels[method]}
                </button>
              ))}
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------------------------------------ */

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
  const [kind, setKind] = useState<"supply" | "withdraw" | "close">("close");
  const countedCents = centsFromDigits(counted);
  const difference = counted ? countedCents - session.expectedCashInCents : null;
  const codeOf = (orderId: string | null) => orders.find((order) => order.id === orderId)?.code.slice(-6) ?? "";
  const reversed = new Set(session.movements.filter((movement) => movement.type === "SaleReversal").map((movement) => movement.orderId));

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const done =
      kind === "close"
        ? await run(() => http.post(`${base}/pdv/close`, { countedCashInCents: countedCents, notes: text(form, "notes") }))
        : await run(() => http.post(`${base}/pdv/${kind}`, { amountInCents: countedCents, reason: text(form, "reason") }));
    if (done) {
      setCounted("");
      element.reset();
    }
  }

  async function cancelSale(orderId: string) {
    const reason = window.prompt("Motivo do cancelamento da venda:");
    if (reason?.trim()) {
      await run(() => http.post(`${base}/pdv/sales/${orderId}/cancel`, { reason: reason.trim() }));
    }
  }

  return (
    <div className="cash-panel">
      <div className="kpi-strip">
        <div className="kpi"><small>Fundo de troco</small><strong>{formatBRL(session.openingAmountInCents)}</strong></div>
        {session.totals.map((total) => (
          <div className="kpi" key={total.method}><small>{methodLabels[total.method]} · {total.sales}</small><strong>{formatBRL(total.amountInCents)}</strong></div>
        ))}
        <div className="kpi"><small>Suprimentos</small><strong>{formatBRL(session.suppliesInCents)}</strong></div>
        <div className="kpi"><small>Sangrias</small><strong>{formatBRL(session.withdrawalsInCents)}</strong></div>
        <div className="kpi is-ok"><small>Esperado na gaveta</small><strong>{formatBRL(session.expectedCashInCents)}</strong></div>
      </div>

      <div className="pay-methods" role="group">
        {(
          [
            ["close", "Fechar caixa"],
            ["supply", "Suprimento"],
            ["withdraw", "Sangria"],
          ] as const
        ).map(([value, label]) => (
          <button className={kind === value ? "is-active" : ""} key={value} onClick={() => setKind(value)} type="button">{label}</button>
        ))}
      </div>

      <form className="cash-form" onSubmit={submit}>
        <div className="cash-received">
          <small>{kind === "close" ? "Dinheiro contado na gaveta" : "Valor"}</small>
          <strong>{formatBRL(countedCents)}</strong>
        </div>
        <CashCounter key={kind} onChange={setCounted} value={counted} />
        {kind === "close" && difference !== null ? (
          <p className={difference === 0 ? "pos-change" : "form-error"}>
            {difference === 0 ? "Confere com o esperado." : difference > 0 ? `Sobra de ${formatBRL(difference)}` : `Falta de ${formatBRL(-difference)}`}
          </p>
        ) : null}
        {kind === "close" ? (
          <input maxLength={300} name="notes" placeholder={difference ? "Explique a diferença *" : "Observação (opcional)"} required={Boolean(difference)} />
        ) : (
          <input maxLength={120} name="reason" placeholder="Motivo *" required />
        )}
        <button className={`primary-button primary-button-full ${kind === "close" ? "" : ""}`} disabled={busy || (kind !== "close" && countedCents === 0)} type="submit">
          {kind === "close" ? "Fechar caixa" : kind === "supply" ? "Registrar suprimento" : "Registrar sangria"}
        </button>
      </form>

      <h3>Movimentações</h3>
      <ul className="cash-moves">
        {session.movements.length === 0 ? <li className="pos-empty">Nenhuma movimentação ainda.</li> : null}
        {session.movements.map((item) => (
          <li key={item.id}>
            <span className={`move-tag move-${item.type.toLowerCase()}`}>{movementLabels[item.type]}</span>
            <div>
              <strong>{item.reason} {codeOf(item.orderId) ? `#${codeOf(item.orderId)}` : ""}</strong>
              <small>{formatDateTime(item.createdAt)} · {methodLabels[item.method]} · {item.operatorName.split("@")[0]}</small>
            </div>
            <b className={item.type === "Withdrawal" || item.type === "SaleReversal" ? "is-negative" : ""}>{formatBRL(item.amountInCents)}</b>
            {canManage && item.type === "Sale" && item.orderId && !reversed.has(item.orderId) ? (
              <button className="link-button danger" disabled={busy} onClick={() => void cancelSale(item.orderId!)} type="button">Cancelar</button>
            ) : <span />}
          </li>
        ))}
      </ul>
    </div>
  );
}

function History({ sessions }: { sessions: CashSession[] }) {
  return (
    <ul className="cash-moves">
      {sessions.length === 0 ? <li className="pos-empty">Nenhum caixa fechado ainda.</li> : null}
      {sessions.map((session) => (
        <li key={session.id}>
          <span className={`move-tag ${session.differenceInCents ? "move-withdrawal" : "move-sale"}`}>{session.differenceInCents ? "Diferença" : "Conferido"}</span>
          <div>
            <strong>{formatDateTime(session.openedAt)} → {session.closedAt ? formatDateTime(session.closedAt) : ""}</strong>
            <small>
              {session.openedBy.split("@")[0]} · esperado {formatBRL(session.expectedCashInCents)} · contado {formatBRL(session.countedCashInCents ?? 0)}
              {session.closingNotes ? ` · ${session.closingNotes}` : ""}
            </small>
          </div>
          <b className={session.differenceInCents ? "is-negative" : ""}>{session.differenceInCents ? formatBRL(session.differenceInCents) : "OK"}</b>
          <span />
        </li>
      ))}
    </ul>
  );
}

/* ------------------------------------------------------------------------------------------------ */

/** Sale receipt (not a fiscal note): shown after the sale, printable on any printer. */
function Receipt({ order, storeName, onClose }: { order: AdminOrder; storeName: string; onClose: () => void }) {
  useEffect(() => {
    const handler = (event: KeyboardEvent) => event.key === "Escape" && onClose();
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onClose]);

  return (
    <div className="modal-backdrop receipt-backdrop" role="presentation">
      <section className="modal receipt-modal" role="dialog">
        <div className="receipt" id="receipt">
          <strong className="receipt-store">{storeName}</strong>
          <small>Comprovante de venda · sem valor fiscal</small>
          <small>#{order.code} · {formatDateTime(order.createdAt)}</small>
          {order.tableLabel ? <small>Mesa {order.tableLabel}</small> : null}
          <hr />
          {order.items.map((item) => (
            <div className="receipt-line" key={item.id}>
              <span>{item.quantity}× {item.name}</span>
              <span>{formatBRL(item.totalInCents)}</span>
            </div>
          ))}
          <hr />
          {order.discountInCents ? <div className="receipt-line"><span>Desconto</span><span>− {formatBRL(order.discountInCents)}</span></div> : null}
          {order.deliveryFeeInCents ? <div className="receipt-line"><span>Entrega</span><span>{formatBRL(order.deliveryFeeInCents)}</span></div> : null}
          <div className="receipt-line receipt-total"><span>Total</span><span>{formatBRL(order.totalInCents)}</span></div>
          <small>Pagamento: {methodLabels[order.paymentMethod]}</small>
          <small>Obrigado pela preferência!</small>
        </div>
        <div className="receipt-actions">
          <button className="secondary-button" onClick={() => window.print()} type="button">Imprimir comprovante</button>
          <button className="primary-button" onClick={onClose} type="button">Nova venda (Esc)</button>
        </div>
      </section>
    </div>
  );
}
