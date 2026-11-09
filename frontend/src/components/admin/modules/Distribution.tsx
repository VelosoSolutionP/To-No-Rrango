"use client";

import type { FormEvent } from "react";
import type { AdminHttp } from "@/lib/admin-api";
import { Zones } from "./Settings";
import { Empty, ErrorMessage, Input, ModuleHeading, text, useAction, useLoad } from "./shared";

interface Courier {
  id: string;
  name: string;
  phone: string;
  isActive: boolean;
}

interface Props {
  http: AdminHttp;
  establishmentId: string;
}

function formatPhone(phone: string) {
  return phone.replace(/^55(\d{2})(\d{4,5})(\d{4})$/, "($1) $2-$3");
}

function Couriers({ http, establishmentId }: Props) {
  const couriers = useLoad(() => http.get<Courier[]>(`/establishments/${establishmentId}/couriers`), [establishmentId]);
  const action = useAction(couriers.reload);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const done = await action.run(() =>
      http.post(`/establishments/${establishmentId}/couriers`, {
        name: text(form, "name"),
        phone: text(form, "phone"),
        isActive: true,
      }),
    );

    if (done) {
      element.reset();
    }
  }

  return (
    <>
      <div className="vx-panel-title"><h3>Entregadores</h3></div>
      <form className="card module-form" onSubmit={submit}>
        <Input label="Nome *" name="name" required />
        <Input inputMode="tel" label="WhatsApp com DDD *" name="phone" required />
        <button className="primary-button" disabled={action.busy} type="submit">Adicionar entregador</button>
      </form>
      <ErrorMessage message={action.error ?? couriers.error} />
      {couriers.data?.length === 0 ? <Empty>Nenhum entregador cadastrado. Depois de cadastrar, o link da entrega sai direto para o WhatsApp dele em Pedidos.</Empty> : null}
      {couriers.data && couriers.data.length > 0 ? (
        <div className="card admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Nome</th><th>WhatsApp</th><th>Situação</th><th /></tr></thead>
            <tbody>
              {couriers.data.map((courier) => (
                <tr key={courier.id}>
                  <td>{courier.name}</td>
                  <td>{formatPhone(courier.phone)}</td>
                  <td>{courier.isActive ? "Ativo" : "Inativo"}</td>
                  <td className="table-actions">
                    <button
                      className="link-button"
                      disabled={action.busy}
                      onClick={() => action.run(() => http.put(`/couriers/${courier.id}`, { name: courier.name, phone: courier.phone, isActive: !courier.isActive }))}
                      type="button"
                    >
                      {courier.isActive ? "Desativar" : "Ativar"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}

export function Distribution({ http, establishmentId, modules }: Props & { modules: string[] }) {
  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Distribuição" title="Distribuição" hint="Regiões atendidas, taxas de entrega e entregadores." />
      <Zones establishmentId={establishmentId} http={http} />
      {modules.includes("rastreio") ? <Couriers establishmentId={establishmentId} http={http} /> : null}
      <article className="card vx-box">
        <div className="vx-panel-title"><h3>Ainda não disponível</h3></div>
        <ul className="vx-unavailable">
          <li><strong>Atacado e varejo</strong><span>Tabelas de preço por canal ainda não existem.</span></li>
          <li><strong>Pedidos de distribuição</strong><span>Hoje os pedidos são apenas de consumidor final.</span></li>
          <li><strong>Rotas de entrega</strong><span>Montar uma rota com vários pedidos ainda não existe.</span></li>
        </ul>
      </article>
    </section>
  );
}
