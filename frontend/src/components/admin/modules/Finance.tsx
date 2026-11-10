"use client";

import { type FormEvent, useMemo, useState } from "react";
import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import { DataTable, FormDrawer, StatusChip } from "../DataTable";
import { ErrorMessage, ModuleHeading, SubTabs, text, toCents, useAction, useLoad } from "./shared";

interface Entry {
  id: string;
  type: "Receivable" | "Payable";
  status: "Open" | "Settled" | "Cancelled";
  source: "Manual" | "Order" | "Purchase";
  category: string;
  description: string;
  amountInCents: number;
  dueOn: string;
  settledAt: string | null;
  counterparty?: string | null;
  documentNumber?: string | null;
  settlementMethod?: string | null;
  settledOn?: string | null;
  createdAt?: string | null;
}

type View = "receivable" | "payable" | "flow" | "statement";

const sourceLabels = { Manual: "Manual", Order: "Pedido", Purchase: "Compra" } as const;
const natures: Record<Entry["type"], string[]> = {
  Receivable: ["Vendas", "Vendas a prazo", "Serviços", "Aportes", "Outras receitas"],
  Payable: ["Fornecedores", "Aluguel", "Folha de pagamento", "Energia e água", "Impostos", "Marketing", "Manutenção", "Taxas e tarifas", "Outras despesas"],
};
const methods = ["Dinheiro", "Pix", "Cartão", "Boleto", "Transferência", "Débito em conta"];

const todayIso = () => new Date().toISOString().slice(0, 10);
const day = (iso: string) => iso.split("-").reverse().join("/");
const addDays = (iso: string, days: number) => {
  const date = new Date(`${iso}T12:00:00`);
  date.setDate(date.getDate() + days);
  return date.toISOString().slice(0, 10);
};
const addMonths = (iso: string, months: number) => {
  const date = new Date(`${iso}T12:00:00`);
  date.setMonth(date.getMonth() + months);
  return date.toISOString().slice(0, 10);
};

function situation(entry: Entry): { label: string; tone: "ok" | "warn" | "bad" | "muted" | "info" } {
  if (entry.status === "Cancelled") return { label: "Cancelado", tone: "muted" };
  if (entry.status === "Settled") return { label: "Baixado", tone: "ok" };
  const today = todayIso();
  if (entry.dueOn < today) return { label: "Vencido", tone: "bad" };
  if (entry.dueOn === today) return { label: "Vence hoje", tone: "warn" };
  return { label: "Em aberto", tone: "info" };
}

