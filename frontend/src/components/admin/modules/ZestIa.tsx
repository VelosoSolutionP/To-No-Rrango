"use client";

import { type FormEvent, useState } from "react";
import { formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { Empty, ErrorMessage, ModuleHeading, text, useAction, useLoad } from "./shared";

interface Gap {
  id: string;
  question: string;
  times: number;
  source: string;
  firstAskedAt: string;
  lastAskedAt: string;
}

interface Connection {
  isConnected: boolean;
  keyHint: string | null;
  createdAt: string | null;
  lastUsedAt: string | null;
  messagesRelayed: number;
  endpoint: string | null;
}

interface Knowledge {
  id: string;
  topic: string;
  answer: string;
  keywords: string;
  isActive: boolean;
}

/**
 * What the store's ZEST could not answer, and what the store taught it. The AI itself runs in
 * ZEST; here the store only fills the gaps with its own answers, so nothing is made up.
 */
export function ZestIa({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const gaps = useLoad(() => http.get<Gap[]>(`/establishments/${establishmentId}/zest/gaps`), [establishmentId]);
  const knowledge = useLoad(() => http.get<Knowledge[]>(`/establishments/${establishmentId}/zest/knowledge`), [establishmentId]);
  const action = useAction(async () => {
    await gaps.reload();
    await knowledge.reload();
  });
  const [answering, setAnswering] = useState<Gap | null>(null);
  const connection = useLoad(() => http.get<Connection>(`/establishments/${establishmentId}/zest/connector`), [establishmentId]);
  const [newKey, setNewKey] = useState<string | null>(null);
  const connectorAction = useAction(connection.reload);

  async function createKey() {
    if (connection.data?.isConnected && !window.confirm("Gerar uma chave nova desliga a anterior na hora. Continuar?")) {
      return;
    }

    await connectorAction.run(async () => {
      const created = await http.post<{ key: string }>(`/establishments/${establishmentId}/zest/connector/key`);
      setNewKey(created.key);
    });
  }

  async function teach(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const gap = answering;
    const done = await action.run(async () => {
      await http.post(`/establishments/${establishmentId}/zest/knowledge`, {
        topic: text(form, "topic"),
        answer: text(form, "answer"),
        keywords: text(form, "keywords"),
        isActive: true,
      });

      if (gap) {
        await http.post(`/zest/gaps/${gap.id}/resolve`);
      }
    });

    if (done) {
      element.reset();
      setAnswering(null);
    }
  }

  return (
    <section className="admin-section zest-ia">
      <ModuleHeading
        eyebrow="Inteligência"
        title="ZEST IA"
        hint="Perguntas dos clientes que ficaram sem resposta. Responda uma vez e o atendimento passa a usar a sua resposta."
      />
      <ErrorMessage message={gaps.error ?? knowledge.error ?? action.error ?? connection.error ?? connectorAction.error} />

      <article className="card zest-teach">
        <h3>Conexão com o ZEST</h3>
        <p className="muted">
          A IA roda no ZEST. Ele manda aqui a mensagem do cliente (texto ou áudio) e recebe as respostas para entregar: cardápio, pedido, pagamento e
          acompanhamento saem dos dados desta loja.
        </p>
        {connection.data?.isConnected ? (
          <p>
            Conectado · chave terminada em <strong>{connection.data.keyHint}</strong> · {connection.data.messagesRelayed} mensagem(ns)
            {connection.data.lastUsedAt ? ` · última em ${formatDateTime(connection.data.lastUsedAt)}` : " · ainda não usada"}
          </p>
        ) : (
          <p className="muted">Ainda não conectado.</p>
        )}
        {newKey ? (
          <div className="zest-key">
            <strong>Copie agora: esta chave não aparece de novo.</strong>
            <code>{newKey}</code>
            <button className="secondary-button" onClick={() => void navigator.clipboard?.writeText(newKey)} type="button">Copiar chave</button>
          </div>
        ) : null}
        {connection.data?.endpoint ? (
          <pre className="zest-snippet">{`POST ${connection.data.endpoint}
X-Zest-Key: <chave>
Content-Type: application/json

{ "phone": "5511999998888", "name": "Cliente",
  "text": "quero pedir",            // ou
  "audioBase64": "<ogg em base64>", "audioMime": "audio/ogg",
  "messageId": "id-da-mensagem" }

→ { "replies": [ { "text": "...", "imageBase64": null, "audioBase64": null } ],
    "handoff": false }`}</pre>
        ) : null}
        <div className="zest-gap-actions">
          {connection.data?.isConnected ? (
            <button className="link-button" disabled={connectorAction.busy} onClick={() => window.confirm("Desconectar o ZEST desta loja?") && void connectorAction.run(() => http.del(`/establishments/${establishmentId}/zest/connector`))} type="button">
              Desconectar
            </button>
          ) : null}
          <button className="primary-button" disabled={connectorAction.busy} onClick={() => void createKey()} type="button">
            {connection.data?.isConnected ? "Gerar chave nova" : "Gerar chave de conexão"}
          </button>
        </div>
      </article>

      <h3>Perguntas sem resposta</h3>
      {gaps.data?.length === 0 ? <Empty>Nenhuma pergunta pendente.</Empty> : null}
      <div className="ratings-list">
        {gaps.data?.map((gap) => (
          <article className="card" key={gap.id}>
            <header>
              <strong>“{gap.question}”</strong>
              <small>{gap.times}× · última {formatDateTime(gap.lastAskedAt)}</small>
            </header>
            <div className="zest-gap-actions">
              <button className="secondary-button" onClick={() => setAnswering(gap)} type="button">Responder</button>
              <button className="link-button" disabled={action.busy} onClick={() => void action.run(() => http.post(`/zest/gaps/${gap.id}/resolve`))} type="button">Ignorar</button>
            </div>
          </article>
        ))}
      </div>

      <form className="card zest-teach" key={answering?.id ?? "novo"} onSubmit={teach}>
        <h3>{answering ? `Responder: “${answering.question}”` : "Ensinar uma resposta"}</h3>
        <label className="field"><span>Assunto</span><input defaultValue={answering?.question.slice(0, 120) ?? ""} maxLength={120} name="topic" required /></label>
        <label className="field"><span>Resposta</span><textarea maxLength={1500} name="answer" required rows={3} /></label>
        <label className="field"><span>Palavras que levam a esta resposta (separe por vírgula)</span><input maxLength={300} name="keywords" /></label>
        <div className="zest-gap-actions">
          {answering ? <button className="secondary-button" onClick={() => setAnswering(null)} type="button">Cancelar</button> : null}
          <button className="primary-button" disabled={action.busy} type="submit">Salvar resposta</button>
        </div>
      </form>

      <h3>Respostas ensinadas</h3>
      {knowledge.data?.length === 0 ? <Empty>Nenhuma resposta ensinada ainda.</Empty> : null}
      <div className="ratings-list">
        {knowledge.data?.map((entry) => (
          <article className="card" key={entry.id}>
            <header><strong>{entry.topic}</strong>{entry.isActive ? null : <small>inativa</small>}</header>
            <p>{entry.answer}</p>
            {entry.keywords ? <small className="muted">{entry.keywords}</small> : null}
          </article>
        ))}
      </div>
    </section>
  );
}
