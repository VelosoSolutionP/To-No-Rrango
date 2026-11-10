"use client";

import { useEffect, useState } from "react";
import { prepLabels, type AdminOrder, type ItemPrepStatus, type OrderItem, type OrderStatus, type PrepStation } from "@/lib/types";
import { Empty, ModuleHeading } from "./shared";

const columns: Array<{ status: OrderStatus; title: string; action: string; next: (order: AdminOrder) => OrderStatus }> = [
  { status: "Pending", title: "Novos", action: "Confirmar", next: () => "Confirmed" },
  { status: "Confirmed", title: "Confirmados", action: "Iniciar preparo", next: () => "Preparing" },
  { status: "Preparing", title: "Em preparo", action: "Marcar tudo pronto", next: () => "Ready" },
  {
    status: "Ready",
    title: "Prontos",
    action: "Despachar",
    next: (order) => (order.fulfillmentType === "Delivery" ? "OutForDelivery" : "Delivered"),
  },
];

const channel = { Pickup: "Retirada", Delivery: "Entrega", DineIn: "Mesa" } as const;

/** Minutes after which a waiting order is shown as late. */
const LATE_MINUTES = 25;

/** The next step of an item at its station; null when it is done for the kitchen. */
function nextStep(item: OrderItem): ItemPrepStatus | null {
  const steps: ItemPrepStatus[] = item.station === "Bar" ? ["Received", "Preparing", "Ready"] : ["Received", "Queued", "Preparing", "Finishing", "Ready"];
  const index = steps.indexOf(item.prepStatus ?? "Received");
  const next = index < 0 ? null : steps[index + 1] ?? null;
  return next === "Queued" ? "Preparing" : next;
}

function elapsed(milliseconds: number) {
  const minutes = Math.max(0, Math.floor(milliseconds / 60000));

  if (minutes < 60) {
    return `${minutes} min`;
  }

  const hours = Math.floor(minutes / 60);

  return hours < 24 ? `${hours} h ${minutes % 60} min` : `${Math.floor(hours / 24)} d ${hours % 24} h`;
}

function formatTime(value: string) {
  return new Date(value).toLocaleString("pt-BR", { weekday: "short", hour: "2-digit", minute: "2-digit" });
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

  useEffect(() => {
    const interval = window.setInterval(() => setNow(Date.now()), 30000);
    return () => window.clearInterval(interval);
  }, []);

  async function run(id: string, action: () => Promise<void>) {
    setBusyId(id);
    try {
      await action();
    } catch {
      // O erro é exibido pela prop error.
    } finally {
      setBusyId(null);
    }
  }

  const ofStation = (item: OrderItem) => station === "all" || (item.station ?? "Kitchen") === station;

  return (
    <section className="admin-section kitchen">
      <ModuleHeading eyebrow="Operação" title="Cozinha" hint="Toque no item para avançar a etapa. O pedido fica pronto quando todos os itens ficam prontos.">
        <div className="admin-filter-row" role="group" aria-label="Setor">
          {(["all", "Kitchen", "Bar"] as const).map((value) => (
            <button className={`admin-filter ${station === value ? "is-active" : ""}`} key={value} onClick={() => setStation(value)} type="button">
              {value === "all" ? "Todos os setores" : value === "Kitchen" ? "Cozinha" : "Bar / bebidas"}
            </button>
          ))}
        </div>
      </ModuleHeading>
      {error ? <p className="form-error">{error}</p> : null}
      <div className="kitchen-board">
        {columns.map((column) => {
          const list = orders
            .filter((order) => order.status === column.status)
            .filter((order) => station === "all" || order.items.some(ofStation))
            .sort((a, b) => (a.scheduledFor ?? a.createdAt).localeCompare(b.scheduledFor ?? b.createdAt));

          return (
            <div className="kitchen-column" key={column.status}>
              <h3>
                {column.title} <span className="pill">{list.length}</span>
              </h3>
              {list.length === 0 ? <Empty>Nada aqui.</Empty> : null}
              {list.map((order) => {
                const since = order.confirmedAt ?? order.createdAt;
                const waited = now - new Date(since).getTime();
                const late = column.status !== "Ready" && waited > LATE_MINUTES * 60000 && !order.scheduledFor;
                const canMoveItems = onAdvanceItem && (order.status === "Confirmed" || order.status === "Preparing");

                return (
                  <article className={`card kitchen-card ${late ? "is-late" : ""}`} key={order.id}>
                    <header>
                      <strong>{order.tableLabel ? `Mesa ${order.tableLabel}` : order.code.slice(-8)}</strong>
                      <span>{late ? `⚠ ${elapsed(waited)}` : elapsed(waited)}</span>
                    </header>
                    <small>
                      {order.channel === "Pdv" ? "Balcão" : channel[order.fulfillmentType]} · {order.customerName}
                    </small>
                    {order.scheduledFor ? <small className="kitchen-scheduled">Agendado · {formatTime(order.scheduledFor)}</small> : null}
                    <ul>
                      {order.items.filter(ofStation).map((item) => {
                        const next = nextStep(item);
                        const label = prepLabels[item.station ?? "Kitchen"][item.prepStatus ?? "Received"];

                        return (
                          <li key={item.id}>
                            <button
                              className={`kitchen-item prep-${(item.prepStatus ?? "Received").toLowerCase()}`}
                              disabled={!canMoveItems || !next || busyId === order.id}
                              onClick={() => next && onAdvanceItem && void run(order.id, () => onAdvanceItem(order.id, item.id, next))}
                              type="button"
                            >
                              <span>
                                <strong>{item.quantity}×</strong> {item.name}
                                {item.station === "Bar" ? <em className="kitchen-station">bar</em> : null}
                              </span>
                              <span className="kitchen-item-step">
                                {label}
                                {canMoveItems && next ? ` → ${prepLabels[item.station ?? "Kitchen"][next]}` : ""}
                              </span>
                            </button>
                            {item.options.length > 0 ? <small>{item.options.map((option) => option.name).join(", ")}</small> : null}
                            {item.notes ? <small>“{item.notes}”</small> : null}
                          </li>
                        );
                      })}
                    </ul>
                    {order.notes ? <small>Obs.: {order.notes}</small> : null}
                    <button
                      className="primary-button primary-button-full"
                      disabled={busyId === order.id}
                      onClick={() => void run(order.id, () => onAdvance(order.id, column.next(order), null))}
                      type="button"
                    >
                      {column.action}
                    </button>
                  </article>
                );
              })}
            </div>
          );
        })}
      </div>
    </section>
  );
}
