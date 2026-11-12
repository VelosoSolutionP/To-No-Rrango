"use client";

import "../admin/admin.css";
import "../admin/admin-theme.css";
import "../admin/admin-neon.css";
import { type FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import { API_BASE_URL, ApiError, problemDetail } from "@/lib/api";
import { createAdminHttp, formatDateTime, UNAUTHORIZED_EVENT } from "@/lib/admin-api";
import { StatusChip } from "../admin/DataTable";
import { AccountantChat } from "../admin/modules/FiscalAccountant";
import { FiscalApiView } from "../admin/modules/FiscalApi";
import { FiscalBooksView, FiscalHistoryView } from "../admin/modules/FiscalBooks";
import { AccountantPackageView, FiscalReviewView } from "../admin/modules/FiscalExtras";
import { Empty, ErrorMessage, messageOf, SubTabs, useAction, useLoad } from "../admin/modules/shared";

interface Firm {
  id: string;
  name: string;
  document: string;
  crc: string;
  email: string;
  phone: string | null;
  isPartner: boolean;
  partnerSince: string | null;
  partnerRequestedAt: string | null;
  kind: "Accounting" | "SoftwareHouse";
}

interface PortalSession {
  token: string;
  expiresAt: string;
  name: string;
  email: string;
  firm: Firm;
}

interface Company {
  linkId: string;
  establishmentId: string;
  name: string;
  since: string | null;
  openRequests: number;
  unread: number;
  kind: "Accounting" | "SoftwareHouse";
}

const storageKey = "tonorango.accountant.session";
const prefix = "/api/accountant";

async function call<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${prefix}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const detail = problemDetail(problem);
    throw new ApiError(response.status === 429 && !detail ? "Muitas tentativas. Aguarde um minuto." : (detail ?? "Não foi possível entrar."), response.status, detail);
  }

  return (await response.json()) as T;
}

function readSession(): PortalSession | null {
  try {
    const raw = window.sessionStorage.getItem(storageKey);
    const session = raw ? (JSON.parse(raw) as PortalSession) : null;
    return session && new Date(session.expiresAt) > new Date() ? session : null;
  } catch {
    return null;
  }
}

export function AccountantPortal() {
  const [session, setSession] = useState<PortalSession | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    setSession(readSession());
    setReady(true);
  }, []);

  const signIn = useCallback((next: PortalSession) => {
    window.sessionStorage.setItem(storageKey, JSON.stringify(next));
    setSession(next);
  }, []);

  const signOut = useCallback(() => {
    window.sessionStorage.removeItem(storageKey);
    setSession(null);
  }, []);

  useEffect(() => {
    window.addEventListener(UNAUTHORIZED_EVENT, signOut);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, signOut);
  }, [signOut]);

  if (!ready) {
    return <main className="vx-login"><p className="muted">Carregando…</p></main>;
  }

  return session ? <Desk onSignOut={signOut} onFirm={(firm) => signIn({ ...session, firm })} session={session} /> : <Gate onSignIn={signIn} />;
}

