"use client";

import type { AdminHttp } from "@/lib/admin-api";
import { Zones } from "./Settings";
import { ModuleHeading } from "./shared";

export function Distribution({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Distribuição" title="Distribuição" hint="Regiões atendidas e taxas de entrega." />
      <Zones establishmentId={establishmentId} http={http} />
      <article className="card vx-box">
        <div className="vx-panel-title"><h3>Ainda não disponível</h3></div>
        <ul className="vx-unavailable">
          <li><strong>Atacado e varejo</strong><span>Tabelas de preço por canal ainda não existem.</span></li>
          <li><strong>Pedidos de distribuição</strong><span>Hoje os pedidos são apenas de consumidor final.</span></li>
          <li><strong>Entregadores e rotas</strong><span>Ainda não implementado.</span></li>
        </ul>
      </article>
    </section>
  );
}
