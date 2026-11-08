"use client";

import { ImageUpload } from "./ImageUpload";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { formatBRL } from "@/lib/admin-api";
import type {
  CreateEstablishmentPayload,
  Establishment,
  UpdateEstablishmentPayload,
} from "@/lib/types";

interface AdminEstablishmentProps {
  establishment: Establishment | null;
  tenantId: string;
  busy: boolean;
  error: string | null;
  onCreate: (payload: CreateEstablishmentPayload) => Promise<void>;
  onUpdate: (payload: UpdateEstablishmentPayload) => Promise<void>;
}

function moneyToInput(cents: number): string {
  return String(cents / 100);
}

function inputToCents(value: string): number {
  const parsed = Number(value.replace(",", "."));
  return Number.isFinite(parsed) ? Math.round(parsed * 100) : 0;
}

export function AdminEstablishment({
  establishment,
  tenantId,
  busy,
  error,
  onCreate,
  onUpdate,
}: AdminEstablishmentProps) {
  const [logoUrl, setLogoUrl] = useState(establishment?.logoUrl ?? "");
  const [coverUrl, setCoverUrl] = useState(establishment?.coverUrl ?? "");
  const [name, setName] = useState(establishment?.name ?? "");
  const [slug, setSlug] = useState(establishment?.slug ?? "");
  const [description, setDescription] = useState(establishment?.description ?? "");
  const [phone, setPhone] = useState(establishment?.phone ?? "");
  const [whatsApp, setWhatsApp] = useState(establishment?.whatsApp ?? "");
  const [addressLine, setAddressLine] = useState(establishment?.addressLine ?? "");
  const [city, setCity] = useState(establishment?.city ?? "");
  const [state, setState] = useState(establishment?.state ?? "");
  const [postalCode, setPostalCode] = useState(establishment?.postalCode ?? "");
  const [timeZone, setTimeZone] = useState(establishment?.timeZone ?? "America/Sao_Paulo");
  const [primaryColor, setPrimaryColor] = useState(establishment?.primaryColor ?? "#F97316");
  const [secondaryColor, setSecondaryColor] = useState(establishment?.secondaryColor ?? "#8B5CF6");
  const [deliveryFee, setDeliveryFee] = useState(
    moneyToInput(establishment?.deliveryFeeInCents ?? 0),
  );
  const [minimumOrder, setMinimumOrder] = useState(
    moneyToInput(establishment?.minimumOrderInCents ?? 0),
  );
  const [isActive, setIsActive] = useState(establishment?.isActive ?? true);
  const [isPickupEnabled, setIsPickupEnabled] = useState(
    establishment?.isPickupEnabled ?? true,
  );
  const [isDeliveryEnabled, setIsDeliveryEnabled] = useState(
    establishment?.isDeliveryEnabled ?? true,
  );
  const [isDineInEnabled, setIsDineInEnabled] = useState(establishment?.isDineInEnabled ?? false);

  useEffect(() => {
    setName(establishment?.name ?? "");
    setSlug(establishment?.slug ?? "");
    setDescription(establishment?.description ?? "");
    setPhone(establishment?.phone ?? "");
    setWhatsApp(establishment?.whatsApp ?? "");
    setAddressLine(establishment?.addressLine ?? "");
    setCity(establishment?.city ?? "");
    setState(establishment?.state ?? "");
    setPostalCode(establishment?.postalCode ?? "");
    setTimeZone(establishment?.timeZone ?? "America/Sao_Paulo");
    setPrimaryColor(establishment?.primaryColor ?? "#F97316");
    setSecondaryColor(establishment?.secondaryColor ?? "#8B5CF6");
    setDeliveryFee(moneyToInput(establishment?.deliveryFeeInCents ?? 0));
    setMinimumOrder(moneyToInput(establishment?.minimumOrderInCents ?? 0));
    setIsActive(establishment?.isActive ?? true);
    setIsPickupEnabled(establishment?.isPickupEnabled ?? true);
    setIsDeliveryEnabled(establishment?.isDeliveryEnabled ?? true);
    setIsDineInEnabled(establishment?.isDineInEnabled ?? false);
  }, [establishment]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const common = {
      name: name.trim(),
      description: description.trim() || null,
      phone: phone.trim() || null,
      whatsApp: whatsApp.trim() || null,
      addressLine: addressLine.trim() || null,
      city: city.trim() || null,
      state: state.trim() || null,
      postalCode: postalCode.trim() || null,
      primaryColor,
      secondaryColor,
      isPickupEnabled,
      isDeliveryEnabled,
      isDineInEnabled,
      deliveryFeeInCents: inputToCents(deliveryFee),
      minimumOrderInCents: inputToCents(minimumOrder),
    };

    try {
      if (establishment) {
        await onUpdate({
        logoUrl,
        coverUrl,
          ...common,
          timeZone,
          isActive,
        });
        return;
      }

      await onCreate({
        ...common,
        tenantId,
        slug: slug.trim(),
        logoUrl: logoUrl || null,
        coverUrl: coverUrl || null,
      });
    } catch {
      // O erro é exibido pela prop error.
    }
  }

  return (
    <section className="admin-section">
      <div className="admin-section-heading">
        <div>
          <p className="eyebrow">Configurações</p>
          <h2>{establishment ? "Estabelecimento" : "Cadastre seu estabelecimento"}</h2>
          <p className="muted">
            {establishment
              ? "Dados públicos, identidade visual e regras de entrega."
              : "Crie o estabelecimento que será exibido no cardápio público."}
          </p>
        </div>
        {establishment ? (
          <Link
            className="secondary-button secondary-button-small"
            href={`/menu/${establishment.slug}`}
            target="_blank"
          >
            Ver cardápio
          </Link>
        ) : null}
      </div>

      <form className="admin-form admin-establishment-form" onSubmit={handleSubmit}>
        <section className="admin-form-card card">
          <div className="admin-form-card-head">
            <h3>Identidade</h3>
            <p className="muted">Como o estabelecimento aparece para o cliente.</p>
          </div>

          {establishment ? (
            <div className="upload-pair">
              <ImageUpload label="Logo" onChange={setLogoUrl} shape="logo" value={logoUrl} />
              <ImageUpload hint="Aparece no topo do cardápio." label="Foto de capa" onChange={setCoverUrl} shape="cover" value={coverUrl} />
            </div>
          ) : null}

          <div className="admin-form-grid">
            <label className="field">
              <span>Nome</span>
              <input onChange={(event) => setName(event.target.value)} required value={name} />
            </label>

            <label className="field">
              <span>Endereço do cardápio</span>
              <input
                disabled={Boolean(establishment)}
                onChange={(event) => setSlug(event.target.value)}
                placeholder="acai-do-veloso"
                required={!establishment}
                value={slug}
              />
            </label>
          </div>

          <label className="field">
            <span>Descrição</span>
            <textarea
              onChange={(event) => setDescription(event.target.value)}
              rows={3}
              value={description}
            />
          </label>

          <div className="admin-form-grid">
            <label className="field">
              <span>Cor principal</span>
              <div className="admin-color-field">
                <input
                  onChange={(event) => setPrimaryColor(event.target.value)}
                  type="color"
                  value={primaryColor}
                />
                <input
                  onChange={(event) => setPrimaryColor(event.target.value)}
                  value={primaryColor}
                />
              </div>
            </label>

            <label className="field">
              <span>Cor secundária</span>
              <div className="admin-color-field">
                <input
                  onChange={(event) => setSecondaryColor(event.target.value)}
                  type="color"
                  value={secondaryColor}
                />
                <input
                  onChange={(event) => setSecondaryColor(event.target.value)}
                  value={secondaryColor}
                />
              </div>
            </label>
          </div>
        </section>

        <section className="admin-form-card card">
          <div className="admin-form-card-head">
            <h3>Contato e endereço</h3>
            <p className="muted">Informações exibidas no cardápio e usadas na operação.</p>
          </div>

          <div className="admin-form-grid">
            <label className="field">
              <span>Telefone</span>
              <input onChange={(event) => setPhone(event.target.value)} value={phone} />
            </label>

            <label className="field">
              <span>WhatsApp</span>
              <input onChange={(event) => setWhatsApp(event.target.value)} value={whatsApp} />
            </label>
          </div>

          <label className="field">
            <span>Endereço</span>
            <input
              onChange={(event) => setAddressLine(event.target.value)}
              placeholder="Rua, número e bairro"
              value={addressLine}
            />
          </label>

          <div className="admin-form-grid admin-form-grid-3">
            <label className="field">
              <span>Cidade</span>
              <input onChange={(event) => setCity(event.target.value)} value={city} />
            </label>

            <label className="field">
              <span>UF</span>
              <input maxLength={2} onChange={(event) => setState(event.target.value)} value={state} />
            </label>

            <label className="field">
              <span>CEP</span>
              <input onChange={(event) => setPostalCode(event.target.value)} value={postalCode} />
            </label>
          </div>
        </section>

        <section className="admin-form-card card">
          <div className="admin-form-card-head">
            <h3>Operação</h3>
            <p className="muted">Modalidades, taxas e disponibilidade da loja.</p>
          </div>

          <div className="admin-form-grid admin-form-grid-3">
            <label className="field">
              <span>Taxa de entrega (R$)</span>
              <input
                min={0}
                onChange={(event) => setDeliveryFee(event.target.value)}
                step="0.01"
                type="number"
                value={deliveryFee}
              />
            </label>

            <label className="field">
              <span>Pedido mínimo (R$)</span>
              <input
                min={0}
                onChange={(event) => setMinimumOrder(event.target.value)}
                step="0.01"
                type="number"
                value={minimumOrder}
              />
            </label>

            <label className="field">
              <span>Fuso horário</span>
              <input onChange={(event) => setTimeZone(event.target.value)} value={timeZone} />
            </label>
          </div>

          <div className="admin-check-grid">
            {establishment ? (
              <label className="admin-check">
                <input
                  checked={isActive}
                  onChange={(event) => setIsActive(event.target.checked)}
                  type="checkbox"
                />
                Estabelecimento ativo
              </label>
            ) : null}
            <label className="admin-check">
              <input
                checked={isPickupEnabled}
                onChange={(event) => setIsPickupEnabled(event.target.checked)}
                type="checkbox"
              />
              Retirada
            </label>
            <label className="admin-check">
              <input
                checked={isDeliveryEnabled}
                onChange={(event) => setIsDeliveryEnabled(event.target.checked)}
                type="checkbox"
              />
              Entrega
            </label>
            <label className="admin-check">
              <input
                checked={isDineInEnabled}
                onChange={(event) => setIsDineInEnabled(event.target.checked)}
                type="checkbox"
              />
              Consumo no local
            </label>
          </div>

          <p className="admin-summary-line">
            Entrega {formatBRL(inputToCents(deliveryFee))} · Mínimo{" "}
            {formatBRL(inputToCents(minimumOrder))}
          </p>
        </section>

        {error ? <p className="form-error">{error}</p> : null}

        <div className="admin-form-actions">
          <button className="primary-button" disabled={busy} type="submit">
            {busy ? "Salvando..." : establishment ? "Salvar alterações" : "Criar estabelecimento"}
          </button>
        </div>
      </form>
    </section>
  );
}
