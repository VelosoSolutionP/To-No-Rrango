"use client";

import { type FormEvent, useMemo, useState } from "react";
import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import type { AdminOrder } from "@/lib/types";
import { DataTable, FormDrawer, StatusChip } from "../DataTable";
import type { TrackResult } from "../OrdersBoard";
import { LiveDeliveries } from "./LiveDeliveries";
import { ErrorMessage, ModuleHeading, SubTabs, text, toCents, useAction, useLoad } from "./shared";

interface Courier {
  id: string;
  name: string;
  phone: string;
  isActive: boolean;
}

interface Zone {
  id: string;
  district: string;
  feeInCents: number;
  estimatedMinutes: number | null;
  isActive: boolean;
}

interface RatingSummary {
  couriers: Array<{ courierName: string; count: number; service: number | null; punctuality: number | null; care: number | null }>;
}

type View = "dispatch" | "map" | "couriers" | "zones";

function minutesBetween(from: string, to: string | null) {
  return to ? Math.max(0, Math.round((new Date(to).getTime() - new Date(from).getTime()) / 60000)) : null;
}

function formatPhone(value: string) {
  const digits = value.replace(/\D/g, "").replace(/^55(?=\d{10,11}$)/, "");
  return digits.length === 11 ? digits.replace(/^(\d{2})(\d{5})(\d{4})$/, "($1) $2-$3") : value;
}

