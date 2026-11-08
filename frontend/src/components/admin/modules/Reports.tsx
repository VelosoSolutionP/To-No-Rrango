"use client";

import { useState } from "react";
import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import { Icon } from "../Icon";
import { Empty, ErrorMessage as ErrorText, SubTabs, useLoad } from "./shared";

interface Dashboard {
  sales: {
    ordersCount: number;
    cancelledCount: number;
    revenueInCents: number;
    paidInCents: number;
    byPayment: Array<{ method: string; count: number; totalInCents: number }>;
    topItems: Array<{ name: string; quantity: number; revenueInCents: number }>;
    byDay: Array<{ day: string; orders: number; revenueInCents: number }>;
  };
  receivedInCents: number;
  paidOutInCents: number;
  openPayablesInCents: number;
  openReceivablesInCents: number;
  costOfGoodsInCents: number;
  lowStockCount: number;
}

const paymentNames: Record<string, string> = { Cash: "Dinheiro", Pix: "Pix", CardOnDelivery: "Cartão na entrega", Online: "Cartão online" };

function shortDay(value: string) {
  const [, month, day] = value.split("-");
  return `${day}/${month}`;
}

function Stat({ label, value, hint, icon, tone = "silver" }: { label: string; value: string; hint?: string; icon: string; tone?: "silver" | "orange" | "lilac" }) {
  return (
    <div className="card stat-tile">
      <i className={`vx-chip vx-chip-${tone}`}><Icon name={icon} /></i>
      <span>{label}</span>
      <strong>{value}</strong>
      {hint ? <small>{hint}</small> : null}
    </div>
  );
}

/** Days without sales stay on the axis, so the bars show the real rhythm of the period. */
function fillPeriod(days: Dashboard["sales"]["byDay"], length: number) {
  const known = new Map(days.map((day) => [day.day, day]));

  return Array.from({ length }, (_, index) => {
    const date = new Date();
    date.setDate(date.getDate() - (length - 1 - index));
    const key = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;

    return known.get(key) ?? { day: key, orders: 0, revenueInCents: 0 };
  });
}

function DailyRevenue({ days }: { days: Dashboard["sales"]["byDay"] }) {
  const [active, setActive] = useState<number | null>(null);
  const [showTable, setShowTable] = useState(false);
  const max = Math.max(...days.map((day) => day.revenueInCents), 1);
  const current = active === null ? null : days[active];

  return (
    <div className="card chart-card">
      <div className="chart-head">
        <h3>Faturamento por dia</h3>
        <button className="link-button" onClick={() => setShowTable((value) => !value)} type="button">
          {showTable ? "Ver gráfico" : "Ver tabela"}
        </button>
      </div>
      {showTable ? (
        <table className="admin-table">
          <thead><tr><th>Dia</th><th className="num">Pedidos</th><th className="num">Faturamento</th></tr></thead>
          <tbody>
            {days.map((day) => (
              <tr key={day.day}><td>{shortDay(day.day)}</td><td className="num">{day.orders}</td><td className="num">{formatBRL(day.revenueInCents)}</td></tr>
            ))}
          </tbody>
        </table>
      ) : (
        <>
          <p className="chart-readout" aria-live="polite">
            {current
              ? `${shortDay(current.day)}: ${formatBRL(current.revenueInCents)} · ${current.orders} pedido(s)`
              : `Maior dia: ${formatBRL(max)}`}
          </p>
          <div className="bar-chart" onMouseLeave={() => setActive(null)} role="img" aria-label="Faturamento por dia">
            {days.map((day, index) => (
              <button
                aria-label={`${shortDay(day.day)}: ${formatBRL(day.revenueInCents)}`}
                className={`bar-column ${active === index ? "is-active" : ""}`}
                key={day.day}
                onBlur={() => setActive(null)}
                onFocus={() => setActive(index)}
                onMouseEnter={() => setActive(index)}
                type="button"
              >
                <span style={{ height: day.revenueInCents > 0 ? `${Math.max((day.revenueInCents / max) * 100, 2)}%` : 0 }} />
              </button>
            ))}
          </div>
          <div className="bar-axis">
            <span>{shortDay(days[0].day)}</span>
            {days.length > 1 ? <span>{shortDay(days[days.length - 1].day)}</span> : null}
          </div>
        </>
      )}
    </div>
  );
}

export function Reports({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const [days, setDays] = useState<"7" | "30" | "90">("30");
  const report = useLoad(() => http.get<Dashboard>(`/establishments/${establishmentId}/dashboard?days=${days}`), [establishmentId, days]);
  const data = report.data;
  const valid = data ? data.sales.ordersCount - data.sales.cancelledCount : 0;

  return (
    <section className="admin-section">
      <SubTabs onChange={setDays} options={[{ value: "7", label: "7 dias" }, { value: "30", label: "30 dias" }, { value: "90", label: "90 dias" }]} value={days} />
      <ErrorText message={report.error} />
      {data ? (
        <>
          <div className="stat-grid">
            <Stat icon="finance" tone="orange" label="Faturamento" value={formatBRL(data.sales.revenueInCents)} hint={`${valid} pedido(s) válidos`} />
            <Stat icon="orders" label="Ticket médio" value={formatBRL(valid > 0 ? Math.round(data.sales.revenueInCents / valid) : 0)} />
            <Stat icon="kitchen" label="Cancelados" value={String(data.sales.cancelledCount)} />
            <Stat icon="inventory" tone="lilac" label="Custo dos insumos" value={formatBRL(data.costOfGoodsInCents)} hint="Pelas fichas técnicas" />
            <Stat icon="reports" tone="orange" label="Entradas" value={formatBRL(data.receivedInCents)} hint="Baixadas no período" />
            <Stat icon="distribution" label="Saídas" value={formatBRL(data.paidOutInCents)} hint="Baixadas no período" />
            <Stat icon="fiscal" tone="lilac" label="Contas a pagar" value={formatBRL(data.openPayablesInCents)} hint="Em aberto" />
            <Stat icon="inventory" label="Insumos em alerta" value={String(data.lowStockCount)} />
          </div>
          {data.sales.byDay.length > 0 ? <DailyRevenue days={fillPeriod(data.sales.byDay, Number(days))} /> : <Empty>Sem vendas neste período.</Empty>}
          <div className="module-columns">
            <div className="card chart-card">
              <h3>Mais vendidos</h3>
              <table className="admin-table">
                <thead><tr><th>Item</th><th className="num">Qtd</th><th className="num">Receita</th></tr></thead>
                <tbody>
                  {data.sales.topItems.map((item) => (
                    <tr key={item.name}><td>{item.name}</td><td className="num">{item.quantity}</td><td className="num">{formatBRL(item.revenueInCents)}</td></tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="card chart-card">
              <h3>Formas de pagamento</h3>
              <table className="admin-table">
                <thead><tr><th>Forma</th><th className="num">Pedidos</th><th className="num">Total</th></tr></thead>
                <tbody>
                  {data.sales.byPayment.map((payment) => (
                    <tr key={payment.method}><td>{paymentNames[payment.method] ?? payment.method}</td><td className="num">{payment.count}</td><td className="num">{formatBRL(payment.totalInCents)}</td></tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </>
      ) : null}
    </section>
  );
}
