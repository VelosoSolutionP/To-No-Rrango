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
      <aside className="vx-login-stage">
        <svg aria-hidden="true" className="vx-login-map" preserveAspectRatio="xMidYMid slice" viewBox="0 0 900 1000">
          <g className="vx-login-blocks">
            <rect height="150" rx="10" width="190" x="60" y="60" />
            <rect height="150" rx="10" width="230" x="300" y="60" />
            <rect height="150" rx="10" width="250" x="580" y="60" />
            <rect height="210" rx="10" width="190" x="60" y="260" />
            <rect height="90" rx="10" width="230" x="300" y="260" />
            <rect height="70" rx="10" width="230" x="300" y="400" />
            <rect height="210" rx="10" width="250" x="580" y="260" />
            <rect height="180" rx="10" width="190" x="60" y="520" />
            <rect className="vx-login-park" height="180" rx="10" width="230" x="300" y="520" />
            <rect height="180" rx="10" width="250" x="580" y="520" />
            <rect height="220" rx="10" width="190" x="60" y="750" />
            <rect height="220" rx="10" width="230" x="300" y="750" />
            <rect height="220" rx="10" width="250" x="580" y="750" />
          </g>
          <path className="vx-login-avenue" d="M-40 930 C 240 800 330 720 555 495 S 800 150 960 60" />
          <path className="vx-login-route" d="M30 495 H275 V235 H555 V495 H770" id="vx-login-route" />
          <g className="vx-login-pin" transform="translate(770 495)">
            <circle r="30" />
            <text dy="10" textAnchor="middle">🏠</text>
          </g>
          <g className="vx-login-pin vx-login-moto">
            <circle r="30" />
            <text dy="10" textAnchor="middle">🛵</text>
            <animateMotion dur="14s" fill="freeze" repeatCount="indefinite">
              <mpath href="#vx-login-route" />
            </animateMotion>
          </g>
        </svg>

        <div className="vx-login-eta">
          <i />
          <span>
            <strong>Chega em cerca de 8 min</strong>
            <small>Entrega acompanhada no mapa</small>
          </span>
        </div>

        <div className="vx-login-pitch">
          <p className="eyebrow">Painel da loja</p>
          <h2>Sua operação inteira, ao vivo.</h2>
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
