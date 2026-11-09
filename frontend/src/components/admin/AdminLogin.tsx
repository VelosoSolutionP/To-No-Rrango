"use client";

import "./admin.css";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { WHATSAPP_URL } from "@/lib/site";

interface AdminLoginProps {
  onLogin: (email: string, password: string) => Promise<void>;
  busy: boolean;
  error: string | null;
  /** Entering through the Portal. Absent while the Veloso ID is not configured. */
  onVelosoId?: () => void;
  /** Off only after the customers have been migrated. */
  passwordLogin?: boolean;
}

type Theme = "dark" | "light";

const THEME_KEY = "tonorango.login.theme";

const modules = [
  { tone: "orange", title: "Pedidos e cozinha", text: "Do cardápio à cozinha, em tempo real.", items: ["Cardápio digital", "Pedidos", "Cozinha"], icon: "M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4ZM3 6h18M16 10a4 4 0 0 1-8 0" },
  { tone: "green", title: "Entregas no mapa", text: "Cada moto acompanhada até o cliente.", items: ["Rastreio ao vivo", "Entregadores", "Taxas por bairro"], icon: "M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0ZM12 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" },
  { tone: "lilac", title: "WhatsApp com a ZEST", text: "Atendimento e venda direto na conversa.", items: ["Atendimento", "Venda na conversa"], icon: "M21 12a9 9 0 0 1-13.3 7.9L3 21l1.2-4.6A9 9 0 1 1 21 12Z" },
  { tone: "silver", title: "Gestão da loja", text: "Os números do negócio em um só lugar.", items: ["Estoque", "Financeiro", "Clientes", "Fiscal"], icon: "M3 3v18h18M8 17v-5M13 17V7M18 17v-8" },
];

const steps = [
  { title: "O cliente pede", text: "Pelo cardápio ou pelo WhatsApp." },
  { title: "A cozinha prepara", text: "Cada pedido na fila, em tempo real." },
  { title: "A moto entrega", text: "Com o trajeto acompanhado no mapa." },
];

const products = [
  { name: "To-No-Rango", color: "#ff6a13", live: true, current: true },
  { name: "ZEST", color: "#7c3aed", live: true, current: false },
  { name: "Quebra-Galho", color: "#62626e", live: false, current: false },
];

function formatDocument(value: string): string {
  const digits = value.replace(/\D/g, "").slice(0, 14);

  if (digits.length <= 11) {
    return digits
      .replace(/^(\d{3})(\d)/, "$1.$2")
      .replace(/^(\d{3})\.(\d{3})(\d)/, "$1.$2.$3")
      .replace(/\.(\d{3})(\d)/, ".$1-$2");
  }

  return digits
    .replace(/^(\d{2})(\d)/, "$1.$2")
    .replace(/^(\d{2})\.(\d{3})(\d)/, "$1.$2.$3")
    .replace(/\.(\d{3})(\d)/, ".$1/$2")
    .replace(/(\d{4})(\d)/, "$1-$2");
}

