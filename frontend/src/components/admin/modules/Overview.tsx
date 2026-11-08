"use client";

import type { AdminHttp } from "@/lib/admin-api";
import type { AdminOrder, Establishment, OrderStatus } from "@/lib/types";
import { Reports } from "./Reports";
import { ModuleHeading, useLoad } from "./shared";

interface Props {
  http: AdminHttp;
  establishment: Establishment;
  orders: AdminOrder[];
  modules: string[];
  onNavigate: (tab: "orders" | "kitchen" | "whatsapp") => void;
}

const queue: Array<{ status: OrderStatus; label: string }> = [
  { status: "Pending", label: "Novos" },
  { status: "Confirmed", label: "Confirmados" },
  { status: "Preparing", label: "Em preparo" },
  { status: "Ready", label: "Prontos" },
];

export function Overview({ http, establishment, orders, modules, onNavigate }: Props) {
  const id = establishment.id;
  const hasWhatsApp = modules.includes("whatsapp");
  const connection = useLoad(() => (hasWhatsApp ? http.get<{ connected: boolean; number: string | null }>(`/establishments/${id}/whatsapp/connection`).catch(() => null) : Promise.resolve(null)), [id, hasWhatsApp]);
  const settings = useLoad(() => (hasWhatsApp ? http.get<{ autoReply: boolean }>(`/establishments/${id}/whatsapp`).catch(() => null) : Promise.resolve(null)), [id, hasWhatsApp]);

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
        {hasWhatsApp ? (
          <article className="card vx-box">
            <div className="vx-panel-title">
              <h3>WhatsApp e IA</h3>
              <button className="link-button" onClick={() => onNavigate("whatsapp")} type="button">Gerenciar</button>
            </div>
            <p><span className={`vx-state ${connection.data?.connected ? "is-on" : ""}`}><i />{connection.data?.connected ? `Conectado${connection.data.number ? ` · +${connection.data.number}` : ""}` : "Sem conexão"}</span></p>
            <p><span className={`vx-state ${settings.data?.autoReply ? "is-on" : ""}`}><i />{settings.data?.autoReply ? "Atendimento automático ativo" : "Atendimento automático desligado"}</span></p>
          </article>
        ) : null}
      </div>
      {modules.includes("relatorios") ? <Reports establishmentId={id} http={http} /> : null}
    </section>
  );
}
