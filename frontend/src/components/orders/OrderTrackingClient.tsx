"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, formatBRL, formatDateTime } from "@/lib/api";
import { prepLabels, type OrderStatus, type OrderTracking } from "@/lib/types";
import { DeliveryMap } from "./DeliveryMap";
import { OrderPaymentPanel } from "./OrderPaymentPanel";
import { OrderRating } from "./OrderRating";
import { storeThemeStyle, useStoreTheme } from "@/lib/storeTheme";

interface OrderTrackingClientProps {
  code: string;
}

const statusLabels: Record<OrderStatus, string> = {
  Pending: "Pedido recebido",
  Confirmed: "Pedido confirmado",
  Preparing: "Em preparo",
  Ready: "Pronto",
  OutForDelivery: "Saiu para entrega",
  Delivered: "Entregue",
  Cancelled: "Cancelado",
};

const statusDescriptions: Record<OrderStatus, string> = {
  Pending: "Estamos aguardando a confirmação do estabelecimento.",
  Confirmed: "O estabelecimento confirmou seu pedido.",
  Preparing: "Sua cozinha já começou a preparar.",
  Ready: "Seu pedido está pronto.",
  OutForDelivery: "O entregador está a caminho.",
  Delivered: "Bom apetite!",
  Cancelled: "Este pedido foi cancelado.",
};

const pickupFlow: OrderStatus[] = ["Pending", "Confirmed", "Preparing", "Ready", "Delivered"];
const deliveryFlow: OrderStatus[] = [
  "Pending",
  "Confirmed",
  "Preparing",
  "Ready",
  "OutForDelivery",
  "Delivered",
];