export function AdminLogin({ onLogin, busy, error, onVelosoId, passwordLogin = true }: AdminLoginProps) {
  const [theme, setTheme] = useState<Theme>("dark");
  const [usingEmail, setUsingEmail] = useState(false);
  const [document, setDocument] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  useEffect(() => {
    try {
      if (window.localStorage.getItem(THEME_KEY) === "light") {
        setTheme("light");
      }
    } catch {
      // Sem acesso ao armazenamento, fica no tema padrão.
    }
  }, []);

  function chooseTheme(next: Theme) {
    setTheme(next);

    try {
      window.localStorage.setItem(THEME_KEY, next);
    } catch {
      // A escolha vale só para esta visita.
    }
  }

  function handleContinue(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const digits = document.replace(/\D/g, "");

    if (digits.length !== 11 && digits.length !== 14) {
      setNotice("Informe um CPF ou CNPJ completo.");
      return;
    }

    if (onVelosoId) {
      setNotice(null);
      onVelosoId();
      return;
    }

    setNotice("O Veloso ID ainda não está ativo nesta loja. Entre com seu e-mail por enquanto.");
    setUsingEmail(passwordLogin);
  }

  async function handleEmail(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await onLogin(email.trim(), password);
  }

  const showEmail = usingEmail && passwordLogin;

  return (
    <main className="vx-login" data-theme={theme}>
      <aside className="vx-login-side">
        <header className="vx-login-welcome">
          <h2>Que bom ter você de volta.</h2>
          <p>Cardápio, pedidos, entregas e gestão no mesmo ritmo.</p>
        </header>

        <ul className="vx-login-modules">
          {modules.map((module) => (
            <li data-tone={module.tone} key={module.title}>
              <span className="vx-login-icon">
                <svg aria-hidden="true" fill="none" height="19" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8" viewBox="0 0 24 24" width="19">
                  <path d={module.icon} />
                </svg>
              </span>
              <div>
                <strong>{module.title}</strong>
                <small>{module.text}</small>
                <p className="vx-login-chips">{module.items.map((item) => <span key={item}>{item}</span>)}</p>
              </div>
              <span className="vx-login-count">
                <b>{String(module.items.length).padStart(2, "0")}</b>
                <em>recursos</em>
              </span>
            </li>
          ))}
        </ul>

        <section className="vx-login-block vx-login-how">
          <h3>Como funciona</h3>
          <ol className="vx-login-steps">
            {steps.map((step, index) => (
              <li key={step.title}>
                <b>{index + 1}</b>
                <strong>{step.title}</strong>
                <small>{step.text}</small>
              </li>
            ))}
          </ol>
        </section>

        <section className="vx-login-block vx-login-family">
          <h3>Produtos Veloso Solution</h3>
          <ul className="vx-login-products">
            {products.map((product) => (
              <li data-current={product.current} key={product.name}>
                <span style={{ background: product.color }}>{product.name[0]}</span>
                <div>
                  <strong>{product.name}</strong>
                  <small data-live={product.live}>{product.live ? "No ar" : "Em desenvolvimento"}</small>
                </div>
              </li>
            ))}
          </ul>
        </section>
      </aside>

      <section className="vx-login-main">
        <div aria-label="Tema da tela" className="vx-login-theme" role="group">
          <button aria-pressed={theme === "light"} onClick={() => chooseTheme("light")} type="button">Claro</button>
          <button aria-pressed={theme === "dark"} onClick={() => chooseTheme("dark")} type="button">Escuro</button>
        </div>

        <div className="vx-login-card">
          <Link className="vx-login-hero" href="/">
            <span>TR</span>
            <strong>To-No-Rango</strong>
            <small>por Veloso Solution</small>
            <em>Peça. Prepare. Entregue.</em>
          </Link>

          <div className="vx-login-id">
            <svg aria-hidden="true" fill="none" height="20" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8" viewBox="0 0 24 24" width="20">
              <path d="M12 3 5 6v5c0 4.5 2.9 8.3 7 10 4.1-1.7 7-5.5 7-10V6Z" />
              <path d="m9 12 2 2 4-4" />
            </svg>
            <span>
              <strong>Veloso ID</strong>
              <small>Um acesso para os produtos Veloso Solution</small>
            </span>
          </div>

          {showEmail ? (
            <div className="vx-login-entry">
              <h1>Entrar com e-mail</h1>
              <form className="vx-login-form" onSubmit={handleEmail}>
                <label className="field">
                  <span>E-mail</span>
                  <input autoComplete="username" onChange={(event) => setEmail(event.target.value)} placeholder="voce@sualoja.com.br" required type="email" value={email} />
                </label>

                <label className="field">
                  <span>Senha</span>
                  <div className="vx-login-password">
                    <input autoComplete="current-password" onChange={(event) => setPassword(event.target.value)} required type={showPassword ? "text" : "password"} value={password} />
                    <button aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"} aria-pressed={showPassword} onClick={() => setShowPassword((shown) => !shown)} type="button">
                      {showPassword ? "Ocultar" : "Mostrar"}
                    </button>
                  </div>
                </label>

                {notice ? <p className="vx-login-notice">{notice}</p> : null}
                {error ? <p className="form-error" role="alert">{error}</p> : null}

                <button className="vx-login-submit" disabled={busy} type="submit">{busy ? "Validando acesso..." : "Entrar"}</button>
              </form>
              <button className="vx-login-switch" onClick={() => { setUsingEmail(false); setNotice(null); }} type="button">Voltar para CPF ou CNPJ</button>
            </div>
          ) : (
            <div className="vx-login-entry">
              <h1>Acesse sua loja</h1>
              <form className="vx-login-form" noValidate onSubmit={handleContinue}>
                <label className="field">
                  <span>CPF ou CNPJ</span>
                  <input autoComplete="username" inputMode="numeric" onChange={(event) => { setDocument(formatDocument(event.target.value)); setNotice(null); }} placeholder="000.000.000-00" value={document} />
                </label>

                {notice ? <p className="form-error" role="alert">{notice}</p> : null}
                {error ? <p className="form-error" role="alert">{error}</p> : null}

                <button className="vx-login-submit" disabled={busy} type="submit">{busy ? "Aguarde..." : "Continuar"}</button>
              </form>
              {passwordLogin ? <button className="vx-login-switch" onClick={() => { setUsingEmail(true); setNotice(null); }} type="button">Entrar com e-mail</button> : null}
            </div>
          )}

          <nav aria-label="Ajuda" className="vx-login-links">
            <Link href="/">
              <span><strong>Ainda não é cliente?</strong><small>Conheça o To-No-Rango</small></span>
              <i aria-hidden="true">→</i>
            </Link>
            <a href={WHATSAPP_URL} rel="noreferrer" target="_blank">
              <span><strong>Precisa de ajuda?</strong><small>Fale com a Veloso no WhatsApp</small></span>
              <i aria-hidden="true">→</i>
            </a>
          </nav>

          <small className="vx-login-foot">To-No-Rango · Veloso Solution · 2026</small>
        </div>
      </section>
    </main>
  );
}
