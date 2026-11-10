"use client";

import { useEffect, useMemo, useState } from "react";
import { formatBRL, formatDateTime } from "@/lib/admin-api";
import type { AdminOrder, OrderStatus } from "@/lib/types";

export interface CourierContact {
  id: string;
  name: string;
  phone: string;
  isActive: boolean;
}

interface AdminOrdersProps {
  orders: AdminOrder[];
  loading: boolean;
  error: string | null;
  onRefresh: () => Promise<void>;
  onUpdateStatus: (orderId: string, status: OrderStatus, reason: string | null) => Promise<void>;
  onMarkPaid?: (orderId: string) => Promise<void>;
  onRefund?: (orderId: string) => Promise<void>;
  onTrack?: (orderId: string, courierId?: string) => Promise<string>;
  onLoadCouriers?: () => Promise<CourierContact[]>;
  onOpenMap?: () => void;
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
  onTrack,
  onLoadCouriers,
  onOpenMap,
}: AdminOrdersProps) {
  const [couriers, setCouriers] = useState<CourierContact[] | null>(null);
  const [couriersError, setCouriersError] = useState<string | null>(null);
  const [tracking, setTracking] = useState<{ orderId: string; url: string } | null>(null);
  const [trackingError, setTrackingError] = useState<string | null>(null);
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

  async function shareTracking(orderId: string) {
    if (!onTrack) {
      return;
    }

    setBusyId(orderId);
    setTrackingError(null);

    try {
      const token = await onTrack(orderId);
      const url = `${window.location.origin}/entrega/${token}`;
      setTracking({ orderId, url });
      void navigator.clipboard?.writeText(url).catch(() => undefined);
    } catch (trackingFailure) {
      setTracking(null);
      setTrackingError(trackingFailure instanceof Error ? trackingFailure.message : "Não foi possível gerar o link de rastreio.");
    } finally {
      setBusyId(null);
    }
  }

  async function openMap(orderId: string) {
    if (!onTrack || !onOpenMap) {
      return;
    }

    setBusyId(orderId);
    setTrackingError(null);

    try {
      // Garante que a entrega exista no mapa, mesmo antes de o link ir para o entregador.
      await onTrack(orderId);
      onOpenMap();
    } catch (trackingFailure) {
      setTrackingError(trackingFailure instanceof Error ? trackingFailure.message : "Não foi possível abrir o mapa.");
    } finally {
      setBusyId(null);
    }
  }

  async function sendToCourier(order: AdminOrder, courier: CourierContact) {
    if (!onTrack) {
      return;
    }

    // Aberta ainda dentro do clique: depois de esperar a API o navegador bloquearia a janela.
    const whatsApp = window.open("", "_blank");

    if (whatsApp) {
      whatsApp.opener = null;
    }

    setBusyId(order.id);
    setTrackingError(null);

    try {
      const token = await onTrack(order.id, courier.id);
      const url = `${window.location.origin}/entrega/${token}`;
      const target = `https://wa.me/${courier.phone}?text=${encodeURIComponent(`Entrega do pedido ${order.code}. Abra para iniciar: ${url}`)}`;
      setTracking({ orderId: order.id, url });

      if (whatsApp) {
        whatsApp.location.href = target;
      } else {
        window.location.href = target;
      }
    } catch (trackingFailure) {
      whatsApp?.close();
      setTrackingError(trackingFailure instanceof Error ? trackingFailure.message : "Não foi possível gerar o link de rastreio.");
    } finally {
      setBusyId(null);
    }
  }

  const selected = filtered.find((order) => order.id === selectedId) ?? filtered[0] ?? null;
  const selectedOrderId = selected?.id ?? null;
  const canTrackSelected = Boolean(onTrack && selected && selected.fulfillmentType === "Delivery" && selected.status !== "Cancelled" && selected.status !== "Delivered");

  useEffect(() => {
    if (!onLoadCouriers || !selectedOrderId) {
      return;
    }

    let alive = true;

    onLoadCouriers()
      .then((list) => {
        if (alive) {
          setCouriers(list);
          setCouriersError(null);
        }
      })
      .catch(() => {
        if (alive) {
          setCouriers(null);
          setCouriersError("Não foi possível carregar os entregadores. Atualize a página.");
        }
      });

    return () => {
      alive = false;
    };
    // Recarrega ao trocar de pedido; a função muda a cada render do painel.
  }, [selectedOrderId]); // eslint-disable-line react-hooks/exhaustive-deps
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
                <small>
                  {order.customerName} · {order.channel === "Pdv" ? "Balcão" : fulfillmentLabels[order.fulfillmentType]}
                  {order.tableLabel ? ` · Mesa ${order.tableLabel}` : ""}
                  {order.scheduledFor && order.status === "Pending" ? " · Agendado" : ""}
                </small>
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
              {selected.scheduledFor ? <div><dt>Agendado para</dt><dd>{new Date(selected.scheduledFor).toLocaleString("pt-BR", { weekday: "short", day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })}</dd></div> : null}
              {selected.tableLabel ? <div><dt>Mesa</dt><dd>{selected.tableLabel}</dd></div> : null}
              {selected.courierName ? <div><dt>Entregador</dt><dd>{selected.courierName}</dd></div> : null}
              {selected.deliveryReference ? <div><dt>Referência</dt><dd>{selected.deliveryReference}</dd></div> : null}
              {selected.discountInCents ? <div><dt>Desconto</dt><dd>{formatBRL(selected.discountInCents)} · {selected.discountReason}</dd></div> : null}
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

            {tracking?.orderId === selected.id ? (
              <p className="admin-order-note">Link do entregador (tela dele, sem mapa): <a href={tracking.url} rel="noreferrer" target="_blank">{tracking.url}</a></p>
            ) : null}
            {canTrackSelected && couriers ? (
              couriers.some((courier) => courier.isActive) ? (
                <div className="vx-actions">
                  {couriers.filter((courier) => courier.isActive).map((courier) => (
                    <button className="secondary-button" disabled={isBusy} key={courier.id} onClick={() => void sendToCourier(selected, courier)} type="button">
                      Enviar para {courier.name}
                    </button>
                  ))}
                </div>
              ) : (
                <p className="admin-order-note">Cadastre entregadores em Distribuição para enviar o link direto pelo WhatsApp.</p>
              )
            ) : null}
            {canTrackSelected && couriersError ? <p className="form-error">{couriersError}</p> : null}
            {trackingError ? <p className="form-error">{trackingError}</p> : null}

            <div className="vx-actions">
              {canTrackSelected ? (
                <button className="secondary-button" disabled={isBusy} onClick={() => void shareTracking(selected.id)} type="button">Copiar link do entregador</button>
              ) : null}
              {canTrackSelected && onOpenMap ? (
                <button className="secondary-button" disabled={isBusy} onClick={() => void openMap(selected.id)} type="button">Ver no mapa</button>
              ) : null}
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
