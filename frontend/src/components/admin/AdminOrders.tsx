"use client";

import { useMemo, useState } from "react";
import { formatBRL, formatDateTime } from "@/lib/admin-api";
import type { AdminOrder, OrderStatus } from "@/lib/types";

interface AdminOrdersProps {
  orders: AdminOrder[];
  loading: boolean;
  error: string | null;
  onRefresh: () => Promise<void>;
  onUpdateStatus: (orderId: string, status: OrderStatus, reason: string | null) => Promise<void>;
  onMarkPaid?: (orderId: string) => Promise<void>;
  onRefund?: (orderId: string) => Promise<void>;
}

const paymentLabels: Record<AdminOrder["paymentMethod"], string> = {
  Cash: "Dinheiro",
  Pix: "Pix",
  CardOnDelivery: "Cartão na entrega",
  Online: "Cartão online",
};

const paymentStatusLabels: Record<AdminOrder["paymentStatus"], string> = {
  Pending: "A receber",
  Paid: "Pago",
  Refunded: "Estornado",
};

const statusLabels: Record<OrderStatus, string> = {
  Pending: "Novo",
  Confirmed: "Confirmado",
  Preparing: "Em preparo",
  Ready: "Pronto",
  OutForDelivery: "Em entrega",
  Delivered: "Entregue",
  Cancelled: "Cancelado",
};

const fulfillmentLabels = {
  Pickup: "Retirada",
  Delivery: "Entrega",
  DineIn: "Mesa",
} as const;

const filters: Array<{ value: "all" | OrderStatus; label: string }> = [
  { value: "all", label: "Todos" },
  { value: "Pending", label: "Novos" },
  { value: "Confirmed", label: "Confirmados" },
  { value: "Preparing", label: "Em preparo" },
  { value: "Ready", label: "Prontos" },
  { value: "OutForDelivery", label: "Em entrega" },
  { value: "Delivered", label: "Entregues" },
  { value: "Cancelled", label: "Cancelados" },
];

function nextStatuses(order: AdminOrder): OrderStatus[] {
  switch (order.status) {
    case "Pending":
      return ["Confirmed"];
    case "Confirmed":
      return ["Preparing"];
    case "Preparing":
      return ["Ready"];
    case "Ready":
      return [order.fulfillmentType === "Delivery" ? "OutForDelivery" : "Delivered"];
    case "OutForDelivery":
      return ["Delivered"];
    default:
      return [];
  }
}

