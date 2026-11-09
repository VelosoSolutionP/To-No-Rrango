"use client";

import type { AdminHttp } from "@/lib/admin-api";
import type { AdminOrder, Establishment, OrderStatus } from "@/lib/types";
import { Reports } from "./Reports";
import { ModuleHeading } from "./shared";

interface Props {
  http: AdminHttp;
  establishment: Establishment;
  orders: AdminOrder[];
  modules: string[];
  onNavigate: (tab: "orders" | "kitchen") => void;
}

const queue: Array<{ status: OrderStatus; label: string }> = [
  { status: "Pending", label: "Novos" },
  { status: "Confirmed", label: "Confirmados" },
  { status: "Preparing", label: "Em preparo" },
  { status: "Ready", label: "Prontos" },
];

export function Overview({ http, establishment, orders, modules, onNavigate }: Props) {
  const id = establishment.id;

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Operação" title={establishment.name} />
      <div className="vx-grid-2">
        <article className="card vx-box">
          <div className="vx-panel-title">
            <h3>Fila agora</h3>
            <button className="link-button" onClick={() => onNavigate("kitchen")} type="button">Abrir cozinha</button>
          </div>
          <div className="vx-pipeline">
            {queue.map((item) => (
              <button key={item.status} onClick={() => onNavigate("orders")} type="button">
                <strong>{orders.filter((order) => order.status === item.status).length}</strong>
                {item.label}
              </button>
            ))}
          </div>
        </article>
      </div>
      {modules.includes("relatorios") ? <Reports establishmentId={id} http={http} /> : null}
    </section>
  );
}
