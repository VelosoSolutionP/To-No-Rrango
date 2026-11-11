"use client";

import { useEffect, useMemo, useState } from "react";
import { formatBRL, formatDateTime } from "@/lib/admin-api";
import { prepLabels, type AdminOrder, type OrderStatus } from "@/lib/types";
import type { CourierContact } from "./AdminOrders";

export type TrackResult = { token: string; sentToCourier: boolean };

/** Where the order came from. Each origin has its own color across the panel. */
export type Origin = "whatsapp" | "menu" | "table" | "waiter" | "counter";

export const originOf = (order: AdminOrder): Origin =>
  order.channel === "WhatsApp"
    ? "whatsapp"
    : order.channel === "Pdv"
      ? "counter"
      : order.channel === "Waiter"
        ? "waiter"
        : order.channel === "Table" || (order.tableLabel && order.channel === "Web")
          ? "table"
          : "menu";

export const originLabels: Record<Origin, string> = {
  whatsapp: "WhatsApp",
  menu: "Cardápio",
  table: "Mesa QR",
  waiter: "Garçom",
  counter: "Balcão",
};

const columns: Array<{ id: string; title: string; statuses: OrderStatus[] }> = [
  { id: "new", title: "Novos", statuses: ["Pending"] },
  { id: "kitchen", title: "Em preparo", statuses: ["Confirmed", "Preparing"] },
  { id: "ready", title: "Prontos", statuses: ["Ready"] },
  { id: "route", title: "Em rota", statuses: ["OutForDelivery"] },
  { id: "done", title: "Finalizados", statuses: ["Delivered", "Cancelled"] },
];

const statusLabels: Record<OrderStatus, string> = {
  Pending: "Novo",
  Confirmed: "Confirmado",
  Preparing: "Em preparo",
  Ready: "Pronto",
  OutForDelivery: "Em rota",
  Delivered: "Entregue",
  Cancelled: "Cancelado",
};

const paymentLabels = { Cash: "Dinheiro", Pix: "Pix", CardOnDelivery: "Cartão", Online: "Online" } as const;

const LATE_MINUTES = 25;

function nextStatus(order: AdminOrder): { status: OrderStatus; label: string } | null {
  switch (order.status) {
    case "Pending":
      return { status: "Confirmed", label: "Aceitar" };
    case "Confirmed":
      return { status: "Preparing", label: "Preparar" };
    case "Preparing":
      return { status: "Ready", label: "Pronto" };
    case "Ready":
      return order.fulfillmentType === "Delivery" ? { status: "OutForDelivery", label: "Despachar" } : { status: "Delivered", label: "Entregar" };
    case "OutForDelivery":
      return { status: "Delivered", label: "Entregue" };
    default:
      return null;
  }
}

function minutesSince(value: string, now: number) {
  return Math.max(0, Math.floor((now - new Date(value).getTime()) / 60000));
}

function since(minutes: number) {
  return minutes < 60 ? `${minutes} min` : `${Math.floor(minutes / 60)} h ${minutes % 60} min`;
}

function where(order: AdminOrder) {
  if (order.tableLabel) {
    return `Mesa ${order.tableLabel}`;
  }

  return order.fulfillmentType === "Delivery" ? "Entrega" : order.fulfillmentType === "Pickup" ? "Retirada" : "No local";
}

