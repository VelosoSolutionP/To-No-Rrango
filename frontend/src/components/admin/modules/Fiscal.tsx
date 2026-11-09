"use client";

import { type FormEvent, useState } from "react";
import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import { Empty, ErrorMessage, Input, ModuleHeading, text, toCents, toDecimal, useAction, useLoad } from "./shared";

interface Purchase {
  id: string;
  invoiceNumber: string | null;
  accessKey: string | null;
  issuedOn: string;
  totalInCents: number;
}

interface FiscalProfile {
  legalName: string;
  tradeName: string | null;
  cnpj: string;
  stateRegistration: string;
  street: string;
  number: string;
  district: string;
  city: string;
  state: string;
  postalCode: string;
  cityIbgeCode: string;
  series: number;
  nextNumber: number;
  taxRegime: "LucroPresumido" | "LucroReal";
  accountantName: string | null;
  accountantCpf: string | null;
  accountantCrc: string | null;
  accountantEmail: string | null;
  spedProfile: string | null;
}

interface FiscalProduct {
  menuItemId: string;
  name: string;
  ncm: string | null;
  cfop: string | null;
  cest: string | null;
  icmsCst: string | null;
  icmsRate: number | null;
  pisCofinsCst: string | null;
  pisRate: number | null;
  cofinsRate: number | null;
}

interface PendingOrder {
  orderId: string;
  code: string;
  customerName: string;
  totalInCents: number;
  createdAt: string;
}

interface FiscalDocument {
  id: string;
  orderId: string;
  orderCode: string;
  series: number;
  number: number;
  environment: number;
  status: "Processing" | "Authorized" | "Rejected";
  accessKey: string | null;
  message: string | null;
  totalInCents: number;
  hasXml: boolean;
}

interface FiscalOverview {
  isMonitorConfigured: boolean;
  environment: number;
  profile: FiscalProfile | null;
  products: FiscalProduct[];
  pendingOrders: PendingOrder[];
  documents: FiscalDocument[];
}

const statusLabels: Record<FiscalDocument["status"], string> = {
  Processing: "Sem resposta",
  Authorized: "Autorizada",
  Rejected: "Rejeitada",
};

function ProductRow({ product, onSave, busy }: { product: FiscalProduct; onSave: (product: FiscalProduct) => void; busy: boolean }) {
  const [draft, setDraft] = useState(product);
  const edit = (field: keyof FiscalProduct, value: string) => setDraft({ ...draft, [field]: value });
  const rate = (field: "icmsRate" | "pisRate" | "cofinsRate", label: string) => (
    <td><input aria-label={`${label} de ${product.name}`} className="fiscal-input" inputMode="decimal" onChange={(event) => edit(field, event.target.value)} value={draft[field] ?? ""} /></td>
  );
  const code = (field: "icmsCst" | "pisCofinsCst", label: string, options: string[]) => (
    <td>
      <select aria-label={`${label} de ${product.name}`} className="fiscal-input" onChange={(event) => edit(field, event.target.value)} value={draft[field] ?? ""}>
        <option value="">—</option>
        {options.map((item) => <option key={item} value={item}>{item}</option>)}
      </select>
    </td>
  );

  return (
    <tr>
      <td>{product.name}</td>
      <td><input aria-label={`NCM de ${product.name}`} className="fiscal-input" inputMode="numeric" onChange={(event) => edit("ncm", event.target.value)} value={draft.ncm ?? ""} /></td>
      <td><input aria-label={`CFOP de ${product.name}`} className="fiscal-input" inputMode="numeric" onChange={(event) => edit("cfop", event.target.value)} value={draft.cfop ?? ""} /></td>
      <td><input aria-label={`CEST de ${product.name}`} className="fiscal-input" inputMode="numeric" onChange={(event) => edit("cest", event.target.value)} value={draft.cest ?? ""} /></td>
      {code("icmsCst", "CST de ICMS", ["00", "40", "41", "60"])}
      {rate("icmsRate", "Alíquota de ICMS")}
      {code("pisCofinsCst", "CST de PIS e COFINS", ["01", "02", "04", "06", "07", "08", "09"])}
      {rate("pisRate", "Alíquota de PIS")}
      {rate("cofinsRate", "Alíquota de COFINS")}
      <td className="table-actions"><button className="link-button" disabled={busy} onClick={() => onSave(draft)} type="button">Salvar</button></td>
    </tr>
  );
}

