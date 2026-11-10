"use client";

import { useEffect, useMemo, useState } from "react";
import { api, formatBRL } from "@/lib/api";
import { cartLineTotal, cartSubtotal, type CartLine } from "@/lib/cart";
import type {
  CreateOrderRequest,
  DeliveryAddress,
  CheckoutOptions,
  Establishment,
  FulfillmentType,
  OrderTracking,
  PaymentMethod,
} from "@/lib/types";

interface CheckoutModalProps {
  establishment: Establishment;
  lines: CartLine[];
  onClose: () => void;
  onSuccess: (order: OrderTracking) => void;
  /** Set when the customer opened the menu from a table QR Code. */
  tableLabel?: string | null;
}

const fulfillmentLabels: Record<FulfillmentType, string> = {
  Delivery: "Entrega",
  Pickup: "Retirada",
  DineIn: "Consumo no local",
};

const paymentLabels: Record<PaymentMethod, string> = {
  Pix: "Pix",
  Cash: "Dinheiro",
  CardOnDelivery: "Cartão na entrega",
  Online: "Cartão online",
};

function firstFulfillment(establishment: Establishment): FulfillmentType {
  if (establishment.isDeliveryEnabled) return "Delivery";
  if (establishment.isPickupEnabled) return "Pickup";
  return "DineIn";
}

