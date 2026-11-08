"use client";

import { type FormEvent, useEffect, useRef, useState } from "react";
import { formatBRL, formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { Empty, ErrorMessage as ErrorText, Input, ModuleHeading, SubTabs, messageOf, text, useAction, useLoad } from "./shared";

interface Knowledge {
  id: string;
  topic: string;
  answer: string;
  keywords: string;
  isActive: boolean;
}

interface Conversation {
  id: string;
  phone: string;
  customerName: string | null;
  lastMessage: string | null;
  lastMessageAt: string;
  isBotPaused: boolean;
}

interface Message {
  id: string;
  direction: "Inbound" | "Outbound";
  author: "Customer" | "Zest" | "Staff" | "System";
  text: string;
  createdAt: string;
}

interface Props {
  http: AdminHttp;
  establishmentId: string;
}

function Teach({ http, establishmentId }: Props) {
  const list = useLoad(() => http.get<Knowledge[]>(`/establishments/${establishmentId}/zest/knowledge`), [establishmentId]);
  const action = useAction(list.reload);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const done = await action.run(() =>
      http.post(`/establishments/${establishmentId}/zest/knowledge`, {
        topic: text(form, "topic"),
        answer: text(form, "answer"),
        keywords: text(form, "keywords"),
        isActive: true,
      }),
    );

    if (done) {
      element.reset();
    }
  }

  return (
    <>
      <form className="card module-form" onSubmit={submit}>
        <Input label="Assunto *" name="topic" placeholder="Horário de funcionamento" required />
        <Input label="Palavras-chave (separe por vírgula)" name="keywords" placeholder="horário, abre, fecha" />
        <Input label="Resposta da ZEST *" name="answer" required wide />
        <button className="primary-button" disabled={action.busy} type="submit">Ensinar</button>
      </form>
      <ErrorText message={action.error ?? list.error} />
      {list.data?.length === 0 ? <Empty>Ensine horários, regras e promoções. Cada estabelecimento tem o próprio conhecimento.</Empty> : null}
      <div className="module-stack">
        {list.data?.map((entry) => (
          <article className="card module-knowledge" key={entry.id}>
            <div>
              <strong>{entry.topic}</strong>
              <p>{entry.answer}</p>
              {entry.keywords ? <small className="muted">{entry.keywords}</small> : null}
            </div>
            <button className="link-button" onClick={() => action.run(() => http.del(`/zest/knowledge/${entry.id}`))} type="button">Remover</button>
          </article>
        ))}
      </div>
    </>
  );
}

function renderWhatsApp(value: string) {
  return value.split(/(\*[^*\n]+\*)/g).map((part, index) =>
    part.startsWith("*") && part.endsWith("*") && part.length > 2 ? <strong key={index}>{part.slice(1, -1)}</strong> : part,
  );
}

const quickReplies = ["quero pedir", "qual a taxa de entrega?", "1", "2", "finalizar"];

