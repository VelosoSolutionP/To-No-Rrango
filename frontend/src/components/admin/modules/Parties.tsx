"use client";

import { type FormEvent, useRef, useState } from "react";
import { formatBRL, formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { DataTable, FormDrawer, StatusChip } from "../DataTable";
import { ErrorMessage, ModuleHeading, text, useAction, useLoad } from "./shared";

interface Customer {
  id: string;
  name: string;
  phone: string;
  document: string | null;
  email: string | null;
  notes: string | null;
  ordersCount: number;
  totalOrderedInCents: number;
  lastOrderAt: string | null;
  isActive?: boolean;
  createdAt?: string | null;
}

interface Supplier {
  id: string;
  name: string;
  tradeName: string | null;
  document: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
  isActive: boolean;
}

interface CnpjData {
  cnpj: string;
  legalName: string;
  tradeName: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
}

interface Props {
  http: AdminHttp;
  establishmentId: string;
}

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((word) => word[0]).join("").toUpperCase();
}

function formatDocument(value: string | null) {
  if (!value) return "—";
  const digits = value.replace(/\D/g, "");
  if (digits.length === 14) return digits.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, "$1.$2.$3/$4-$5");
  if (digits.length === 11) return digits.replace(/^(\d{3})(\d{3})(\d{3})(\d{2})$/, "$1.$2.$3-$4");
  return value;
}

function formatPhone(value: string | null) {
  if (!value) return "—";
  const digits = value.replace(/\D/g, "").replace(/^55(?=\d{10,11}$)/, "");
  if (digits.length === 11) return digits.replace(/^(\d{2})(\d{5})(\d{4})$/, "($1) $2-$3");
  if (digits.length === 10) return digits.replace(/^(\d{2})(\d{4})(\d{4})$/, "($1) $2-$3");
  return value;
}

function daysSince(value: string | null) {
  return value ? Math.floor((Date.now() - new Date(value).getTime()) / 86400000) : null;
}

/* ------------------------------------------------------------------------------------------------ */

