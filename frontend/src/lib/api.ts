import type {
  CepData,
  CheckoutOptions,
  CreateOrderRequest,
  OrderPayment,
  OrderTracking,
  PublicMenu,
  RatePayload,
  RatingState,
} from "./types";

export const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

/** Reads the human sentence of a ProblemDetails response, including validation problems. */
export function problemDetail(problem: { detail?: string; errors?: Record<string, string[]> } | null): string | undefined {
  const message = problem?.detail ?? (problem?.errors ? Object.values(problem.errors)[0]?.[0] : undefined);
  // Domain messages may carry the .NET parameter name; the person only needs the sentence.
  return message?.replace(/\s*\(Parameter '[^']*'\)/g, "").trim();
}

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly detail?: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
    },
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const detail = problemDetail(problem);
    throw new ApiError(detail ?? problem?.title ?? `Falha ao chamar ${path}`, response.status, detail);
  }

  return (await response.json()) as T;
}

export const api = {
  getPublicMenu(slug: string): Promise<PublicMenu> {
    return apiFetch<PublicMenu>(`/api/public/establishments/${encodeURIComponent(slug)}/menu`, {
      cache: "no-store",
    });
  },

  createOrder(slug: string, request: CreateOrderRequest): Promise<OrderTracking> {
    return apiFetch<OrderTracking>(`/api/public/establishments/${encodeURIComponent(slug)}/orders`, {
      method: "POST",
      body: JSON.stringify(request),
    });
  },

  getCheckoutOptions(slug: string): Promise<CheckoutOptions> {
    return apiFetch<CheckoutOptions>(`/api/public/establishments/${encodeURIComponent(slug)}/checkout-options`, {
      cache: "no-store",
    });
  },

  getDeliveryQuote(slug: string, district: string): Promise<{ feeInCents: number; estimatedMinutes: number | null }> {
    const query = new URLSearchParams({ district });
    return apiFetch(`/api/public/establishments/${encodeURIComponent(slug)}/delivery-quote?${query.toString()}`, {
      cache: "no-store",
    });
  },

  lookupCep(cep: string): Promise<CepData> {
    return apiFetch<CepData>(`/api/public/lookups/cep/${encodeURIComponent(cep)}`, { cache: "no-store" });
  },

  getPaymentOptions(code: string): Promise<CheckoutOptions> {
    return apiFetch<CheckoutOptions>(`/api/public/orders/${encodeURIComponent(code)}/payment-options`, {
      cache: "no-store",
    });
  },

  getPayment(code: string): Promise<OrderPayment | null> {
    return apiFetch<OrderPayment | null>(`/api/public/orders/${encodeURIComponent(code)}/payment`, {
      cache: "no-store",
    });
  },

  createPix(code: string): Promise<OrderPayment> {
    return apiFetch<OrderPayment>(`/api/public/orders/${encodeURIComponent(code)}/payments/pix`, {
      method: "POST",
      body: JSON.stringify({ payerEmail: null }),
    });
  },

  /** Mercado Pago checkout page (card or Pix) for one order. */
  createCheckout(code: string): Promise<{ url: string }> {
    return apiFetch<{ url: string }>(`/api/public/orders/${encodeURIComponent(code)}/payments/checkout`, { method: "POST" });
  },

  confirmCheckout(code: string, paymentId: string): Promise<OrderPayment | null> {
    return apiFetch<OrderPayment | null>(`/api/public/orders/${encodeURIComponent(code)}/payments/checkout/confirm`, {
      method: "POST",
      body: JSON.stringify({ paymentId }),
    });
  },

  createCardPayment(code: string, payload: unknown): Promise<OrderPayment> {
    return apiFetch<OrderPayment>(`/api/public/orders/${encodeURIComponent(code)}/payments/card`, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  orderEventsUrl(code: string): string {
    return `${API_BASE_URL}/api/public/orders/${encodeURIComponent(code)}/events`;
  },

  getRating(code: string): Promise<RatingState> {
    return apiFetch<RatingState>(`/api/public/orders/${encodeURIComponent(code)}/rating`, { cache: "no-store" });
  },

  rateOrder(code: string, payload: RatePayload): Promise<RatingState> {
    return apiFetch<RatingState>(`/api/public/orders/${encodeURIComponent(code)}/rating`, {
      method: "POST",
      body: JSON.stringify(payload),
    });
  },

  trackOrder(code: string): Promise<OrderTracking> {
    return apiFetch<OrderTracking>(`/api/public/orders/${encodeURIComponent(code)}`, {
      cache: "no-store",
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