function Phone({ http, establishmentId, storeName }: Props & { storeName: string }) {
  const [messages, setMessages] = useState<Array<{ from: "customer" | "zest"; text: string }>>([
    { from: "zest", text: `Olá! Aqui é a ZEST do ${storeName}. Escreva *quero pedir* para testar uma venda.` },
  ]);
  const [state, setState] = useState<unknown>(null);
  const [draft, setDraft] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const log = useRef<HTMLDivElement>(null);

  useEffect(() => {
    log.current?.scrollTo({ top: log.current.scrollHeight, behavior: "smooth" });
  }, [messages]);

  async function say(message: string) {
    if (!message.trim() || busy) {
      return;
    }

    setDraft("");
    setBusy(true);
    setError(null);
    setMessages((current) => [...current, { from: "customer", text: message.trim() }]);

    try {
      const answer = await http.post<{ state: unknown; reply: string }>(`/establishments/${establishmentId}/zest/simulate`, {
        state,
        message: message.trim(),
        customerName: "Cliente Teste",
      });
      setState(answer.state);
      setMessages((current) => [...current, { from: "zest", text: answer.reply }]);
    } catch (askError) {
      setError(messageOf(askError));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="phone">
      <header className="phone-head">
        <div className="zx-orb zx-orb-tiny" aria-hidden="true"><span>Z</span></div>
        <div>
          <strong>{storeName}</strong>
          <small>{busy ? "digitando..." : "ZEST · simulação"}</small>
        </div>
      </header>
      <div className="phone-log" ref={log}>
        {messages.map((message, index) => (
          <p className={`phone-bubble ${message.from === "customer" ? "is-out" : ""}`} key={index}>{renderWhatsApp(message.text)}</p>
        ))}
      </div>
      {error ? <p className="form-error phone-error">{error}</p> : null}
      <div className="phone-chips">
        {quickReplies.map((reply) => <button key={reply} onClick={() => say(reply)} type="button">{reply}</button>)}
      </div>
      <form
        className="phone-input"
        onSubmit={(event: FormEvent) => {
          event.preventDefault();
          void say(draft);
        }}
      >
        <input aria-label="Mensagem" onChange={(event) => setDraft(event.target.value)} placeholder="Mensagem" value={draft} />
        <button aria-label="Enviar" disabled={busy} type="submit">➤</button>
      </form>
    </div>
  );
}

interface Connection {
  connected: boolean;
  qrCodeBase64: string | null;
  canProvision: boolean;
  problem: string | null;
  pairingCode: string | null;
  number: string | null;
}

interface BrainBot {
  id: string;
  name: string;
  profession: string | null;
  photo: string | null;
  persona: string | null;
  firstMessage: string | null;
  knowledgeNodes: number;
}

interface Brain {
  configured: boolean;
  attachedId: string | null;
  attachedName: string | null;
  available: BrainBot[];
  problem: string | null;
}

function BrainCard({ http, establishmentId }: Props) {
  const brain = useLoad(() => http.get<Brain>(`/establishments/${establishmentId}/zest/brain`), [establishmentId]);
  const action = useAction(brain.reload);
  const data = brain.data;

  return (
    <article className="zx-card zest-brain">
      <h2>{data?.attachedName ? `${data.attachedName} assumiu o cardápio` : "IA do Hotel dos Bots"}</h2>
      <p className="zx-status"><i className={data?.attachedName ? "is-on" : ""} />{data?.attachedName ? "IA vinculada" : "Escolha quem assume o cardápio"}</p>
      {data?.problem ? <p className="form-error">{data.problem}</p> : null}
      {brain.error ? <p className="form-error">{brain.error}</p> : null}
      <ErrorText message={action.error} />
      {data && data.available.length > 0 ? (
        <div className="zest-bots">
          {data.available.map((bot) => (
            <div className={`zest-bot ${bot.id === data.attachedId ? "is-active" : ""}`} key={bot.id}>
              {bot.photo ? (
                // eslint-disable-next-line @next/next/no-img-element
                <img alt="" src={bot.photo} />
              ) : <span className="zest-bot-avatar">{bot.name.slice(0, 1)}</span>}
              <div>
                <strong>{bot.name}</strong>
                <small>{bot.profession ? `${bot.profession} · ` : ""}{bot.knowledgeNodes} conhecimento(s) homologado(s)</small>
                {bot.firstMessage ? <p className="muted">“{bot.firstMessage}”</p> : null}
              </div>
              {bot.id === data.attachedId ? (
                <button className="secondary-button secondary-button-small" disabled={action.busy} onClick={() => action.run(() => http.del(`/establishments/${establishmentId}/zest/brain`))} type="button">Retirar</button>
              ) : (
                <button className="primary-button" disabled={action.busy} onClick={() => action.run(() => http.put(`/establishments/${establishmentId}/zest/brain`, { botId: bot.id }))} type="button">Assumir</button>
              )}
            </div>
          ))}
        </div>
      ) : null}
    </article>
  );
}

interface Stats {
  conversations: number;
  ordersByZest: number;
  revenueByZestInCents: number;
  knowledgeEntries: number;
  activeSales: number;
}

function ConnectCard({ http, establishmentId }: Props) {
  const connection = useLoad(() => http.get<Connection>(`/establishments/${establishmentId}/whatsapp/connection`), [establishmentId]);
  const [qr, setQr] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [phone, setPhone] = useState("");
  const [pairing, setPairing] = useState<string | null>(null);
  const reload = useRef(connection.reload);
  reload.current = connection.reload;
  const connected = connection.data?.connected ?? false;

  useEffect(() => {
    if ((!qr && !pairing) || connected) {
      return;
    }

    const interval = window.setInterval(() => void reload.current(), 4000);
    return () => window.clearInterval(interval);
  }, [connected, pairing, qr]);

  async function connect() {
    setBusy(true);
    setProblem(null);

    try {
      const result = await http.post<Connection>(`/establishments/${establishmentId}/whatsapp/connect`, { phone: phone.trim() || null });
      setProblem(result.problem);
      setPairing(result.pairingCode);
      setQr(result.qrCodeBase64);
      await connection.reload();
    } catch (error) {
      setProblem(messageOf(error));
    } finally {
      setBusy(false);
    }
  }

  async function disconnect() {
    if (!window.confirm("Desconectar este WhatsApp? A ZEST para de atender até você conectar de novo.")) {
      return;
    }

    setBusy(true);
    setProblem(null);

    try {
      await http.post(`/establishments/${establishmentId}/whatsapp/disconnect`);
      setQr(null);
      setPairing(null);
      await connection.reload();
    } catch (error) {
      setProblem(messageOf(error));
    } finally {
      setBusy(false);
    }
  }

  const qrImage = qr ? (qr.startsWith("data:") ? qr : `data:image/png;base64,${qr}`) : null;

  return (
    <article className="zx-card zest-connect">
      <h2>{connected ? "WhatsApp conectado" : "Conecte o WhatsApp da loja"}</h2>
      <p className="zx-status"><i className={connected ? "is-on" : ""} />{connected ? "Número conectado" : "Sem conexão"}</p>
      {!connected ? (
        <label className="field">
          <span>Número do WhatsApp da loja</span>
          <input inputMode="tel" onChange={(event) => setPhone(event.target.value)} placeholder="(31) 99999-9999" value={phone} />
        </label>
      ) : null}
      {!connected && pairing ? (
        <div className="zest-pairing">
          <strong>{pairing}</strong>
          <p className="muted">No celular: WhatsApp → Aparelhos conectados → Conectar um aparelho → Conectar com número de telefone, e digite este código.</p>
        </div>
      ) : null}
      {!connected && qrImage ? (
        <div className="zest-qr">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img alt="QR Code do WhatsApp" src={qrImage} />
          <p className="muted">No celular: WhatsApp → Aparelhos conectados → Conectar um aparelho.</p>
        </div>
      ) : null}
      {connected ? (
        <>
          {connection.data?.number ? <p className="muted">Número: +{connection.data.number}</p> : null}
          <button className="secondary-button" disabled={busy} onClick={disconnect} type="button">{busy ? "Desconectando..." : "Desconectar WhatsApp"}</button>
        </>
      ) : null}
      {!connected ? (
        <button className="primary-button primary-button-full" disabled={busy} onClick={connect} type="button">
          {busy ? "Conectando..." : qrImage || pairing ? "Gerar novo código" : "Conectar"}
        </button>
      ) : null}
      {problem ?? connection.data?.problem ? <p className="form-error">{problem ?? connection.data?.problem}</p> : null}
    </article>
  );
}

function Automation({ http, establishmentId }: Props) {
  const settings = useLoad(() => http.get<{ autoReply: boolean }>(`/establishments/${establishmentId}/whatsapp`), [establishmentId]);
  const connection = useLoad(() => http.get<Connection>(`/establishments/${establishmentId}/whatsapp/connection`), [establishmentId]);
  const brain = useLoad(() => http.get<Brain>(`/establishments/${establishmentId}/zest/brain`), [establishmentId]);
  const action = useAction(settings.reload);
  const connected = connection.data?.connected ?? false;
  const attached = Boolean(brain.data?.attachedId);
  const homologated = attached && (brain.data?.available.some((bot) => bot.id === brain.data?.attachedId) ?? false);
  const active = settings.data?.autoReply ?? false;

  function turnOn() {
    if (window.confirm("Ativar o atendimento automático? A IA passa a responder os clientes reais deste número.")) {
      void action.run(() => http.put(`/establishments/${establishmentId}/whatsapp/automation`, { enabled: true }));
    }
  }

  return (
    <article className="zx-card vx-box">
      <div className="vx-panel-title">
        <h2>Atendimento automático</h2>
        <span className={`vx-state ${active ? "is-on" : ""}`}><i />{active ? "Ativo" : "Desligado"}</span>
      </div>
      <ul className="vx-checks">
        <li className={connected ? "is-ok" : ""}><b>{connected ? "✓" : "–"}</b>WhatsApp conectado</li>
        <li className={homologated ? "is-ok" : ""}><b>{homologated ? "✓" : "–"}</b>IA homologada vinculada</li>
        <li className="is-ok"><b>✓</b>Trava contra conversa entre robôs</li>
      </ul>
      <ErrorText message={action.error} />
      {active ? (
        <button className="secondary-button" disabled={action.busy} onClick={() => action.run(() => http.put(`/establishments/${establishmentId}/whatsapp/automation`, { enabled: false }))} type="button">Desligar atendimento</button>
      ) : (
        <button className="primary-button" disabled={action.busy || !connected || !homologated} onClick={turnOn} type="button">Ativar atendimento</button>
      )}
    </article>
  );
}

function Sell({ storeName, ...props }: Props & { storeName: string }) {
  const stats = useLoad(() => props.http.get<Stats>(`/establishments/${props.establishmentId}/zest/stats`), [props.establishmentId]);

  return (
    <>
      <div className="zest-stats">
        <div className="zx-card"><span>Pedidos pela IA</span><strong>{stats.data?.ordersByZest ?? 0}</strong><small>30 dias</small></div>
        <div className="zx-card"><span>Vendido pela IA</span><strong>{formatBRL(stats.data?.revenueByZestInCents ?? 0)}</strong><small>30 dias</small></div>
        <div className="zx-card"><span>Conversas</span><strong>{stats.data?.conversations ?? 0}</strong><small>30 dias</small></div>
        <div className="zx-card"><span>Vendas em andamento</span><strong>{stats.data?.activeSales ?? 0}</strong><small>agora</small></div>
      </div>
      <div className="vx-grid-2">
        <ConnectCard {...props} />
        <Automation {...props} />
      </div>
      <BrainCard {...props} />
    </>
  );
}

function Inbox({ http, establishmentId }: Props) {
  const conversations = useLoad(() => http.get<Conversation[]>(`/establishments/${establishmentId}/conversations`), [establishmentId]);
  const [selected, setSelected] = useState<Conversation | null>(null);
  const messages = useLoad(
    () => (selected ? http.get<Message[]>(`/conversations/${selected.id}/messages`) : Promise.resolve([])),
    [selected?.id],
  );
  const reloadMessages = useRef(messages.reload);
  reloadMessages.current = messages.reload;
  const reloadConversations = useRef(conversations.reload);
  reloadConversations.current = conversations.reload;
  const action = useAction(async () => {
    await Promise.all([messages.reload(), conversations.reload()]);
  });
  const [draft, setDraft] = useState("");

  useEffect(() => {
    const interval = window.setInterval(() => {
      void reloadConversations.current();
      void reloadMessages.current();
    }, 6000);

    return () => window.clearInterval(interval);
  }, []);

  const current = conversations.data?.find((conversation) => conversation.id === selected?.id) ?? selected;

  async function send(event: FormEvent) {
    event.preventDefault();

    if (!selected || !draft.trim()) {
      return;
    }

    if (await action.run(() => http.post(`/conversations/${selected.id}/messages`, { text: draft.trim() }))) {
      setDraft("");
    }
  }

  if (conversations.data?.length === 0) {
    return <Empty>As conversas do WhatsApp aparecem aqui quando o canal estiver conectado.</Empty>;
  }

  return (
    <div className="inbox">
      <div className="card inbox-list">
        {conversations.data?.map((conversation) => (
          <button className={conversation.id === selected?.id ? "is-active" : ""} key={conversation.id} onClick={() => setSelected(conversation)} type="button">
            <strong>{conversation.customerName ?? conversation.phone}</strong>
            <small>{conversation.lastMessage}</small>
            <small>{formatDateTime(conversation.lastMessageAt)}</small>
          </button>
        ))}
      </div>
      <div className="card module-stack">
        {current ? (
          <>
            <div className="chart-head">
              <h3>{current.customerName ?? current.phone}</h3>
              <button
                className="link-button"
                onClick={() => action.run(() => http.patch(`/conversations/${current.id}/bot`, { paused: !current.isBotPaused }))}
                type="button"
              >
                {current.isBotPaused ? "Devolver para a ZEST" : "Assumir conversa"}
              </button>
            </div>
            <div className="chat-log">
              {messages.data?.map((message) => (
                <p className={`chat-bubble ${message.direction === "Inbound" ? "is-customer" : ""}`} key={message.id}>
                  {message.text}
                  <small>{message.author === "Zest" ? "ZEST" : message.author === "Staff" ? "Equipe" : "Cliente"}</small>
                </p>
              ))}
            </div>
            <ErrorText message={action.error} />
            <form className="module-line" onSubmit={send}>
              <input aria-label="Resposta" onChange={(event) => setDraft(event.target.value)} placeholder="Responder como equipe" value={draft} />
              <button className="primary-button" disabled={action.busy} type="submit">Enviar</button>
            </form>
          </>
        ) : <p className="muted">Selecione uma conversa.</p>}
      </div>
    </div>
  );
}

export function Zest({ canTeach, canInbox, storeName, ...props }: Props & { canTeach: boolean; canInbox: boolean; storeName: string }) {
  type View = "connection" | "inbox" | "teach" | "test";
  const options: Array<{ value: View; label: string }> = [
    ...(canTeach && canInbox ? [{ value: "connection" as View, label: "Conexão e IA" }] : []),
    ...(canInbox ? [{ value: "inbox" as View, label: "Conversas" }] : []),
    ...(canTeach ? [{ value: "teach" as View, label: "Conhecimento da loja" }, { value: "test" as View, label: "Testar venda" }] : []),
  ];
  const [view, setView] = useState<View>(options[0]?.value ?? "inbox");

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Inteligência" title="WhatsApp e IA" hint="Conexão do número, IA do Hotel dos Bots e atendimento." />
      <SubTabs onChange={setView} options={options} value={view} />
      {view === "connection" ? <Sell {...props} storeName={storeName} /> : null}
      {view === "inbox" ? <Inbox {...props} /> : null}
      {view === "teach" ? <Teach {...props} /> : null}
      {view === "test" ? <Phone {...props} storeName={storeName} /> : null}
    </section>
  );
}