export function Customers({ http, establishmentId }: Props) {
  const list = useLoad(() => http.get<Customer[]>(`/establishments/${establishmentId}/customers`), [establishmentId]);
  const action = useAction(list.reload);
  const [editing, setEditing] = useState<Customer | "new" | null>(null);
  const rows = list.data ?? [];
  const active = rows.filter((row) => row.isActive !== false);
  const revenue = active.reduce((sum, row) => sum + row.totalOrderedInCents, 0);
  const recurring = active.filter((row) => row.ordersCount > 1).length;

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const payload = { name: text(form, "name"), phone: text(form, "phone"), document: text(form, "document"), email: text(form, "email"), notes: text(form, "notes") };
    const done = await action.run(() =>
      editing === "new" ? http.post(`/establishments/${establishmentId}/customers`, payload) : http.put(`/customers/${(editing as Customer).id}`, payload),
    );
    if (done) setEditing(null);
  }

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Cadastros" title="Clientes" hint="Criados sozinhos a cada pedido (cardápio, WhatsApp, PDV), com histórico de compras." />
      <div className="kpi-strip">
        <div className="kpi"><small>Clientes ativos</small><strong>{active.length}</strong></div>
        <div className="kpi"><small>Compram de novo</small><strong>{recurring}</strong></div>
        <div className="kpi"><small>Total comprado</small><strong>{formatBRL(revenue)}</strong></div>
        <div className="kpi"><small>Ticket médio por cliente</small><strong>{formatBRL(active.length ? Math.round(revenue / active.length) : 0)}</strong></div>
      </div>
      <ErrorMessage message={list.error ?? action.error} />
      <DataTable
        actions={[
          { icon: "edit", label: "Editar", onClick: (row) => setEditing(row) },
          { icon: "whatsappSend", label: "Chamar no WhatsApp", tone: "accent", onClick: (row) => window.open(`https://wa.me/${row.phone.replace(/\D/g, "").replace(/^(?!55)/, "55")}`, "_blank", "noopener") },
          { icon: "power", label: "Reativar", hidden: (row) => row.isActive !== false, onClick: (row) => void action.run(() => http.patch(`/customers/${row.id}/active`, { isActive: true })) },
          {
            icon: "trash",
            label: "Excluir",
            tone: "danger",
            hidden: (row) => row.isActive === false,
            onClick: (row) =>
              window.confirm(row.ordersCount > 0 ? `${row.name} tem ${row.ordersCount} pedido(s): será inativado, não apagado. Continuar?` : `Excluir ${row.name}?`) &&
              void action.run(() => http.del(`/customers/${row.id}`)),
          },
        ]}
        columns={[
          {
            key: "name",
            title: "Cliente",
            value: (row) => `${row.name} ${row.email ?? ""}`,
            render: (row) => (
              <span className="cell-main">
                <span className="avatar">{initials(row.name)}</span>
                <div>
                  <strong>{row.name}</strong>
                  <small>{row.email ?? "sem e-mail"}</small>
                </div>
              </span>
            ),
          },
          { key: "phone", title: "Telefone", value: (row) => row.phone, render: (row) => formatPhone(row.phone) },
          { key: "doc", title: "CPF/CNPJ", value: (row) => row.document ?? "", render: (row) => formatDocument(row.document), hideOnMobile: true },
          { key: "orders", title: "Pedidos", value: (row) => row.ordersCount, align: "right" },
          { key: "total", title: "Total comprado", value: (row) => row.totalOrderedInCents, render: (row) => formatBRL(row.totalOrderedInCents), align: "right" },
          {
            key: "last",
            title: "Último pedido",
            value: (row) => row.lastOrderAt ?? "",
            render: (row) => (row.lastOrderAt ? <>{formatDateTime(row.lastOrderAt)}<small>há {daysSince(row.lastOrderAt)} dia(s)</small></> : "—"),
            hideOnMobile: true,
          },
          {
            key: "status",
            title: "Situação",
            value: (row) => (row.isActive === false ? "inativo" : row.ordersCount > 1 ? "recorrente" : "novo"),
            render: (row) =>
              row.isActive === false ? <StatusChip tone="muted">Inativo</StatusChip> : row.ordersCount > 1 ? <StatusChip tone="ok">Recorrente</StatusChip> : <StatusChip tone="info">Novo</StatusChip>,
          },
        ]}
        createLabel="Novo cliente"
        empty="Os clientes aparecem aqui a cada pedido."
        filters={[
          { key: "recurring", label: "Recorrentes", test: (row) => row.isActive !== false && row.ordersCount > 1 },
          { key: "away", label: "Sumidos (30+ dias)", test: (row) => row.isActive !== false && (daysSince(row.lastOrderAt) ?? 0) > 30 },
          { key: "inactive", label: "Inativos", test: (row) => row.isActive === false },
        ]}
        loading={list.loading && !list.data}
        onCreate={() => setEditing("new")}
        onRowClick={(row) => setEditing(row)}
        rowKey={(row) => row.id}
        rows={rows}
        searchPlaceholder="Pesquisar por nome, telefone, CPF, e-mail…"
      />

      {editing ? (
        <FormDrawer onClose={() => setEditing(null)} subtitle={editing === "new" ? "Novo cadastro" : "Cliente"} title={editing === "new" ? "Novo cliente" : editing.name}>
          <form className="drawer-form" key={editing === "new" ? "new" : editing.id} onSubmit={save}>
            <label className="field"><span>Nome *</span><input defaultValue={editing === "new" ? "" : editing.name} maxLength={120} name="name" required /></label>
            <div className="field-row">
              <label className="field"><span>Telefone / WhatsApp *</span><input defaultValue={editing === "new" ? "" : editing.phone} inputMode="tel" name="phone" readOnly={editing !== "new"} required /></label>
              <label className="field"><span>CPF ou CNPJ</span><input defaultValue={editing === "new" ? "" : editing.document ?? ""} name="document" /></label>
            </div>
            <label className="field"><span>E-mail</span><input defaultValue={editing === "new" ? "" : editing.email ?? ""} name="email" type="email" /></label>
            <label className="field"><span>Observações</span><textarea defaultValue={editing === "new" ? "" : editing.notes ?? ""} maxLength={500} name="notes" /></label>
            {editing !== "new" ? (
              <div className="kpi-strip">
                <div className="kpi"><small>Pedidos</small><strong>{editing.ordersCount}</strong></div>
                <div className="kpi"><small>Total</small><strong>{formatBRL(editing.totalOrderedInCents)}</strong></div>
              </div>
            ) : null}
            <ErrorMessage message={action.error} />
            <div className="drawer-foot">
              <button className="secondary-button" onClick={() => setEditing(null)} type="button">Cancelar</button>
              <button className="primary-button" disabled={action.busy} type="submit">Salvar</button>
            </div>
          </form>
        </FormDrawer>
      ) : null}
    </section>
  );
}

/* ------------------------------------------------------------------------------------------------ */

