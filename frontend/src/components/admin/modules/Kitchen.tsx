"use client";

import { useEffect, useState } from "react";
import type { AdminOrder, OrderStatus } from "@/lib/types";
import { Empty, ModuleHeading } from "./shared";

const columns: Array<{ status: OrderStatus; title: string; action: string; next: (order: AdminOrder) => OrderStatus }> = [
  { status: "Pending", title: "Novos", action: "Confirmar", next: () => "Confirmed" },
  { status: "Confirmed", title: "Confirmados", action: "Iniciar preparo", next: () => "Preparing" },
  { status: "Preparing", title: "Em preparo", action: "Marcar pronto", next: () => "Ready" },
  {
    status: "Ready",
    title: "Prontos",
    action: "Despachar",
    next: (order) => (order.fulfillmentType === "Delivery" ? "OutForDelivery" : "Delivered"),
  },
];

const channel = { Pickup: "Retirada", Delivery: "Entrega", DineIn: "Mesa" } as const;

function elapsed(milliseconds: number) {
  const minutes = Math.max(0, Math.floor(milliseconds / 60000));

  if (minutes < 60) {
    return `${minutes} min`;
  }

  const hours = Math.floor(minutes / 60);

  return hours < 24 ? `${hours} h ${minutes % 60} min` : `${Math.floor(hours / 24)} d ${hours % 24} h`;
}

export function Kitchen({
  orders,
  error,
  onAdvance,
}: {
  orders: AdminOrder[];
  error: string | null;
  onAdvance: (orderId: string, status: OrderStatus, reason: string | null) => Promise<void>;
}) {
  const [busyId, setBusyId] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const interval = window.setInterval(() => setNow(Date.now()), 30000);
    return () => window.clearInterval(interval);
  }, []);

  async function advance(order: AdminOrder, status: OrderStatus) {
    setBusyId(order.id);
    try {
      await onAdvance(order.id, status, null);
    } catch {
      // O erro é exibido pela prop error.
    } finally {
      setBusyId(null);
    }
  }

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Operação" title="Cozinha" hint="A fila atualiza sozinha a cada poucos segundos." />
      {error ? <p className="form-error">{error}</p> : null}
      <div className="kitchen-board">
        {columns.map((column) => {
          const list = orders
            .filter((order) => order.status === column.status)
            .sort((a, b) => a.createdAt.localeCompare(b.createdAt));

          return (
            <div className="kitchen-column" key={column.status}>
              <h3>
                {column.title} <span className="pill">{list.length}</span>
              </h3>
              {list.length === 0 ? <Empty>Nada aqui.</Empty> : null}
              {list.map((order) => (
                <article className="card kitchen-card" key={order.id}>
                  <header>
                    <strong>{order.code.slice(-8)}</strong>
                    <span>{elapsed(now - new Date(order.createdAt).getTime())}</span>
                  </header>
                  <small>
                    {channel[order.fulfillmentType]} · {order.customerName}
                  </small>
                  <ul>
                    {order.items.map((item) => (
                      <li key={item.id}>
                        <strong>{item.quantity}×</strong> {item.name}
                        {item.options.length > 0 ? <small>{item.options.map((option) => option.name).join(", ")}</small> : null}
                        {item.notes ? <small>“{item.notes}”</small> : null}
                      </li>
                    ))}
                  </ul>
                  {order.notes ? <small>Obs.: {order.notes}</small> : null}
                  <button
                    className="primary-button primary-button-full"
                    disabled={busyId === order.id}
                    onClick={() => advance(order, column.next(order))}
                    type="button"
                  >
                    {column.action}
                  </button>
                </article>
              ))}
            </div>
          );
        })}
      </div>
    </section>
  );
}