export function Finance({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const base = `/establishments/${establishmentId}`;
  const list = useLoad(() => http.get<Entry[]>(`${base}/financial-entries`), [base]);
  const action = useAction(list.reload);
  const [view, setView] = useState<View>("receivable");
  const [creating, setCreating] = useState<Entry["type"] | null>(null);
  const [settling, setSettling] = useState<Entry | null>(null);
  const entries = list.data ?? [];
  const today = todayIso();

  const kpis = useMemo(() => {
    const open = entries.filter((entry) => entry.status === "Open");
    const sum = (items: Entry[]) => items.reduce((total, entry) => total + entry.amountInCents, 0);
    const monthStart = today.slice(0, 8) + "01";
    const settledMonth = entries.filter((entry) => entry.status === "Settled" && (entry.settledOn ?? entry.settledAt?.slice(0, 10) ?? "") >= monthStart);
    return {
      receivable: sum(open.filter((entry) => entry.type === "Receivable")),
      payable: sum(open.filter((entry) => entry.type === "Payable")),
      overdueIn: sum(open.filter((entry) => entry.type === "Receivable" && entry.dueOn < today)),
      overdueOut: sum(open.filter((entry) => entry.type === "Payable" && entry.dueOn < today)),
      week: sum(open.filter((entry) => entry.type === "Receivable" && entry.dueOn <= addDays(today, 7))) - sum(open.filter((entry) => entry.type === "Payable" && entry.dueOn <= addDays(today, 7))),
      monthResult: sum(settledMonth.filter((entry) => entry.type === "Receivable")) - sum(settledMonth.filter((entry) => entry.type === "Payable")),
    };
  }, [entries, today]);

  /** Projected cash flow: realized balance today plus everything open, day by day for 30 days. */
  const flow = useMemo(() => {
    const realized = entries
      .filter((entry) => entry.status === "Settled")
      .reduce((total, entry) => total + (entry.type === "Receivable" ? entry.amountInCents : -entry.amountInCents), 0);
    const open = entries.filter((entry) => entry.status === "Open");
    const overdue = open.filter((entry) => entry.dueOn < today);
    let balance = realized;
    const days: Array<{ date: string; inflow: number; outflow: number; balance: number }> = [];
    const overdueIn = overdue.filter((entry) => entry.type === "Receivable").reduce((t, e) => t + e.amountInCents, 0);
    const overdueOut = overdue.filter((entry) => entry.type === "Payable").reduce((t, e) => t + e.amountInCents, 0);
    for (let index = 0; index < 30; index++) {
      const date = addDays(today, index);
      const dayEntries = open.filter((entry) => entry.dueOn === date);
      const inflow = dayEntries.filter((entry) => entry.type === "Receivable").reduce((t, e) => t + e.amountInCents, 0) + (index === 0 ? overdueIn : 0);
      const outflow = dayEntries.filter((entry) => entry.type === "Payable").reduce((t, e) => t + e.amountInCents, 0) + (index === 0 ? overdueOut : 0);
      balance += inflow - outflow;
      days.push({ date, inflow, outflow, balance });
    }
    return { realized, days, peak: Math.max(1, ...days.map((entry) => Math.max(entry.inflow, entry.outflow))) };
  }, [entries, today]);

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!creating) return;
    const form = new FormData(event.currentTarget);
    const installments = Math.max(1, Math.min(36, Number(form.get("installments")) || 1));
    const total = toCents(form.get("amount"));
    const first = String(form.get("dueOn"));
    const each = Math.floor(total / installments);
    const done = await action.run(async () => {
      for (let index = 0; index < installments; index++) {
        const amount = index === installments - 1 ? total - each * (installments - 1) : each;
        const description = text(form, "description") ?? "";
        await http.post(`${base}/financial-entries`, {
          type: creating,
          category: String(form.get("category")),
          description: installments > 1 ? `${description} (${index + 1}/${installments})` : description,
          amountInCents: amount,
          dueOn: addMonths(first, index),
          counterparty: text(form, "counterparty"),
          documentNumber: installments > 1 && text(form, "documentNumber") ? `${text(form, "documentNumber")}/${index + 1}` : text(form, "documentNumber"),
        });
      }
    });
    if (done) setCreating(null);
  }

  async function settle(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!settling) return;
    const form = new FormData(event.currentTarget);
    const done = await action.run(() => http.post(`/financial-entries/${settling.id}/settle`, { method: String(form.get("method")), on: String(form.get("on")) }));
    if (done) setSettling(null);
  }

  const titles = (type: Entry["type"]) => (
    <DataTable
      actions={[
        { icon: "check", label: type === "Receivable" ? "Baixar (recebido)" : "Baixar (pago)", tone: "accent", hidden: (row) => row.status !== "Open", onClick: (row) => setSettling(row) },
        { icon: "undo", label: "Estornar baixa", hidden: (row) => row.status !== "Settled" || row.source !== "Manual", onClick: (row) => window.confirm("Estornar a baixa deste título?") && void action.run(() => http.post(`/financial-entries/${row.id}/reopen`)) },
        { icon: "trash", label: "Cancelar título", tone: "danger", hidden: (row) => row.status !== "Open" || row.source !== "Manual", onClick: (row) => window.confirm(`Cancelar "${row.description}"?`) && void action.run(() => http.post(`/financial-entries/${row.id}/cancel`)) },
      ]}
      columns={[
        { key: "doc", title: "Documento", value: (row) => row.documentNumber ?? "", render: (row) => <span className="mono">{row.documentNumber ?? "—"}</span>, hideOnMobile: true },
        { key: "desc", title: "Histórico", value: (row) => `${row.description} ${row.counterparty ?? ""}`, render: (row) => <><strong>{row.description}</strong><small>{row.counterparty ?? sourceLabels[row.source]}</small></> },
        { key: "nature", title: "Natureza", value: (row) => row.category, hideOnMobile: true },
        { key: "due", title: "Vencimento", value: (row) => row.dueOn, render: (row) => <>{day(row.dueOn)}{row.settledOn ? <small>baixa {day(row.settledOn)}{row.settlementMethod ? ` · ${row.settlementMethod}` : ""}</small> : null}</> },
        { key: "value", title: "Valor", align: "right", value: (row) => row.amountInCents, render: (row) => <b className={type === "Payable" ? "is-negative" : ""}>{formatBRL(row.amountInCents)}</b> },
        { key: "origin", title: "Origem", value: (row) => sourceLabels[row.source], render: (row) => <StatusChip tone={row.source === "Manual" ? "muted" : "accent"}>{sourceLabels[row.source]}</StatusChip>, hideOnMobile: true },
        { key: "status", title: "Situação", value: (row) => situation(row).label, render: (row) => <StatusChip tone={situation(row).tone}>{situation(row).label}</StatusChip> },
      ]}
      createLabel={type === "Receivable" ? "Novo a receber" : "Novo a pagar"}
      empty={type === "Receivable" ? "Nenhum título a receber. Os pedidos pagos entram aqui sozinhos." : "Nenhuma conta a pagar. As compras lançadas entram aqui sozinhas."}
      filters={[
        { key: "open", label: "Em aberto", test: (row) => row.status === "Open" },
        { key: "overdue", label: "Vencidos", test: (row) => row.status === "Open" && row.dueOn < today },
        { key: "week", label: "Próximos 7 dias", test: (row) => row.status === "Open" && row.dueOn >= today && row.dueOn <= addDays(today, 7) },
        { key: "settled", label: "Baixados", test: (row) => row.status === "Settled" },
        { key: "cancelled", label: "Cancelados", test: (row) => row.status === "Cancelled" },
      ]}
      loading={list.loading && !list.data}
      onCreate={() => setCreating(type)}
      onRowClick={(row) => row.status === "Open" && setSettling(row)}
      rowKey={(row) => row.id}
      rows={entries.filter((entry) => entry.type === type).sort((a, b) => a.dueOn.localeCompare(b.dueOn))}
      searchPlaceholder="Pesquisar histórico, cliente, fornecedor, documento…"
    />
  );

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Gestão" title="Financeiro" hint="Títulos a receber e a pagar, baixas e fluxo de caixa. Pedidos e compras entram sozinhos." />
      <div className="kpi-strip">
        <div className="kpi"><small>A receber (aberto)</small><strong>{formatBRL(kpis.receivable)}</strong></div>
        <div className="kpi"><small>A pagar (aberto)</small><strong>{formatBRL(kpis.payable)}</strong></div>
        <div className={`kpi ${kpis.overdueIn ? "is-warn" : ""}`}><small>Recebimentos vencidos</small><strong>{formatBRL(kpis.overdueIn)}</strong></div>
        <div className={`kpi ${kpis.overdueOut ? "is-alert" : ""}`}><small>Pagamentos vencidos</small><strong>{formatBRL(kpis.overdueOut)}</strong></div>
        <div className={`kpi ${kpis.week < 0 ? "is-alert" : "is-ok"}`}><small>Saldo previsto 7 dias</small><strong>{formatBRL(kpis.week)}</strong></div>
        <div className={`kpi ${kpis.monthResult < 0 ? "is-alert" : "is-ok"}`}><small>Resultado do mês (baixado)</small><strong>{formatBRL(kpis.monthResult)}</strong></div>
      </div>
      <SubTabs
        onChange={setView}
        options={[
          { value: "receivable", label: "Contas a receber" },
          { value: "payable", label: "Contas a pagar" },
          { value: "flow", label: "Fluxo de caixa" },
          { value: "statement", label: "Extrato" },
        ]}
        value={view}
      />
      <ErrorMessage message={list.error ?? action.error} />

      {view === "receivable" ? titles("Receivable") : null}
      {view === "payable" ? titles("Payable") : null}

      {view === "flow" ? (
        <div className="flow">
          <div className="flow-head">
            <span>Saldo realizado hoje <b className={flow.realized < 0 ? "is-negative" : ""}>{formatBRL(flow.realized)}</b></span>
            <span className="muted">Vencidos entram no dia de hoje. Projeção com tudo que está em aberto.</span>
          </div>
          <div className="dt-wrap">
            <table className="dt-table flow-table">
              <thead><tr><th>Dia</th><th className="is-right">Entradas</th><th className="is-right">Saídas</th><th>Movimento</th><th className="is-right">Saldo</th></tr></thead>
              <tbody>
                {flow.days.filter((entry, index) => index === 0 || entry.inflow || entry.outflow).map((entry) => (
                  <tr key={entry.date}>
                    <td>{entry.date === today ? "Hoje" : day(entry.date)}<small>{new Date(`${entry.date}T12:00:00`).toLocaleDateString("pt-BR", { weekday: "long" })}</small></td>
                    <td className="is-right">{entry.inflow ? <span className="is-positive">{formatBRL(entry.inflow)}</span> : "—"}</td>
                    <td className="is-right">{entry.outflow ? <span className="is-negative">{formatBRL(entry.outflow)}</span> : "—"}</td>
                    <td className="flow-bars">
                      <i className="in" style={{ width: `${(entry.inflow / flow.peak) * 100}%` }} />
                      <i className="out" style={{ width: `${(entry.outflow / flow.peak) * 100}%` }} />
                    </td>
                    <td className="is-right"><b className={entry.balance < 0 ? "is-negative" : ""}>{formatBRL(entry.balance)}</b></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}

      {view === "statement" ? (
        <DataTable
          columns={[
            { key: "date", title: "Data", value: (row) => row.settledOn ?? row.settledAt ?? "", render: (row) => day(row.settledOn ?? row.settledAt?.slice(0, 10) ?? "") },
            { key: "desc", title: "Histórico", value: (row) => `${row.description} ${row.counterparty ?? ""}`, render: (row) => <><strong>{row.description}</strong><small>{row.category}{row.counterparty ? ` · ${row.counterparty}` : ""}</small></> },
            { key: "method", title: "Forma", value: (row) => row.settlementMethod ?? "", hideOnMobile: true },
            { key: "in", title: "Entrada", align: "right", value: (row) => (row.type === "Receivable" ? row.amountInCents : 0), render: (row) => (row.type === "Receivable" ? <span className="is-positive">{formatBRL(row.amountInCents)}</span> : "") },
            { key: "out", title: "Saída", align: "right", value: (row) => (row.type === "Payable" ? row.amountInCents : 0), render: (row) => (row.type === "Payable" ? <span className="is-negative">{formatBRL(row.amountInCents)}</span> : "") },
          ]}
          empty="Nenhuma baixa ainda."
          rowKey={(row) => row.id}
          rows={entries.filter((entry) => entry.status === "Settled").sort((a, b) => (b.settledOn ?? b.settledAt ?? "").localeCompare(a.settledOn ?? a.settledAt ?? ""))}
          searchPlaceholder="Pesquisar no extrato…"
        />
      ) : null}

      {creating ? (
        <FormDrawer onClose={() => setCreating(null)} subtitle="Novo título" title={creating === "Receivable" ? "Conta a receber" : "Conta a pagar"}>
          <form className="drawer-form" onSubmit={create}>
            <label className="field"><span>Histórico *</span><input maxLength={180} name="description" placeholder={creating === "Payable" ? "Ex.: Aluguel de novembro" : "Ex.: Evento empresa X"} required /></label>
            <div className="field-row">
              <label className="field">
                <span>Natureza *</span>
                <select name="category">{natures[creating].map((nature) => <option key={nature}>{nature}</option>)}</select>
              </label>
              <label className="field"><span>Nº do documento</span><input maxLength={30} name="documentNumber" placeholder="NF, boleto, contrato" /></label>
            </div>
            <label className="field"><span>{creating === "Receivable" ? "Cliente" : "Fornecedor / favorecido"}</span><input maxLength={120} name="counterparty" /></label>
            <div className="field-row">
              <label className="field"><span>Valor total (R$) *</span><input inputMode="decimal" name="amount" required /></label>
              <label className="field"><span>1º vencimento *</span><input defaultValue={today} name="dueOn" required type="date" /></label>
            </div>
            <label className="field"><span>Parcelas (mensais)</span><input defaultValue={1} max={36} min={1} name="installments" type="number" /></label>
            <div className="drawer-foot">
              <button className="secondary-button" onClick={() => setCreating(null)} type="button">Cancelar</button>
              <button className="primary-button" disabled={action.busy} type="submit">Lançar</button>
            </div>
          </form>
        </FormDrawer>
      ) : null}

      {settling ? (
        <FormDrawer onClose={() => setSettling(null)} subtitle={settling.type === "Receivable" ? "Baixa de recebimento" : "Baixa de pagamento"} title={settling.description}>
          <div className="kpi-strip">
            <div className="kpi"><small>Valor</small><strong>{formatBRL(settling.amountInCents)}</strong></div>
            <div className="kpi"><small>Vencimento</small><strong className="kpi-text">{day(settling.dueOn)}</strong></div>
          </div>
          <form className="drawer-form" onSubmit={settle}>
            <div className="field-row">
              <label className="field"><span>Data da baixa *</span><input defaultValue={today} name="on" required type="date" /></label>
              <label className="field"><span>Forma *</span><select name="method">{methods.map((method) => <option key={method}>{method}</option>)}</select></label>
            </div>
            <div className="drawer-foot">
              <button className="secondary-button" onClick={() => setSettling(null)} type="button">Cancelar</button>
              <button className="primary-button" disabled={action.busy} type="submit">Confirmar baixa</button>
            </div>
          </form>
        </FormDrawer>
      ) : null}
    </section>
  );
}
