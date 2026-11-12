"use client";

import { type FormEvent, useMemo, useState } from "react";
import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import { WHATSAPP_URL } from "@/lib/site";
import { openDanfe } from "./danfe";
import { Empty, ErrorMessage, text, toCents, useAction, useLoad } from "./shared";

export interface FiscalCode {
  code: string;
  description: string;
}

export interface FiscalTables {
  cfop: FiscalCode[];
  icmsCst: FiscalCode[];
  csosn: FiscalCode[];
  pisCofinsCst: FiscalCode[];
  ipiCst: FiscalCode[];
  origin: FiscalCode[];
}

interface ReceivedNote {
  id: string;
  accessKey: string;
  issuerDocument: string;
  issuerName: string;
  issuedAt: string;
  totalInCents: number;
  situation: number;
  status: "Summary" | "Full" | "Imported";
  hasXml: boolean;
  acknowledgedAt: string | null;
  lastEvent: string | null;
}

interface SyncResult {
  downloaded: number;
  acknowledged: number;
  message: string;
  nextAllowedAt: string | null;
  notes: ReceivedNote[];
}

interface CnpjData {
  legalName: string;
  street: string | null;
  number: string | null;
  district: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  cityIbgeCode: string | null;
}

/** Rascunho vindo de Compras: nota de origem para devolução ou ajuste. */
export interface ManualDraft {
  purpose: 2 | 3 | 4;
  referencedKey: string;
  supplierCnpj: string;
  interstate: boolean;
  items: Array<{ code: string; description: string; ncm: string | null; quantity: number; total: number }>;
}

const situationLabel = (situation: number) => (situation === 3 ? "Cancelada pelo emissor" : situation === 2 ? "Denegada" : "Autorizada");
const formatKey = (key: string) => key.replace(/(\d{4})(?=\d)/g, "$1 ");
const digits = (value: string) => value.replace(/\D/g, "");

/** Recomendação de contabilidade e serviços da Veloso Solution, fixa no topo da Central Fiscal. */
export function ConsultingBanner() {
  return (
    <aside className="fx-banner">
      <div className="fx-banner-glow" aria-hidden="true" />
      <div className="fx-banner-body">
        <p className="fx-banner-tag">Recomendação Veloso Solution</p>
        <h3>Não jogue seu dinheiro no ralo escolhendo <em>tributações incorretas.</em></h3>
        <p>
          Recomendamos fortemente a consultoria de um contador ou escritório de contabilidade. A Veloso Solution oferece
          treinamento ou faz os lançamentos por você, por um adicional acessível: das notas aos arquivos digitais, tudo feito
          pelas nossas IAs, e o seu escritório contábil só homologa.
        </p>
        <p className="fx-banner-more">Consulte este e outros serviços para organizar e expandir o seu negócio.</p>
      </div>
      <div className="fx-banner-offer">
        <span className="fx-offer-chip">Este mês</span>
        <strong>Painel BI grátis por 30 dias</strong>
        <a className="primary-button" href={`${WHATSAPP_URL}?text=${encodeURIComponent("Quero assinar o Painel BI e a assessoria fiscal da Veloso Solution")}`} rel="noreferrer" target="_blank">Assine já</a>
      </div>
    </aside>
  );
}

export function useFiscalTables(http: AdminHttp, establishmentId: string) {
  return useLoad<FiscalTables | null>(() => http.get<FiscalTables>(`/establishments/${establishmentId}/fiscal/tables`).catch(() => null), [establishmentId]);
}

/** Datalists para os campos de CFOP e CST: digita o código ou parte da descrição. */
export function FiscalDatalists({ tables }: { tables: FiscalTables | null }) {
  if (!tables) {
    return null;
  }

  return (
    <>
      <datalist id="fx-cfop">{tables.cfop.map((item) => <option key={item.code} value={item.code}>{item.description}</option>)}</datalist>
      <datalist id="fx-icms">{[...tables.icmsCst, ...tables.csosn].map((item) => <option key={item.code} value={item.code}>{item.description}</option>)}</datalist>
      <datalist id="fx-piscofins">{tables.pisCofinsCst.map((item) => <option key={item.code} value={item.code}>{item.description}</option>)}</datalist>
    </>
  );
}

