"use client";

import { type FormEvent, useRef, useState } from "react";
import { formatBRL, formatDateTime, type AdminHttp } from "@/lib/admin-api";
import { Empty, ErrorMessage as ErrorText, Input, ModuleHeading, messageOf, text, useAction, useLoad } from "./shared";

interface Customer {
  id: string;
  name: string;
  phone: string;
  document: string | null;
  email: string | null;
  ordersCount: number;
  totalOrderedInCents: number;
  lastOrderAt: string | null;
}

interface Supplier {
  id: string;
  name: string;
  tradeName: string | null;
  document: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
}

interface CnpjData {
  legalName: string;
  tradeName: string | null;
  phone: string | null;
  email: string | null;
  address: string | null;
  status: string | null;
}

interface Props {
  http: AdminHttp;
  establishmentId: string;
}

function CustomerList({ http, establishmentId }: Props) {
  const list = useLoad(() => http.get<Customer[]>(`/establishments/${establishmentId}/customers`), [establishmentId]);
  const action = useAction(list.reload);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const done = await action.run(() =>
      http.post(`/establishments/${establishmentId}/customers`, {
        name: text(form, "name"),
        phone: text(form, "phone"),
        document: text(form, "document"),
        email: text(form, "email"),
        notes: null,
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
        <Input label="WhatsApp *" name="phone" required />
        <Input label="CPF/CNPJ" name="document" />
        <Input label="E-mail" name="email" type="email" />
        <button className="primary-button" disabled={action.busy} type="submit">Adicionar cliente</button>
      </form>
      <ErrorText message={action.error ?? list.error} />
      {list.data?.length === 0 ? <Empty>Os clientes aparecem aqui automaticamente a cada pedido.</Empty> : null}
      {list.data && list.data.length > 0 ? (
        <div className="card admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr><th>Cliente</th><th>WhatsApp</th><th>Documento</th><th className="num">Pedidos</th><th className="num">Total pedido</th><th>Último pedido</th></tr>
            </thead>
            <tbody>
              {list.data.map((customer) => (
                <tr key={customer.id}>
                  <td>{customer.name}</td>
                  <td>{customer.phone}</td>
                  <td>{customer.document ?? "—"}</td>
                  <td className="num">{customer.ordersCount}</td>
                  <td className="num">{formatBRL(customer.totalOrderedInCents)}</td>
                  <td>{customer.lastOrderAt ? formatDateTime(customer.lastOrderAt) : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}

export function Suppliers({ http, establishmentId }: Props) {
  const list = useLoad(() => http.get<Supplier[]>(`/establishments/${establishmentId}/suppliers`), [establishmentId]);
  const action = useAction(list.reload);
  const [lookupMessage, setLookupMessage] = useState<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);

  function setField(name: string, value: string | null) {
    const input = formRef.current?.elements.namedItem(name) as HTMLInputElement | null;

    if (input && value) {
      input.value = value;
    }
  }

  async function lookup() {
    const cnpj = (formRef.current?.elements.namedItem("document") as HTMLInputElement | null)?.value.replace(/\D/g, "");

    if (!cnpj || cnpj.length !== 14) {
      return;
    }

    setLookupMessage("Consultando CNPJ...");

    try {
      const data = await http.get<CnpjData>(`/lookups/cnpj/${cnpj}`);
      setField("name", data.legalName);
      setField("tradeName", data.tradeName);
      setField("phone", data.phone);
      setField("email", data.email);
      setField("address", data.address);
      setLookupMessage(data.status ? `Situação cadastral: ${data.status}` : null);
    } catch (error) {
      setLookupMessage(messageOf(error));
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const done = await action.run(() =>
      http.post(`/establishments/${establishmentId}/suppliers`, {
        name: text(form, "name"),
        tradeName: text(form, "tradeName"),
        document: text(form, "document"),
        phone: text(form, "phone"),
        email: text(form, "email"),
        address: text(form, "address"),
        isActive: true,
      }),
    );

    if (done) {
      element.reset();
      setLookupMessage(null);
    }
  }

  return (
    <>
      <form className="card module-form" onSubmit={submit} ref={formRef}>
        <Input label="CNPJ/CPF (preenche sozinho)" name="document" onBlur={lookup} />
        <Input label="Razão social *" name="name" required />
        <Input label="Nome fantasia" name="tradeName" />
        <Input label="Telefone" name="phone" />
        <Input label="E-mail" name="email" />
        <Input label="Endereço" name="address" wide />
        <button className="primary-button" disabled={action.busy} type="submit">Adicionar fornecedor</button>
        {lookupMessage ? <p className="muted module-note">{lookupMessage}</p> : null}
      </form>
      <ErrorText message={action.error ?? list.error} />
      {list.data?.length === 0 ? <Empty>Nenhum fornecedor cadastrado.</Empty> : null}
      {list.data && list.data.length > 0 ? (
        <div className="card admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Fornecedor</th><th>Documento</th><th>Telefone</th><th>E-mail</th></tr></thead>
            <tbody>
              {list.data.map((supplier) => (
                <tr key={supplier.id}>
                  <td>{supplier.tradeName ?? supplier.name}</td>
                  <td>{supplier.document ?? "—"}</td>
                  <td>{supplier.phone ?? "—"}</td>
                  <td>{supplier.email ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}

export function Customers(props: Props) {
  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Cadastros" title="Clientes" hint="Criados automaticamente a cada pedido, com o histórico de compras." />
      <CustomerList {...props} />
    </section>
  );
}
