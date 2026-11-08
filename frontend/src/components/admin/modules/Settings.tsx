"use client";

import { type FormEvent, type ReactNode, useState } from "react";
import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import type { UserRole } from "@/lib/types";
import { Check, Empty, ErrorMessage as ErrorText, Input, ModuleHeading, SubTabs, text, toCents, toNumber, useAction, useLoad } from "./shared";

interface Props {
  http: AdminHttp;
  establishmentId: string;
}

interface PaymentSettings {
  hasAccessToken: boolean;
  hasWebhookSecret: boolean;
  publicKey: string | null;
  isPixEnabled: boolean;
  isCardEnabled: boolean;
  webhookUrl: string | null;
}

interface Zone {
  id: string;
  district: string;
  feeInCents: number;
  estimatedMinutes: number | null;
  isActive: boolean;
}

interface User {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  isActive: boolean;
}

const roleLabels: Record<UserRole, string> = { Owner: "Dono", Manager: "Gerente", Attendant: "Atendente", Kitchen: "Cozinha" };

function Payments({ http, establishmentId }: Props) {
  const settings = useLoad(() => http.get<PaymentSettings>(`/establishments/${establishmentId}/payment-settings`), [establishmentId]);
  const action = useAction(settings.reload);

  if (!settings.data) {
    return <ErrorText message={settings.error} />;
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const done = await action.run(() =>
      http.put(`/establishments/${establishmentId}/payment-settings`, {
        accessToken: text(form, "accessToken"),
        webhookSecret: text(form, "webhookSecret"),
        publicKey: text(form, "publicKey"),
        isPixEnabled: form.get("pix") === "on",
        isCardEnabled: form.get("card") === "on",
      }),
    );

    if (done) {
      (element.elements.namedItem("accessToken") as HTMLInputElement).value = "";
      (element.elements.namedItem("webhookSecret") as HTMLInputElement).value = "";
    }
  }

  return (
    <form className="card module-form" key={String(settings.data.isPixEnabled) + settings.data.publicKey} onSubmit={submit}>
      <Input autoComplete="off" label={`Access Token ${settings.data.hasAccessToken ? "(já salvo; preencha para trocar)" : "*"}`} name="accessToken" type="password" />
      <Input defaultValue={settings.data.publicKey ?? ""} label="Public Key (necessária para cartão)" name="publicKey" />
      <Input autoComplete="off" label={`Assinatura do webhook ${settings.data.hasWebhookSecret ? "(já salva)" : ""}`} name="webhookSecret" type="password" />
      <Check defaultChecked={settings.data.isPixEnabled} label="Aceitar Pix online" name="pix" />
      <Check defaultChecked={settings.data.isCardEnabled} label="Aceitar cartão online" name="card" />
      <button className="primary-button" disabled={action.busy} type="submit">Salvar</button>
      {settings.data.webhookUrl ? <p className="muted module-note">URL de notificação: <code>{settings.data.webhookUrl}</code></p> : null}
      <p className="muted module-note">O dinheiro cai direto na conta Mercado Pago deste estabelecimento. As credenciais ficam criptografadas.</p>
      <ErrorText message={action.error} />
    </form>
  );
}

