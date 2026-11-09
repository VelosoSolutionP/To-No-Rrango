import { API_BASE_URL, ApiError } from "./api";
import { loadModules } from "./admin-api";
import type { AdminSession } from "./types";

const NONCE_KEY = "tonorango.veloso.nonce";

export interface VelosoOptions {
  velosoId: boolean;
  passwordLogin: boolean;
}

export async function loadVelosoOptions(): Promise<VelosoOptions> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/auth/veloso-id/options`);
    return response.ok ? ((await response.json()) as VelosoOptions) : { velosoId: false, passwordLogin: false };
  } catch {
    return { velosoId: false, passwordLogin: false };
  }
}

function randomNonce(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(24));
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/**
 * Goes to the Portal, replacing this page so the back button does not bounce the person here again.
 * The nonce ties the return to this very tab.
 */
export async function startVelosoId(silent = false): Promise<void> {
  const nonce = randomNonce();
  window.sessionStorage.setItem(NONCE_KEY, nonce);

  const response = await fetch(`${API_BASE_URL}/api/auth/veloso-id/start`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ nonce }),
  });

  if (!response.ok) {
    throw new ApiError("Não foi possível iniciar o acesso.", response.status);
  }

  const { url } = (await response.json()) as { url: string };
  window.location.replace(silent ? `${url}&prompt=none` : url);
}

const TRIED_KEY = "tonorango.veloso.tried";

/** Saindo do painel, o Portal pede o CPF ou CNPJ em vez de entrar de novo sozinho. */
export function markVelosoLeft(): void {
  window.sessionStorage.setItem(TRIED_KEY, "1");
}

/**
 * Sem sessão, o acesso é sempre pelo Portal. Na primeira vez da aba vai sem mostrar nada, e aparelho
 * já confirmado volta logado; depois disso abre a tela do Portal, que pede o CPF ou CNPJ.
 * @returns false quando a página fica aqui: Veloso ID desligado ou Portal fora do ar.
 */
export async function enterThroughPortal(): Promise<boolean> {
  if (new URLSearchParams(window.location.search).has("error")) {
    window.history.replaceState(null, "", window.location.pathname);
  }

  const options = await loadVelosoOptions();

  if (!options.velosoId) {
    return false;
  }

  const silent = !window.sessionStorage.getItem(TRIED_KEY);
  window.sessionStorage.setItem(TRIED_KEY, "1");

  try {
    await startVelosoId(silent);
    return true;
  } catch {
    return false;
  }
}

/** A porta do dono da plataforma, igual ao /entrar?acesso=email do Portal. Cliente não tem senha. */
export function isOwnerDoor(): boolean {
  return new URLSearchParams(window.location.search).get("acesso") === "email";
}

export function isVelosoIdReturn(): boolean {
  if (typeof window === "undefined") {
    return false;
  }

  const query = new URLSearchParams(window.location.search);
  return query.has("code") && query.has("state");
}

export async function completeVelosoId(): Promise<AdminSession> {
  const query = new URLSearchParams(window.location.search);
  const nonce = window.sessionStorage.getItem(NONCE_KEY) ?? "";

  // The code leaves the address bar and the history before anything else happens.
  window.history.replaceState(null, "", window.location.pathname);
  window.sessionStorage.removeItem(NONCE_KEY);

  const response = await fetch(`${API_BASE_URL}/api/auth/veloso-id/complete`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code: query.get("code"), state: query.get("state"), nonce }),
  });

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { detail?: string } | null;
    throw new ApiError(problem?.detail ?? "Não foi possível concluir o acesso.", response.status, problem?.detail);
  }

  const result = (await response.json()) as { token: string; expiresAt: string; user: { tenantId: string; name: string; role: AdminSession["role"] } };
  const session: AdminSession = {
    token: result.token,
    expiresAt: result.expiresAt,
    tenantId: result.user.tenantId,
    userName: result.user.name,
    role: result.user.role,
    modules: [],
  };

  return { ...session, modules: await loadModules(session) };
}

export async function endVelosoId(session: AdminSession): Promise<void> {
  await fetch(`${API_BASE_URL}/api/auth/veloso-id/logout`, {
    method: "POST",
    headers: { Authorization: `Bearer ${session.token}` },
  }).catch(() => undefined);
}
