"use client";

import { formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { StatusChip } from "../DataTable";
import { ErrorMessage, ModuleHeading, useLoad } from "./shared";

interface IntegrationStatus {
  code: string;
  name: string;
  category: string;
  status: "ok" | "warn" | "bad" | "off";
  detail: string;
  module: string | null;
  target: string;
  lastActivityAt: string | null;
}

const statusLabel = { ok: "Funcionando", warn: "Atenção", bad: "Com falha", off: "Desligada" } as const;
const statusTone = { ok: "ok", warn: "warn", bad: "bad", off: "muted" } as const;
const order = { bad: 0, warn: 1, ok: 2, off: 3 } as const;

/** Every connection of the store in one place; each card opens the screen where it is set up. */
export function Integrations({ http, establishmentId, modules, onOpen }: { http: AdminHttp; establishmentId: string; modules: string[]; onOpen: (target: string) => void }) {
  const hub = useLoad(() => http.get<IntegrationStatus[]>(`/establishments/${establishmentId}/integrations`), [establishmentId]);
  const items = [...(hub.data ?? [])].sort((left, right) => order[left.status] - order[right.status]);
  const categories = [...new Set(items.map((item) => item.category))];
  const problems = items.filter((item) => item.status === "bad" || item.status === "warn").length;

  return (
    <>
      <ModuleHeading
        eyebrow="Ajustes"
        title="Central de integrações"
        hint={hub.data ? (problems ? `${problems} integração(ões) pedem atenção.` : "Tudo que está ligado está funcionando.") : "Conferindo as conexões da loja…"}
      />
      <div className="vx-panel-title">
        <span />
        <button className="secondary-button" disabled={hub.loading} onClick={() => void hub.reload()} type="button">{hub.loading ? "Conferindo…" : "Conferir de novo"}</button>
      </div>
      <ErrorMessage message={hub.error} />
      {categories.map((category) => (
        <section key={category}>
          <p className="vx-sec">{category}</p>
          <div className="fx-hub">
            {items.filter((item) => item.category === category).map((item) => {
              const contracted = !item.module || modules.includes(item.module);
              return (
                <article className={`card vx-box fx-hub-card is-${item.status}`} key={item.code}>
                  <header>
                    <h3>{item.name}</h3>
                    {contracted ? <StatusChip tone={statusTone[item.status]}>{statusLabel[item.status]}</StatusChip> : <StatusChip tone="muted">Fora do contrato</StatusChip>}
                  </header>
                  <p className="muted">{contracted ? item.detail : "Este módulo não está no seu contrato. Fale com a Veloso Solution para liberar."}</p>
                  <footer>
                    <small className="muted">{item.lastActivityAt ? `Último uso ${formatDateTime(item.lastActivityAt)}` : ""}</small>
                    {contracted ? <button className="secondary-button" onClick={() => onOpen(item.target)} type="button">Abrir</button> : null}
                  </footer>
                </article>
              );
            })}
          </div>
        </section>
      ))}
    </>
  );
}
