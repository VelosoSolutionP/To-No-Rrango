"use client";

import "./admin.css";
import Link from "next/link";
import { FormEvent, useState } from "react";
import cena from "./login-cena.webp";

interface AdminLoginProps {
  onLogin: (email: string, password: string) => Promise<void>;
  busy: boolean;
  error: string | null;
  /** Back to the Portal after a failed return. Absent while the Veloso ID is not configured. */
  onVelosoId?: () => void;
  /** Only where there is no Portal to go to, such as a local copy. */
  passwordLogin?: boolean;
  /** /admin?acesso=email: the owner of the platform, never a customer. */
  ownerDoor?: boolean;
}

const tiles = [
  { label: "Pedidos", path: "M3 18h18M5 18a7 7 0 0 1 14 0M12 8.5V6M10 6h4" },
  { label: "Cozinha", path: "M7 14a4 4 0 1 1 1-7.9 4.5 4.5 0 0 1 8 0 4 4 0 1 1 1 7.9v5a1 1 0 0 1-1 1H8a1 1 0 0 1-1-1ZM7 16h10" },
  { label: "Estoque", path: "m12 3 8 4.5v9L12 21l-8-4.5v-9ZM4 7.5l8 4.5 8-4.5M12 12v9" },
  { label: "Financeiro", path: "M5 20v-6M12 20V9M19 20V4" },
  { label: "Cardápio Digital", path: "M4 4h6v6H4ZM14 4h6v6h-6ZM4 14h6v6H4ZM14 14h3v3h-3ZM20 14v2M14 20h2M20 20h.01", accent: true },
];

function Icon({ path, size = 22 }: { path: string; size?: number }) {
  return (
    <svg aria-hidden="true" fill="none" height={size} stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.7" viewBox="0 0 24 24" width={size}>
      <path d={path} />
    </svg>
  );
}

export function AdminLogin({ onLogin, busy, error, onVelosoId, passwordLogin = false, ownerDoor = false }: AdminLoginProps) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  async function handleEmail(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await onLogin(email.trim(), password);
  }

  const showEmail = ownerDoor || (passwordLogin && !onVelosoId);

  return (
    <main className="vx-login">
      <header className="vx-login-top">
        <Link className="vx-login-brand" href="/">
          <span>TR</span>
          <div>
            <strong>TO-NO-<b>RANGO</b></strong>
            <small>por Veloso Solution</small>
          </div>
        </Link>
        <Link className="vx-login-product" href="/">
          <i aria-hidden="true">▶</i>
          Conheça o produto
        </Link>
      </header>

      <div className="vx-login-stage">
        <section className="vx-login-copy">
          <p className="vx-login-kicker">Gestão para restaurantes</p>
          <h2>Seu negócio funcionando. <em>Seu controle na palma da mão.</em></h2>
          <p className="vx-login-lead">Cardápio, pedidos, cozinha, entregas e gestão em um só lugar.</p>
          <ul className="vx-login-tiles">
            {tiles.map((tile) => (
              <li data-accent={tile.accent ? "true" : undefined} key={tile.label}>
                <Icon path={tile.path} size={30} />
                <span>{tile.label}</span>
              </li>
            ))}
          </ul>
        </section>

        <div aria-hidden="true" className="vx-login-art">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img alt="" height={cena.height} src={cena.src} width={cena.width} />
        </div>

        <section className="vx-login-card">
          <div className="vx-login-id">
            <svg aria-hidden="true" fill="none" height="46" viewBox="0 0 24 24" width="40">
              <defs>
                <linearGradient id="vx-shield" x1="0" x2="1" y1="0" y2="1">
                  <stop offset="0" stopColor="#8b5cf6" />
                  <stop offset="1" stopColor="#ff6a13" />
                </linearGradient>
              </defs>
              <path d="M12 2.5 4.5 5.4v6c0 4.7 3.1 8.4 7.5 10.1 4.4-1.7 7.5-5.4 7.5-10.1v-6Z" stroke="url(#vx-shield)" strokeLinejoin="round" strokeWidth="1.7" />
              <path d="M12 7.5 8 9v2.7c0 2.3 1.6 4.2 4 5.1 2.4-.9 4-2.8 4-5.1V9Z" fill="url(#vx-shield)" />
            </svg>
            <span>
              <strong>Veloso ID</strong>
              <small>Acesso unificado e seguro</small>
            </span>
          </div>

          <h1>{showEmail ? "Entrar com e-mail" : "Acessar o To-No-Rango"}</h1>

          {showEmail ? (
            <form className="vx-login-form" onSubmit={handleEmail}>
              <label className="vx-login-input">
                <Icon path="M3 6h18v12H3ZM3 7l9 6 9-6" />
                <input aria-label="E-mail" autoComplete="username" onChange={(event) => setEmail(event.target.value)} placeholder="E-mail" required type="email" value={email} />
              </label>
              <label className="vx-login-input">
                <Icon path="M6 11h12v9H6ZM8.5 11V8a3.5 3.5 0 0 1 7 0v3" />
                <input aria-label="Senha" autoComplete="current-password" onChange={(event) => setPassword(event.target.value)} placeholder="Senha" required type={showPassword ? "text" : "password"} value={password} />
                <button aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"} aria-pressed={showPassword} onClick={() => setShowPassword((shown) => !shown)} type="button">
                  {showPassword ? "Ocultar" : "Mostrar"}
                </button>
              </label>

              {error ? <p className="form-error" role="alert">{error}</p> : null}

              <button className="vx-login-submit" disabled={busy} type="submit">{busy ? "Validando acesso..." : "Entrar"}</button>
            </form>
          ) : (
            <div className="vx-login-form">
              <p className="form-error" role="alert">{error ?? "Não foi possível abrir o Veloso ID agora. Tente novamente."}</p>
              <button className="vx-login-submit" disabled={busy || !onVelosoId} onClick={onVelosoId} type="button">
                Entrar com CPF ou CNPJ <Icon path="M5 12h14M13 6l6 6-6 6" />
              </button>
            </div>
          )}

          <p className="vx-login-safe">
            <Icon path="M6 11h12v9H6ZM8.5 11V8a3.5 3.5 0 0 1 7 0v3" size={18} />
            Acesso protegido pelo <b>Veloso ID</b>
          </p>
        </section>
      </div>

      <footer className="vx-login-foot">
        <div>
          <svg aria-hidden="true" height="30" viewBox="0 0 34 28" width="36">
            <defs>
              <linearGradient id="vx-v" x1="0" x2="1" y1="0" y2="1">
                <stop offset="0" stopColor="#ff6a13" />
                <stop offset="1" stopColor="#8b5cf6" />
              </linearGradient>
            </defs>
            <path d="M1 2h9l7 13 7-13h9L17 27Z" fill="url(#vx-v)" />
          </svg>
          <strong>VELOSO <span>SOLUTION</span></strong>
          <small>Ecossistema de gestão, vendas e atendimento.</small>
        </div>
        <p><i aria-hidden="true">⚡</i> Tecnologia para o seu crescimento.</p>
      </footer>
    </main>
  );
}
