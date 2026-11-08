"use client";

import "./admin.css";
import Link from "next/link";
import { FormEvent, useState } from "react";

interface AdminLoginProps {
  onLogin: (email: string, password: string) => Promise<void>;
  busy: boolean;
  error: string | null;
}

export function AdminLogin({ onLogin, busy, error }: AdminLoginProps) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await onLogin(email.trim(), password);
  }

  return (
    <main className="vx-login">
      <section className="admin-login-card card">
        <Link className="admin-brand" href="/">
          <span>TR</span>
          <div>
            <strong>To-No-Rango</strong>
            <small>Painel administrativo</small>
          </div>
        </Link>

        <div>
          <p className="eyebrow">Acesso restrito</p>
          <h1>Entre para operar seu rango.</h1>
          <p className="muted">
            Use o e-mail e a senha cadastrados para o seu estabelecimento.
          </p>
        </div>

        <form className="admin-login-form" onSubmit={handleSubmit}>
          <label className="field">
            <span>E-mail</span>
            <input
              autoComplete="username"
              onChange={(event) => setEmail(event.target.value)}
              required
              type="email"
              value={email}
            />
          </label>

          <label className="field">
            <span>Senha</span>
            <input
              autoComplete="current-password"
              onChange={(event) => setPassword(event.target.value)}
              required
              type="password"
              value={password}
            />
          </label>

          {error ? <p className="form-error">{error}</p> : null}

          <button className="primary-button primary-button-full" disabled={busy} type="submit">
            {busy ? "Validando acesso..." : "Entrar no painel"}
          </button>
        </form>

      </section>
    </main>
  );
}