export function Zones({ http, establishmentId }: Props) {
  const zones = useLoad(() => http.get<Zone[]>(`/establishments/${establishmentId}/delivery-zones`), [establishmentId]);
  const action = useAction(zones.reload);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const minutes = toNumber(form.get("minutes"));
    const done = await action.run(() =>
      http.post(`/establishments/${establishmentId}/delivery-zones`, {
        district: text(form, "district"),
        feeInCents: toCents(form.get("fee")),
        estimatedMinutes: minutes > 0 ? minutes : null,
        isActive: true,
      }),
    );

    if (done) {
      element.reset();
    }
  }

  return (
    <>
      <form className="card module-form" onSubmit={submit}>
        <Input label="Bairro *" name="district" required />
        <Input inputMode="decimal" label="Taxa (R$) *" name="fee" required />
        <Input inputMode="numeric" label="Tempo estimado (min)" name="minutes" />
        <button className="primary-button" disabled={action.busy} type="submit">Adicionar região</button>
      </form>
      <ErrorText message={action.error ?? zones.error} />
      {zones.data?.length === 0 ? <Empty>Sem regiões, vale a taxa fixa do estabelecimento para qualquer endereço. Ao cadastrar uma região, só os bairros listados são atendidos.</Empty> : null}
      {zones.data && zones.data.length > 0 ? (
        <div className="card admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Bairro</th><th className="num">Taxa</th><th className="num">Tempo</th><th>Situação</th><th /></tr></thead>
            <tbody>
              {zones.data.map((zone) => (
                <tr key={zone.id}>
                  <td>{zone.district}</td>
                  <td className="num">{formatBRL(zone.feeInCents)}</td>
                  <td className="num">{zone.estimatedMinutes ? `${zone.estimatedMinutes} min` : "—"}</td>
                  <td>{zone.isActive ? "Ativa" : "Inativa"}</td>
                  <td className="table-actions">
                    <button
                      className="link-button"
                      disabled={action.busy}
                      onClick={() => action.run(() => http.put(`/delivery-zones/${zone.id}`, { ...zone, isActive: !zone.isActive }))}
                      type="button"
                    >
                      {zone.isActive ? "Desativar" : "Ativar"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}

function Users({ http }: { http: AdminHttp }) {
  const users = useLoad(() => http.get<User[]>("/users"), []);
  const action = useAction(users.reload);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const done = await action.run(() =>
      http.post("/users", {
        name: text(form, "name"),
        email: text(form, "email"),
        password: String(form.get("password") ?? ""),
        role: form.get("role"),
      }),
    );

    if (done) {
      element.reset();
    }
  }

  return (
    <>
      <form className="card module-form" onSubmit={submit}>
        <Input label="Nome *" name="name" required />
        <Input autoComplete="off" label="E-mail *" name="email" required type="email" />
        <Input autoComplete="new-password" label="Senha inicial *" minLength={8} name="password" required type="password" />
        <label className="field">
          <span>Perfil</span>
          <select name="role">
            <option value="Manager">Gerente</option>
            <option value="Attendant">Atendente</option>
            <option value="Kitchen">Cozinha</option>
            <option value="Owner">Dono</option>
          </select>
        </label>
        <button className="primary-button" disabled={action.busy} type="submit">Adicionar usuário</button>
      </form>
      <ErrorText message={action.error ?? users.error} />
      {users.data && users.data.length > 0 ? (
        <div className="card admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Nome</th><th>E-mail</th><th>Perfil</th><th>Situação</th><th /></tr></thead>
            <tbody>
              {users.data.map((user) => (
                <tr key={user.id}>
                  <td>{user.name}</td>
                  <td>{user.email}</td>
                  <td>{roleLabels[user.role]}</td>
                  <td>{user.isActive ? "Ativo" : "Inativo"}</td>
                  <td className="table-actions">
                    <button className="link-button" disabled={action.busy} onClick={() => action.run(() => http.patch(`/users/${user.id}/active`, { isActive: !user.isActive }))} type="button">
                      {user.isActive ? "Desativar" : "Ativar"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}

export function Settings({ company, modules, isOwner, http, establishmentId }: { company: ReactNode; modules: string[]; isOwner: boolean; http: AdminHttp | null; establishmentId: string }) {
  type View = "company" | "integrations" | "users";
  const ready = http !== null && establishmentId !== "";
  const options: Array<{ value: View; label: string }> = [
    { value: "company", label: "Empresa" },
    ...(ready && modules.includes("pagamentos") ? [{ value: "integrations" as View, label: "Pagamentos e integrações" }] : []),
    ...(ready && isOwner ? [{ value: "users" as View, label: "Usuários e permissões" }] : []),
  ];
  const [view, setView] = useState<View>("company");

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Ajustes" title="Configurações" />
      <SubTabs onChange={setView} options={options} value={view} />
      {view === "company" ? company : null}
      {view === "integrations" && http ? <Payments establishmentId={establishmentId} http={http} /> : null}
      {view === "users" && isOwner && http ? <Users http={http} /> : null}
    </section>
  );
}