export function OrdersBoard({
  orders,
  error,
  onUpdateStatus,
  onMarkPaid,
  onRefund,
  onTrack,
  onLoadCouriers,
  onOpenMap,
}: {
  orders: AdminOrder[];
  error: string | null;
  onUpdateStatus: (orderId: string, status: OrderStatus, reason: string | null) => Promise<void>;
  onMarkPaid?: (orderId: string) => Promise<void>;
  onRefund?: (orderId: string) => Promise<void>;
  onTrack?: (orderId: string, courierId?: string) => Promise<TrackResult>;
  onLoadCouriers?: () => Promise<CourierContact[]>;
  onOpenMap?: () => void;
}) {
  const [now, setNow] = useState(() => Date.now());
  const [origin, setOrigin] = useState<"all" | Origin>("all");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [couriers, setCouriers] = useState<CourierContact[] | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 30000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    if (selectedId && onLoadCouriers && couriers === null) {
      onLoadCouriers().then(setCouriers).catch(() => setCouriers([]));
    }
  }, [couriers, onLoadCouriers, selectedId]);

  const today = useMemo(() => new Date(now).toDateString(), [now]);

  // Finished orders only matter for the day; open ones stay until they are done.
  const visible = useMemo(() => {
    const query = search.trim().toLowerCase();
    return orders.filter((order) => {
      const done = order.status === "Delivered" || order.status === "Cancelled";
      if (done && new Date(order.updatedAt ?? order.createdAt).toDateString() !== today) {
        return false;
      }
      if (origin !== "all" && originOf(order) !== origin) {
        return false;
      }
      return !query || `${order.code} ${order.customerName} ${order.tableLabel ?? ""} ${order.waiterName ?? ""}`.toLowerCase().includes(query);
    });
  }, [orders, origin, search, today]);

  const counts = useMemo(() => {
    const result: Record<string, number> = { all: 0 };
    for (const order of orders) {
      if (order.status === "Delivered" || order.status === "Cancelled") {
        continue;
      }
      result.all++;
      result[originOf(order)] = (result[originOf(order)] ?? 0) + 1;
    }
    return result;
  }, [orders]);

  const kpis = useMemo(() => {
    const todays = orders.filter((order) => new Date(order.createdAt).toDateString() === today && order.status !== "Cancelled");
    const open = orders.filter((order) => !["Delivered", "Cancelled"].includes(order.status));
    const revenue = todays.reduce((sum, order) => sum + order.totalInCents, 0);
    const late = open.filter((order) => !order.scheduledFor && minutesSince(order.confirmedAt ?? order.createdAt, now) > LATE_MINUTES && order.status !== "OutForDelivery").length;
    const toReceive = open.filter((order) => order.paymentStatus === "Pending").reduce((sum, order) => sum + order.totalInCents, 0);
    return { open: open.length, revenue, ticket: todays.length ? Math.round(revenue / todays.length) : 0, late, toReceive, count: todays.length };
  }, [now, orders, today]);

  const selected = orders.find((order) => order.id === selectedId) ?? null;

  async function run(id: string, action: () => Promise<unknown>) {
    setBusy(id);
    setLocalError(null);
    try {
      await action();
    } catch (failure) {
      setLocalError(failure instanceof Error ? failure.message : "Não foi possível concluir.");
    } finally {
      setBusy(null);
    }
  }

  async function advance(order: AdminOrder) {
    const next = nextStatus(order);
    if (next) {
      await run(order.id, () => onUpdateStatus(order.id, next.status, null));
    }
  }

  async function cancel(order: AdminOrder) {
    const reason = window.prompt("Motivo do cancelamento:");
    if (reason?.trim()) {
      await run(order.id, () => onUpdateStatus(order.id, "Cancelled", reason.trim()));
    }
  }

  async function sendToCourier(order: AdminOrder, courier: CourierContact) {
    if (!onTrack) {
      return;
    }
    // Opened inside the click: after waiting for the API the browser would block the window.
    const fallback = window.open("", "_blank");
    await run(order.id, async () => {
      const result = await onTrack(order.id, courier.id);
      const url = `${window.location.origin}/entrega/${result.token}`;
      if (result.sentToCourier) {
        fallback?.close();
        setNotice(`Link enviado pelo WhatsApp da loja para ${courier.name}.`);
        return;
      }
      const target = `https://wa.me/${courier.phone}?text=${encodeURIComponent(`Entrega do pedido ${order.code.slice(-8)}. Abra para iniciar: ${url}`)}`;
      if (fallback) {
        fallback.opener = null;
        fallback.location.href = target;
      } else {
        window.location.href = target;
      }
      setNotice(`WhatsApp da loja desconectado: abri a conversa com ${courier.name} para você enviar o link.`);
    });
  }

  return (
    <section className="board">
      <div className="kpi-strip">
        <div className="kpi"><small>Em andamento</small><strong>{kpis.open}</strong></div>
        <div className="kpi"><small>Pedidos hoje</small><strong>{kpis.count}</strong></div>
        <div className="kpi"><small>Faturado hoje</small><strong>{formatBRL(kpis.revenue)}</strong></div>
        <div className="kpi"><small>Ticket médio</small><strong>{formatBRL(kpis.ticket)}</strong></div>
        <div className="kpi"><small>A receber</small><strong>{formatBRL(kpis.toReceive)}</strong></div>
        <div className={`kpi ${kpis.late ? "is-alert" : ""}`}><small>Atrasados (+{LATE_MINUTES} min)</small><strong>{kpis.late}</strong></div>
      </div>

      <div className="board-toolbar">
        <div className="origin-filter" role="group" aria-label="Origem">
          {(["all", "whatsapp", "menu", "table", "waiter", "counter"] as const).map((value) => (
            <button
              className={`origin-chip ${value !== "all" ? `origin-${value}` : ""} ${origin === value ? "is-active" : ""}`}
              key={value}
              onClick={() => setOrigin(value)}
              type="button"
            >
              {value === "all" ? "Todos" : originLabels[value]}
              <b>{counts[value] ?? 0}</b>
            </button>
          ))}
        </div>
        <input className="board-search" onChange={(event) => setSearch(event.target.value)} placeholder="Buscar pedido, cliente, mesa, garçom" type="search" value={search} />
      </div>

      {error || localError ? <p className="form-error">{localError ?? error}</p> : null}
      {notice ? <p className="board-notice" onClick={() => setNotice(null)}>{notice}</p> : null}

      <div className="board-columns">
        {columns.map((column) => {
          const list = visible
            .filter((order) => column.statuses.includes(order.status))
            .sort((a, b) => (column.id === "done" ? b.updatedAt.localeCompare(a.updatedAt) : (a.scheduledFor ?? a.createdAt).localeCompare(b.scheduledFor ?? b.createdAt)));
          const total = list.reduce((sum, order) => sum + order.totalInCents, 0);

          return (
            <div className={`board-column column-${column.id}`} key={column.id}>
              <header>
                <span>{column.title}</span>
                <b>{list.length}</b>
                <small>{formatBRL(total)}</small>
              </header>
              <div className="board-list">
                {list.length === 0 ? <p className="board-empty">Nada aqui</p> : null}
                {list.map((order) => {
                  const kind = originOf(order);
                  const minutes = minutesSince(order.confirmedAt ?? order.createdAt, now);
                  const late = column.id !== "done" && column.id !== "route" && !order.scheduledFor && minutes > LATE_MINUTES;
                  const next = nextStatus(order);
                  const ready = order.items.filter((item) => item.prepStatus === "Ready" || item.prepStatus === "Delivered").length;

                  return (
                    <article
                      className={`ticket origin-${kind} ${late ? "is-late" : ""} ${selectedId === order.id ? "is-selected" : ""} ${order.status === "Cancelled" ? "is-cancelled" : ""}`}
                      key={order.id}
                      onClick={() => setSelectedId(order.id)}
                    >
                      <div className="ticket-top">
                        <span className={`origin-tag origin-${kind}`}>{originLabels[kind]}</span>
                        <span className="ticket-code">#{order.code.slice(-6)}</span>
                        <span className={`ticket-time ${late ? "is-late" : ""}`}>{order.scheduledFor && order.status === "Pending" ? `agendado ${new Date(order.scheduledFor).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}` : since(minutes)}</span>
                      </div>
                      <div className="ticket-who">
                        <strong>{order.tableLabel ? `Mesa ${order.tableLabel}` : order.customerName}</strong>
                        <span>{order.tableLabel ? (order.waiterName ? `Garçom ${order.waiterName}` : "Pedido pelo QR") : where(order)}</span>
                      </div>
                      <ul className="ticket-items">
                        {order.items.slice(0, 3).map((item) => (
                          <li key={item.id}>
                            <b>{item.quantity}×</b> {item.name}
                          </li>
                        ))}
                        {order.items.length > 3 ? <li className="muted">+{order.items.length - 3} itens</li> : null}
                      </ul>
                      <div className="ticket-foot">
                        <strong>{formatBRL(order.totalInCents)}</strong>
                        <span className={`pay-tag ${order.paymentStatus === "Paid" ? "is-paid" : order.paymentStatus === "Refunded" ? "is-refunded" : ""}`}>
                          {order.paymentStatus === "Paid" ? "Pago" : order.paymentStatus === "Refunded" ? "Estornado" : paymentLabels[order.paymentMethod]}
                        </span>
                      </div>
                      {column.id === "kitchen" && order.items.length > 0 ? (
                        <div className="ticket-progress" title={`${ready} de ${order.items.length} itens prontos`}>
                          <i style={{ width: `${(ready / order.items.length) * 100}%` }} />
                        </div>
                      ) : null}
                      {order.courierName && order.fulfillmentType === "Delivery" ? <small className="ticket-courier">🛵 {order.courierName}</small> : null}
                      {order.deliveryIssue ? <small className="ticket-issue">⚠ {order.deliveryIssue}</small> : null}
                      {next ? (
                        <button
                          className="ticket-action"
                          disabled={busy === order.id}
                          onClick={(event) => {
                            event.stopPropagation();
                            void advance(order);
                          }}
                          type="button"
                        >
                          {next.label} →
                        </button>
                      ) : null}
                    </article>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>

      {selected ? (
        <>
          <button aria-label="Fechar" className="drawer-backdrop" onClick={() => setSelectedId(null)} type="button" />
          <aside className="drawer" aria-label="Detalhe do pedido">
            <header className="drawer-head">
              <div>
                <span className={`origin-tag origin-${originOf(selected)}`}>{originLabels[originOf(selected)]}</span>
                <h2>#{selected.code.slice(-8)}</h2>
                <small>{formatDateTime(selected.createdAt)} · {statusLabels[selected.status]}</small>
              </div>
              <button className="drawer-close" onClick={() => setSelectedId(null)} type="button">×</button>
            </header>

            <dl className="drawer-facts">
              <div><dt>{selected.tableLabel ? "Mesa" : "Cliente"}</dt><dd>{selected.tableLabel ? `Mesa ${selected.tableLabel}` : selected.customerName}</dd></div>
              {selected.waiterName ? <div><dt>Garçom</dt><dd>{selected.waiterName}</dd></div> : null}
              {!selected.tableLabel ? <div><dt>Telefone</dt><dd>{selected.customerPhone}</dd></div> : null}
              <div><dt>Recebimento</dt><dd>{where(selected)}</dd></div>
              <div><dt>Pagamento</dt><dd>{paymentLabels[selected.paymentMethod]} · {selected.paymentStatus === "Paid" ? "pago" : selected.paymentStatus === "Refunded" ? "estornado" : "a receber"}</dd></div>
              {selected.scheduledFor ? <div><dt>Agendado</dt><dd>{formatDateTime(selected.scheduledFor)}</dd></div> : null}
              {selected.courierName ? <div><dt>Entregador</dt><dd>{selected.courierName}</dd></div> : null}
              {selected.deliveryIssue ? <div><dt>Problema na entrega</dt><dd className="issue-text">{selected.deliveryIssue}{selected.deliveryIssueAt ? ` · ${formatDateTime(selected.deliveryIssueAt)}` : ""}</dd></div> : null}
            </dl>

            {selected.deliveryAddress ? (
              <p className="drawer-address">
                {selected.deliveryAddress.street}, {selected.deliveryAddress.number}
                {selected.deliveryAddress.complement ? ` · ${selected.deliveryAddress.complement}` : ""} — {selected.deliveryAddress.district}
                {selected.deliveryReference ? <><br />Ref.: {selected.deliveryReference}</> : null}
              </p>
            ) : null}

            <ul className="drawer-items">
              {selected.items.map((item) => (
                <li key={item.id}>
                  <div>
                    <strong>{item.quantity}× {item.name}</strong>
                    {item.options.length > 0 ? <small>{item.options.map((option) => option.name).join(", ")}</small> : null}
                    {item.notes ? <small>“{item.notes}”</small> : null}
                    {item.prepStatus && selected.status !== "Pending" && selected.status !== "Cancelled" ? (
                      <span className={`prep-tag prep-${item.prepStatus.toLowerCase()}`}>{prepLabels[item.station ?? "Kitchen"][item.prepStatus] ?? item.prepStatus}</span>
                    ) : null}
                  </div>
                  <span>{formatBRL(item.totalInCents)}</span>
                </li>
              ))}
            </ul>
            {selected.notes ? <p className="drawer-note">Obs.: {selected.notes}</p> : null}

            <div className="drawer-total">
              {selected.discountInCents ? <span>Desconto − {formatBRL(selected.discountInCents)}</span> : null}
              {selected.deliveryFeeInCents ? <span>Entrega {formatBRL(selected.deliveryFeeInCents)}</span> : null}
              <strong>{formatBRL(selected.totalInCents)}</strong>
            </div>

            <div className="drawer-actions">
              {nextStatus(selected) ? (
                <button className="primary-button" disabled={busy === selected.id} onClick={() => void advance(selected)} type="button">
                  {nextStatus(selected)!.label}
                </button>
              ) : null}
              {onMarkPaid && selected.paymentStatus === "Pending" && selected.status !== "Cancelled" ? (
                <button className="secondary-button" disabled={busy === selected.id} onClick={() => void run(selected.id, () => onMarkPaid(selected.id))} type="button">
                  Marcar como pago
                </button>
              ) : null}
              {["Pending", "Confirmed", "Preparing", "Ready"].includes(selected.status) ? (
                <button className="link-button danger" disabled={busy === selected.id} onClick={() => void cancel(selected)} type="button">
                  Cancelar pedido
                </button>
              ) : null}
              {onRefund && selected.paymentStatus === "Paid" && selected.paymentMethod === "Online" ? (
                <button className="link-button" disabled={busy === selected.id} onClick={() => window.confirm("Estornar o pagamento online?") && void run(selected.id, () => onRefund(selected.id))} type="button">
                  Estornar
                </button>
              ) : null}
            </div>

            {onTrack && selected.fulfillmentType === "Delivery" && !["Delivered", "Cancelled"].includes(selected.status) ? (
              <section className="drawer-courier">
                <h3>Entregador</h3>
                <p className="muted">O link com o código e o mapa vai pelo WhatsApp da loja para o entregador.</p>
                <div className="drawer-couriers">
                  {(couriers ?? []).filter((courier) => courier.isActive).map((courier) => (
                    <button
                      className={`courier-chip ${selected.courierName === courier.name ? "is-active" : ""}`}
                      disabled={busy === selected.id}
                      key={courier.id}
                      onClick={() => void sendToCourier(selected, courier)}
                      type="button"
                    >
                      🛵 {courier.name}
                    </button>
                  ))}
                  {couriers && couriers.filter((courier) => courier.isActive).length === 0 ? <span className="muted">Cadastre entregadores em Distribuição.</span> : null}
                </div>
                {onOpenMap ? <button className="link-button" onClick={onOpenMap} type="button">Ver no mapa</button> : null}
              </section>
            ) : null}

            <ol className="drawer-history">
              {selected.statusHistory.map((entry, index) => (
                <li key={index}>
                  <b>{statusLabels[entry.toStatus]}</b> · {formatDateTime(entry.changedAt)}
                  {entry.changedBy ? ` · ${entry.changedBy}` : ""}
                  {entry.reason ? ` · ${entry.reason}` : ""}
                </li>
              ))}
            </ol>
          </aside>
        </>
      ) : null}
    </section>
  );
}
