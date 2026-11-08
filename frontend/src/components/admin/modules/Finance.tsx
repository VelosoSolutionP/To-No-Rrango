"use client";

import { type FormEvent, useState } from "react";
import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import { Empty, ErrorMessage as ErrorText, Input, ModuleHeading, SubTabs, text, toCents, useAction, useLoad } from "./shared";

interface Entry {
  id: string;
  type: "Receivable" | "Payable";
  status: "Open" | "Settled" | "Cancelled";
  source: "Manual" | "Order" | "Purchase";
  category: string;
  description: string;
  amountInCents: number;
  dueOn: string;
}

const statusLabels = { Open: "Em aberto", Settled: "Baixado", Cancelled: "Cancelado" } as const;

function formatDay(value: string) {
  const [year, month, day] = value.split("-");
  return `${day}/${month}/${year}`;
}

export function Finance({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const [filter, setFilter] = useState<"all" | Entry["status"]>("Open");
  const query = filter === "all" ? "" : `?status=${filter}`;
  const list = useLoad(() => http.get<Entry[]>(`/establishments/${establishmentId}/financial-entries${query}`), [establishmentId, query]);
  const action = useAction(list.reload);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const done = await action.run(() =>
      http.post(`/establishments/${establishmentId}/financial-entries`, {
        type: form.get("type"),
        category: text(form, "category"),
        description: text(form, "description"),
        amountInCents: toCents(form.get("amount")),
        dueOn: form.get("dueOn"),
      }),
    );

    if (done) {
      element.reset();
    }
  }

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Gestão" title="Financeiro" hint="Vendas pagas e compras entram aqui automaticamente." />
      <form className="card module-form" onSubmit={submit}>
        <label className="field">
          <span>Tipo</span>
          <select name="type">
            <option value="Payable">A pagar</option>
            <option value="Receivable">A receber</option>
          </select>
        </label>
        <Input label="Categoria *" name="category" placeholder="Aluguel, energia..." required />
        <Input label="Descrição *" name="description" required />
        <Input inputMode="decimal" label="Valor (R$) *" name="amount" required />
        <Input label="Vencimento *" name="dueOn" required type="date" />
        <button className="primary-button" disabled={action.busy} type="submit">Lançar</button>
      </form>
      <SubTabs
        onChange={setFilter}
        options={[
          { value: "Open", label: "Em aberto" },
          { value: "Settled", label: "Baixados" },
          { value: "Cancelled", label: "Cancelados" },
          { value: "all", label: "Todos" },
        ]}
        value={filter}
      />
      <ErrorText message={action.error ?? list.error} />
      {list.data?.length === 0 ? <Empty>Nenhum lançamento neste filtro.</Empty> : null}
      {list.data && list.data.length > 0 ? (
        <div className="card admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Vencimento</th><th>Tipo</th><th>Categoria</th><th>Descrição</th><th className="num">Valor</th><th>Situação</th><th /></tr></thead>
            <tbody>
              {list.data.map((entry) => (
                <tr key={entry.id}>
                  <td>{formatDay(entry.dueOn)}</td>
                  <td>{entry.type === "Payable" ? "A pagar" : "A receber"}</td>
                  <td>{entry.category}</td>
                  <td>{entry.description}</td>
                  <td className="num">{formatBRL(entry.amountInCents)}</td>
                  <td>{statusLabels[entry.status]}</td>
                  <td className="table-actions">
                    {entry.status === "Open" ? (
                      <button className="link-button" disabled={action.busy} onClick={() => action.run(() => http.post(`/financial-entries/${entry.id}/settle`))} type="button">Baixar</button>
                    ) : null}
                    {entry.status === "Open" && entry.source === "Manual" ? (
                      <button className="link-button" disabled={action.busy} onClick={() => action.run(() => http.post(`/financial-entries/${entry.id}/cancel`))} type="button">Cancelar</button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
