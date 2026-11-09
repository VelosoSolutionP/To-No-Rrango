"use client";

import "./admin.css";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";

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
        <Link className="admin-brand" href="/">
          <span>TR</span>
          <div>
            <strong>To-No-Rango</strong>
            <small>por Veloso Solution</small>
          </div>
        </Link>

        <div className="vx-login-welcome">
          <p className="eyebrow">Bem-vindo</p>
          <h2>Que bom ter você de volta.</h2>
          <p>Cardápio, pedidos e entregas da sua loja em um só lugar.</p>
        </div>

        <small className="vx-login-foot">Veloso Solution · 2026</small>
      </aside>

      <section className="vx-login-main">
        <div aria-label="Tema da tela" className="vx-login-theme" role="group">
          <button aria-pressed={theme === "light"} onClick={() => chooseTheme("light")} type="button">Claro</button>
          <button aria-pressed={theme === "dark"} onClick={() => chooseTheme("dark")} type="button">Escuro</button>
        </div>

        <div className="vx-login-card">
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
            <>
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
            </>
          ) : (
            <>
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
            </>
          )}
        </div>
      </section>
    </main>
  );
}