export function AdminOrders({
  orders,
  loading,
  error,
  onRefresh,
  onUpdateStatus,
  onMarkPaid,
  onRefund,
}: AdminOrdersProps) {
  const [filter, setFilter] = useState<"all" | OrderStatus>("all");
  const [busyId, setBusyId] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const filtered = useMemo(
    () => (filter === "all" ? orders : orders.filter((order) => order.status === filter)),
    [filter, orders],
  );

  async function advance(order: AdminOrder, status: OrderStatus) {
    let reason: string | null = null;

    if (status === "Cancelled") {
      const answer = window.prompt(`Motivo do cancelamento de ${order.code}:`);
      if (!answer?.trim()) {
        return;
      }
      reason = answer.trim();
    }

    setBusyId(order.id);
    try {
      await onUpdateStatus(order.id, status, reason);
    } catch {
      // O erro é exibido pela prop error.
    } finally {
      setBusyId(null);
    }
  }

  async function runPayment(order: AdminOrder, action: (orderId: string) => Promise<void>) {
    setBusyId(order.id);
    try {
      await action(order.id);
    } catch {
      // O erro é exibido pela prop error.
    } finally {
      setBusyId(null);
    }
  }

  const selected = filtered.find((order) => order.id === selectedId) ?? filtered[0] ?? null;
  const isBusy = selected !== null && busyId === selected.id;
  const canCancel = selected !== null && selected.status !== "Delivered" && selected.status !== "Cancelled";

  return (
    <section className="admin-section">
      <div className="admin-section-heading">
        <div>
          <h2>Pedidos</h2>
          <p className="muted">Atualiza sozinho a cada 5 segundos.</p>
        </div>
        <button className="secondary-button secondary-button-small" onClick={onRefresh} type="button">Atualizar</button>
      </div>

      <div className="admin-filter-row">
        {filters.map((item) => (
          <button className={`admin-filter ${filter === item.value ? "is-active" : ""}`} key={item.value} onClick={() => setFilter(item.value)} type="button">
            {item.label}
          </button>
        ))}
      </div>

      {error ? <p className="form-error">{error}</p> : null}

      {loading && orders.length === 0 ? (
        <div className="admin-empty card"><p className="muted">Carregando pedidos...</p></div>
      ) : filtered.length === 0 || !selected ? (
        <div className="admin-empty card"><p className="muted">Nenhum pedido neste filtro.</p></div>
      ) : (
        <div className="vx-orders">
          <div className="card vx-order-list">
            {filtered.map((order) => (
              <button className={`vx-order-row ${order.id === selected.id ? "is-active" : ""}`} key={order.id} onClick={() => setSelectedId(order.id)} type="button">
                <strong>{order.code.slice(-8)}</strong>
                <strong>{formatBRL(order.totalInCents)}</strong>
                <small>{order.customerName} · {fulfillmentLabels[order.fulfillmentType]}</small>
                <small>{statusLabels[order.status]}</small>
              </button>
            ))}
          </div>

          <article className="card vx-order-detail">
            <header>
              <div>
                <h3>{selected.code}</h3>
                <small className="muted">{formatDateTime(selected.createdAt)}</small>
              </div>
              <span className={`status-badge status-${selected.status.toLowerCase()}`}>{statusLabels[selected.status]}</span>
            </header>

            <dl className="vx-facts">
              <div><dt>Cliente</dt><dd>{selected.customerName}</dd></div>
              <div><dt>Telefone</dt><dd>{selected.customerPhone}</dd></div>
              <div><dt>Recebimento</dt><dd>{fulfillmentLabels[selected.fulfillmentType]}</dd></div>
              <div><dt>Pagamento</dt><dd>{paymentLabels[selected.paymentMethod]} · {paymentStatusLabels[selected.paymentStatus]}</dd></div>
            </dl>

            <ul className="vx-lines">
              {selected.items.map((item) => (
                <li key={item.id}>
                  <span>
                    {item.quantity}× {item.name}
                    {item.options.length > 0 ? <small>{item.options.map((option) => `${option.groupName}: ${option.name}`).join(" · ")}</small> : null}
                    {item.notes ? <small>“{item.notes}”</small> : null}
                  </span>
                  <strong>{formatBRL(item.totalInCents)}</strong>
                </li>
              ))}
              {selected.deliveryFeeInCents > 0 ? <li><span>Entrega</span><strong>{formatBRL(selected.deliveryFeeInCents)}</strong></li> : null}
              <li><span>Total</span><strong>{formatBRL(selected.totalInCents)}</strong></li>
            </ul>

            {selected.deliveryAddress ? (
              <p className="admin-order-address">
                {selected.deliveryAddress.street}, {selected.deliveryAddress.number}
                {selected.deliveryAddress.complement ? ` · ${selected.deliveryAddress.complement}` : ""} — {selected.deliveryAddress.district}, {selected.deliveryAddress.city}/{selected.deliveryAddress.state}
              </p>
            ) : null}
            {selected.notes ? <p className="admin-order-note">Obs.: {selected.notes}</p> : null}

            <div className="vx-actions">
              {nextStatuses(selected).map((status) => (
                <button className="primary-button" disabled={isBusy} key={status} onClick={() => advance(selected, status)} type="button">
                  {isBusy ? "Atualizando..." : `Marcar como ${statusLabels[status].toLowerCase()}`}
                </button>
              ))}
              {selected.paymentStatus === "Pending" && selected.status !== "Cancelled" && onMarkPaid ? (
                <button className="secondary-button" disabled={isBusy} onClick={() => runPayment(selected, onMarkPaid)} type="button">Marcar como pago</button>
              ) : null}
              {selected.paymentStatus === "Paid" && onRefund ? (
                <button
                  className="link-button"
                  disabled={isBusy}
                  onClick={() => {
                    if (window.confirm(`Estornar o pagamento de ${selected.code}?`)) {
                      void runPayment(selected, onRefund);
                    }
                  }}
                  type="button"
                >
                  Estornar pagamento
                </button>
              ) : null}
              {canCancel ? <button className="link-button" disabled={isBusy} onClick={() => advance(selected, "Cancelled")} type="button">Cancelar pedido</button> : null}
            </div>

            <ul className="vx-history">
              {selected.statusHistory.map((entry) => (
                <li key={`${entry.toStatus}-${entry.changedAt}`}>
                  {formatDateTime(entry.changedAt)} · {statusLabels[entry.toStatus]}{entry.changedBy ? ` · ${entry.changedBy}` : ""}{entry.reason ? ` · ${entry.reason}` : ""}
                </li>
              ))}
            </ul>
          </article>
        </div>
      )}
    </section>
  );
}