export function Fiscal({ http, establishmentId, modules, onOpenInventory }: { http: AdminHttp; establishmentId: string; modules: string[]; onOpenInventory: () => void }) {
  const hasNfce = modules.includes("fiscal_nfce");
  const hasEntry = modules.includes("fiscal");
  const hasSintegra = hasNfce && modules.includes("fiscal_sintegra");
  const hasSped = hasNfce && modules.includes("fiscal_sped");
  const [warnings, setWarnings] = useState<string[]>([]);
  const base = `/establishments/${establishmentId}`;
  const purchases = useLoad(() => http.get<Purchase[]>(`${base}/purchases`).catch(() => []), [establishmentId]);
  const overview = useLoad<FiscalOverview | null>(() => (hasNfce ? http.get<FiscalOverview>(`${base}/fiscal`) : Promise.resolve(null)), [establishmentId, hasNfce]);
  const action = useAction(overview.reload);
  const notes = purchases.data?.filter((purchase) => purchase.accessKey) ?? [];
  const data = overview.data;
  const profile = data?.profile ?? null;
  const canIssue = Boolean(data?.isMonitorConfigured && profile);

  function saveProfile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);

    void action.run(() => http.put(`${base}/fiscal/profile`, {
      legalName: text(form, "legalName"),
      tradeName: text(form, "tradeName"),
      cnpj: text(form, "cnpj"),
      stateRegistration: text(form, "stateRegistration"),
      street: text(form, "street"),
      number: text(form, "number"),
      district: text(form, "district"),
      city: text(form, "city"),
      state: text(form, "state"),
      postalCode: text(form, "postalCode"),
      cityIbgeCode: text(form, "cityIbgeCode"),
      series: Number(text(form, "series")),
      nextNumber: Number(text(form, "nextNumber")),
      taxRegime: text(form, "taxRegime"),
      accountantName: text(form, "accountantName"),
      accountantCpf: text(form, "accountantCpf"),
      accountantCrc: text(form, "accountantCrc"),
      accountantEmail: text(form, "accountantEmail"),
      spedProfile: text(form, "spedProfile"),
    }));
  }

  function saveProduct(product: FiscalProduct) {
    void action.run(() => http.put(`${base}/fiscal/products/${product.menuItemId}`, {
      ncm: product.ncm ?? "",
      cfop: product.cfop ?? "",
      cest: product.cest || null,
      icmsCst: product.icmsCst ?? "",
      icmsRate: toDecimal(String(product.icmsRate ?? 0)),
      pisCofinsCst: product.pisCofinsCst ?? "",
      pisRate: toDecimal(String(product.pisRate ?? 0)),
      cofinsRate: toDecimal(String(product.cofinsRate ?? 0)),
    }));
  }

  function generateFile(route: string, event: FormEvent<HTMLButtonElement>) {
    const form = new FormData(event.currentTarget.form ?? undefined);
    const [year, month] = String(form.get("period") ?? "").split("-").map(Number);

    void action.run(async () => {
      const file = await http.post<{ fileName: string; contentBase64: string; notes: number; warnings: string[] }>(`${base}/fiscal/files/${route}`, {
        year,
        month,
        previousCreditInCents: toCents(form.get("previousCredit")),
        revenueCode: text(form, "revenueCode"),
        dueDate: text(form, "dueDate"),
      });
      setWarnings([`${file.fileName}: ${file.notes} nota(s).`, ...file.warnings]);
      const bytes = Uint8Array.from(atob(file.contentBase64), (character) => character.charCodeAt(0));
      const url = URL.createObjectURL(new Blob([bytes], { type: "text/plain;charset=iso-8859-1" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = file.fileName;
      link.click();
      URL.revokeObjectURL(url);
    });
  }

  function downloadXml(document_: FiscalDocument) {
    void action.run(async () => {
      const file = await http.get<{ fileName: string; xml: string }>(`${base}/fiscal/documents/${document_.id}/xml`);
      const url = URL.createObjectURL(new Blob([file.xml], { type: "application/xml" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = file.fileName;
      link.click();
      URL.revokeObjectURL(url);
    });
  }

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Fiscal" title="Fiscal" hint={hasNfce ? "NFC-e de venda para Lucro Presumido e Lucro Real." : "Notas de entrada importadas por XML."} />
      <ErrorMessage message={action.error ?? overview.error} />

      {data ? (
        <article className="card vx-box">
          <div className="vx-panel-title">
            <h3>Emissor fiscal</h3>
            <span className="vx-pill"><i />{data.isMonitorConfigured ? (data.environment === 2 ? "Homologação, sem valor fiscal" : "Produção") : "Desligado"}</span>
          </div>
          <p className="muted">
            {data.isMonitorConfigured
              ? "Conectado às configurações do ACBrMonitor, onde ficam o certificado A1 e o CSC."
              : "A emissão fica disponível quando o ACBrMonitor, com o certificado A1 e o CSC da loja, estiver ligado. Enquanto isso, você já pode preencher os dados abaixo."}
          </p>
        </article>
      ) : null}

      {data ? (
        <form className="card module-form" key={profile?.cnpj ?? "novo"} onSubmit={saveProfile}>
          <label className="field">
            <span>Regime tributário *</span>
            <select defaultValue={profile?.taxRegime ?? ""} name="taxRegime" required>
              <option disabled value="">Selecione</option>
              <option value="LucroPresumido">Lucro Presumido</option>
              <option value="LucroReal">Lucro Real</option>
            </select>
          </label>
          <Input defaultValue={profile?.legalName} label="Razão social *" maxLength={60} name="legalName" required wide />
          <Input defaultValue={profile?.tradeName ?? ""} label="Nome fantasia" maxLength={60} name="tradeName" />
          <Input defaultValue={profile?.cnpj} label="CNPJ *" name="cnpj" required />
          <Input defaultValue={profile?.stateRegistration} label="Inscrição estadual *" name="stateRegistration" required />
          <Input defaultValue={profile?.street} label="Rua *" maxLength={60} name="street" required wide />
          <Input defaultValue={profile?.number} label="Número *" maxLength={60} name="number" required />
          <Input defaultValue={profile?.district} label="Bairro *" maxLength={60} name="district" required />
          <Input defaultValue={profile?.city} label="Cidade *" maxLength={60} name="city" required />
          <Input defaultValue={profile?.state} label="UF *" maxLength={2} name="state" required />
          <Input defaultValue={profile?.postalCode} label="CEP *" name="postalCode" required />
          <Input defaultValue={profile?.cityIbgeCode} label="Código IBGE do município *" name="cityIbgeCode" required />
          <Input defaultValue={profile?.series ?? 1} label="Série *" min={1} name="series" required type="number" />
          <Input defaultValue={profile?.nextNumber ?? 1} label="Próximo número *" min={1} name="nextNumber" required type="number" />
          {hasSintegra || hasSped ? (
            <>
              <Input defaultValue={profile?.accountantName ?? ""} label="Contador" name="accountantName" />
              <Input defaultValue={profile?.accountantCpf ?? ""} label="CPF do contador" name="accountantCpf" />
              <Input defaultValue={profile?.accountantCrc ?? ""} label="CRC" name="accountantCrc" />
              <Input defaultValue={profile?.accountantEmail ?? ""} label="E-mail do contador" name="accountantEmail" />
              <Input defaultValue={profile?.spedProfile ?? ""} label="Perfil do SPED (A, B ou C)" maxLength={1} name="spedProfile" />
            </>
          ) : null}
          <button className="primary-button" disabled={action.busy} type="submit">Salvar emitente</button>
        </form>
      ) : null}

      {data && data.products.length > 0 ? (
        <article className="card vx-box">
          <div className="vx-panel-title"><h3>Tributação dos produtos</h3></div>
          <p className="muted">Preencha com o seu contador. Produto sem esses dados não entra em nota.</p>
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Produto</th><th>NCM</th><th>CFOP</th><th>CEST</th><th>CST ICMS</th><th>% ICMS</th><th>CST PIS/COFINS</th><th>% PIS</th><th>% COFINS</th><th /></tr></thead>
              <tbody>{data.products.map((product) => <ProductRow busy={action.busy} key={product.menuItemId} onSave={saveProduct} product={product} />)}</tbody>
            </table>
          </div>
        </article>
      ) : null}

      {data && data.pendingOrders.length > 0 ? (
        <article className="card vx-box">
          <div className="vx-panel-title"><h3>Pedidos pagos sem nota</h3></div>
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Pedido</th><th>Cliente</th><th className="num">Total</th><th /></tr></thead>
              <tbody>
                {data.pendingOrders.map((order) => (
                  <tr key={order.orderId}>
                    <td>{order.code}</td>
                    <td>{order.customerName}</td>
                    <td className="num">{formatBRL(order.totalInCents)}</td>
                    <td className="table-actions">
                      {canIssue ? (
                        <button className="link-button" disabled={action.busy} onClick={() => void action.run(() => http.post(`${base}/fiscal/orders/${order.orderId}/nfce`))} type="button">Emitir NFC-e</button>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </article>
      ) : null}

      {data && data.documents.length > 0 ? (
        <article className="card vx-box">
          <div className="vx-panel-title"><h3>Notas emitidas</h3></div>
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Número</th><th>Pedido</th><th>Situação</th><th>Retorno</th><th className="num">Total</th><th /></tr></thead>
              <tbody>
                {data.documents.map((note) => (
                  <tr key={note.id}>
                    <td>{note.series}/{note.number}</td>
                    <td>{note.orderCode}</td>
                    <td>{statusLabels[note.status]}{note.environment === 2 ? " (homologação)" : ""}</td>
                    <td>{note.message ?? "—"}</td>
                    <td className="num">{formatBRL(note.totalInCents)}</td>
                    <td className="table-actions">{note.hasXml ? <button className="link-button" disabled={action.busy} onClick={() => downloadXml(note)} type="button">Baixar XML</button> : null}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </article>
      ) : null}

      {profile && (hasSintegra || hasSped) ? (
        <form className="card module-form" onSubmit={(event) => event.preventDefault()}>
          <Input label="Mês *" name="period" required type="month" />
          {hasSped ? <Input inputMode="decimal" label="Saldo credor anterior de ICMS (R$)" name="previousCredit" /> : null}
          {hasSped ? <Input label="Código de receita do ICMS" name="revenueCode" /> : null}
          {hasSped ? <Input label="Vencimento do ICMS" name="dueDate" type="date" /> : null}
          {hasSintegra ? <button className="secondary-button" disabled={action.busy} onClick={(event) => generateFile("sintegra", event)} type="button">Gerar SINTEGRA</button> : null}
          {hasSped ? <button className="secondary-button" disabled={action.busy} onClick={(event) => generateFile("efd-icms-ipi", event)} type="button">Gerar SPED Fiscal</button> : null}
          {hasSped && profile.taxRegime === "LucroPresumido" ? <button className="secondary-button" disabled={action.busy} onClick={(event) => generateFile("efd-contribuicoes", event)} type="button">Gerar SPED Contribuições</button> : null}
        </form>
      ) : null}
      {warnings.length > 0 ? <ul className="vx-unavailable">{warnings.map((warning) => <li key={warning}><span>{warning}</span></li>)}</ul> : null}

      {hasEntry ? <div className="vx-panel-title"><h3>Notas de entrada</h3></div> : null}
      {hasEntry ? <div><button className="primary-button" onClick={onOpenInventory} type="button">Importar XML de NF-e</button></div> : null}
      <ErrorMessage message={purchases.error} />
      {hasEntry && purchases.data && notes.length === 0 ? <Empty>Nenhuma NF-e de entrada importada.</Empty> : null}
      {hasEntry && notes.length > 0 ? (
        <div className="card admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Número</th><th>Emissão</th><th>Chave de acesso</th><th className="num">Total</th></tr></thead>
            <tbody>
              {notes.map((note) => (
                <tr key={note.id}><td>{note.invoiceNumber ?? "—"}</td><td>{note.issuedOn.split("-").reverse().join("/")}</td><td>{note.accessKey}</td><td className="num">{formatBRL(note.totalInCents)}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}

      <article className="card vx-box">
        <div className="vx-panel-title"><h3>Ainda não disponível</h3></div>
        <ul className="vx-unavailable">
          <li><strong>Cancelamento de NFC-e e NF-e modelo 55</strong><span>Entram depois da homologação da NFC-e.</span></li>
          <li><strong>IBS e CBS</strong><span>Ainda não enviados na nota; dependem da validação em homologação.</span></li>
          <li><strong>SPED Contribuições do Lucro Real</strong><span>Exige apuração de créditos, ainda não implementada.</span></li>
        </ul>
      </article>
    </section>
  );
}
