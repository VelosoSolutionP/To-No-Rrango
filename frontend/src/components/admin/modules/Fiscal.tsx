"use client";

import { type FormEvent, useState } from "react";
import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import { Empty, ErrorMessage, Input, ModuleHeading, SubTabs, text, toCents, toDecimal, useAction, useLoad } from "./shared";

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
  status: "Processing" | "Authorized" | "Rejected" | "Cancelled" | "Contingency";
  accessKey: string | null;
  message: string | null;
  totalInCents: number;
  hasXml: boolean;
  model?: number;
  recipientName?: string | null;
  cancelReason?: string | null;
}

interface NumberGap {
  id: string;
  model: number;
  series: number;
  fromNumber: number;
  toNumber: number;
  environment: number;
  reason: string;
  status: "Pending" | "Done" | "Rejected";
  protocol: string | null;
  message: string | null;
}

interface DfeSettings {
  hasCertificate: boolean;
  certificateSubject: string | null;
  certificateExpiresAt: string | null;
  environment: number;
  cscId: string | null;
  hasCscToken: boolean;
  nfeSeries: number;
  nfeNextNumber: number;
  contingencyPending: number;
  gaps: NumberGap[];
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
  Cancelled: "Cancelada",
  Contingency: "Contingência (enviar em 24 h)",
};

function readFileBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(",")[1] ?? "");
    reader.onerror = () => reject(new Error("Não consegui ler o arquivo."));
    reader.readAsDataURL(file);
  });
}

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
  const [view, setView] = useState<"issue" | "config" | "products" | "duties" | "entries">(hasNfce ? "issue" : "entries");
  const base = `/establishments/${establishmentId}`;
  const purchases = useLoad(() => http.get<Purchase[]>(`${base}/purchases`).catch(() => []), [establishmentId]);
  const overview = useLoad<FiscalOverview | null>(() => (hasNfce ? http.get<FiscalOverview>(`${base}/fiscal`) : Promise.resolve(null)), [establishmentId, hasNfce]);
  const action = useAction(overview.reload);
  const notes = purchases.data?.filter((purchase) => purchase.accessKey) ?? [];
  const data = overview.data;
  const profile = data?.profile ?? null;
  const dfe = useLoad<DfeSettings | null>(() => (hasNfce ? http.get<DfeSettings>(`${base}/fiscal/dfe`) : Promise.resolve(null)), [establishmentId, hasNfce]);
  const dfeAction = useAction(async () => {
    await dfe.reload();
    await overview.reload();
  });
  const direct = dfe.data?.hasCertificate ?? false;
  const canIssue = Boolean((data?.isMonitorConfigured || direct) && profile);
  const [nfeFor, setNfeFor] = useState<PendingOrder | null>(null);
  const [sefaz, setSefaz] = useState<string | null>(null);

  async function uploadCertificate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const file = form.get("pfx");

    if (!(file instanceof File) || file.size === 0) {
      return;
    }

    const pfxBase64 = await readFileBase64(file);
    const done = await dfeAction.run(() => http.post(`${base}/fiscal/certificate`, { pfxBase64, password: String(form.get("password") ?? "") }));

    if (done) {
      element.reset();
    }
  }

  function saveDfe(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    void dfeAction.run(() =>
      http.put(`${base}/fiscal/dfe`, {
        environment: Number(form.get("environment")),
        cscId: text(form, "cscId"),
        cscToken: text(form, "cscToken"),
        nfeSeries: Number(form.get("nfeSeries")) || 1,
        nfeNextNumber: Number(form.get("nfeNextNumber")) || 1,
      }),
    );
  }

  function issueNfe(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!nfeFor) {
      return;
    }
    const form = new FormData(event.currentTarget);
    void dfeAction
      .run(() =>
        http.post(`${base}/fiscal/orders/${nfeFor.orderId}/nfe`, {
          document: text(form, "document"),
          name: text(form, "name"),
          stateRegistration: text(form, "stateRegistration"),
          street: text(form, "street"),
          number: text(form, "number"),
          district: text(form, "district"),
          cityIbgeCode: text(form, "cityIbgeCode"),
          city: text(form, "city"),
          state: text(form, "state"),
          postalCode: text(form, "postalCode"),
          email: text(form, "email"),
        }),
      )
      .then((done) => done && setNfeFor(null));
  }

  function cancelNote(note: FiscalDocument) {
    const reason = window.prompt("Justificativa do cancelamento (de 15 a 255 caracteres):");
    if (reason && reason.trim().length >= 15) {
      void dfeAction.run(() => http.post(`${base}/fiscal/documents/${note.id}/cancel`, { reason: reason.trim() }));
    } else if (reason) {
      window.alert("A justificativa precisa ter pelo menos 15 caracteres.");
    }
  }

  function giveUpNumbers(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    void dfeAction
      .run(() =>
        http.post(`${base}/fiscal/number-gaps`, {
          model: Number(form.get("model")),
          series: Number(form.get("series")),
          fromNumber: Number(form.get("fromNumber")),
          toNumber: Number(form.get("toNumber")),
          reason: text(form, "reason"),
        }),
      )
      .then((done) => done && element.reset());
  }

  async function checkSefaz() {
    setSefaz("Consultando…");
    try {
      const answer = await http.get<{ accepted: boolean; message: string }>(`${base}/fiscal/status?model=65`);
      setSefaz(`${answer.accepted ? "SEFAZ no ar" : "SEFAZ com problema"}: ${answer.message}`);
    } catch (failure) {
      setSefaz(failure instanceof Error ? failure.message : "Não foi possível consultar.");
    }
  }

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
      <ModuleHeading eyebrow="Fiscal" title="Fiscal" hint={hasNfce ? "NFC-e e NF-e de venda para Lucro Presumido e Lucro Real." : "Notas de entrada importadas por XML."} />
      <ErrorMessage message={action.error ?? overview.error} />
      {hasNfce && data ? (
        <div className="kpi-strip">
          <div className="kpi"><small>Notas emitidas</small><strong>{data.documents.filter((note) => note.status === "Authorized").length}</strong></div>
          <div className={`kpi ${data.pendingOrders.length ? "is-warn" : ""}`}><small>Pedidos pagos sem nota</small><strong>{data.pendingOrders.length}</strong></div>
          <div className={`kpi ${data.documents.some((note) => note.status === "Rejected") ? "is-alert" : ""}`}><small>Rejeitadas</small><strong>{data.documents.filter((note) => note.status === "Rejected").length}</strong></div>
          <div className={`kpi ${dfe.data?.contingencyPending ? "is-alert" : ""}`}><small>Contingência a enviar</small><strong>{dfe.data?.contingencyPending ?? 0}</strong></div>
          <div className="kpi"><small>Emissor</small><strong className="kpi-text">{direct ? "SEFAZ direto" : data.isMonitorConfigured ? "ACBr" : "Desligado"}</strong></div>
          <div className="kpi"><small>Ambiente</small><strong className="kpi-text">{(dfe.data?.environment ?? data.environment) === 1 ? "Produção" : "Homologação"}</strong></div>
        </div>
      ) : null}
      <SubTabs
        onChange={setView}
        options={[
          ...(hasNfce ? [{ value: "issue" as const, label: "Emitir" }, { value: "config" as const, label: "Configuração" }, { value: "products" as const, label: "Tributação dos produtos" }] : []),
          ...(hasNfce ? [{ value: "duties" as const, label: "Obrigações e inutilização" }] : []),
          ...(hasEntry ? [{ value: "entries" as const, label: "Notas de entrada" }] : []),
        ]}
        value={view}
      />

      {view === "config" && data ? (
        <article className="card vx-box">
          <div className="vx-panel-title">
            <h3>Emissor fiscal</h3>
            <span className="vx-pill"><i />{data.isMonitorConfigured ? (data.environment === 2 ? "Homologação, sem valor fiscal" : "Produção") : "Desligado"}</span>
          </div>
          <p className="muted">
            {direct
              ? "Emissão direta na SEFAZ com o certificado A1 da loja (sem programa instalado)."
              : data.isMonitorConfigured
                ? "Conectado às configurações do ACBrMonitor, onde ficam o certificado A1 e o CSC."
                : "Envie o certificado A1 da loja abaixo para emitir direto na SEFAZ. Enquanto isso, você já pode preencher os dados do emitente."}
          </p>
        </article>
      ) : null}

      {view === "config" && data && profile && dfe.data ? (
        <article className="card vx-box">
          <div className="vx-panel-title">
            <h3>Certificado e SEFAZ</h3>
            <span className="vx-pill"><i />{dfe.data.environment === 1 ? "Produção" : "Homologação, sem valor fiscal"}</span>
          </div>
          <ErrorMessage message={dfeAction.error ?? dfe.error} />
          {dfe.data.hasCertificate ? (
            <p>
              Certificado: <strong>{dfe.data.certificateSubject}</strong>
              <br />
              Vence em {dfe.data.certificateExpiresAt ? new Date(dfe.data.certificateExpiresAt).toLocaleDateString("pt-BR") : "—"}{" "}
              <button className="link-button" disabled={dfeAction.busy} onClick={() => window.confirm("Remover o certificado desta loja?") && void dfeAction.run(() => http.del(`${base}/fiscal/certificate`))} type="button">Remover</button>
            </p>
          ) : (
            <p className="muted">Nenhum certificado enviado. Ele fica guardado criptografado e só é aberto na hora de assinar.</p>
          )}
          <form className="module-form" onSubmit={(event) => void uploadCertificate(event)}>
            <label className="field"><span>Certificado A1 (.pfx)</span><input accept=".pfx,.p12" name="pfx" required type="file" /></label>
            <Input autoComplete="off" label="Senha do certificado" name="password" required type="password" />
            <button className="secondary-button" disabled={dfeAction.busy} type="submit">{dfe.data.hasCertificate ? "Trocar certificado" : "Enviar certificado"}</button>
          </form>
          <form className="module-form" key={`${dfe.data.environment}-${dfe.data.cscId}-${dfe.data.nfeNextNumber}`} onSubmit={saveDfe}>
            <label className="field">
              <span>Ambiente</span>
              <select defaultValue={dfe.data.environment} name="environment">
                <option value={2}>Homologação (teste, sem valor fiscal)</option>
                <option value={1}>Produção</option>
              </select>
            </label>
            <Input defaultValue={dfe.data.cscId ?? ""} label="ID do CSC (NFC-e)" name="cscId" />
            <Input autoComplete="off" label={dfe.data.hasCscToken ? "CSC (deixe vazio para manter)" : "CSC (código da SEFAZ)"} name="cscToken" type="password" />
            <Input defaultValue={dfe.data.nfeSeries} label="Série da NF-e" min={1} name="nfeSeries" type="number" />
            <Input defaultValue={dfe.data.nfeNextNumber} label="Próximo número da NF-e" min={1} name="nfeNextNumber" type="number" />
            <button className="secondary-button" disabled={dfeAction.busy} type="submit">Salvar</button>
          </form>
          {dfe.data.hasCertificate ? (
            <div className="zest-gap-actions">
              <button className="link-button" onClick={() => void checkSefaz()} type="button">Testar comunicação com a SEFAZ</button>
              {sefaz ? <span className="muted">{sefaz}</span> : null}
            </div>
          ) : null}
          {dfe.data.contingencyPending > 0 ? (
            <p className="form-error">
              {dfe.data.contingencyPending} NFC-e em contingência aguardando envio (prazo de 24 horas).{" "}
              <button className="secondary-button" disabled={dfeAction.busy} onClick={() => void dfeAction.run(() => http.post(`${base}/fiscal/contingency/send`))} type="button">Enviar agora</button>
            </p>
          ) : null}
        </article>
      ) : null}

      {view === "config" && data ? (
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

      {view === "products" && data && data.products.length > 0 ? (
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

      {view === "issue" && data && data.pendingOrders.length === 0 && data.documents.length === 0 ? (
        <Empty>Nenhum pedido pago esperando nota. Quando houver, ele aparece aqui para emitir NFC-e ou NF-e.</Empty>
      ) : null}
      {view === "issue" && !canIssue && data ? (
        <p className="board-notice is-warn" onClick={() => setView("config")}>Para emitir, envie o certificado A1 e o CSC da loja em Configuração.</p>
      ) : null}
      {view === "issue" && data && data.pendingOrders.length > 0 ? (
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
                      {direct ? (
                        <button className="link-button" disabled={dfeAction.busy} onClick={() => setNfeFor(order)} type="button">Emitir NF-e</button>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </article>
      ) : null}

      {view === "issue" && nfeFor ? (
        <form className="card module-form" onSubmit={issueNfe}>
          <h3 className="field-wide">NF-e do pedido {nfeFor.code} — destinatário</h3>
          <Input label="CPF ou CNPJ *" name="document" required />
          <Input defaultValue={nfeFor.customerName} label="Nome / razão social *" maxLength={60} name="name" required wide />
          <Input label="Inscrição estadual (se contribuinte)" name="stateRegistration" />
          <Input label="Rua *" maxLength={60} name="street" required wide />
          <Input label="Número *" name="number" required />
          <Input label="Bairro *" name="district" required />
          <Input defaultValue={profile?.city} label="Cidade *" name="city" required />
          <Input defaultValue={profile?.state} label="UF *" maxLength={2} name="state" required />
          <Input defaultValue={profile?.cityIbgeCode} label="Código IBGE do município *" name="cityIbgeCode" required />
          <Input label="CEP *" name="postalCode" required />
          <Input label="E-mail" name="email" type="email" />
          <button className="secondary-button" onClick={() => setNfeFor(null)} type="button">Cancelar</button>
          <button className="primary-button" disabled={dfeAction.busy} type="submit">Emitir NF-e</button>
        </form>
      ) : null}

      {view === "issue" && data && data.documents.length > 0 ? (
        <article className="card vx-box">
          <div className="vx-panel-title"><h3>Notas emitidas</h3></div>
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead><tr><th>Modelo</th><th>Número</th><th>Pedido</th><th>Situação</th><th>Retorno</th><th className="num">Total</th><th /></tr></thead>
              <tbody>
                {data.documents.map((note) => (
                  <tr key={note.id}>
                    <td>{note.model === 55 ? "NF-e" : "NFC-e"}</td>
                    <td>{note.series}/{note.number}</td>
                    <td>{note.orderCode}</td>
                    <td>{statusLabels[note.status]}{note.environment === 2 ? " (homologação)" : ""}</td>
                    <td>{note.message ?? "—"}</td>
                    <td className="num">{formatBRL(note.totalInCents)}</td>
                    <td className="table-actions">
                      {note.hasXml ? <button className="link-button" disabled={action.busy} onClick={() => downloadXml(note)} type="button">Baixar XML</button> : null}
                      {direct && note.status === "Authorized" ? <button className="link-button" disabled={dfeAction.busy} onClick={() => cancelNote(note)} type="button">Cancelar</button> : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </article>
      ) : null}

      {view === "duties" && profile && (hasSintegra || hasSped) ? (
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
      {view === "duties" && warnings.length > 0 ? <ul className="vx-unavailable">{warnings.map((warning) => <li key={warning}><span>{warning}</span></li>)}</ul> : null}

      {view === "entries" && hasEntry ? <div className="vx-panel-title"><h3>Notas de entrada</h3></div> : null}
      {view === "entries" && hasEntry ? <div><button className="primary-button" onClick={onOpenInventory} type="button">Importar XML de NF-e</button></div> : null}
      {view === "entries" ? <ErrorMessage message={purchases.error} /> : null}
      {view === "entries" && hasEntry && purchases.data && notes.length === 0 ? <Empty>Nenhuma NF-e de entrada importada.</Empty> : null}
      {view === "entries" && hasEntry && notes.length > 0 ? (
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

      {view === "duties" && direct && dfe.data ? (
        <article className="card vx-box">
          <div className="vx-panel-title"><h3>Inutilizar numeração</h3></div>
          <p className="muted">Use quando números foram pulados e nunca serão usados. A SEFAZ registra a faixa como inutilizada.</p>
          <form className="module-form" onSubmit={giveUpNumbers}>
            <label className="field"><span>Modelo</span><select name="model"><option value={65}>NFC-e (65)</option><option value={55}>NF-e (55)</option></select></label>
            <Input defaultValue={1} label="Série" min={1} name="series" required type="number" />
            <Input label="Do número" min={1} name="fromNumber" required type="number" />
            <Input label="Até o número" min={1} name="toNumber" required type="number" />
            <Input label="Justificativa (mín. 15 caracteres)" maxLength={255} minLength={15} name="reason" required wide />
            <button className="secondary-button" disabled={dfeAction.busy} type="submit">Inutilizar</button>
          </form>
          {dfe.data.gaps.length > 0 ? (
            <table className="admin-table">
              <tbody>
                {dfe.data.gaps.map((gap) => (
                  <tr key={gap.id}>
                    <td>{gap.model === 55 ? "NF-e" : "NFC-e"} série {gap.series}</td>
                    <td>{gap.fromNumber} a {gap.toNumber}</td>
                    <td>{gap.status === "Done" ? `Inutilizada (${gap.protocol})` : gap.status === "Rejected" ? "Recusada" : "Pendente"}</td>
                    <td>{gap.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
        </article>
      ) : null}

      {view === "config" ? <article className="card vx-box">
        <div className="vx-panel-title"><h3>Ainda não disponível</h3></div>
        <ul className="vx-unavailable">
          <li><strong>Transmissão real à SEFAZ</strong><span>Pronta, mas só testada com certificado de teste e validação nos esquemas oficiais. Falta homologar com um certificado A1 real.</span></li>
          <li><strong>IBS e CBS</strong><span>Ainda não enviados na nota; dependem da validação em homologação.</span></li>
          <li><strong>SPED Contribuições do Lucro Real</strong><span>Exige apuração de créditos, ainda não implementada.</span></li>
        </ul>
      </article> : null}
    </section>
  );
}