export function Suppliers({ http, establishmentId }: Props) {
  const list = useLoad(() => http.get<Supplier[]>(`/establishments/${establishmentId}/suppliers`), [establishmentId]);
  const action = useAction(list.reload);
  const [editing, setEditing] = useState<Supplier | "new" | null>(null);
  const [lookupMessage, setLookupMessage] = useState<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const rows = list.data ?? [];

  async function lookup() {
    const field = (name: string) => formRef.current?.elements.namedItem(name) as HTMLInputElement | null;
    const cnpj = field("document")?.value.replace(/\D/g, "");
    if (!cnpj || cnpj.length !== 14) return;
    setLookupMessage("Consultando CNPJ…");
    try {
      const data = await http.get<CnpjData>(`/lookups/cnpj/${cnpj}`);
      const fill = (name: string, value: string | null) => {
        const input = field(name);
        if (input && !input.value && value) input.value = value;
      };
      fill("name", data.legalName);
      fill("tradeName", data.tradeName);
      fill("phone", data.phone);
      fill("email", data.email);
      fill("address", data.address);
      setLookupMessage("Dados preenchidos pela Receita. Confira antes de salvar.");
    } catch {
      setLookupMessage("Não consegui consultar o CNPJ agora. Preencha à mão.");
    }
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const payload = {
      name: text(form, "name"),
      tradeName: text(form, "tradeName"),
      document: text(form, "document"),
      phone: text(form, "phone"),
      email: text(form, "email"),
      address: text(form, "address"),
      isActive: editing === "new" ? true : (editing as Supplier).isActive,
    };
    const done = await action.run(() =>
      editing === "new" ? http.post(`/establishments/${establishmentId}/suppliers`, payload) : http.put(`/suppliers/${(editing as Supplier).id}`, payload),
    );
    if (done) {
      setEditing(null);
      setLookupMessage(null);
    }
  }

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Cadastros" title="Fornecedores" hint="Usados nas entradas de mercadoria e nas notas de compra." />
      <ErrorMessage message={list.error ?? action.error} />
      <DataTable
        actions={[
          { icon: "edit", label: "Editar", onClick: (row) => setEditing(row) },
          { icon: "power", label: "Reativar", hidden: (row) => row.isActive, onClick: (row) => void action.run(() => http.put(`/suppliers/${row.id}`, { ...row, isActive: true })) },
          { icon: "trash", label: "Inativar", tone: "danger", hidden: (row) => !row.isActive, onClick: (row) => window.confirm(`Inativar ${row.tradeName ?? row.name}?`) && void action.run(() => http.del(`/suppliers/${row.id}`)) },
        ]}
        columns={[
          {
            key: "name",
            title: "Fornecedor",
            value: (row) => `${row.tradeName ?? ""} ${row.name}`,
            render: (row) => (
              <span className="cell-main">
                <span className="avatar">{initials(row.tradeName ?? row.name)}</span>
                <div>
                  <strong>{row.tradeName ?? row.name}</strong>
                  <small>{row.tradeName ? row.name : row.email ?? "—"}</small>
                </div>
              </span>
            ),
          },
          { key: "doc", title: "CNPJ/CPF", value: (row) => row.document ?? "", render: (row) => formatDocument(row.document) },
          { key: "phone", title: "Telefone", value: (row) => row.phone ?? "", render: (row) => formatPhone(row.phone), hideOnMobile: true },
          { key: "email", title: "E-mail", value: (row) => row.email ?? "", hideOnMobile: true },
          { key: "status", title: "Situação", value: (row) => (row.isActive ? "ativo" : "inativo"), render: (row) => (row.isActive ? <StatusChip tone="ok">Ativo</StatusChip> : <StatusChip tone="muted">Inativo</StatusChip>) },
        ]}
        createLabel="Novo fornecedor"
        empty="Nenhum fornecedor cadastrado."
        filters={[
          { key: "active", label: "Ativos", test: (row) => row.isActive },
          { key: "inactive", label: "Inativos", test: (row) => !row.isActive },
        ]}
        loading={list.loading && !list.data}
        onCreate={() => setEditing("new")}
        onRowClick={(row) => setEditing(row)}
        rowKey={(row) => row.id}
        rows={rows}
        searchPlaceholder="Pesquisar por nome, fantasia, CNPJ…"
      />

      {editing ? (
        <FormDrawer onClose={() => setEditing(null)} subtitle={editing === "new" ? "Novo cadastro" : "Fornecedor"} title={editing === "new" ? "Novo fornecedor" : editing.tradeName ?? editing.name}>
          <form className="drawer-form" key={editing === "new" ? "new" : editing.id} onSubmit={save} ref={formRef}>
            <label className="field"><span>CNPJ ou CPF (o CNPJ preenche o resto sozinho)</span><input defaultValue={editing === "new" ? "" : editing.document ?? ""} name="document" onBlur={() => void lookup()} /></label>
            {lookupMessage ? <p className="muted">{lookupMessage}</p> : null}
            <label className="field"><span>Razão social *</span><input defaultValue={editing === "new" ? "" : editing.name} name="name" required /></label>
            <label className="field"><span>Nome fantasia</span><input defaultValue={editing === "new" ? "" : editing.tradeName ?? ""} name="tradeName" /></label>
            <div className="field-row">
              <label className="field"><span>Telefone</span><input defaultValue={editing === "new" ? "" : editing.phone ?? ""} name="phone" /></label>
              <label className="field"><span>E-mail</span><input defaultValue={editing === "new" ? "" : editing.email ?? ""} name="email" /></label>
            </div>
            <label className="field"><span>Endereço</span><input defaultValue={editing === "new" ? "" : editing.address ?? ""} name="address" /></label>
            <ErrorMessage message={action.error} />
            <div className="drawer-foot">
              <button className="secondary-button" onClick={() => setEditing(null)} type="button">Cancelar</button>
              <button className="primary-button" disabled={action.busy} type="submit">Salvar</button>
            </div>
          </form>
        </FormDrawer>
      ) : null}
    </section>
  );
}