export function ReceivedNotesView({ http, establishmentId, onOpenInventory }: { http: AdminHttp; establishmentId: string; onOpenInventory: (xml: string) => void }) {
  const base = `/establishments/${establishmentId}/fiscal/received`;
  const list = useLoad(() => http.get<ReceivedNote[]>(base), [establishmentId]);
  const [result, setResult] = useState<SyncResult | null>(null);
  const [filter, setFilter] = useState<"open" | "all">("open");
  const action = useAction(list.reload);
  const notes = (result?.notes ?? list.data ?? []).filter((note) => filter === "all" || note.status !== "Imported");

  async function sync() {
    await action.run(async () => setResult(await http.post<SyncResult>(`${base}/sync`, {})));
  }

  async function toPurchase(note: ReceivedNote) {
    await action.run(async () => {
      const xml = await http.get<{ fileName: string; xml: string }>(`${base}/${note.id}/xml`);
      await http.post(`${base}/${note.id}/imported`, {});
      onOpenInventory(xml.xml);
    });
  }

  async function download(note: ReceivedNote) {
    const xml = await http.get<{ fileName: string; xml: string }>(`${base}/${note.id}/xml`);
    const url = URL.createObjectURL(new Blob([xml.xml], { type: "application/xml" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = xml.fileName;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <article className="card vx-box fx-card">
      <div className="vx-panel-title">
        <h3>Notas contra o seu CNPJ <em>direto da SEFAZ</em></h3>
        <button className="primary-button" disabled={action.busy} onClick={() => void sync()} type="button">{action.busy ? "Buscando…" : "Baixar notas agora"}</button>
      </div>
      <p className="muted">
        Só com o certificado A1 da loja: a SEFAZ entrega todas as NF-e emitidas contra o CNPJ. Quem chega só como resumo
        recebe a ciência da operação automaticamente e o XML completo vem na busca seguinte (a SEFAZ libera em até uma hora
        e não deixa buscar mais de uma vez por hora quando não há nada novo).
      </p>
      <ErrorMessage message={action.error ?? list.error} />
      {result ? (
        <p className="fx-sync">
          <b>{result.downloaded}</b> documento(s) baixado(s) · <b>{result.acknowledged}</b> ciência(s) registrada(s) · {result.message}
          {result.nextAllowedAt ? ` · próxima busca a partir de ${new Date(result.nextAllowedAt).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}` : ""}
        </p>
      ) : null}
      <div className="admin-filter-row">
        <button className={`admin-filter ${filter === "open" ? "is-active" : ""}`} onClick={() => setFilter("open")} type="button">A lançar</button>
        <button className={`admin-filter ${filter === "all" ? "is-active" : ""}`} onClick={() => setFilter("all")} type="button">Todas</button>
      </div>
      {notes.length === 0 ? (
        <Empty>Nenhuma nota por aqui. Clique em Baixar notas agora: a primeira busca traz as notas dos últimos 90 dias.</Empty>
      ) : (
        <div className="fx-received">
          {notes.map((note) => (
            <div className={`fx-note ${note.situation !== 1 ? "is-off" : ""}`} key={note.id}>
              <div className="fx-note-main">
                <strong>{note.issuerName || note.issuerDocument}</strong>
                <small>{new Date(note.issuedAt).toLocaleDateString("pt-BR")} · {formatKey(note.accessKey)}</small>
                <span className="fx-note-tags">
                  <span className={`fx-tag ${note.situation === 1 ? "ok" : "bad"}`}>{situationLabel(note.situation)}</span>
                  <span className={`fx-tag ${note.status === "Summary" ? "warn" : "ok"}`}>{note.status === "Summary" ? (note.acknowledgedAt ? "Ciência dada · XML a caminho" : "Só resumo") : note.status === "Imported" ? "Lançada em Compras" : "XML completo"}</span>
                  {note.lastEvent ? <span className="fx-tag">{note.lastEvent}</span> : null}
                </span>
              </div>
              <strong className="fx-note-total">{formatBRL(note.totalInCents)}</strong>
              <div className="fx-note-actions">
                {note.hasXml ? <button className="link-button" onClick={() => void http.get<{ xml: string }>(`${base}/${note.id}/xml`).then((file) => openDanfe(file.xml, { cancelled: note.situation === 3 }))} type="button">DANFE</button> : null}
                {note.hasXml ? <button className="link-button" onClick={() => void download(note)} type="button">XML</button> : null}
                {note.hasXml && note.status !== "Imported" && note.situation === 1 ? <button className="secondary-button" disabled={action.busy} onClick={() => void toPurchase(note)} type="button">Lançar em Compras</button> : null}
              </div>
            </div>
          ))}
        </div>
      )}
    </article>
  );
}

interface LineState {
  code: string;
  description: string;
  quantity: string;
  total: string;
  ncm: string;
  cfop: string;
  icmsCst: string;
  icmsRate: string;
  pisCofinsCst: string;
  pisRate: string;
  cofinsRate: string;
}

const emptyLine = (): LineState => ({ code: "", description: "", quantity: "1", total: "", ncm: "", cfop: "", icmsCst: "00", icmsRate: "", pisCofinsCst: "01", pisRate: "1.65", cofinsRate: "7.6" });

const purposes = [
  { value: 4, label: "Devolução", nature: "DEVOLUCAO DE COMPRA PARA COMERCIALIZACAO", entry: false },
  { value: 3, label: "Ajuste", nature: "AJUSTE DE ICMS", entry: true },
  { value: 2, label: "Complementar", nature: "COMPLEMENTO DE VALOR", entry: false },
] as const;

export function ManualNoteForm({ http, establishmentId, tables, draft, onIssued }: { http: AdminHttp; establishmentId: string; tables: FiscalTables | null; draft: ManualDraft | null; onIssued: () => void }) {
  const [purpose, setPurpose] = useState<2 | 3 | 4>(draft?.purpose ?? 4);
  const preset = purposes.find((item) => item.value === purpose)!;
  const [entry, setEntry] = useState<boolean>(preset.entry);
  const [lines, setLines] = useState<LineState[]>(() =>
    draft?.items.length
      ? draft.items.map((item) => ({ ...emptyLine(), code: item.code, description: item.description, ncm: item.ncm ?? "", quantity: String(item.quantity), total: item.total.toFixed(2), cfop: draft.purpose === 4 ? (draft.interstate ? "6202" : "5202") : "" }))
      : [emptyLine()],
  );
  const [lookup, setLookup] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const action = useAction(async () => onIssued());
  const cfopName = useMemo(() => new Map((tables?.cfop ?? []).map((item) => [item.code, item.description])), [tables]);

  function choosePurpose(value: 2 | 3 | 4) {
    setPurpose(value);
    setEntry(purposes.find((item) => item.value === value)!.entry);
  }

  async function findCnpj(form: HTMLFormElement) {
    const field = (name: string) => form.elements.namedItem(name) as HTMLInputElement | null;
    const cnpj = digits(field("document")?.value ?? "");
    if (cnpj.length !== 14) return;
    setLookup("Consultando CNPJ…");
    try {
      const data = await http.get<CnpjData>(`/lookups/cnpj/${cnpj}`);
      const fill = (name: string, value: string | null) => {
        const input = field(name);
        if (input && !input.value && value) input.value = value;
      };
      fill("name", data.legalName);
      fill("street", data.street);
      fill("number", data.number);
      fill("district", data.district);
      fill("city", data.city);
      fill("state", data.state);
      fill("postalCode", data.postalCode ? digits(data.postalCode) : null);
      fill("cityIbgeCode", data.cityIbgeCode);
      setLookup("Dados da Receita preenchidos. Complete a inscrição estadual e confira.");
    } catch {
      setLookup("Não consegui consultar o CNPJ agora. Preencha à mão.");
    }
  }

  const update = (index: number, patch: Partial<LineState>) => setLines((current) => current.map((line, at) => (at === index ? { ...line, ...patch } : line)));

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setMessage(null);
    void action.run(async () => {
      const note = await http.post<{ status: string; message: string | null; number: number }>(`/establishments/${establishmentId}/fiscal/manual-notes`, {
        purpose,
        nature: text(form, "nature"),
        isEntry: entry,
        referencedKey: digits(String(form.get("referencedKey") ?? "")) || null,
        recipient: {
          document: digits(String(form.get("document") ?? "")),
          name: text(form, "name"),
          stateRegistration: text(form, "stateRegistration"),
          street: text(form, "street"),
          number: text(form, "number"),
          district: text(form, "district"),
          cityIbgeCode: text(form, "cityIbgeCode"),
          city: text(form, "city"),
          state: text(form, "state"),
          postalCode: digits(String(form.get("postalCode") ?? "")),
          email: null,
        },
        lines: lines.map((line) => ({
          code: line.code || "AVULSO",
          description: line.description,
          quantity: Number(line.quantity.replace(",", ".")),
          totalInCents: toCents(line.total),
          ncm: digits(line.ncm),
          cfop: digits(line.cfop),
          icmsCst: line.icmsCst,
          icmsRate: Number(line.icmsRate.replace(",", ".")) || 0,
          pisCofinsCst: line.pisCofinsCst,
          pisRate: Number(line.pisRate.replace(",", ".")) || 0,
          cofinsRate: Number(line.cofinsRate.replace(",", ".")) || 0,
        })),
        additionalInfo: text(form, "additionalInfo"),
      });
      setMessage(note.status === "Authorized" ? `NF-e ${note.number} autorizada.` : `NF-e ${note.number}: ${note.message ?? note.status}`);
    });
  }

  return (
    <article className="card vx-box fx-card">
      <div className="vx-panel-title">
        <h3>Nota avulsa <em>devolução, ajuste e complementar</em></h3>
      </div>
      {draft?.interstate ? (
        <p className="fx-warn">Compra de outro estado: confira com o contador o DIFAL e o crédito de ICMS. Se a escrituração pedir, emita aqui a nota de ajuste referenciando a nota do fornecedor.</p>
      ) : null}
      <div className="admin-filter-row">
        {purposes.map((item) => (
          <button className={`admin-filter ${purpose === item.value ? "is-active" : ""}`} key={item.value} onClick={() => choosePurpose(item.value)} type="button">{item.label}</button>
        ))}
      </div>
      <form className="module-form" key={`${purpose}-${draft?.referencedKey ?? ""}`} onSubmit={submit}>
        <label className="field"><span>Natureza da operação</span><input defaultValue={preset.nature} maxLength={60} name="nature" required /></label>
        <label className="field">
          <span>Tipo</span>
          <select onChange={(event) => setEntry(event.target.value === "entry")} value={entry ? "entry" : "exit"}>
            <option value="exit">Saída</option>
            <option value="entry">Entrada</option>
          </select>
        </label>
        <label className="field fx-wide"><span>Chave da nota de origem {purpose === 3 ? "(opcional no ajuste)" : ""}</span><input defaultValue={draft?.referencedKey ?? ""} inputMode="numeric" name="referencedKey" placeholder="44 dígitos" /></label>
        <label className="field"><span>CNPJ ou CPF do destinatário</span><input defaultValue={draft?.supplierCnpj ?? ""} name="document" onBlur={(event) => void findCnpj(event.currentTarget.form!)} required /></label>
        <label className="field"><span>Razão social</span><input name="name" required /></label>
        <label className="field"><span>Inscrição estadual</span><input name="stateRegistration" /></label>
        <label className="field"><span>Rua</span><input name="street" required /></label>
        <label className="field"><span>Número</span><input name="number" required /></label>
        <label className="field"><span>Bairro</span><input name="district" required /></label>
        <label className="field"><span>Cidade</span><input name="city" required /></label>
        <label className="field"><span>UF</span><input maxLength={2} name="state" required /></label>
        <label className="field"><span>CEP</span><input name="postalCode" required /></label>
        <label className="field"><span>Código IBGE da cidade</span><input name="cityIbgeCode" required /></label>
        {lookup ? <p className="muted fx-wide">{lookup}</p> : null}

        <div className="fx-lines fx-wide">
          {lines.map((line, index) => (
            <div className="fx-line" key={index}>
              <input aria-label="Descrição" onChange={(event) => update(index, { description: event.target.value })} placeholder="Descrição" required value={line.description} />
              <input aria-label="Quantidade" inputMode="decimal" onChange={(event) => update(index, { quantity: event.target.value })} placeholder="Qtd" value={line.quantity} />
              <input aria-label="Valor total" inputMode="decimal" onChange={(event) => update(index, { total: event.target.value })} placeholder="Total R$" value={line.total} />
              <input aria-label="NCM" inputMode="numeric" onChange={(event) => update(index, { ncm: event.target.value })} placeholder="NCM" value={line.ncm} />
              <input aria-label="CFOP" list="fx-cfop" onChange={(event) => update(index, { cfop: event.target.value })} placeholder="CFOP" title={cfopName.get(line.cfop) ?? ""} value={line.cfop} />
              <input aria-label="CST ICMS" list="fx-icms" onChange={(event) => update(index, { icmsCst: event.target.value })} placeholder="CST" value={line.icmsCst} />
              <input aria-label="Alíquota ICMS" inputMode="decimal" onChange={(event) => update(index, { icmsRate: event.target.value })} placeholder="ICMS %" value={line.icmsRate} />
              <input aria-label="CST PIS/COFINS" list="fx-piscofins" onChange={(event) => update(index, { pisCofinsCst: event.target.value })} placeholder="PIS/COF" value={line.pisCofinsCst} />
              <button aria-label="Remover item" className="link-button" disabled={lines.length === 1} onClick={() => setLines((current) => current.filter((_, at) => at !== index))} type="button">×</button>
              {line.cfop && cfopName.get(line.cfop) ? <small className="fx-cfop-name">{line.cfop}: {cfopName.get(line.cfop)}</small> : line.cfop.length === 4 ? <small className="fx-cfop-name is-bad">CFOP {line.cfop} não está na tabela</small> : null}
            </div>
          ))}
          <button className="link-button" onClick={() => setLines((current) => [...current, emptyLine()])} type="button">+ Item</button>
        </div>
        <label className="field fx-wide"><span>Informações complementares</span><input maxLength={2000} name="additionalInfo" /></label>
        <ErrorMessage message={action.error} />
        {message ? <p className="fx-sync fx-wide">{message}</p> : null}
        <button className="primary-button" disabled={action.busy} type="submit">{action.busy ? "Enviando à SEFAZ…" : "Emitir NF-e"}</button>
      </form>
    </article>
  );
}

export function FiscalTablesView({ tables }: { tables: FiscalTables | null }) {
  const [table, setTable] = useState<keyof FiscalTables>("cfop");
  const [query, setQuery] = useState("");
  if (!tables) return <Empty>Não consegui carregar as tabelas agora.</Empty>;
  const labels: Record<keyof FiscalTables, string> = { cfop: "CFOP", icmsCst: "CST ICMS", csosn: "CSOSN (Simples)", pisCofinsCst: "CST PIS/COFINS", ipiCst: "CST IPI", origin: "Origem" };
  const words = query.trim().toLowerCase();
  const rows = tables[table].filter((item) => !words || item.code.includes(words) || item.description.toLowerCase().includes(words));

  return (
    <article className="card vx-box fx-card">
      <div className="vx-panel-title"><h3>Tabelas fiscais <em>para consulta</em></h3><span className="vx-pill"><i />Conferir com o contador</span></div>
      <div className="admin-filter-row">
        {(Object.keys(labels) as Array<keyof FiscalTables>).map((key) => (
          <button className={`admin-filter ${table === key ? "is-active" : ""}`} key={key} onClick={() => setTable(key)} type="button">{labels[key]} <b>{tables[key].length}</b></button>
        ))}
      </div>
      <input className="fx-search" onChange={(event) => setQuery(event.target.value)} placeholder="Buscar código ou descrição…" value={query} />
      <div className="fx-table">
        {rows.slice(0, 400).map((item) => <div key={item.code}><b>{item.code}</b><span>{item.description}</span></div>)}
      </div>
    </article>
  );
}

interface Finding {
  level: "Info" | "Warning" | "Critical";
  area: string;
  title: string;
  detail: string;
  action: string;
}

interface Review {
  checkedAt: string;
  critical: number;
  warnings: number;
  findings: Finding[];
}

export function FiscalReviewView({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const review = useLoad(() => http.get<Review>(`/establishments/${establishmentId}/fiscal/review`), [establishmentId]);
  const data = review.data;
  const levels = { Critical: "Crítico", Warning: "Atenção", Info: "Aviso" } as const;

  return (
    <article className="card vx-box fx-card">
      <div className="vx-panel-title">
        <h3>Conferência da IA fiscal <em>ZEST</em></h3>
        <button className="secondary-button" disabled={review.loading} onClick={() => void review.reload()} type="button">{review.loading ? "Conferindo…" : "Conferir de novo"}</button>
      </div>
      <p className="muted">A IA confere cadastro, notas, prazos e rejeições e diz o que fazer. Ela não decide tributação: toda mudança passa pelo contador.</p>
      <ErrorMessage message={review.error} />
      {data ? (
        <>
          <div className="kpi-strip">
            <div className={`kpi ${data.critical ? "is-alert" : ""}`}><small>Críticos</small><strong>{data.critical}</strong></div>
            <div className={`kpi ${data.warnings ? "is-warn" : ""}`}><small>Atenção</small><strong>{data.warnings}</strong></div>
            <div className="kpi"><small>Conferido</small><strong className="kpi-text">{new Date(data.checkedAt).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}</strong></div>
          </div>
          {data.findings.length === 0 ? <Empty>Nada a apontar agora. Tudo em ordem.</Empty> : (
            <div className="fx-findings">
              {data.findings.map((finding, index) => (
                <div className={`fx-finding is-${finding.level.toLowerCase()}`} key={index}>
                  <span className="fx-finding-level">{levels[finding.level]} · {finding.area}</span>
                  <strong>{finding.title}</strong>
                  <p>{finding.detail}</p>
                  <p className="fx-finding-action">→ {finding.action}</p>
                </div>
              ))}
            </div>
          )}
        </>
      ) : null}
    </article>
  );
}

export function AccountantPackageView({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const last = new Date();
  last.setMonth(last.getMonth() - 1);
  const [period, setPeriod] = useState(`${last.getFullYear()}-${String(last.getMonth() + 1).padStart(2, "0")}`);
  const [result, setResult] = useState<{ issuedNotes: number; receivedNotes: number; warnings: string[] } | null>(null);
  const action = useAction(async () => undefined);

  function build() {
    const [year, month] = period.split("-").map(Number);
    void action.run(async () => {
      const file = await http.post<{ fileName: string; contentBase64: string; issuedNotes: number; receivedNotes: number; warnings: string[] }>(
        `/establishments/${establishmentId}/fiscal/accountant-package`, { year, month });
      const bytes = Uint8Array.from(atob(file.contentBase64), (char) => char.charCodeAt(0));
      const url = URL.createObjectURL(new Blob([bytes], { type: "application/zip" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = file.fileName;
      link.click();
      URL.revokeObjectURL(url);
      setResult(file);
    });
  }

  return (
    <article className="card vx-box fx-card">
      <div className="vx-panel-title"><h3>Fechamento do mês <em>para o contador</em></h3></div>
      <p className="muted">Um arquivo .zip com os XMLs emitidos e recebidos, o SINTEGRA e o SPED do mês (se liberados no contrato) e um resumo em planilha. O escritório confere, valida no programa oficial e transmite.</p>
      <div className="module-form module-form-flat">
        <label className="field"><span>Competência</span><input onChange={(event) => setPeriod(event.target.value)} type="month" value={period} /></label>
        <button className="primary-button" disabled={action.busy} onClick={build} type="button">{action.busy ? "Montando o pacote…" : "Baixar pacote do mês"}</button>
      </div>
      <ErrorMessage message={action.error} />
      {result ? (
        <div className="fx-sync">
          <b>{result.issuedNotes}</b> nota(s) emitida(s) e <b>{result.receivedNotes}</b> recebida(s) no pacote.
          {result.warnings.length ? <ul>{result.warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul> : " Sem avisos."}
        </div>
      ) : null}
    </article>
  );
}