function Gate({ onSignIn }: { onSignIn: (session: PortalSession) => void }) {
  const [mode, setMode] = useState<"login" | "signup">("login");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const values = Object.fromEntries(new FormData(event.currentTarget)) as Record<string, string>;
    setBusy(true);
    setError(null);

    try {
      onSignIn(mode === "login"
        ? await call<PortalSession>("/auth/login", { email: values.email, password: values.password })
        : await call<PortalSession>("/auth/signup", values));
    } catch (failure) {
      setError(messageOf(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="vx fx-gate">
      <main className="vx-main fx-portal-login">
        <header>
          <p className="muted">TO-NO-RANGO · Veloso Solution</p>
          <h1>Portal de parceiros</h1>
          <p className="muted">
            Para contadores: pendências, apuração, livros, auditoria e o fechamento do mês das empresas que você atende.
            Para software houses: chaves da API fiscal e webhooks das empresas que usam o seu sistema.
          </p>
        </header>
        <SubTabs
          onChange={(value) => { setMode(value); setError(null); }}
          options={[{ value: "login", label: "Entrar" }, { value: "signup", label: "Primeiro acesso (tenho convite)" }]}
          value={mode}
        />
        <article className="card vx-box">
          <form className="module-form" key={mode} onSubmit={submit}>
            {mode === "signup" ? (
              <>
                <p className="muted field-wide">A empresa gera o convite no painel dela (Fiscal → Contador e parceiros) para o seu e-mail.</p>
                <label className="field field-wide"><span>Código do convite</span><input autoComplete="off" name="inviteCode" placeholder="ABCDE-FGHJK" required /></label>
                <label className="field field-wide"><span>Nome do escritório ou da empresa</span><input maxLength={120} name="firmName" required /></label>
                <label className="field"><span>CNPJ (ou CPF do contador)</span><input inputMode="numeric" name="document" required /></label>
                <label className="field"><span>CRC (só contador)</span><input name="crc" placeholder="MG-123456/O" /></label>
                <label className="field"><span>Seu nome</span><input maxLength={120} name="name" required /></label>
                <label className="field"><span>Telefone (opcional)</span><input name="phone" /></label>
              </>
            ) : null}
            <label className="field"><span>E-mail</span><input autoComplete="username" name="email" required type="email" /></label>
            <label className="field"><span>Senha{mode === "signup" ? " (8 ou mais caracteres)" : ""}</span><input autoComplete={mode === "login" ? "current-password" : "new-password"} minLength={mode === "signup" ? 8 : undefined} name="password" required type="password" /></label>
            <ErrorMessage message={error} />
            <div><button className="primary-button" disabled={busy} type="submit">{busy ? "Aguarde…" : mode === "login" ? "Entrar" : "Criar acesso"}</button></div>
          </form>
        </article>
      </main>
    </div>
  );
}

type CompanyView = "review" | "books" | "history" | "closing" | "integration" | "chat";

function Desk({ session, onSignOut, onFirm }: { session: PortalSession; onSignOut: () => void; onFirm: (firm: Firm) => void }) {
  const http = useMemo(() => createAdminHttp({ token: session.token }, prefix), [session.token]);
  const companies = useLoad(() => http.get<Company[]>("/companies"), [http]);
  const [selected, setSelected] = useState<string | null>(null);
  const [view, setView] = useState<CompanyView>("review");
  const [navOpen, setNavOpen] = useState(false);
  const company = (companies.data ?? []).find((item) => item.establishmentId === selected) ?? null;

  const software = session.firm.kind === "SoftwareHouse";

  function open(establishmentId: string | null) {
    setSelected(establishmentId);
    setView(software ? "integration" : "review");
    setNavOpen(false);
    void companies.reload();
  }

  return (
    <div className={`vx ${navOpen ? "is-open" : ""}`}>
      <aside className="vx-side">
        <button className="vx-brand link-button" onClick={() => open(null)} type="button">
          <span className="vx-mark">TR</span>
          <div>
            <strong>Portal de <em>Parceiros</em></strong>
            <small>{session.firm.name}</small>
          </div>
        </button>
        <nav aria-label="Empresas" className="vx-nav">
          <p className="vx-sec">Empresas</p>
          <button aria-current={selected === null ? "page" : undefined} className={selected === null ? "is-active" : ""} onClick={() => open(null)} type="button">
            <span>Visão geral</span>
          </button>
          {(companies.data ?? []).map((item) => (
            <button aria-current={selected === item.establishmentId ? "page" : undefined} className={`fx-company ${selected === item.establishmentId ? "is-active" : ""}`} key={item.linkId} onClick={() => open(item.establishmentId)} type="button">
              <span>{item.name}</span>
              {item.openRequests + item.unread > 0 ? <b>{item.openRequests + item.unread}</b> : null}
            </button>
          ))}
        </nav>
        <div className="vx-foot">
          <span className="vx-avatar">{session.name.slice(0, 1).toUpperCase()}</span>
          <span className="vx-user"><strong>{session.name}</strong><small>{software ? "Software house" : "Contador"}{session.firm.isPartner ? " parceiro" : ""}</small></span>
          <button className="link-button" onClick={onSignOut} type="button">Sair</button>
        </div>
      </aside>
      <button aria-label="Fechar menu" className="vx-backdrop" onClick={() => setNavOpen(false)} type="button" />
      <div className="vx-body">
        <header className="vx-top">
          <button aria-label="Abrir menu" className="vx-menu-button" onClick={() => setNavOpen(true)} type="button">☰</button>
          <h1>{company?.name ?? "Visão geral"}</h1>
          <div className="vx-top-right"><span className="vx-top-store">{session.firm.name}{session.firm.crc ? ` · CRC ${session.firm.crc}` : ""}</span></div>
        </header>
        <main className="vx-main">
          <ErrorMessage message={companies.error} />
          {company ? (
            <>
              <SubTabs
                onChange={setView}
                options={[
                  ...(software
                    ? [{ value: "integration" as const, label: "API e webhooks" }]
                    : [
                        { value: "review" as const, label: "Pendências" },
                        { value: "books" as const, label: "Apuração e livros" },
                        { value: "history" as const, label: "Competências e auditoria" },
                        { value: "closing" as const, label: "Fechamento do mês" },
                      ]),
                  { value: "chat", label: company.openRequests + company.unread > 0 ? `Conversa (${company.openRequests + company.unread})` : "Conversa" },
                ]}
                value={view}
              />
              {view === "review" ? (
                <>
                  {company.openRequests > 0 ? (
                    <p className="card vx-box">Há {company.openRequests} pedido(s) seu(s) em aberto com esta empresa. <button className="link-button" onClick={() => setView("chat")} type="button">Ver conversa</button></p>
                  ) : null}
                  <FiscalReviewView establishmentId={company.establishmentId} http={http} />
                </>
              ) : null}
              {view === "books" ? <FiscalBooksView establishmentId={company.establishmentId} http={http} /> : null}
              {view === "history" ? <FiscalHistoryView establishmentId={company.establishmentId} http={http} /> : null}
              {view === "closing" ? <AccountantPackageView establishmentId={company.establishmentId} http={http} /> : null}
              {view === "integration" ? <FiscalApiView establishmentId={company.establishmentId} http={http} /> : null}
              {view === "chat" ? <AccountantChat establishmentId={company.establishmentId} http={http} side="accountant" /> : null}
            </>
          ) : (
            <Overview companies={companies.data} http={http} onAdded={(id) => { void companies.reload(); setSelected(id); }} onFirm={onFirm} onOpen={open} session={session} />
          )}
        </main>
      </div>
    </div>
  );
}

function Overview({ session, companies, http, onOpen, onAdded, onFirm }: {
  session: PortalSession;
  companies: Company[] | null;
  http: ReturnType<typeof createAdminHttp>;
  onOpen: (establishmentId: string) => void;
  onAdded: (establishmentId: string) => void;
  onFirm: (firm: Firm) => void;
}) {
  const action = useAction();
  const firm = session.firm;

  async function accept(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const code = String(new FormData(form).get("code") ?? "");
    await action.run(async () => {
      const added = await http.post<Company>("/companies/accept", { code });
      form.reset();
      onAdded(added.establishmentId);
    });
  }

  return (
    <>
      <article className="card vx-box">
        <div className="vx-panel-title"><h3>Empresas atendidas <em>{companies?.length ?? 0}</em></h3></div>
        {companies && companies.length === 0 ? <Empty>Nenhuma empresa ainda. Peça o convite à empresa e use o código abaixo.</Empty> : null}
        {companies?.length ? (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Empresa</th><th>Atende desde</th><th className="num">Pedidos em aberto</th><th className="num">Mensagens novas</th><th /></tr></thead>
              <tbody>
                {companies.map((item) => (
                  <tr key={item.linkId}>
                    <td><strong>{item.name}</strong></td>
                    <td>{item.since ? formatDateTime(item.since) : "—"}</td>
                    <td className="num">{item.openRequests ? <StatusChip tone="warn">{item.openRequests}</StatusChip> : 0}</td>
                    <td className="num">{item.unread ? <StatusChip tone="info">{item.unread}</StatusChip> : 0}</td>
                    <td><button className="secondary-button" onClick={() => onOpen(item.establishmentId)} type="button">Abrir</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
        <form className="module-form module-form-flat" onSubmit={accept}>
          <label className="field"><span>Adicionar empresa com código de convite</span><input autoComplete="off" name="code" placeholder="ABCDE-FGHJK" required /></label>
          <div><button className="primary-button" disabled={action.busy} type="submit">Adicionar</button></div>
        </form>
        <ErrorMessage message={action.error} />
      </article>

      <article className="card vx-box">
        <div className="vx-panel-title"><h3>{firm.kind === "SoftwareHouse" ? "Programa de software houses parceiras" : "Programa de parceiros contábeis"}</h3></div>
        {firm.isPartner ? (
          <p><StatusChip tone="accent">Parceiro</StatusChip> Sua empresa é parceira da Veloso Solution{firm.partnerSince ? ` desde ${formatDateTime(firm.partnerSince)}` : ""}. As empresas veem o selo de parceiro ao lado do seu nome.</p>
        ) : firm.partnerRequestedAt ? (
          <p className="muted">Pedido enviado em {formatDateTime(firm.partnerRequestedAt)}. A Veloso Solution analisa e aprova os escritórios parceiros.</p>
        ) : (
          <>
            <p className="muted">Parceiros aparecem com selo para as empresas. A aprovação é feita pela equipe da Veloso Solution.</p>
            <div>
              <button className="primary-button" disabled={action.busy} onClick={() => void action.run(async () => onFirm(await http.post<Firm>("/partnership")))} type="button">Quero ser parceiro</button>
            </div>
          </>
        )}
      </article>
    </>
  );
}
