"use client";

import { type FormEvent, useState } from "react";
import { formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { StatusChip } from "../DataTable";
import { Empty, ErrorMessage, useAction, useLoad } from "./shared";

export interface AccountantMessage {
  id: string;
  fromAccountant: boolean;
  mine: boolean;
  author: string;
  text: string;
  isRequest: boolean;
  resolvedAt: string | null;
  resolvedBy: string | null;
  readAt: string | null;
  createdAt: string;
}

interface AccountantLink {
  id: string;
  kind: "Accounting" | "SoftwareHouse";
  invitedEmail: string;
  status: "Invited" | "Active" | "Revoked";
  firmName: string | null;
  firmCrc: string | null;
  firmIsPartner: boolean;
  invitedBy: string;
  createdAt: string;
  codeExpiresAt: string | null;
  acceptedAt: string | null;
  revokedAt: string | null;
}

interface Desk {
  links: AccountantLink[];
  messages: AccountantMessage[];
}

/**
 * The conversation between store and accountant. Same component on both sides: the routes have the
 * same shape in the panel and in the accountant portal, only the http prefix changes.
 */
export function AccountantChat({ http, establishmentId, side }: { http: AdminHttp; establishmentId: string; side: "store" | "accountant" }) {
  const path = `/establishments/${establishmentId}/fiscal/accountant/messages`;
  const conversation = useLoad(() => http.get<AccountantMessage[]>(path), [path]);
  const action = useAction(() => conversation.reload());
  const [text, setText] = useState("");
  const [isRequest, setIsRequest] = useState(false);
  const messages = conversation.data ?? [];
  const open = messages.filter((message) => message.isRequest && !message.resolvedAt);

  async function send(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (await action.run(() => http.post(path, { text, isRequest }))) {
      setText("");
      setIsRequest(false);
    }
  }

  return (
    <article className="card vx-box">
      <div className="vx-panel-title">
        <h3>Conversa com {side === "store" ? "o contador" : "a empresa"} {open.length ? <em>{open.length} pedido(s) em aberto</em> : null}</h3>
        <button className="secondary-button" disabled={conversation.loading} onClick={() => void conversation.reload()} type="button">Atualizar</button>
      </div>
      <ErrorMessage message={conversation.error ?? action.error} />
      {conversation.data && messages.length === 0 ? <Empty>Nenhuma mensagem ainda.</Empty> : null}
      {messages.length ? (
        <ol className="fx-chat">
          {messages.map((message) => (
            <li className={message.mine ? "is-mine" : ""} key={message.id}>
              <header>
                <strong>{message.author}</strong>
                <small className="muted">{message.fromAccountant ? "Contador" : "Empresa"} · {formatDateTime(message.createdAt)}</small>
                {message.isRequest ? (message.resolvedAt ? <StatusChip tone="ok">Pedido resolvido</StatusChip> : <StatusChip tone="warn">Pedido em aberto</StatusChip>) : null}
              </header>
              <p>{message.text}</p>
              {message.isRequest && !message.resolvedAt ? (
                <button className="secondary-button" disabled={action.busy} onClick={() => void action.run(() => http.post(`${path}/${message.id}/resolve`))} type="button">Marcar como resolvido</button>
              ) : null}
              {message.resolvedAt ? <small className="muted">Resolvido por {message.resolvedBy} em {formatDateTime(message.resolvedAt)}</small> : null}
            </li>
          ))}
        </ol>
      ) : null}
      <form className="fx-chat-form" onSubmit={send}>
        <textarea aria-label="Mensagem" maxLength={2000} onChange={(event) => setText(event.target.value)} placeholder={side === "accountant" ? "Ex.: envie o extrato bancário de outubro" : "Escreva para o contador"} required rows={3} value={text} />
        <div className="fx-actions">
          <label className="module-check"><input checked={isRequest} onChange={(event) => setIsRequest(event.target.checked)} type="checkbox" /> É um pedido (fica pendente até alguém resolver)</label>
          <button className="primary-button" disabled={action.busy || !text.trim()} type="submit">Enviar</button>
        </div>
      </form>
    </article>
  );
}

const statusLabel: Record<AccountantLink["status"], string> = { Invited: "Convite enviado", Active: "Com acesso", Revoked: "Sem acesso" };

/** Store side: invite the accountant, see who has access, talk. */
export function FiscalAccountantView({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const base = `/establishments/${establishmentId}/fiscal/accountant`;
  const desk = useLoad(() => http.get<Desk>(base), [base]);
  const action = useAction(() => desk.reload());
  const [invite, setInvite] = useState<{ email: string; code: string } | null>(null);
  const portal = typeof window === "undefined" ? "/contador" : `${window.location.origin}/contador`;
  const hasAccountant = (desk.data?.links ?? []).some((link) => link.status === "Active");

  async function sendInvite(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const values = new FormData(form);
    const email = String(values.get("email") ?? "");
    const kind = String(values.get("kind") ?? "Accounting");
    await action.run(async () => {
      const created = await http.post<{ code: string }>(`${base}/invites`, { email, kind });
      setInvite({ email, code: created.code });
      form.reset();
    });
  }

  function revoke(link: AccountantLink) {
    const who = link.firmName ?? link.invitedEmail;

    if (window.confirm(`Tirar o acesso de ${who}? Ele deixa de ver esta loja na hora e as chaves de API que ele criou param de funcionar.`)) {
      void action.run(() => http.del(`${base}/links/${link.id}`));
    }
  }

  return (
    <>
      <article className="card vx-box">
        <div className="vx-panel-title"><h3>Contador e parceiros <em>portal de parceiros</em></h3></div>
        <p className="muted">
          Convide pelo e-mail. O <strong>contador</strong> vê só esta loja (pendências, apuração, livros, auditoria e o fechamento do mês).
          A <strong>software house</strong> (quem faz seu ERP, PDV ou loja virtual) cria as próprias chaves de API e webhooks, sem ver os livros.
          Os dois conversam com você por aqui e você tira o acesso quando quiser: as chaves do parceiro param na hora.
        </p>
        <ErrorMessage message={desk.error ?? action.error} />
        {invite ? (
          <div className="zest-key">
            <strong>Mande para {invite.email}: o código vale 7 dias e só uma vez.</strong>
            <code>{invite.code}</code>
            <span className="muted">Endereço do portal: {portal}</span>
            <div className="fx-actions">
              <button className="secondary-button" onClick={() => void navigator.clipboard?.writeText(`Acesse ${portal} e use o código ${invite.code} com o e-mail ${invite.email}.`)} type="button">Copiar convite</button>
              <button className="secondary-button" onClick={() => setInvite(null)} type="button">Já mandei</button>
            </div>
          </div>
        ) : null}
        <form className="module-form module-form-flat" onSubmit={sendInvite}>
          <label className="field"><span>Quem</span><select defaultValue="Accounting" name="kind"><option value="Accounting">Contador / escritório contábil</option><option value="SoftwareHouse">Software house (sistema integrado)</option></select></label>
          <label className="field"><span>E-mail</span><input maxLength={200} name="email" placeholder="contato@parceiro.com.br" required type="email" /></label>
          <div><button className="primary-button" disabled={action.busy} type="submit">Gerar convite</button></div>
        </form>

        {desk.data && desk.data.links.length === 0 ? <Empty>Nenhum parceiro convidado.</Empty> : null}
        {desk.data?.links.length ? (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Parceiro</th><th>Tipo</th><th>E-mail</th><th>Situação</th><th>Desde</th><th /></tr></thead>
              <tbody>
                {desk.data.links.map((link) => (
                  <tr key={link.id}>
                    <td>{link.firmName ? <><strong>{link.firmName}</strong>{link.firmCrc ? <small className="muted"> CRC {link.firmCrc}</small> : null}{link.firmIsPartner ? <> <StatusChip tone="accent">Parceiro</StatusChip></> : null}</> : <span className="muted">aguardando aceite</span>}</td>
                    <td>{link.kind === "SoftwareHouse" ? "Software house" : "Contador"}</td>
                    <td>{link.invitedEmail}</td>
                    <td>
                      <StatusChip tone={link.status === "Active" ? "ok" : link.status === "Invited" ? "warn" : "muted"}>{statusLabel[link.status]}</StatusChip>
                      {link.status === "Invited" && link.codeExpiresAt ? <small className="muted"> até {formatDateTime(link.codeExpiresAt)}</small> : null}
                    </td>
                    <td>{formatDateTime(link.acceptedAt ?? link.createdAt)}</td>
                    <td>{link.status !== "Revoked" ? <button className="secondary-button" disabled={action.busy} onClick={() => revoke(link)} type="button">{link.status === "Active" ? "Tirar acesso" : "Cancelar convite"}</button> : null}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </article>

      {hasAccountant ? <AccountantChat establishmentId={establishmentId} http={http} side="store" /> : null}
    </>
  );
}
