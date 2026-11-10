"use client";

import { useState } from "react";
import { formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { Empty, ErrorMessage, ModuleHeading, SubTabs, useLoad } from "./shared";

interface Summary {
  days: number;
  count: number;
  store: { service: number | null; product: number | null; overall: number | null };
  couriers: Array<{ courierName: string; count: number; service: number | null; punctuality: number | null; care: number | null }>;
  problems: Array<{ dimension: string; lowScores: number; total: number }>;
  recent: Array<{
    orderCode: string;
    serviceScore: number;
    productScore: number;
    overallScore: number;
    storeComment: string | null;
    courierName: string | null;
    courierServiceScore: number | null;
    courierPunctualityScore: number | null;
    courierCareScore: number | null;
    courierComment: string | null;
    createdAt: string;
  }>;
}

function score(value: number | null | undefined) {
  return value == null ? "—" : `${value.toFixed(1).replace(".", ",")} ★`;
}

export function Ratings({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const [days, setDays] = useState<"7" | "30" | "90">("30");
  const summary = useLoad(() => http.get<Summary>(`/establishments/${establishmentId}/ratings?days=${days}`), [establishmentId, days]);
  const data = summary.data;

  return (
    <section className="admin-section ratings">
      <ModuleHeading eyebrow="Qualidade" title="Avaliações" hint="Notas que os clientes dão depois da entrega: a loja e o entregador, separados.">
        <SubTabs
          onChange={setDays}
          options={[
            { value: "7", label: "7 dias" },
            { value: "30", label: "30 dias" },
            { value: "90", label: "90 dias" },
          ]}
          value={days}
        />
      </ModuleHeading>
      <ErrorMessage message={summary.error} />
      {data ? (
        <>
          <div className="pdv-summary">
            <div className="card"><small>Avaliações</small><strong>{data.count}</strong></div>
            <div className="card"><small>Atendimento</small><strong>{score(data.store.service)}</strong></div>
            <div className="card"><small>Produtos</small><strong>{score(data.store.product)}</strong></div>
            <div className="card"><small>Experiência geral</small><strong>{score(data.store.overall)}</strong></div>
          </div>

          {data.problems.length > 0 ? (
            <div className="card ratings-problems">
              <h3>Pontos de atenção</h3>
              <ul>
                {data.problems.map((problem) => (
                  <li key={problem.dimension}>
                    <strong>{problem.dimension}</strong>: {problem.lowScores} nota(s) baixa(s) em {problem.total}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          <h3>Entregadores</h3>
          {data.couriers.length === 0 ? <Empty>Nenhuma entrega avaliada com entregador no período.</Empty> : null}
          <table className="admin-table">
            <tbody>
              {data.couriers.map((courier) => (
                <tr key={courier.courierName}>
                  <td><strong>{courier.courierName}</strong></td>
                  <td>{courier.count} avaliação(ões)</td>
                  <td>Atendimento {score(courier.service)}</td>
                  <td>Pontualidade {score(courier.punctuality)}</td>
                  <td>Cuidado {score(courier.care)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <h3>Últimas avaliações</h3>
          {data.recent.length === 0 ? <Empty>Ainda não há avaliações no período.</Empty> : null}
          <div className="ratings-list">
            {data.recent.map((rating) => (
              <article className="card" key={rating.orderCode}>
                <header>
                  <strong>{rating.orderCode.slice(-8)}</strong>
                  <small>{formatDateTime(rating.createdAt)}</small>
                </header>
                <p>Loja: atendimento {rating.serviceScore} · produtos {rating.productScore} · geral {rating.overallScore}</p>
                {rating.storeComment ? <p className="muted">“{rating.storeComment}”</p> : null}
                {rating.courierName ? (
                  <p>
                    {rating.courierName}: atendimento {rating.courierServiceScore ?? "—"} · pontualidade {rating.courierPunctualityScore ?? "—"} · cuidado {rating.courierCareScore ?? "—"}
                  </p>
                ) : null}
                {rating.courierComment ? <p className="muted">“{rating.courierComment}”</p> : null}
              </article>
            ))}
          </div>
        </>
      ) : null}
    </section>
  );
}
