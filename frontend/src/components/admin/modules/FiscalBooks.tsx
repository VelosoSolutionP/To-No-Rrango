"use client";

import { useState } from "react";
import { formatBRL, formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { StatusChip } from "../DataTable";
import { Empty, ErrorMessage, SubTabs, toCents, useLoad } from "./shared";

interface TaxLine {
  tax: string;
  label: string;
  debitsInCents: number;
  creditsInCents: number;
  balanceInCents: number;
  basis: string;
}

interface CfopTotal {
  isEntry: boolean;
  cfop: string;
  icmsCst: string;
  icmsRate: number;
  valueInCents: number;
  icmsBaseInCents: number;
  icmsInCents: number;
}

interface Assessment {
  regime: string;
  exitsInCents: number;
  entriesInCents: number;
  exitNotes: number;
  entryNotes: number;
  taxes: TaxLine[];
  byCfop: CfopTotal[];
  warnings: string[];
}

interface BookRow {
  issuedOn: string;
  isEntry: boolean;
  model: number;
  series: string;
  number: number;
  key: string;
  partyDocument: string;
  partyName: string;
  partyUf: string;
  cfops: string;
  totalInCents: number;
  icmsBaseInCents: number;
  icmsInCents: number;
  stInCents: number;
  ipiInCents: number;
  pisInCents: number;
  cofinsInCents: number;
}

interface Book {
  rows: BookRow[];
  warnings: string[];
}

interface PeriodSummary {
  year: number;
  month: number;
  exitNotes: number;
  entryNotes: number;
  exitsInCents: number;
  entriesInCents: number;
  filesGenerated: number;
  lastFileAt: string | null;
  files: string[];
}

interface AuditEntry {
  id: string;
  at: string;
  actor: string;
  category: string;
  action: string;
  target: string | null;
  period: string | null;
  details: string | null;
  succeeded: boolean;
  statusCode: number;
}

const monthNames = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

function lastMonth() {
  const date = new Date();
  date.setMonth(date.getMonth() - 1);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
}

const day = (value: string) => value.split("-").reverse().join("/");
const decimal = (cents: number) => (cents / 100).toFixed(2).replace(".", ",");

/** Spreadsheet-friendly CSV (semicolon, comma decimals, BOM for Excel). */
function downloadCsv(name: string, header: string[], rows: Array<Array<string | number>>) {
  const escape = (value: string | number) => {
    const text = String(value);
    return /[;"\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
  };
  const body = [header, ...rows].map((row) => row.map(escape).join(";")).join("\r\n");
  const url = URL.createObjectURL(new Blob(["﻿" + body], { type: "text/csv;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
}

export function FiscalBooksView({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const [period, setPeriod] = useState(lastMonth);
  const [previous, setPrevious] = useState("");
  const [show, setShow] = useState<"entries" | "exits">("exits");
  const [year, month] = period.split("-").map(Number);
  const base = `/establishments/${establishmentId}/fiscal`;
  const credit = toCents(previous);
  const assessment = useLoad(
    () => http.get<Assessment>(`${base}/assessment?year=${year}&month=${month}${credit > 0 ? `&previousIcmsCredit=${credit}` : ""}`),
    [base, year, month, credit],
  );
  const book = useLoad(() => http.get<Book>(`${base}/book?year=${year}&month=${month}`), [base, year, month]);
  const rows = (book.data?.rows ?? []).filter((row) => row.isEntry === (show === "entries"));
  const data = assessment.data;

  function exportBook() {
    const all = book.data?.rows ?? [];
    downloadCsv(
      `livro-entradas-saidas-${period}.csv`,
      ["Tipo", "Data", "Modelo", "Série", "Número", "Chave", "CNPJ/CPF", "Participante", "UF", "CFOP", "Valor", "Base ICMS", "ICMS", "ICMS-ST", "IPI", "PIS", "COFINS"],
      all.map((row) => [row.isEntry ? "Entrada" : "Saída", day(row.issuedOn), row.model, row.series, row.number, row.key, row.partyDocument, row.partyName, row.partyUf, row.cfops,
        decimal(row.totalInCents), decimal(row.icmsBaseInCents), decimal(row.icmsInCents), decimal(row.stInCents), decimal(row.ipiInCents), decimal(row.pisInCents), decimal(row.cofinsInCents)]),
    );
  }

  function exportAssessment() {
    if (!data) return;
    downloadCsv(
      `apuracao-${period}.csv`,
      ["Imposto", "Débitos", "Créditos", "Saldo", "Base"],
      [
        ...data.taxes.map((tax) => [tax.label, decimal(tax.debitsInCents), decimal(tax.creditsInCents), decimal(tax.balanceInCents), tax.basis]),
        [],
        ["Tipo", "CFOP", "CST", "Alíquota", "Valor", "Base ICMS", "ICMS"],
        ...data.byCfop.map((total) => [total.isEntry ? "Entrada" : "Saída", total.cfop, total.icmsCst, String(total.icmsRate).replace(".", ","), decimal(total.valueInCents), decimal(total.icmsBaseInCents), decimal(total.icmsInCents)]),
      ],
    );
  }

  return (
    <>
      <article className="card vx-box fx-card">
        <div className="vx-panel-title"><h3>Apuração do mês <em>para conferência</em></h3></div>
        <p className="muted">Calculada a partir dos XMLs das notas emitidas e das compras. Serve para conferir com o contador; as guias e os valores definitivos são dele.</p>
        <div className="module-form module-form-flat">
          <label className="field"><span>Competência</span><input onChange={(event) => setPeriod(event.target.value)} type="month" value={period} /></label>
          <label className="field"><span>Saldo credor de ICMS do mês anterior (R$)</span><input inputMode="decimal" onChange={(event) => setPrevious(event.target.value)} placeholder="0,00" value={previous} /></label>
          <button className="secondary-button" disabled={!data} onClick={exportAssessment} type="button">Baixar apuração (planilha)</button>
        </div>
        <ErrorMessage message={assessment.error} />
        {data ? (
          <>
            <div className="kpi-strip">
              <div className="kpi"><small>Regime</small><strong className="kpi-text">{data.regime}</strong></div>
              <div className="kpi"><small>Saídas ({data.exitNotes})</small><strong>{formatBRL(data.exitsInCents)}</strong></div>
              <div className="kpi"><small>Entradas ({data.entryNotes})</small><strong>{formatBRL(data.entriesInCents)}</strong></div>
            </div>
            <div className="admin-table-wrap">
              <table className="admin-table">
                <thead><tr><th>Imposto</th><th className="num">Débitos</th><th className="num">Créditos</th><th className="num">A recolher</th><th>Como foi calculado</th></tr></thead>
                <tbody>
                  {data.taxes.map((tax) => (
                    <tr key={tax.tax}>
                      <td><strong>{tax.label}</strong></td>
                      <td className="num">{formatBRL(tax.debitsInCents)}</td>
                      <td className="num">{formatBRL(tax.creditsInCents)}</td>
                      <td className="num">{tax.balanceInCents < 0 ? <span title="Saldo credor para o próximo mês">{formatBRL(-tax.balanceInCents)} credor</span> : <b>{formatBRL(tax.balanceInCents)}</b>}</td>
                      <td className="muted">{tax.basis}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {data.warnings.length ? <ul className="fx-sync">{data.warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul> : null}
          </>
        ) : null}
      </article>

      {data && data.byCfop.length ? (
        <article className="card vx-box">
          <div className="vx-panel-title"><h3>Resumo por CFOP, CST e alíquota</h3></div>
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Tipo</th><th>CFOP</th><th>CST</th><th className="num">Alíquota</th><th className="num">Valor</th><th className="num">Base ICMS</th><th className="num">ICMS</th></tr></thead>
              <tbody>
                {data.byCfop.map((total) => (
                  <tr key={`${total.isEntry}-${total.cfop}-${total.icmsCst}-${total.icmsRate}`}>
                    <td>{total.isEntry ? "Entrada" : "Saída"}</td>
                    <td>{total.cfop}</td>
                    <td>{total.icmsCst}</td>
                    <td className="num">{String(total.icmsRate).replace(".", ",")}%</td>
                    <td className="num">{formatBRL(total.valueInCents)}</td>
                    <td className="num">{formatBRL(total.icmsBaseInCents)}</td>
                    <td className="num">{formatBRL(total.icmsInCents)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </article>
      ) : null}

      <article className="card vx-box">
        <div className="vx-panel-title">
          <h3>Livro de entradas e saídas</h3>
          <button className="secondary-button" disabled={!book.data?.rows.length} onClick={exportBook} type="button">Baixar livro (planilha)</button>
        </div>
        <SubTabs
          onChange={setShow}
          options={[
            { value: "exits" as const, label: `Saídas (${(book.data?.rows ?? []).filter((row) => !row.isEntry).length})` },
            { value: "entries" as const, label: `Entradas (${(book.data?.rows ?? []).filter((row) => row.isEntry).length})` },
          ]}
          value={show}
        />
        <ErrorMessage message={book.error} />
        {rows.length === 0 && book.data ? <Empty>Nenhuma nota de {show === "entries" ? "entrada" : "saída"} com XML neste mês.</Empty> : null}
        {rows.length ? (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Data</th><th>Nota</th><th>Participante</th><th>CFOP</th><th className="num">Valor</th><th className="num">ICMS</th><th className="num">PIS</th><th className="num">COFINS</th></tr></thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.key || `${row.model}-${row.series}-${row.number}`}>
                    <td>{day(row.issuedOn)}</td>
                    <td>{row.model === 65 ? "NFC-e" : "NF-e"} {row.series}/{row.number}</td>
                    <td>{row.partyName || "Consumidor"}<small className="muted">{row.partyDocument ? ` ${row.partyDocument}` : ""}{row.partyUf ? ` · ${row.partyUf}` : ""}</small></td>
                    <td>{row.cfops}</td>
                    <td className="num">{formatBRL(row.totalInCents)}</td>
                    <td className="num">{formatBRL(row.icmsInCents)}</td>
                    <td className="num">{formatBRL(row.pisInCents)}</td>
                    <td className="num">{formatBRL(row.cofinsInCents)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
        {book.data?.warnings.length ? <ul className="fx-sync">{book.data.warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul> : null}
      </article>
    </>
  );
}

const categoryLabel: Record<string, string> = {
  emissao: "Emissão",
  cancelamento: "Cancelamento",
  correcao: "Correção",
  inutilizacao: "Inutilização",
  certificado: "Certificado",
  configuracao: "Configuração",
  recebidas: "Notas recebidas",
  arquivo: "Arquivo",
  integracao: "API e webhooks",
  contador: "Contador",
};

export function FiscalHistoryView({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const base = `/establishments/${establishmentId}/fiscal`;
  const periods = useLoad(() => http.get<PeriodSummary[]>(`${base}/periods?months=12`), [base]);
  const audit = useLoad(() => http.get<AuditEntry[]>(`${base}/audit?take=300`), [base]);
  const [category, setCategory] = useState("");
  const entries = (audit.data ?? []).filter((entry) => !category || entry.category === category);

  return (
    <>
      <article className="card vx-box fx-card">
        <div className="vx-panel-title"><h3>Competências <em>últimos 12 meses</em></h3></div>
        <p className="muted">Quantas notas entraram e saíram em cada mês e quais arquivos já foram gerados para o fisco ou para o contador.</p>
        <ErrorMessage message={periods.error} />
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Mês</th><th className="num">Saídas</th><th className="num">Entradas</th><th>Arquivos gerados</th><th>Último</th></tr></thead>
            <tbody>
              {(periods.data ?? []).map((item) => (
                <tr key={`${item.year}-${item.month}`}>
                  <td><strong>{monthNames[item.month - 1]}/{item.year}</strong></td>
                  <td className="num">{item.exitNotes} · {formatBRL(item.exitsInCents)}</td>
                  <td className="num">{item.entryNotes} · {formatBRL(item.entriesInCents)}</td>
                  <td>{item.files.length ? item.files.map((file) => <StatusChip key={file} tone="ok">{file}</StatusChip>) : <span className="muted">nenhum</span>}</td>
                  <td>{item.lastFileAt ? formatDateTime(item.lastFileAt) : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>

      <article className="card vx-box">
        <div className="vx-panel-title">
          <h3>Auditoria fiscal</h3>
          <select aria-label="Filtrar por tipo" onChange={(event) => setCategory(event.target.value)} value={category}>
            <option value="">Todas as operações</option>
            {Object.entries(categoryLabel).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </div>
        <p className="muted">Toda operação fiscal fica registrada com quem fez, quando e o resultado. O registro não pode ser alterado nem apagado.</p>
        <ErrorMessage message={audit.error} />
        {entries.length === 0 && audit.data ? <Empty>Nenhuma operação fiscal registrada ainda.</Empty> : null}
        {entries.length ? (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Quando</th><th>Quem</th><th>Operação</th><th>Detalhe</th><th>Resultado</th></tr></thead>
              <tbody>
                {entries.map((entry) => (
                  <tr key={entry.id}>
                    <td>{formatDateTime(entry.at)}</td>
                    <td>{entry.actor}</td>
                    <td><strong>{entry.action}</strong><small className="muted"> {categoryLabel[entry.category] ?? entry.category}{entry.period ? ` · ${entry.period}` : ""}</small></td>
                    <td className="muted">{entry.details ?? ""}</td>
                    <td>{entry.succeeded ? <StatusChip tone="ok">Feito</StatusChip> : <StatusChip tone="bad">{`Recusado (${entry.statusCode})`}</StatusChip>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </article>
    </>
  );
}