export function CheckoutModal({ establishment, lines, onClose, onSuccess, tableLabel = null }: CheckoutModalProps) {
  const [fulfillmentType, setFulfillmentType] = useState<FulfillmentType>(() =>
    tableLabel ? "DineIn" : firstFulfillment(establishment),
  );
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>("Pix");
  const [customerName, setCustomerName] = useState("");
  const [customerPhone, setCustomerPhone] = useState("");
  const [notes, setNotes] = useState("");
  const [address, setAddress] = useState<DeliveryAddress>({
    street: "",
    number: "",
    complement: null,
    district: "",
    city: establishment.city ?? "",
    state: establishment.state ?? "",
    postalCode: establishment.postalCode ?? "",
  });
  const [idempotencyKey, setIdempotencyKey] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [options, setOptions] = useState<CheckoutOptions | null>(null);
  const [zoneFee, setZoneFee] = useState<number | null>(null);
  const [zoneError, setZoneError] = useState<string | null>(null);

  useEffect(() => {
    setIdempotencyKey(crypto.randomUUID());
    api.getCheckoutOptions(establishment.slug).then(setOptions).catch(() => setOptions(null));
  }, [establishment.slug]);

  async function fillFromCep(value: string) {
    if (value.replace(/\D/g, "").length !== 8) {
      return;
    }

    try {
      const found = await api.lookupCep(value);
      setAddress((current) => ({
        ...current,
        street: found.street ?? current.street,
        district: found.district ?? current.district,
        city: found.city || current.city,
        state: found.state || current.state,
      }));

      if (found.district) {
        await quoteDistrict(found.district);
      }
    } catch {
      // O cliente segue preenchendo o endereço manualmente.
    }
  }

  async function quoteDistrict(district: string) {
    if (!options?.hasDeliveryZones || !district.trim()) {
      return;
    }

    try {
      const quote = await api.getDeliveryQuote(establishment.slug, district.trim());
      setZoneFee(quote.feeInCents);
      setZoneError(null);
    } catch (quoteError) {
      setZoneFee(null);
      setZoneError(quoteError instanceof Error ? quoteError.message : "Não entregamos neste bairro.");
    }
  }

  const subtotal = cartSubtotal(lines);
  const deliveryFee = fulfillmentType === "Delivery" ? (zoneFee ?? (options?.hasDeliveryZones ? 0 : establishment.deliveryFeeInCents)) : 0;
  const total = subtotal + deliveryFee;
  const belowMinimum = subtotal < establishment.minimumOrderInCents;

  const fulfillmentOptions = useMemo(() => {
    const options: FulfillmentType[] = [];
    if (establishment.isDeliveryEnabled) options.push("Delivery");
    if (establishment.isPickupEnabled) options.push("Pickup");
    if (establishment.isDineInEnabled) options.push("DineIn");
    return options;
  }, [establishment]);

  function updateAddress(field: keyof DeliveryAddress, value: string) {
    setAddress((current) => ({ ...current, [field]: value }));
  }

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    if (belowMinimum) {
      setError("O pedido ainda não atingiu o valor mínimo.");
      return;
    }

    if (!customerName.trim() || !customerPhone.trim()) {
      setError("Informe seu nome e telefone para acompanhar o pedido.");
      return;
    }

    if (fulfillmentType === "Delivery") {
      if (
        !address.street.trim() ||
        !address.number.trim() ||
        !address.district.trim() ||
        !address.city.trim() ||
        !address.state.trim() ||
        !address.postalCode.trim()
      ) {
        setError("Preencha o endereço completo para entrega.");
        return;
      }
    }

    const request: CreateOrderRequest = {
      customerName: customerName.trim(),
      customerPhone: customerPhone.trim(),
      fulfillmentType,
      paymentMethod,
      tableLabel: tableLabel ?? undefined,
      notes: notes.trim() || null,
      idempotencyKey: idempotencyKey || null,
      deliveryAddress:
        fulfillmentType === "Delivery"
          ? {
              ...address,
              street: address.street.trim(),
              number: address.number.trim(),
              complement: address.complement?.trim() || null,
              reference: address.reference?.trim() || null,
              district: address.district.trim(),
              city: address.city.trim(),
              state: address.state.trim(),
              postalCode: address.postalCode.trim(),
            }
          : null,
      items: lines.map((line) => ({
        menuItemId: line.item.id,
        quantity: line.quantity,
        notes: line.notes || null,
        optionIds: line.options.map((selected) => selected.option.id),
      })),
    };

    try {
      setSubmitting(true);
      const order = await api.createOrder(establishment.slug, request);
      onSuccess(order);
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : "Não foi possível criar o pedido.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section
        aria-modal="true"
        className="modal checkout-modal"
        role="dialog"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <button aria-label="Fechar" className="modal-close" onClick={onClose} type="button">
          ×
        </button>

        <header>
          <p className="eyebrow">Última etapa</p>
          <h2>Finalizar pedido</h2>
          <p className="muted">Confira os dados e escolha como deseja receber.</p>
        </header>

        <form className="checkout-form" onSubmit={handleSubmit}>
          {tableLabel ? (
            <p className="table-banner">🍽️ Pedido da <strong>mesa {tableLabel}</strong>: vai direto para a cozinha e chega na sua mesa.</p>
          ) : null}
          <fieldset className="choice-group" hidden={Boolean(tableLabel)}>
            <legend>Como você quer receber?</legend>
            <div className="choice-grid">
              {fulfillmentOptions.map((option) => (
                <label
                  className={`choice-card ${fulfillmentType === option ? "is-selected" : ""}`}
                  key={option}
                >
                  <input
                    checked={fulfillmentType === option}
                    name="fulfillmentType"
                    onChange={() => setFulfillmentType(option)}
                    type="radio"
                  />
                  <strong>{fulfillmentLabels[option]}</strong>
                  <span>
                    {option === "Delivery"
                      ? options?.hasDeliveryZones
                        ? "Taxa por bairro"
                        : formatBRL(establishment.deliveryFeeInCents)
                      : option === "Pickup"
                        ? "Sem taxa"
                        : "No estabelecimento"}
                  </span>
                </label>
              ))}
            </div>
          </fieldset>

          <div className="form-grid">
            <label className="field">
              <span>Seu nome *</span>
              <input
                autoComplete="name"
                maxLength={120}
                onChange={(event) => setCustomerName(event.target.value)}
                placeholder="Como podemos chamar você?"
                value={customerName}
              />
            </label>
            <label className="field">
              <span>WhatsApp *</span>
              <input
                autoComplete="tel"
                inputMode="tel"
                maxLength={30}
                onChange={(event) => setCustomerPhone(event.target.value)}
                placeholder="(11) 99999-9999"
                value={customerPhone}
              />
            </label>
          </div>

          {fulfillmentType === "Delivery" ? (
            <div className="address-box">
              <h3>Endereço de entrega</h3>
              <div className="form-grid form-grid-address">
                <label className="field field-wide">
                  <span>Rua *</span>
                  <input
                    autoComplete="address-line1"
                    maxLength={200}
                    onChange={(event) => updateAddress("street", event.target.value)}
                    value={address.street}
                  />
                </label>
                <label className="field">
                  <span>Número *</span>
                  <input
                    maxLength={20}
                    onChange={(event) => updateAddress("number", event.target.value)}
                    value={address.number}
                  />
                </label>
                <label className="field">
                  <span>Complemento</span>
                  <input
                    maxLength={100}
                    onChange={(event) => updateAddress("complement", event.target.value)}
                    value={address.complement ?? ""}
                  />
                </label>
                <label className="field">
                  <span>Ponto de referência</span>
                  <input
                    maxLength={150}
                    onChange={(event) => updateAddress("reference", event.target.value)}
                    placeholder="Ex.: portão azul, ao lado da padaria"
                    value={address.reference ?? ""}
                  />
                </label>
                <label className="field">
                  <span>Bairro *</span>
                  <input
                    maxLength={80}
                    onBlur={(event) => quoteDistrict(event.target.value)}
                    onChange={(event) => updateAddress("district", event.target.value)}
                    value={address.district}
                  />
                </label>
                <label className="field">
                  <span>Cidade *</span>
                  <input
                    autoComplete="address-level2"
                    maxLength={80}
                    onChange={(event) => updateAddress("city", event.target.value)}
                    value={address.city}
                  />
                </label>
                <label className="field">
                  <span>UF *</span>
                  <input
                    maxLength={2}
                    onChange={(event) => updateAddress("state", event.target.value.toUpperCase())}
                    value={address.state}
                  />
                </label>
                <label className="field">
                  <span>CEP *</span>
                  <input
                    autoComplete="postal-code"
                    maxLength={10}
                    onBlur={(event) => fillFromCep(event.target.value)}
                    onChange={(event) => updateAddress("postalCode", event.target.value)}
                    value={address.postalCode}
                  />
                </label>
              </div>
            </div>
          ) : null}

          <fieldset className="choice-group">
            <legend>Forma de pagamento</legend>
            <div className="choice-grid choice-grid-payment">
              {(
                [...(["Pix", "Cash", "CardOnDelivery"] as PaymentMethod[]), ...(options?.cardOnline ? (["Online"] as PaymentMethod[]) : [])]
              ).map((option) => (
                <label
                  className={`choice-card ${paymentMethod === option ? "is-selected" : ""}`}
                  key={option}
                >
                  <input
                    checked={paymentMethod === option}
                    name="paymentMethod"
                    onChange={() => setPaymentMethod(option)}
                    type="radio"
                  />
                  <strong>{paymentLabels[option]}</strong>
                </label>
              ))}
            </div>
          </fieldset>

          <label className="field">
            <span>Observações do pedido</span>
            <textarea
              maxLength={500}
              onChange={(event) => setNotes(event.target.value)}
              placeholder="Alguma observação geral?"
              rows={2}
              value={notes}
            />
          </label>

          <div className="checkout-summary">
            {lines.map((line) => (
              <div className="summary-row" key={line.key}>
                <span>
                  {line.quantity}× {line.item.name}
                </span>
                <strong>{formatBRL(cartLineTotal(line))}</strong>
              </div>
            ))}
            <div className="summary-row">
              <span>Subtotal</span>
              <strong>{formatBRL(subtotal)}</strong>
            </div>
            {deliveryFee > 0 ? (
              <div className="summary-row">
                <span>Entrega</span>
                <strong>{formatBRL(deliveryFee)}</strong>
              </div>
            ) : null}
            <div className="summary-row summary-total">
              <span>Total</span>
              <strong>{formatBRL(total)}</strong>
            </div>
          </div>

          {zoneError && fulfillmentType === "Delivery" ? <p className="form-error">{zoneError}</p> : null}
          {error ? <p className="form-error">{error}</p> : null}

          <button className="primary-button primary-button-full" disabled={submitting} type="submit">
            {submitting ? "Enviando pedido..." : `Confirmar pedido · ${formatBRL(total)}`}
          </button>
        </form>
      </section>
    </div>
  );
}
