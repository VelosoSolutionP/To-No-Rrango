import { API_BASE_URL, ApiError, problemDetail } from "./api";
import type {
  AdminOrder,
  AdminSession,
  CategoryPayload,
  CreateEstablishmentPayload,
  Establishment,
  MenuCategory,
  MenuItem,
  MenuItemPayload,
  OrderStatus,
  PublicMenu,
  UpdateEstablishmentPayload,
  UpdateOrderStatusPayload,
} from "./types";

export const UNAUTHORIZED_EVENT = "tonorango:admin-unauthorized";

async function adminFetch<T>(
  path: string,
  session: Pick<AdminSession, "token">,
  init?: RequestInit,
): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    cache: "no-store",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${session.token}`,
      ...(init?.headers ?? {}),
    },
  });

  if (!response.ok) {
    if (response.status === 401 && typeof window !== "undefined") {
      window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
    }

    const problem = await response.json().catch(() => null);
    const detail = problemDetail(problem);
    throw new ApiError(detail ?? problem?.title ?? `Falha ao chamar ${path}`, response.status, detail);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export interface LoginResult {
  token: string;
  expiresAt: string;
  user: { tenantId: string; name: string; role: AdminSession["role"] };
}

export async function login(email: string, password: string): Promise<AdminSession> {
  const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password }),
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const detail = problemDetail(problem);
    throw new ApiError(
      response.status === 429 && !detail ? "Muitas tentativas. Aguarde um minuto." : (detail ?? "Não foi possível entrar."),
      response.status,
      detail,
    );
  }

  const result = (await response.json()) as LoginResult;
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

export async function loadModules(session: AdminSession): Promise<string[]> {
  const me = await adminFetch<{ contract: { modules: string[] } }>("/api/admin/me", session);
  return me.contract.modules;
}

/**
 * Small typed client handed to the module screens. The accountant portal passes its own prefix and
 * reuses the same fiscal screens, because its routes mirror the panel's.
 */
export function createAdminHttp(session: Pick<AdminSession, "token">, prefix = "/api/admin") {
  const send = <T>(method: string, path: string, body?: unknown) =>
    adminFetch<T>(`${prefix}${path}`, session, {
      method,
      body: body === undefined ? undefined : JSON.stringify(body),
    });

  return {
    get: <T>(path: string) => send<T>("GET", path),
    post: <T>(path: string, body?: unknown) => send<T>("POST", path, body ?? {}),
    put: <T>(path: string, body: unknown) => send<T>("PUT", path, body),
    patch: <T>(path: string, body: unknown) => send<T>("PATCH", path, body),
    del: <T>(path: string) => send<T>("DELETE", path),
    upload: async <T>(path: string, file: Blob): Promise<T> => {
      const form = new FormData();
      form.append("file", file, "foto.jpg");

      const response = await fetch(`${API_BASE_URL}${prefix}${path}`, {
        method: "POST",
        headers: { Authorization: `Bearer ${session.token}` },
        body: form,
      });

      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        const detail = problemDetail(problem);
        throw new ApiError(detail ?? "Não foi possível enviar a foto.", response.status, detail);
      }

      return (await response.json()) as T;
    },
  };
}

export type AdminHttp = ReturnType<typeof createAdminHttp>;

export const adminApi = {
  listEstablishments(session: AdminSession): Promise<Establishment[]> {
    return adminFetch<Establishment[]>("/api/admin/establishments", session);
  },

  createEstablishment(
    session: AdminSession,
    payload: CreateEstablishmentPayload,
  ): Promise<Establishment> {
    return adminFetch<Establishment>("/api/admin/establishments", session, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  updateEstablishment(
    session: AdminSession,
    establishmentId: string,
    payload: UpdateEstablishmentPayload,
  ): Promise<Establishment> {
    return adminFetch<Establishment>(
      `/api/admin/establishments/${establishmentId}`,
      session,
      { method: "PUT", body: JSON.stringify(payload) },
    );
  },

  getMenu(session: AdminSession, establishmentId: string): Promise<PublicMenu> {
    return adminFetch<PublicMenu>(
      `/api/admin/establishments/${establishmentId}/menu`,
      session,
    );
  },

  createCategory(
    session: AdminSession,
    establishmentId: string,
    payload: CategoryPayload,
  ): Promise<MenuCategory> {
    return adminFetch<MenuCategory>(
      `/api/admin/establishments/${establishmentId}/categories`,
      session,
      { method: "POST", body: JSON.stringify(payload) },
    );
  },

  updateCategory(
    session: AdminSession,
    categoryId: string,
    payload: CategoryPayload,
  ): Promise<MenuCategory> {
    return adminFetch<MenuCategory>(`/api/admin/categories/${categoryId}`, session, {
      method: "PUT",
      body: JSON.stringify(payload),
    });
  },

  deactivateCategory(session: AdminSession, categoryId: string): Promise<MenuCategory> {
    return adminFetch<MenuCategory>(`/api/admin/categories/${categoryId}`, session, {
      method: "DELETE",
    });
  },

  setCategoryActive(
    session: AdminSession,
    categoryId: string,
    isActive: boolean,
  ): Promise<MenuCategory> {
    return adminFetch<MenuCategory>(`/api/admin/categories/${categoryId}/active`, session, {
      method: "PATCH",
      body: JSON.stringify({ isActive }),
    });
  },

  createItem(
    session: AdminSession,
    establishmentId: string,
    payload: MenuItemPayload,
  ): Promise<MenuItem> {
    return adminFetch<MenuItem>(
      `/api/admin/establishments/${establishmentId}/items`,
      session,
      { method: "POST", body: JSON.stringify(payload) },
    );
  },

  updateItem(
    session: AdminSession,
    itemId: string,
    payload: MenuItemPayload,
  ): Promise<MenuItem> {
    return adminFetch<MenuItem>(`/api/admin/items/${itemId}`, session, {
      method: "PUT",
      body: JSON.stringify(payload),
    });
  },

  deactivateItem(session: AdminSession, itemId: string): Promise<MenuItem> {
    return adminFetch<MenuItem>(`/api/admin/items/${itemId}`, session, {
      method: "DELETE",
    });
  },

  setItemActive(
    session: AdminSession,
    itemId: string,
    isActive: boolean,
  ): Promise<MenuItem> {
    return adminFetch<MenuItem>(`/api/admin/items/${itemId}/active`, session, {
      method: "PATCH",
      body: JSON.stringify({ isActive }),
    });
  },

  setItemAvailability(
    session: AdminSession,
    itemId: string,
    isAvailable: boolean,
  ): Promise<MenuItem> {
    return adminFetch<MenuItem>(`/api/admin/items/${itemId}/availability`, session, {
      method: "PATCH",
      body: JSON.stringify({ isAvailable }),
    });
  },

  listOrders(
    session: AdminSession,
    establishmentId: string,
    status?: OrderStatus,
  ): Promise<AdminOrder[]> {
    const params = new URLSearchParams({ take: "100" });
    if (status) {
      params.set("status", status);
    }

    return adminFetch<AdminOrder[]>(
      `/api/admin/establishments/${establishmentId}/orders?${params.toString()}`,
      session,
    );
  },

  updateOrderStatus(
    session: AdminSession,
    orderId: string,
    payload: UpdateOrderStatusPayload,
  ): Promise<AdminOrder> {
    return adminFetch<AdminOrder>(`/api/admin/orders/${orderId}/status`, session, {
      method: "PATCH",
      body: JSON.stringify(payload),
    });
  },
};

export function formatBRL(cents: number): string {
  return new Intl.NumberFormat("pt-BR", {
    style: "currency",
    currency: "BRL",
  }).format(cents / 100);
}

export function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat("pt-BR", {
    dateStyle: "short",
    timeStyle: "short",
  }).format(new Date(value));
}