export function OrderTrackingClient({ code }: OrderTrackingClientProps) {
  const [order, setOrder] = useState<OrderTracking | null>(null);
  const [error, setError] = useState<string | null>(null);
  useStoreTheme(order?.storePrimaryColor, order?.storeSecondaryColor);

  useEffect(() => {
    let active = true;

    async function load() {
      try {
        const data = await api.trackOrder(code);
        if (active) {
          setOrder(data);
          setError(null);
        }
      } catch (loadError) {
        if (active) {
          setError(loadError instanceof Error ? loadError.message : "Pedido não encontrado.");
        }
      }
    }

    load();
    const interval = window.setInterval(load, 30000);
    const events = new EventSource(api.orderEventsUrl(code));
    events.onmessage = (event) => {
      if (event.data === "update") {
        void load();
      }
    };

    return () => {
      active = false;
      window.clearInterval(interval);
      events.close();
    };
  }, [code]);

  const reload = useCallback(() => {
    api.trackOrder(code).then(setOrder).catch(() => undefined);
  }, [code]);

  const flow = useMemo(
    () => (order?.fulfillmentType === "Delivery" ? deliveryFlow : pickupFlow),
    [order?.fulfillmentType],
  );

  const currentIndex = order ? flow.indexOf(order.status) : -1;

  if (error && !order) {
    return (
      <main className="tracking-page pub-hud">
        <div className="empty-state card">
          <p className="eyebrow">Pedido</p>
          <h1>Não encontramos este pedido</h1>
          <p className="muted">{error}</p>
          <Link className="secondary-button" href="/">
            Voltar ao início
          </Link>
        </div>
      </main>
    );
  }

  if (!order) {
    return (
      <main className="tracking-page pub-hud">
        <div className="empty-state card">
          <span className="loading-dot" />
          <h2>Carregando pedido...</h2>
        </div>
      </main>
    );
  }

  return (
    <main className="tracking-page pub-hud" style={storeThemeStyle(order?.storePrimaryColor, order?.storeSecondaryColor)}>
      <header className="tracking-header">
        <Link className="tracking-back" href="/">
          ← To-No-Rango
        </Link>
        <span className={`status-badge status-${order.status.toLowerCase()}`}>
          {statusLabels[order.status]}
        </span>
      </header>

      <section className="tracking-hero">
        <p className="eyebrow">Acompanhamento em tempo real</p>
        <h1>{statusLabels[order.status]}</h1>
        <p className="muted">{statusDescriptions[order.status]}</p>
        <strong className="order-code">{order.code}</strong>
        <span className="tracking-updated">Atualizado em {formatDateTime(order.createdAt)}</span>
        {order.scheduledFor && order.status === "Pending" ? (
          <p className="tracking-scheduled">
            A loja estava fechada: seu pedido está agendado para {formatDateTime(order.scheduledFor)} e será confirmado quando ela abrir.
          </p>
        ) : null}
        {order.courierName && order.fulfillmentType === "Delivery" && order.status !== "Delivered" && order.status !== "Cancelled" ? (
          <p className="tracking-courier">Entregador: {order.courierName}</p>
        ) : null}
        {order.hasDeliveryIssue && order.status !== "Delivered" && order.status !== "Cancelled" ? (
          <p className="tracking-scheduled">O entregador teve um imprevisto. A loja já foi avisada e vai falar com você.</p>
        ) : null}
      </section>

      <OrderRating code={order.code} delivered={order.status === "Delivered"} />

      <OrderPaymentPanel onPaid={reload} order={order} />

      {order.fulfillmentType === "Delivery" && order.status !== "Cancelled" && order.status !== "Delivered" ? <DeliveryMap code={order.code} dispatched={order.status === "OutForDelivery"} /> : null}

      {order.status === "Cancelled" ? (
        <section className="tracking-card cancellation-card">
          <h2>Pedido cancelado</h2>
          <p>{order.cancelReason ?? "O estabelecimento cancelou este pedido."}</p>
        </section>
      ) : (
        <section className="tracking-card">
          <h2>Andamento</h2>
          <ol className="timeline">
            {flow.map((status, index) => {
              const done = index < currentIndex;
              const current = index === currentIndex;

              return (
                <li className={`${done ? "is-done" : ""} ${current ? "is-current" : ""}`} key={status}>
                  <span className="timeline-dot">{done ? "✓" : index + 1}</span>
                  <div>
                    <strong>{statusLabels[status]}</strong>
                    <small>{statusDescriptions[status]}</small>
                  </div>
                </li>
              );
            })}
          </ol>
        </section>
      )}

      <section className="tracking-card">
        <h2>Resumo do pedido</h2>
        <div className="tracking-items">
          {order.items.map((item) => (
            <article className="tracking-item" key={item.id}>
              <div>
                <strong>
                  {item.quantity}× {item.name}
                </strong>
                {item.options.length > 0 ? (
                  <p className="muted">
                    {item.options.map((option) => `${option.groupName}: ${option.name}`).join(" · ")}
                  </p>
                ) : null}
                {item.notes ? <p className="muted">“{item.notes}”</p> : null}
                {item.prepStatus && order.status !== "Cancelled" && order.status !== "Pending" ? (
                  <span className={`prep-chip prep-${item.prepStatus.toLowerCase()}`}>
                    {item.station === "Bar" ? "Bebida" : "Cozinha"} · {prepLabels[item.station ?? "Kitchen"][item.prepStatus] ?? item.prepStatus}
                  </span>
                ) : null}
              </div>
              <strong>{formatBRL(item.totalInCents)}</strong>
            </article>
          ))}
        </div>

        <div className="tracking-totals">
          <div className="summary-row">
            <span>Subtotal</span>
            <strong>{formatBRL(order.subtotalInCents)}</strong>
          </div>
          {order.discountInCents ? (
            <div className="summary-row">
              <span>Desconto</span>
              <strong>− {formatBRL(order.discountInCents)}</strong>
            </div>
          ) : null}
          {order.deliveryFeeInCents > 0 ? (
            <div className="summary-row">
              <span>Entrega</span>
              <strong>{formatBRL(order.deliveryFeeInCents)}</strong>
            </div>
          ) : null}
          <div className="summary-row summary-total">
            <span>Total</span>
            <strong>{formatBRL(order.totalInCents)}</strong>
          </div>
        </div>
      </section>

      <p className="tracking-note">
        Esta página atualiza automaticamente. Telefone do cliente:{" "}
        {order.customerPhoneMasked}.
      </p>
    </main>
  );
}
