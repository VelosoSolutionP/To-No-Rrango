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

const modules = [
  {
    tone: "orange",
    title: "Pedidos e cozinha",
    text: "Cardápio digital, pedidos e cozinha em tempo real.",
    count: 3,
    icon: "M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4ZM3 6h18M16 10a4 4 0 0 1-8 0",
  },
  {
    tone: "green",
    title: "Entregas no mapa",
    text: "Rastreio ao vivo, entregadores e taxas por bairro.",
    count: 3,
    icon: "M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0ZM12 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z",
  },
  {
    tone: "lilac",
    title: "WhatsApp com a ZEST",
    text: "Atendimento e venda direto na conversa.",
    count: 2,
    icon: "M21 12a9 9 0 0 1-13.3 7.9L3 21l1.2-4.6A9 9 0 1 1 21 12Z",
  },
  {
    tone: "silver",
    title: "Gestão completa",
    text: "Estoque, financeiro, clientes e fiscal.",
    count: 4,
    icon: "M3 3v18h18M8 17v-5M13 17V7M18 17v-8",
  },
];

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
      <aside className="vx-login-stage">
        <div className="vx-login-showcase">
          <header>
            <h2>Sua operação inteira, ao vivo.</h2>
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
                </div>
                <span className="vx-login-count">
                  <b>{String(module.count).padStart(2, "0")}</b>
                  <em>recursos</em>
                  <i aria-hidden="true">
                    {[0, 1, 2, 3].map((bar) => <u data-on={bar < module.count} key={bar} />)}
                  </i>
                </span>
              </li>
            ))}
          </ul>

          <figure className="vx-login-preview">
            <figcaption>Veja o painel</figcaption>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img alt="Painel do To-No-Rango com entregas em andamento no mapa" className="vx-login-shot" height={painel.height} src={painel.src} width={painel.width} />
          </figure>

          <p className="vx-login-note">Acesso restrito · dados da sua loja</p>
        </div>
      </aside>

      <section className="vx-login-panel">
        <div className="vx-login-card">
          <Link className="admin-brand" href="/">
            <span>TR</span>
            <div>
              <strong>To-No-Rango</strong>
              <small>por Veloso Solution</small>
            </div>
          </Link>

          <h1>Acessar o painel</h1>
          <p className="vx-login-lead">Use o e-mail e a senha cadastrados para o seu estabelecimento.</p>

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

          <Link className="vx-login-more" href="/">
            <span>
              <strong>Ainda não é cliente?</strong>
              <small>Conheça o To-No-Rango</small>
            </span>
            <em aria-hidden="true">→</em>
          </Link>
        </div>

        <p className="vx-login-foot">To-No-Rango · Veloso Solution · 2026</p>
      </section>
    </main>
  );
}
