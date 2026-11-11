"use client";

import { useEffect, useState } from "react";
import type { AdminHttp } from "@/lib/admin-api";
import { formatBRL } from "@/lib/admin-api";
import type { AdminOrder, Establishment, OrderStatus } from "@/lib/types";
import { originLabels, originOf } from "../OrdersBoard";
import { Reports } from "./Reports";

interface Props {
  http: AdminHttp;
  establishment: Establishment;
  orders: AdminOrder[];
  modules: string[];
  onNavigate: (tab: "orders" | "kitchen") => void;
}

/** Mesmo limite da central de pedidos: passou disso, a comanda fica "quente". */
const LATE_MINUTES = 25;

const lanes: Array<{ id: string; title: string; statuses: OrderStatus[] }> = [
  { id: "new", title: "Novos", statuses: ["Pending"] },
  { id: "prep", title: "Em preparo", statuses: ["Confirmed", "Preparing"] },
  { id: "ready", title: "Prontos", statuses: ["Ready"] },
  { id: "route", title: "Em rota", statuses: ["OutForDelivery"] },
];

const minutesSince = (iso: string, now: number) => Math.max(0, Math.floor((now - new Date(iso).getTime()) / 60000));
const elapsed = (minutes: number) => (minutes < 60 ? `${minutes} min` : `${Math.floor(minutes / 60)}h${String(minutes % 60).padStart(2, "0")}`);

export function Overview({ http, establishment, orders, modules, onNavigate }: Props) {
  /* O tempo das comandas anda sozinho; um minuto é resolução suficiente. */
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60000);
    return () => window.clearInterval(timer);
  }, []);

  const active = orders.filter((order) => lanes.some((lane) => lane.statuses.includes(order.status)));
  const late = active.filter((order) => minutesSince(order.createdAt, now) >= LATE_MINUTES).length;

  return (
    <section className="admin-section">
      <header className="ov-hero">
        <div>
          <p className="ov-tag">Operação · {establishment.name}</p>
          <h2>Sua casa, <em>agora.</em></h2>
          <p className="muted">
            {active.length === 0
              ? "Nenhum pedido em andamento neste momento."
              : `${active.length} ${active.length === 1 ? "pedido em andamento" : "pedidos em andamento"}${late ? ` · ${late} ${late === 1 ? "atrasado" : "atrasados"}` : ""}.`}
          </p>
        </div>
        <div className="ov-actions">
          <button className="secondary-button" onClick={() => onNavigate("kitchen")} type="button">Abrir cozinha</button>
          <button className="primary-button" onClick={() => onNavigate("orders")} type="button">Central de pedidos</button>
        </div>
      </header>

      <article className="card ov-belt">
        <div className="vx-panel-title">
          <h3>Pedidos <em>na esteira</em></h3>
          <span className="ov-live"><i />ao vivo</span>
        </div>
        <div className="ov-lanes">
          {lanes.map((lane) => {
            const list = active
              .filter((order) => lane.statuses.includes(order.status))
              .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());
            return (
              <div className="ov-lane" key={lane.id}>
                <h4>{lane.title} <b>{list.length}</b></h4>
                {list.length === 0 ? <p className="ov-empty">Vazio</p> : null}
                {list.slice(0, 6).map((order) => {
                  const minutes = minutesSince(order.createdAt, now);
                  const origin = originOf(order);
                  const items = order.items.map((item) => `${item.quantity}× ${item.name}`).join(" · ");
                  return (
                    <button className={`ov-ticket ${minutes >= LATE_MINUTES ? "is-hot" : ""}`} key={order.id} onClick={() => onNavigate("orders")} type="button">
                      <span className="ov-ticket-top"><strong>#{order.code.slice(-4)}</strong><span>{formatBRL(order.totalInCents)}</span></span>
                      <span className="ov-ticket-items">{items || "Sem itens"}</span>
                      <span className="ov-ticket-foot">
                        <span className={`origin-tag origin-${origin}`}>{originLabels[origin]}</span>
                        <span className="ov-time">{elapsed(minutes)}</span>
                      </span>
                    </button>
                  );
                })}
                {list.length > 6 ? <button className="link-button" onClick={() => onNavigate("orders")} type="button">+{list.length - 6} na central</button> : null}
              </div>
            );
          })}
        </div>
      </article>

      {modules.includes("relatorios") ? <Reports establishmentId={establishment.id} http={http} /> : null}
    </section>
  );
}
