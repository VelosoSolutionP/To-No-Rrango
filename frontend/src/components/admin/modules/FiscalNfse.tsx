"use client";

import { type FormEvent, useState } from "react";
import { formatBRL, formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { StatusChip } from "../DataTable";
import { Empty, ErrorMessage, toCents, useAction, useLoad } from "./shared";

interface NfseSettings {
  municipalRegistration: string | null;
  series: number;
  nextNumber: number;
  environment: number;
  hasCertificate: boolean;
  cityIbgeCode: string | null;
}

interface ServiceNote {
  id: string;
  externalId: string;
  series: number;
  number: number;
  status: "authorized" | "rejected" | "cancelled" | "contingency" | "processing";
  accessKey: string | null;
  message: string | null;
  takerDocument: string;
  takerName: string;
  serviceCode: string;
  description: string;
  amountInCents: number;
  issRate: number | null;
  issWithheld: boolean;
  competence: string;
  cancelReason: string | null;
  hasXml: boolean;
  danfseUrl: string | null;
  createdAt: string;
}

const statusChip = {
  authorized: <StatusChip tone="ok">Autorizada</StatusChip>,
  rejected: <StatusChip tone="bad">Rejeitada</StatusChip>,
  cancelled: <StatusChip tone="muted">Cancelada</StatusChip>,
  contingency: <StatusChip tone="warn">Contingência</StatusChip>,
  processing: <StatusChip tone="warn">Sem resposta</StatusChip>,
};

/** NFS-e of the national standard: settings, issue, list, cancel and the DANFSe. */
export function FiscalNfseView({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const base = `/establishments/${establishmentId}/fiscal/nfse`;
  const settings = useLoad(() => http.get<NfseSettings>(`${base}/settings`), [base]);
  const notes = useLoad(() => http.get<ServiceNote[]>(base), [base]);
  const action = useAction(async () => { await notes.reload(); await settings.reload(); });
  const [last, setLast] = useState<ServiceNote | null>(null);

  async function saveSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    await action.run(() => http.put(`${base}/settings`, {
      municipalRegistration: String(values.get("im") ?? "") || null,
      series: Number(values.get("series")),
      nextNumber: Number(values.get("next")),
    }));
  }

  async function issue(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const values = Object.fromEntries(new FormData(form)) as Record<string, string>;
    const rate = values.rate?.trim() ? Number(values.rate.replace(",", ".")) : null;
    await action.run(async () => {
      const note = await http.post<ServiceNote>(base, {
        externalId: values.externalId || `nfse-${Date.now()}`,
        taker: {
          document: values.document,
          name: values.name,
          email: values.email || null,
          street: values.street || null,
          number: values.number || null,
          district: values.district || null,
          cityIbgeCode: values.city || null,
          postalCode: values.cep || null,
        },
        serviceCode: values.serviceCode,
        municipalServiceCode: values.municipalCode || null,
        description: values.description,
        amountInCents: toCents(values.amount),
        issRate: rate,
        issWithheld: values.withheld === "on",
      });
      setLast(note);

      if (note.status === "authorized") {
        form.reset();
      }
    });
  }

  async function downloadXml(note: ServiceNote) {
    await action.run(async () => {
      const xml = await http.get<string>(`${base}/${note.id}/xml`);
      const link = document.createElement("a");
      link.href = URL.createObjectURL(new Blob([xml], { type: "application/xml" }));
      link.download = `nfse-${note.accessKey ?? note.number}.xml`;
      link.click();
      URL.revokeObjectURL(link.href);
    });
  }

  function cancel(note: ServiceNote) {
    const reason = window.prompt("Justificativa do cancelamento (15 a 255 letras):");

    if (reason) {
      void action.run(() => http.post(`${base}/${note.id}/cancel`, { reason }));
    }
  }

  const config = settings.data;

  return (
    <>
      <article className="card vx-box">
        <div className="vx-panel-title"><h3>NFS-e <em>padrão nacional</em></h3></div>
        <p className="muted">
          Nota de serviço emitida direto no Sefin Nacional, com o mesmo certificado A1 das notas de venda. Ambiente atual:{" "}
          <strong>{config ? (config.environment === 1 ? "produção" : "produção restrita (homologação, sem valor fiscal)") : "…"}</strong>.
          A cidade da loja precisa ter aderido ao padrão nacional.
        </p>
        <ErrorMessage message={settings.error ?? action.error} />
        {config && !config.hasCertificate ? <p className="form-error">Envie o certificado A1 em Configuração antes de emitir.</p> : null}
        {config ? (
          <form className="module-form module-form-flat" key={`${config.series}-${config.nextNumber}`} onSubmit={saveSettings}>
            <label className="field"><span>Inscrição municipal</span><input defaultValue={config.municipalRegistration ?? ""} maxLength={15} name="im" /></label>
            <label className="field"><span>Série da DPS</span><input defaultValue={config.series} max={49999} min={1} name="series" type="number" /></label>
            <label className="field"><span>Próximo número</span><input defaultValue={config.nextNumber} min={1} name="next" type="number" /></label>
            <div><button className="secondary-button" disabled={action.busy} type="submit">Salvar</button></div>
          </form>
        ) : null}
      </article>

      <article className="card vx-box">
        <div className="vx-panel-title"><h3>Emitir NFS-e</h3></div>
        <form className="module-form" onSubmit={issue}>
          <label className="field"><span>CPF ou CNPJ do tomador</span><input inputMode="numeric" name="document" required /></label>
          <label className="field"><span>Nome do tomador</span><input maxLength={150} name="name" required /></label>
          <label className="field"><span>E-mail (opcional)</span><input name="email" type="email" /></label>
          <label className="field"><span>Rua (opcional)</span><input name="street" /></label>
          <label className="field"><span>Número</span><input name="number" /></label>
          <label className="field"><span>Bairro</span><input name="district" /></label>
          <label className="field"><span>Cidade (código IBGE)</span><input inputMode="numeric" maxLength={7} name="city" placeholder="3118601" /></label>
          <label className="field"><span>CEP</span><input inputMode="numeric" name="cep" /></label>
          <label className="field"><span>Código de tributação nacional</span><input inputMode="numeric" maxLength={6} name="serviceCode" placeholder="010101" required /></label>
          <label className="field"><span>Código municipal (opcional)</span><input maxLength={3} name="municipalCode" /></label>
          <label className="field field-wide"><span>Descrição do serviço</span><textarea maxLength={2000} name="description" required rows={3} /></label>
          <label className="field"><span>Valor (R$)</span><input inputMode="decimal" name="amount" placeholder="0,00" required /></label>
          <label className="field"><span>Alíquota ISS % (vazio = a da cidade)</span><input inputMode="decimal" name="rate" placeholder="2 a 5" /></label>
          <label className="field"><span>Seu código para esta nota (opcional)</span><input maxLength={40} name="externalId" placeholder="ex.: OS-1042" /></label>
          <label className="module-check"><input name="withheld" type="checkbox" /> ISS retido pelo tomador</label>
          <div><button className="primary-button" disabled={action.busy || !config?.hasCertificate} type="submit">{action.busy ? "Enviando…" : "Emitir NFS-e"}</button></div>
        </form>
        {last ? (
          <p className={last.status === "authorized" ? "muted" : "form-error"}>
            DPS {last.series}/{last.number}: {last.status === "authorized" ? `autorizada, chave ${last.accessKey}` : last.message}
          </p>
        ) : null}
      </article>

      <article className="card vx-box">
        <div className="vx-panel-title"><h3>NFS-e emitidas</h3></div>
        <ErrorMessage message={notes.error} />
        {notes.data && notes.data.length === 0 ? <Empty>Nenhuma NFS-e emitida.</Empty> : null}
        {notes.data?.length ? (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>DPS</th><th>Tomador</th><th>Serviço</th><th className="num">Valor</th><th>Situação</th><th>Quando</th><th /></tr></thead>
              <tbody>
                {notes.data.map((note) => (
                  <tr key={note.id}>
                    <td>{note.series}/{note.number}<small className="muted"> {note.externalId}</small></td>
                    <td>{note.takerName}<small className="muted"> {note.takerDocument}</small></td>
                    <td><code>{note.serviceCode}</code> {note.description.slice(0, 60)}</td>
                    <td className="num">{formatBRL(note.amountInCents)}{note.issWithheld ? <small className="muted"> ISS retido</small> : null}</td>
                    <td>{statusChip[note.status]}{note.status !== "authorized" && note.message ? <small className="muted"> {note.message}</small> : null}</td>
                    <td>{formatDateTime(note.createdAt)}</td>
                    <td className="fx-actions">
                      {note.danfseUrl ? <a className="secondary-button" href={note.danfseUrl} rel="noreferrer" target="_blank">DANFSe</a> : null}
                      {note.hasXml ? <button className="secondary-button" disabled={action.busy} onClick={() => void downloadXml(note)} type="button">XML</button> : null}
                      {note.status === "authorized" ? <button className="secondary-button" disabled={action.busy} onClick={() => cancel(note)} type="button">Cancelar</button> : null}
                    </td>
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
