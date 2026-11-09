"use client";

import "./admin.css";
import Link from "next/link";
import { FormEvent, useState } from "react";

interface AdminLoginProps {
  onLogin: (email: string, password: string) => Promise<void>;
  busy: boolean;
  error: string | null;
}

const areas = ["Pedidos", "Cozinha", "Entregas", "WhatsApp", "Estoque", "Financeiro", "Fiscal"];

export function AdminLogin({ onLogin, busy, error }: AdminLoginProps) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await onLogin(email.trim(), password);
  }

  return (
    <main className="vx-login">
      <header className="vx-login-top">
        <Link className="admin-brand" href="/">
          <span>TR</span>
          <div>
            <strong>To-No-Rango</strong>
            <small>por Veloso Solution</small>
          </div>
        </Link>
        <Link className="vx-login-top-link" href="/">Ainda não é cliente? <b>Conheça →</b></Link>
      </header>

      <div className="vx-login-center">
        <section className="vx-login-statement">
          <h2>Do pedido à entrega, <em>tudo na sua mão.</em></h2>
          <p>Cardápio, cozinha, entregas e gestão no mesmo painel, ao vivo.</p>
        </section>

        <section className="vx-login-access">
          <h1>Acessar o painel</h1>

          <form className="admin-login-form" onSubmit={handleSubmit}>
            <label className="field">
              <span>E-mail</span>
              <input
                autoComplete="username"
                onChange={(event) => setEmail(event.target.value)}
                placeholder="voce@sualoja.com.br"
                required
                type="email"
                value={email}
              />
            </label>

            <label className="field">
              <span>Senha</span>
              <div className="vx-login-password">
                <input
                  autoComplete="current-password"
                  onChange={(event) => setPassword(event.target.value)}
                  required
                  type={showPassword ? "text" : "password"}
                  value={password}
                />
                <button aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"} aria-pressed={showPassword} onClick={() => setShowPassword((shown) => !shown)} type="button">
                  {showPassword ? "Ocultar" : "Mostrar"}
                </button>
              </div>
            </label>

            {error ? <p className="form-error">{error}</p> : null}

            <button className="primary-button primary-button-full" disabled={busy} type="submit">
              {busy ? "Validando acesso..." : "Entrar no painel"}
            </button>
          </form>
        </section>
      </div>

      <footer className="vx-login-strip">
        <ul>
          {areas.map((area) => <li key={area}>{area}</li>)}
        </ul>
        <small>Veloso Solution · 2026</small>
      </footer>
    </main>
  );
}
