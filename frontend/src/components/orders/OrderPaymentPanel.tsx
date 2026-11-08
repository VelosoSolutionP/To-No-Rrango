"use client";

import { useEffect, useRef, useState } from "react";
import { api, formatBRL } from "@/lib/api";
import type { CheckoutOptions, OrderPayment, OrderTracking } from "@/lib/types";

interface OrderPaymentPanelProps {
  order: OrderTracking;
  onPaid: () => void;
}

interface CardFormData {
  token: string;
  issuer_id?: string;
  payment_method_id: string;
  installments: number;
  payer: { email: string; identification?: { type: string; number: string } };
}

interface MercadoPagoBricks {
  bricks: () => {
    create: (
      name: string,
      container: string,
      settings: {
        initialization: { amount: number };
        callbacks: {
          onReady: () => void;
          onSubmit: (data: CardFormData) => Promise<void>;
          onError: (error: unknown) => void;
        };
      },
    ) => Promise<{ unmount: () => void }>;
  };
}

declare global {
  interface Window {
    MercadoPago?: new (publicKey: string, options: { locale: string }) => MercadoPagoBricks;
  }
}

function loadSdk(): Promise<void> {
  if (window.MercadoPago) {
    return Promise.resolve();
  }

  return new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "https://sdk.mercadopago.com/js/v2";
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Não foi possível carregar o pagamento com cartão."));
    document.head.appendChild(script);
  });
}

export function OrderPaymentPanel({ order, onPaid }: OrderPaymentPanelProps) {
  const [options, setOptions] = useState<CheckoutOptions | null>(null);
  const [payment, setPayment] = useState<OrderPayment | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const brickStarted = useRef(false);

  const awaitingPayment = order.paymentStatus === "Pending" && order.status !== "Cancelled";
  const wantsPix = awaitingPayment && order.paymentMethod === "Pix" && options?.pixOnline;
  const wantsCard = awaitingPayment && order.paymentMethod === "Online" && options?.cardOnline;

  useEffect(() => {
    if (!awaitingPayment) {
      return;
    }

    let active = true;

    async function check() {
      const current = await api.getPayment(order.code).catch(() => null);

      if (!active) {
        return;
      }

      setPayment(current);

      if (current?.status === "Approved") {
        onPaid();
      }
    }

    api.getPaymentOptions(order.code).then((value) => active && setOptions(value)).catch(() => undefined);
    void check();
    const interval = window.setInterval(check, 8000);

    return () => {
      active = false;
      window.clearInterval(interval);
    };
  }, [awaitingPayment, onPaid, order.code]);

  useEffect(() => {
    if (!wantsCard || !options?.publicKey || brickStarted.current) {
      return;
    }

    brickStarted.current = true;
    let brick: { unmount: () => void } | undefined;

    loadSdk()
      .then(async () => {
        const mp = new window.MercadoPago!(options.publicKey!, { locale: "pt-BR" });
        brick = await mp.bricks().create("cardPayment", "card-payment-brick", {
          initialization: { amount: order.totalInCents / 100 },
          callbacks: {
            onReady: () => undefined,
            onError: () => setError("Verifique os dados do cartão."),
            onSubmit: async (data) => {
              setError(null);
              const result = await api.createCardPayment(order.code, {
                token: data.token,
                paymentMethodId: data.payment_method_id,
                issuerId: data.issuer_id ?? null,
                installments: data.installments,
                payerEmail: data.payer.email,
                documentType: data.payer.identification?.type ?? null,
                documentNumber: data.payer.identification?.number ?? null,
              });
              setPayment(result);

              if (result.status === "Approved") {
                onPaid();
              } else if (result.status === "Rejected") {
                setError("O pagamento foi recusado. Confira os dados ou use outro cartão.");
              }
            },
          },
        });
      })
      .catch((loadError: Error) => setError(loadError.message));

    return () => {
      brick?.unmount();
      brickStarted.current = false;
    };
  }, [onPaid, options?.publicKey, order.code, order.totalInCents, wantsCard]);

  async function generatePix() {
    setBusy(true);
    setError(null);

    try {
      setPayment(await api.createPix(order.code));
    } catch (pixError) {
      setError(pixError instanceof Error ? pixError.message : "Não foi possível gerar o Pix.");
    } finally {
      setBusy(false);
    }
  }

  async function copyPix() {
    if (payment?.pixQrCode) {
      await navigator.clipboard.writeText(payment.pixQrCode);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2500);
    }
  }

  if (order.paymentStatus === "Paid") {
    return (
      <section className="tracking-card">
        <h2>Pagamento confirmado</h2>
        <p className="muted">Recebemos {formatBRL(order.totalInCents)}. Obrigado!</p>
      </section>
    );
  }

  if (!wantsPix && !wantsCard) {
    return null;
  }

  const pix = payment?.channel === "Pix" && payment.status === "Pending" ? payment : null;

  return (
    <section className="tracking-card">
      <h2>Pagamento</h2>
      {wantsPix && !pix ? (
        <button className="primary-button primary-button-full" disabled={busy} onClick={generatePix} type="button">
          {busy ? "Gerando Pix..." : `Pagar ${formatBRL(order.totalInCents)} com Pix`}
        </button>
      ) : null}
      {pix ? (
        <div className="pix-box">
          {pix.pixQrCodeBase64 ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img alt="QR Code do Pix" src={`data:image/png;base64,${pix.pixQrCodeBase64}`} />
          ) : null}
          <p className="muted">Escaneie o QR Code ou use o Pix copia e cola. A confirmação é automática.</p>
          <button className="secondary-button" onClick={copyPix} type="button">
            {copied ? "Código copiado!" : "Copiar código Pix"}
          </button>
        </div>
      ) : null}
      {wantsCard ? <div id="card-payment-brick" /> : null}
      {error ? <p className="form-error">{error}</p> : null}
    </section>
  );
}
