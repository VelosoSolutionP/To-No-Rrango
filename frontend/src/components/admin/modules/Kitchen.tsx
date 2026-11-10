"use client";

import { useEffect, useMemo, useState } from "react";
import { prepLabels, type AdminOrder, type ItemPrepStatus, type OrderItem, type OrderStatus, type PrepStation } from "@/lib/types";
import { originLabels, originOf } from "../OrdersBoard";

const columns: Array<{ id: string; status: OrderStatus; title: string; action: string; next: (order: AdminOrder) => OrderStatus }> = [
  { id: "new", status: "Pending", title: "Novos", action: "Aceitar", next: () => "Confirmed" },
  { id: "queue", status: "Confirmed", title: "Na fila", action: "Começar", next: () => "Preparing" },
  { id: "kitchen", status: "Preparing", title: "Em preparo", action: "Tudo pronto", next: () => "Ready" },
  { id: "ready", status: "Ready", title: "Prontos", action: "Saiu", next: (order) => (order.fulfillmentType === "Delivery" ? "OutForDelivery" : "Delivered") },
];

/** Minutes after which a waiting order turns yellow, then red. */
const WARN_MINUTES = 15;
const LATE_MINUTES = 25;

function nextStep(item: OrderItem): ItemPrepStatus | null {
  const steps: ItemPrepStatus[] = item.station === "Bar" ? ["Received", "Preparing", "Ready"] : ["Received", "Queued", "Preparing", "Finishing", "Ready"];
  const index = steps.indexOf(item.prepStatus ?? "Received");
  const next = index < 0 ? null : steps[index + 1] ?? null;
  return next === "Queued" ? "Preparing" : next;
}

function clock(minutes: number) {
  return minutes < 60 ? `${minutes} min` : `${Math.floor(minutes / 60)}h${String(minutes % 60).padStart(2, "0")}`;
}

