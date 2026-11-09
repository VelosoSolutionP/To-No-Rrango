"use client";

import "./admin.css";
import Link from "next/link";
import { FormEvent, useState } from "react";
import painel from "./login-painel.jpg";

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
      <aside className="vx-login-stage">
        <div className="vx-login-showcase">
          <div>
            <p className="eyebrow">Painel da loja</p>
            <h2>Sua operação inteira, ao vivo.</h2>
          </div>
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img alt="Painel do To-No-Rango com entregas em andamento no mapa" className="vx-login-shot" height={painel.height} src={painel.src} width={painel.width} />
          <ul>
            <li><strong>Pedidos</strong><span>Do cardápio à cozinha, em tempo real.</span></li>
            <li><strong>Rastreio</strong><span>Todas as motos no mesmo mapa.</span></li>
            <li><strong>WhatsApp</strong><span>Atendimento e venda com a ZEST.</span></li>
            <li><strong>Gestão</strong><span>Estoque, financeiro e fiscal.</span></li>
          </ul>
        </div>
      </aside>

      <section className="vx-login-panel">
        <Link className="admin-brand" href="/">
          <span>TR</span>
          <div>
            <strong>To-No-Rango</strong>
            <small>Painel administrativo</small>
          </div>
        </Link>

        <div className="vx-login-body">
          <p className="eyebrow">Acesso restrito</p>
          <h1>Entre para operar seu rango.</h1>
          <p className="muted">Use o e-mail e a senha cadastrados para o seu estabelecimento.</p>

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
        </div>

        <Link className="vx-login-back" href="/">← Voltar para o site</Link>
      </section>
    </main>
  );
}