export function Distribution({
  http,
  establishmentId,
  modules,
  orders,
  onTrack,
  onChanged,
}: {
  http: AdminHttp;
  establishmentId: string;
  modules: string[];
  orders: AdminOrder[];
  onTrack?: (orderId: string, courierId: string) => Promise<TrackResult>;
  onChanged: () => Promise<void>;
}) {
  const base = `/establishments/${establishmentId}`;
  const hasTracking = modules.includes("rastreio");
  const couriers = useLoad(() => (hasTracking ? http.get<Courier[]>(`${base}/couriers`) : Promise.resolve([])), [base, hasTracking]);
  const zones = useLoad(() => http.get<Zone[]>(`${base}/delivery-zones`), [base]);
  const ratings = useLoad(() => http.get<RatingSummary>(`${base}/ratings?days=30`).catch(() => ({ couriers: [] })), [base]);
  const action = useAction(async () => {
    await Promise.all([couriers.reload(), zones.reload(), onChanged()]);
  });
  const [view, setView] = useState<View>("dispatch");
  const [editingCourier, setEditingCourier] = useState<Courier | "new" | null>(null);
  const [editingZone, setEditingZone] = useState<Zone | "new" | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const today = new Date().toDateString();
  const deliveries = orders.filter((order) => order.fulfillmentType === "Delivery");
  const ready = deliveries.filter((order) => order.status === "Ready" || order.status === "Preparing" || order.status === "Confirmed");
  const onRoute = deliveries.filter((order) => order.status === "OutForDelivery");
  const deliveredToday = deliveries.filter((order) => order.status === "Delivered" && new Date(order.deliveredAt ?? order.updatedAt).toDateString() === today);
  const durations = deliveredToday.map((order) => minutesBetween(order.confirmedAt ?? order.createdAt, order.deliveredAt)).filter((value): value is number => value !== null);
  const feesToday = deliveredToday.reduce((sum, order) => sum + order.deliveryFeeInCents, 0);

  const stats = useMemo(() => {
    const map = new Map<string, { today: number; route: number; rating: number | null; ratings: number }>();
    for (const courier of couriers.data ?? []) {
      const score = ratings.data?.couriers.find((entry) => entry.courierName === courier.name);
      const values = score ? [score.service, score.punctuality, score.care].filter((value): value is number => value !== null) : [];
      map.set(courier.id, {
        today: deliveredToday.filter((order) => order.courierName === courier.name).length,
        route: onRoute.filter((order) => order.courierName === courier.name).length,
        rating: values.length ? values.reduce((a, b) => a + b, 0) / values.length : null,
        ratings: score?.count ?? 0,
      });
    }
    return map;
  }, [couriers.data, deliveredToday, onRoute, ratings.data]);

  async function dispatch(order: AdminOrder, courier: Courier) {
    if (!onTrack) return;
    const fallback = window.open("", "_blank");
    await action.run(async () => {
      const result = await onTrack(order.id, courier.id);
      if (result.sentToCourier) {
        fallback?.close();
        setNotice(`Pedido #${order.code.slice(-6)} enviado para ${courier.name} pelo WhatsApp da loja.`);
      } else {
        const url = `${window.location.origin}/entrega/${result.token}`;
        const target = `https://wa.me/${courier.phone}?text=${encodeURIComponent(`Entrega do pedido ${order.code.slice(-8)}. Abra para iniciar: ${url}`)}`;
        if (fallback) {
          fallback.opener = null;
          fallback.location.href = target;
        }
        setNotice(`WhatsApp da loja desconectado: abri a conversa com ${courier.name}.`);
      }
    });
  }

  async function saveCourier(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const payload = { name: text(form, "name"), phone: text(form, "phone"), isActive: editingCourier === "new" ? true : (editingCourier as Courier).isActive };
    const done = await action.run(() => (editingCourier === "new" ? http.post(`${base}/couriers`, payload) : http.put(`/couriers/${(editingCourier as Courier).id}`, payload)));
    if (done) setEditingCourier(null);
  }

  async function saveZone(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const minutes = Number(form.get("minutes")) || null;
    const payload = { district: text(form, "district"), feeInCents: toCents(form.get("fee")), estimatedMinutes: minutes, isActive: editingZone === "new" ? true : (editingZone as Zone).isActive };
    const done = await action.run(() => (editingZone === "new" ? http.post(`${base}/delivery-zones`, payload) : http.put(`/delivery-zones/${(editingZone as Zone).id}`, payload)));
    if (done) setEditingZone(null);
  }

  const activeCouriers = (couriers.data ?? []).filter((courier) => courier.isActive);

  return (
    <section className="admin-section distribution">
      <ModuleHeading eyebrow="Operação" title="Distribuição" hint="Despacho, entregadores ao vivo, desempenho e bairros atendidos." />
      <div className="kpi-strip">
        <div className={`kpi ${ready.some((order) => order.status === "Ready") ? "is-warn" : ""}`}><small>Prontos para sair</small><strong>{ready.filter((order) => order.status === "Ready").length}</strong></div>
        <div className="kpi"><small>Em rota agora</small><strong>{onRoute.length}</strong></div>
        <div className="kpi"><small>Entregues hoje</small><strong>{deliveredToday.length}</strong></div>
        <div className="kpi"><small>Tempo médio (aceite → entrega)</small><strong>{durations.length ? `${Math.round(durations.reduce((a, b) => a + b, 0) / durations.length)} min` : "—"}</strong></div>
        <div className="kpi"><small>Taxas de entrega hoje</small><strong>{formatBRL(feesToday)}</strong></div>
        <div className="kpi"><small>Entregadores ativos</small><strong>{activeCouriers.length}</strong></div>
      </div>
      <SubTabs
        onChange={setView}
        options={[
          { value: "dispatch", label: "Despacho" },
          ...(hasTracking ? [{ value: "map" as const, label: "Mapa ao vivo" }, { value: "couriers" as const, label: "Entregadores" }] : []),
          { value: "zones", label: "Bairros e taxas" },
        ]}
        value={view}
      />
      <ErrorMessage message={action.error ?? couriers.error ?? zones.error} />
      {notice ? <p className="board-notice" onClick={() => setNotice(null)}>{notice}</p> : null}

      {view === "dispatch" ? (
        <div className="dispatch">
          <div className="dispatch-list">
            <h3>Para sair <b>{ready.length}</b></h3>
            {ready.length === 0 ? <p className="board-empty">Nenhuma entrega esperando.</p> : null}
            {ready.map((order) => (
              <article className={`dispatch-card ${order.status === "Ready" ? "is-ready" : ""}`} key={order.id}>
                <header>
                  <strong>#{order.code.slice(-6)} · {order.customerName}</strong>
                  <StatusChip tone={order.status === "Ready" ? "ok" : "warn"}>{order.status === "Ready" ? "Pronto" : "Na cozinha"}</StatusChip>
                </header>
                <p>
                  {order.deliveryAddress ? `${order.deliveryAddress.street}, ${order.deliveryAddress.number} — ${order.deliveryAddress.district}` : "—"}
                  {order.deliveryReference ? <small>Ref.: {order.deliveryReference}</small> : null}
                </p>
                <div className="dispatch-meta">
                  <span>{formatBRL(order.totalInCents)}</span>
                  <span>{order.paymentStatus === "Paid" ? "pago" : `cobrar na entrega (${order.paymentMethod === "Cash" ? "dinheiro" : order.paymentMethod === "CardOnDelivery" ? "cartão" : order.paymentMethod})`}</span>
                  {order.courierName ? <span>🛵 {order.courierName}</span> : null}
                </div>
                {hasTracking ? (
                  <div className="dispatch-couriers">
                    {activeCouriers.map((courier) => (
                      <button className={`courier-chip ${order.courierName === courier.name ? "is-active" : ""}`} disabled={action.busy} key={courier.id} onClick={() => void dispatch(order, courier)} type="button">
                        🛵 {courier.name}
                        {stats.get(courier.id)?.route ? <small> · {stats.get(courier.id)!.route} em rota</small> : null}
                      </button>
                    ))}
                    {activeCouriers.length === 0 ? <span className="muted">Cadastre entregadores na aba Entregadores.</span> : null}
                  </div>
                ) : null}
              </article>
            ))}
          </div>
          <div className="dispatch-list">
            <h3>Em rota <b>{onRoute.length}</b></h3>
            {onRoute.length === 0 ? <p className="board-empty">Ninguém na rua agora.</p> : null}
            {onRoute.map((order) => (
              <article className={`dispatch-card is-route ${order.deliveryIssue ? "has-issue" : ""}`} key={order.id}>
                <header>
                  <strong>#{order.code.slice(-6)} · {order.customerName}</strong>
                  <span className="muted">{minutesBetween(order.updatedAt, new Date().toISOString())} min</span>
                </header>
                <p>{order.deliveryAddress ? `${order.deliveryAddress.street}, ${order.deliveryAddress.number} — ${order.deliveryAddress.district}` : "—"}</p>
                <div className="dispatch-meta">
                  <span>🛵 {order.courierName ?? "sem entregador"}</span>
                  <span>{formatBRL(order.totalInCents)}</span>
                </div>
                {order.deliveryIssue ? (
                  <div className="dispatch-issue">
                    <strong>⚠ Problema na entrega</strong>
                    <span>{order.deliveryIssue}</span>
                    <button className="link-button" disabled={action.busy} onClick={() => void action.run(() => http.post(`/orders/${order.id}/delivery-issue/resolve`, {}))} type="button">Marcar resolvido</button>
                  </div>
                ) : null}
                <button className="ticket-action" disabled={action.busy} onClick={() => void action.run(() => http.patch(`/orders/${order.id}/status`, { status: "Delivered", reason: null, changedBy: null }))} type="button">
                  Marcar entregue
                </button>
              </article>
            ))}
          </div>
        </div>
      ) : null}

      {view === "map" ? <LiveDeliveries establishmentId={establishmentId} http={http} /> : null}

      {view === "couriers" ? (
        <DataTable
          actions={[
            { icon: "edit", label: "Editar", onClick: (row) => setEditingCourier(row) },
            { icon: "whatsappSend", label: "Chamar no WhatsApp", tone: "accent", onClick: (row) => window.open(`https://wa.me/${row.phone.replace(/\D/g, "").replace(/^(?!55)/, "55")}`, "_blank", "noopener") },
            { icon: "power", label: "Reativar", hidden: (row) => row.isActive, onClick: (row) => void action.run(() => http.put(`/couriers/${row.id}`, { ...row, isActive: true })) },
            { icon: "trash", label: "Inativar", tone: "danger", hidden: (row) => !row.isActive, onClick: (row) => window.confirm(`Inativar ${row.name}?`) && void action.run(() => http.put(`/couriers/${row.id}`, { ...row, isActive: false })) },
          ]}
          columns={[
            { key: "name", title: "Entregador", value: (row) => row.name, render: (row) => <span className="cell-main"><span className="avatar">🛵</span><div><strong>{row.name}</strong><small>{formatPhone(row.phone)}</small></div></span> },
            { key: "route", title: "Agora", value: (row) => stats.get(row.id)?.route ?? 0, render: (row) => ((stats.get(row.id)?.route ?? 0) > 0 ? <StatusChip tone="accent">{stats.get(row.id)!.route} em rota</StatusChip> : row.isActive ? <StatusChip tone="ok">Livre</StatusChip> : <StatusChip tone="muted">Inativo</StatusChip>) },
            { key: "today", title: "Entregas hoje", align: "right", value: (row) => stats.get(row.id)?.today ?? 0 },
            {
              key: "rating",
              title: "Avaliação (30 dias)",
              align: "right",
              value: (row) => stats.get(row.id)?.rating ?? 0,
              render: (row) => (stats.get(row.id)?.rating ? <>{"★".repeat(Math.round(stats.get(row.id)!.rating!))} {stats.get(row.id)!.rating!.toFixed(1).replace(".", ",")}<small>{stats.get(row.id)!.ratings} avaliação(ões)</small></> : <span className="muted">sem avaliação</span>),
            },
          ]}
          createLabel="Novo entregador"
          empty="Cadastre os entregadores para despachar com um toque."
          filters={[
            { key: "active", label: "Ativos", test: (row) => row.isActive },
            { key: "inactive", label: "Inativos", test: (row) => !row.isActive },
          ]}
          onCreate={() => setEditingCourier("new")}
          onRowClick={(row) => setEditingCourier(row)}
          rowKey={(row) => row.id}
          rows={couriers.data ?? []}
          searchPlaceholder="Pesquisar entregador…"
        />
      ) : null}

      {view === "zones" ? (
        <DataTable
          actions={[
            { icon: "edit", label: "Editar", onClick: (row) => setEditingZone(row) },
            { icon: "power", label: "Atender de novo", hidden: (row) => row.isActive, onClick: (row) => void action.run(() => http.put(`/delivery-zones/${row.id}`, { ...row, isActive: true })) },
            { icon: "trash", label: "Parar de atender", tone: "danger", hidden: (row) => !row.isActive, onClick: (row) => void action.run(() => http.put(`/delivery-zones/${row.id}`, { ...row, isActive: false })) },
          ]}
          columns={[
            { key: "district", title: "Bairro", value: (row) => row.district, render: (row) => <strong>{row.district}</strong> },
            { key: "fee", title: "Taxa", align: "right", value: (row) => row.feeInCents, render: (row) => (row.feeInCents ? formatBRL(row.feeInCents) : <StatusChip tone="ok">Grátis</StatusChip>) },
            { key: "time", title: "Tempo estimado", align: "right", value: (row) => row.estimatedMinutes ?? 0, render: (row) => (row.estimatedMinutes ? `${row.estimatedMinutes} min` : "—") },
            { key: "orders", title: "Entregas (no período carregado)", align: "right", value: (row) => deliveries.filter((order) => order.deliveryAddress?.district.toLowerCase() === row.district.toLowerCase()).length, hideOnMobile: true },
            { key: "status", title: "Situação", value: (row) => (row.isActive ? "atende" : "parado"), render: (row) => (row.isActive ? <StatusChip tone="ok">Atende</StatusChip> : <StatusChip tone="muted">Não atende</StatusChip>) },
          ]}
          createLabel="Novo bairro"
          empty="Sem bairros cadastrados: a entrega usa a taxa fixa da loja."
          onCreate={() => setEditingZone("new")}
          onRowClick={(row) => setEditingZone(row)}
          rowKey={(row) => row.id}
          rows={zones.data ?? []}
          searchPlaceholder="Pesquisar bairro…"
        />
      ) : null}

      {editingCourier ? (
        <FormDrawer onClose={() => setEditingCourier(null)} subtitle="Entregador" title={editingCourier === "new" ? "Novo entregador" : editingCourier.name}>
          <form className="drawer-form" onSubmit={saveCourier}>
            <label className="field"><span>Nome *</span><input defaultValue={editingCourier === "new" ? "" : editingCourier.name} name="name" required /></label>
            <label className="field"><span>WhatsApp *</span><input defaultValue={editingCourier === "new" ? "" : editingCourier.phone} inputMode="tel" name="phone" required /></label>
            <p className="muted">O link de cada entrega (código, endereço, cobrança e mapa) chega neste WhatsApp, enviado pelo número da loja.</p>
            <div className="drawer-foot">
              <button className="secondary-button" onClick={() => setEditingCourier(null)} type="button">Cancelar</button>
              <button className="primary-button" disabled={action.busy} type="submit">Salvar</button>
            </div>
          </form>
        </FormDrawer>
      ) : null}

      {editingZone ? (
        <FormDrawer onClose={() => setEditingZone(null)} subtitle="Bairro atendido" title={editingZone === "new" ? "Novo bairro" : editingZone.district}>
          <form className="drawer-form" onSubmit={saveZone}>
            <label className="field"><span>Bairro *</span><input defaultValue={editingZone === "new" ? "" : editingZone.district} name="district" required /></label>
            <div className="field-row">
              <label className="field"><span>Taxa (R$)</span><input defaultValue={editingZone === "new" ? "" : (editingZone.feeInCents / 100).toFixed(2).replace(".", ",")} inputMode="decimal" name="fee" /></label>
              <label className="field"><span>Tempo estimado (min)</span><input defaultValue={editingZone === "new" ? "" : editingZone.estimatedMinutes ?? ""} inputMode="numeric" name="minutes" /></label>
            </div>
            <div className="drawer-foot">
              <button className="secondary-button" onClick={() => setEditingZone(null)} type="button">Cancelar</button>
              <button className="primary-button" disabled={action.busy} type="submit">Salvar</button>
            </div>
          </form>
        </FormDrawer>
      ) : null}
    </section>
  );
}
