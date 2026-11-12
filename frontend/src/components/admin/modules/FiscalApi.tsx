"use client";

import { type FormEvent, useState } from "react";
import { API_BASE_URL } from "@/lib/api";
import { formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { StatusChip } from "../DataTable";
import { Empty, ErrorMessage, useAction, useLoad } from "./shared";

interface ApiKey {
  id: string;
  name: string;
  prefix: string;
  isTest: boolean;
  canIssue: boolean;
  createdBy: string;
  createdAt: string;
  lastUsedAt: string | null;
  revokedAt: string | null;
  partner?: string | null;
}

interface Webhook {
  id: string;
  url: string;
  events: string[];
  isActive: boolean;
  createdAt: string;
  partner?: string | null;
}

interface Delivery {
  id: string;
  webhookId: string;
  event: string;
  documentId: string | null;
  status: "Pending" | "Delivered" | "Failed";
  attempts: number;
  nextAttemptAt: string;
  lastStatusCode: number | null;
  lastError: string | null;
  createdAt: string;
  deliveredAt: string | null;
}

interface Overview {
  keys: ApiKey[];
  webhooks: Webhook[];
  deliveries: Delivery[];
  events: string[];
}

const eventLabel: Record<string, string> = {
  "note.authorized": "Nota autorizada",
  "note.rejected": "Nota rejeitada",
  "note.cancelled": "Nota cancelada",
  "note.corrected": "Carta de correção",
  "note.contingency": "Nota em contingência",
  ping: "Teste",
};

const sample = `POST ${API_BASE_URL}/api/fiscal/v1/notes
X-Api-Key: zf_test_...
Content-Type: application/json

{
  "externalId": "venda-1042",        // seu id: repetir não gera nota nova
  "model": 65,                        // 65 NFC-e · 55 NF-e
  "lines": [{
    "code": "P1", "description": "Açaí 500ml", "quantity": 1,
    "totalInCents": 1800, "ncm": "21069090", "cfop": "5102",
    "icmsCst": "00", "icmsRate": 18,
    "pisCofinsCst": "01", "pisRate": 0.65, "cofinsRate": 3
  }],
  "payments": [{ "method": "pix", "amountInCents": 1800 }]
}

→ 201 { "id": "...", "status": "authorized", "accessKey": "3126...", ... }`;

const routes = [
  ["GET", "/me", "Qual loja e que permissão a chave tem"],
  ["GET", "/status", "SEFAZ no ar? (?model=65 ou 55)"],
  ["POST", "/notes", "Emite NFC-e ou NF-e (venda, devolução, ajuste, complementar)"],
  ["GET", "/notes", "Lista notas (?from, ?to, ?externalId, ?take)"],
  ["GET", "/notes/{id}", "Uma nota"],
  ["GET", "/notes/{id}/xml", "XML autorizado"],
  ["POST", "/notes/{id}/cancel", "Cancela ({ reason } com 15 a 255 letras)"],
  ["POST", "/notes/{id}/correction", "Carta de correção da NF-e ({ text })"],
  ["POST", "/service-notes", "Emite NFS-e nacional ({ externalId, taker, serviceCode, description, amountInCents, issRate })"],
  ["GET", "/service-notes", "Lista NFS-e; /service-notes/{id}, /xml e /cancel como nas notas"],
  ["GET", "/books/assessment", "Apuração do mês (?year, ?month); só chave de produção"],
  ["GET", "/books/register", "Livro de entradas e saídas; só chave de produção"],
];

export function FiscalApiView({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const base = `/establishments/${establishmentId}/fiscal/api`;
  const overview = useLoad(() => http.get<Overview>(base), [base]);
  const action = useAction(() => overview.reload());
  const [secret, setSecret] = useState<{ label: string; value: string } | null>(null);
  const data = overview.data;
  const activeKeys = (data?.keys ?? []).filter((key) => !key.revokedAt);
  const revokedKeys = (data?.keys ?? []).filter((key) => key.revokedAt);

  async function createKey(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const values = new FormData(form);
    await action.run(async () => {
      const created = await http.post<{ secret: string }>(`${base}/keys`, {
        name: String(values.get("name") ?? ""),
        isTest: values.get("kind") === "test",
        canIssue: values.get("canIssue") === "on",
      });
      setSecret({ label: "Chave da API", value: created.secret });
      form.reset();
    });
  }

  async function createWebhook(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const values = new FormData(form);
    await action.run(async () => {
      const created = await http.post<{ secret: string }>(`${base}/webhooks`, {
        url: String(values.get("url") ?? ""),
        events: values.getAll("events").map(String),
      });
      setSecret({ label: "Segredo do webhook", value: created.secret });
      form.reset();
    });
  }

  function revoke(key: ApiKey) {
    if (window.confirm(`Revogar a chave "${key.name}"? Quem usa ela para de emitir e consultar na hora.`)) {
      void action.run(() => http.del(`${base}/keys/${key.id}`));
    }
  }

  function removeWebhook(webhook: Webhook) {
    if (window.confirm(`Parar de enviar eventos para ${webhook.url}?`)) {
      void action.run(() => http.del(`${base}/webhooks/${webhook.id}`));
    }
  }

  return (
    <>
      <article className="card vx-box">
        <div className="vx-panel-title"><h3>API fiscal <em>para outros sistemas</em></h3></div>
        <p className="muted">
          ERP, PDV, loja virtual ou sistema do contador emitem e consultam as notas desta loja por aqui, usando o mesmo certificado e a mesma numeração.
          A <strong>chave de teste</strong> simula tudo sem falar com a SEFAZ e sem gastar número: serve para o parceiro montar a integração antes de valer.
        </p>
        <ErrorMessage message={overview.error ?? action.error} />

        {secret ? (
          <div className="zest-key">
            <strong>{secret.label}: copie agora, ela não aparece de novo.</strong>
            <code>{secret.value}</code>
            <div className="fx-actions">
              <button className="secondary-button" onClick={() => void navigator.clipboard?.writeText(secret.value)} type="button">Copiar</button>
              <button className="secondary-button" onClick={() => setSecret(null)} type="button">Já guardei</button>
            </div>
          </div>
        ) : null}

        <form className="module-form module-form-flat" onSubmit={createKey}>
          <label className="field"><span>Nome (quem vai usar)</span><input maxLength={60} minLength={2} name="name" placeholder="Ex.: ERP do escritório" required /></label>
          <label className="field">
            <span>Tipo</span>
            <select defaultValue="test" name="kind">
              <option value="test">Teste (simulação, sem SEFAZ)</option>
              <option value="live">Produção (notas de verdade)</option>
            </select>
          </label>
          <label className="module-check"><input defaultChecked name="canIssue" type="checkbox" /> Pode emitir, cancelar e corrigir</label>
          <div><button className="primary-button" disabled={action.busy} type="submit">Criar chave</button></div>
        </form>

        {data && activeKeys.length === 0 ? <Empty>Nenhuma chave ativa.</Empty> : null}
        {activeKeys.length ? (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Nome</th><th>Chave</th><th>Tipo</th><th>Permissão</th><th>Último uso</th><th /></tr></thead>
              <tbody>
                {activeKeys.map((key) => (
                  <tr key={key.id}>
                    <td><strong>{key.name}</strong>{key.partner ? <> <StatusChip tone="info">{key.partner}</StatusChip></> : null}<small className="muted"> criada por {key.createdBy} em {formatDateTime(key.createdAt)}</small></td>
                    <td><code>{key.prefix}…</code></td>
                    <td>{key.isTest ? <StatusChip tone="info">Teste</StatusChip> : <StatusChip tone="accent">Produção</StatusChip>}</td>
                    <td>{key.canIssue ? "Emite e consulta" : "Só consulta"}</td>
                    <td>{key.lastUsedAt ? formatDateTime(key.lastUsedAt) : <span className="muted">nunca</span>}</td>
                    <td><button className="secondary-button" disabled={action.busy} onClick={() => revoke(key)} type="button">Revogar</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
        {revokedKeys.length ? <p className="muted">{revokedKeys.length} chave(s) revogada(s) não funcionam mais.</p> : null}
      </article>

      <article className="card vx-box">
        <div className="vx-panel-title"><h3>Webhooks <em>avisos automáticos</em></h3></div>
        <p className="muted">
          Quando uma nota desta loja é autorizada, rejeitada, cancelada ou corrigida (pelo painel, pelo pedido ou pela API), o sistema avisa o endereço cadastrado.
          Cada aviso vai assinado no header <code>X-Zest-Signature</code> e, se falhar, é reenviado por até um dia.
        </p>
        <form className="module-form module-form-flat" onSubmit={createWebhook}>
          <label className="field field-wide"><span>Endereço (https)</span><input name="url" placeholder="https://seu-sistema.com.br/webhooks/fiscal" required type="url" /></label>
          <fieldset className="fx-events">
            <legend>Eventos</legend>
            {(data?.events ?? []).map((name) => (
              <label className="module-check" key={name}><input defaultChecked={name !== "note.contingency"} name="events" type="checkbox" value={name} /> {eventLabel[name] ?? name}</label>
            ))}
          </fieldset>
          <div><button className="primary-button" disabled={action.busy} type="submit">Cadastrar webhook</button></div>
        </form>

        {data && data.webhooks.length === 0 ? <Empty>Nenhum webhook cadastrado.</Empty> : null}
        {data?.webhooks.length ? (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Endereço</th><th>Eventos</th><th>Desde</th><th /></tr></thead>
              <tbody>
                {data.webhooks.map((webhook) => (
                  <tr key={webhook.id}>
                    <td><code>{webhook.url}</code>{webhook.partner ? <> <StatusChip tone="info">{webhook.partner}</StatusChip></> : null}</td>
                    <td>{webhook.events.map((name) => eventLabel[name] ?? name).join(", ")}</td>
                    <td>{formatDateTime(webhook.createdAt)}</td>
                    <td className="fx-actions">
                      <button className="secondary-button" disabled={action.busy} onClick={() => void action.run(() => http.post(`${base}/webhooks/${webhook.id}/test`))} type="button">Enviar teste</button>
                      <button className="secondary-button" disabled={action.busy} onClick={() => removeWebhook(webhook)} type="button">Remover</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {data?.deliveries.length ? (
          <>
            <div className="vx-panel-title">
              <h3>Últimos envios</h3>
              <button className="secondary-button" disabled={overview.loading} onClick={() => void overview.reload()} type="button">Atualizar</button>
            </div>
            <div className="admin-table-wrap">
              <table className="admin-table">
                <thead><tr><th>Quando</th><th>Evento</th><th>Situação</th><th>Tentativas</th><th>Resposta</th><th /></tr></thead>
                <tbody>
                  {data.deliveries.map((delivery) => (
                    <tr key={delivery.id}>
                      <td>{formatDateTime(delivery.createdAt)}</td>
                      <td>{eventLabel[delivery.event] ?? delivery.event}</td>
                      <td>
                        {delivery.status === "Delivered" ? <StatusChip tone="ok">Entregue</StatusChip> : null}
                        {delivery.status === "Pending" ? <StatusChip tone="warn">{delivery.attempts ? `Nova tentativa ${formatDateTime(delivery.nextAttemptAt)}` : "Na fila"}</StatusChip> : null}
                        {delivery.status === "Failed" ? <StatusChip tone="bad">Desistiu</StatusChip> : null}
                      </td>
                      <td className="num">{delivery.attempts}</td>
                      <td className="muted">{delivery.lastError ?? (delivery.lastStatusCode ? `HTTP ${delivery.lastStatusCode}` : "")}</td>
                      <td>{delivery.status !== "Delivered" && data.webhooks.some((webhook) => webhook.id === delivery.webhookId) ? <button className="secondary-button" disabled={action.busy} onClick={() => void action.run(() => http.post(`/establishments/${establishmentId}/fiscal/api/deliveries/${delivery.id}/retry`))} type="button">Reenviar</button> : null}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        ) : null}
      </article>

      <article className="card vx-box">
        <div className="vx-panel-title"><h3>Guia rápido para quem integra</h3></div>
        <p className="muted">
          Endereço base <code>{API_BASE_URL}/api/fiscal/v1</code>. Toda chamada leva o header <code>X-Api-Key</code>. Limite de 300 chamadas por minuto por chave.
          A tributação de cada item vem do sistema que chama: é ele quem conhece o produto.
        </p>
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Método</th><th>Rota</th><th>Para quê</th></tr></thead>
            <tbody>
              {routes.map(([method, path, purpose]) => (
                <tr key={`${method}${path}`}><td><code>{method}</code></td><td><code>{path}</code></td><td>{purpose}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
        <pre className="zest-snippet">{sample}</pre>
        <p className="muted">
          Conferir o webhook: calcule HMAC-SHA256 de <code>{"<X-Zest-Timestamp>.<corpo>"}</code> com o segredo e compare com o valor depois de <code>sha256=</code> em <code>X-Zest-Signature</code>.
          Recuse avisos com mais de 5 minutos e use <code>X-Zest-Delivery</code> para não processar o mesmo aviso duas vezes. Cada aviso traz a nota como ela está no momento do envio.
        </p>
      </article>
    </>
  );
}