export function Kitchen({
  orders,
  error,
  onAdvance,
  onAdvanceItem,
}: {
  orders: AdminOrder[];
  error: string | null;
  onAdvance: (orderId: string, status: OrderStatus, reason: string | null) => Promise<void>;
  onAdvanceItem?: (orderId: string, itemId: string, status: ItemPrepStatus) => Promise<void>;
}) {
  const [busyId, setBusyId] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [station, setStation] = useState<"all" | PrepStation>("all");
  const [tv, setTv] = useState(false);

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 20000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    document.body.classList.toggle("kitchen-tv", tv);
    return () => document.body.classList.remove("kitchen-tv");
  }, [tv]);

  async function run(id: string, action: () => Promise<void>) {
    setBusyId(id);
    try {
      await action();
    } catch {
      // A mensagem aparece pela prop error.
    } finally {
      setBusyId(null);
    }
  }

  const ofStation = (item: OrderItem) => station === "all" || (item.station ?? "Kitchen") === station;
  const open = orders.filter((order) => columns.some((column) => column.status === order.status) && order.items.some(ofStation));
  const waited = (order: AdminOrder) => Math.max(0, Math.floor((now - new Date(order.confirmedAt ?? order.createdAt).getTime()) / 60000));

  const kpis = useMemo(() => {
    const active = open.filter((order) => order.status !== "Pending" && order.status !== "Ready");
    const times = active.map(waited);
    return {
      queue: open.filter((order) => order.status === "Confirmed").length,
      cooking: open.filter((order) => order.status === "Preparing").length,
      items: open.flatMap((order) => order.items.filter(ofStation)).filter((item) => (item.prepStatus ?? "Received") !== "Ready" && item.prepStatus !== "Delivered").reduce((sum, item) => sum + item.quantity, 0),
      average: times.length ? Math.round(times.reduce((a, b) => a + b, 0) / times.length) : 0,
      late: active.filter((order) => !order.scheduledFor && waited(order) > LATE_MINUTES).length,
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, now, station]);

  return (
    <section className={`kitchen2 ${tv ? "is-tv" : ""}`}>
      <div className="kitchen-head">
        <div className="kpi-strip">
          <div className="kpi"><small>Na fila</small><strong>{kpis.queue}</strong></div>
          <div className="kpi"><small>Em preparo</small><strong>{kpis.cooking}</strong></div>
          <div className="kpi"><small>Itens a fazer</small><strong>{kpis.items}</strong></div>
          <div className="kpi"><small>Espera média</small><strong>{clock(kpis.average)}</strong></div>
          <div className={`kpi ${kpis.late ? "is-alert" : ""}`}><small>Atrasados (+{LATE_MINUTES} min)</small><strong>{kpis.late}</strong></div>
        </div>
        <div className="kitchen-tools">
          <div className="dt-filters" role="group" aria-label="Setor">
            {(["all", "Kitchen", "Bar"] as const).map((value) => (
              <button className={station === value ? "is-active" : ""} key={value} onClick={() => setStation(value)} type="button">
                {value === "all" ? "Todos os setores" : value === "Kitchen" ? "🔥 Cozinha" : "🥤 Bar"}
              </button>
            ))}
          </div>
          <button className="secondary-button" onClick={() => setTv((value) => !value)} type="button">{tv ? "Sair da tela cheia" : "Tela cheia (TV)"}</button>
        </div>
      </div>
      {error ? <p className="form-error">{error}</p> : null}

      <div className="board-columns kitchen-columns">
        {columns.map((column) => {
          const list = open
            .filter((order) => order.status === column.status)
            .sort((a, b) => (a.scheduledFor ?? a.confirmedAt ?? a.createdAt).localeCompare(b.scheduledFor ?? b.confirmedAt ?? b.createdAt));

          return (
            <div className={`board-column column-${column.id}`} key={column.id}>
              <header>
                <span>{column.title}</span>
                <b>{list.length}</b>
              </header>
              <div className="board-list">
                {list.length === 0 ? <p className="board-empty">Nada aqui</p> : null}
                {list.map((order) => {
                  const kind = originOf(order);
                  const minutes = waited(order);
                  const heat = order.scheduledFor ? "" : minutes > LATE_MINUTES ? "is-late" : minutes > WARN_MINUTES ? "is-warm" : "";
                  const items = order.items.filter(ofStation);
                  const done = items.filter((item) => item.prepStatus === "Ready" || item.prepStatus === "Delivered").length;
                  const canMove = onAdvanceItem && (order.status === "Confirmed" || order.status === "Preparing");

                  return (
                    <article className={`ticket kitchen-ticket origin-${kind} ${heat}`} key={order.id}>
                      <div className="ticket-top">
                        <span className={`origin-tag origin-${kind}`}>{originLabels[kind]}</span>
                        <span className="ticket-code">#{order.code.slice(-6)}</span>
                        <span className={`kitchen-timer ${heat}`}>{order.scheduledFor && order.status === "Pending" ? `⏰ ${new Date(order.scheduledFor).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}` : clock(minutes)}</span>
                      </div>
                      <div className="ticket-who">
                        <strong>{order.tableLabel ? `Mesa ${order.tableLabel}` : order.customerName}</strong>
                        <span>
                          {order.fulfillmentType === "Delivery" ? "🛵 Entrega" : order.tableLabel ? `🍽️ No salão${order.waiterName ? ` · ${order.waiterName}` : ""}` : "🛍️ Retirada"}
                        </span>
                      </div>
                      <ul className="kitchen-items">
                        {items.map((item) => {
                          const next = nextStep(item);
                          const state = (item.prepStatus ?? "Received").toLowerCase();
                          return (
                            <li key={item.id}>
                              <button
                                className={`kitchen-line prep-${state}`}
                                disabled={!canMove || !next || busyId === order.id}
                                onClick={() => next && onAdvanceItem && void run(order.id, () => onAdvanceItem(order.id, item.id, next))}
                                type="button"
                              >
                                <b className="kitchen-qty">{item.quantity}</b>
                                <span className="kitchen-name">
                                  {item.name}
                                  {item.options.length > 0 ? <small>{item.options.map((option) => option.name).join(" · ")}</small> : null}
                                  {item.notes ? <small className="kitchen-note">⚠ {item.notes}</small> : null}
                                </span>
                                <span className={`kitchen-step prep-${state}`}>
                                  {item.station === "Bar" ? "🥤 " : ""}
                                  {prepLabels[item.station ?? "Kitchen"][item.prepStatus ?? "Received"]}
                                </span>
                              </button>
                            </li>
                          );
                        })}
                      </ul>
                      {order.notes ? <p className="kitchen-order-note">📝 {order.notes}</p> : null}
                      {items.length > 0 && column.id === "kitchen" ? (
                        <div className="ticket-progress" title={`${done} de ${items.length} prontos`}>
                          <i style={{ width: `${(done / items.length) * 100}%` }} />
                        </div>
                      ) : null}
                      <button className="ticket-action" disabled={busyId === order.id} onClick={() => void run(order.id, () => onAdvance(order.id, column.next(order), null))} type="button">
                        {column.action} →
                      </button>
                    </article>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </section>
  );
}
