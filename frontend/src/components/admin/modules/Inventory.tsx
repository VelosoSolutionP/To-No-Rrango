"use client";

import { type ChangeEvent, type FormEvent, useEffect, useState } from "react";
import { formatBRL, formatDateTime, type AdminHttp } from "@/lib/admin-api";
import type { PublicMenu } from "@/lib/types";
import type { ManualDraft } from "./FiscalExtras";
import { Empty, ErrorMessage as ErrorText, Input, ModuleHeading, SubTabs, messageOf, quantity, text, toCents, toDecimal, useAction, useLoad } from "./shared";

interface StockItem {
  id: string;
  name: string;
  unit: string;
  quantityOnHand: number;
  minimumQuantity: number;
  averageCostInCents: number;
  isActive: boolean;
  isLow: boolean;
}

interface Movement {
  id: string;
  stockItemId: string;
  type: "Purchase" | "SaleDeduction" | "SaleReversal" | "Adjustment" | "Loss";
  quantity: number;
  notes: string | null;
  createdBy: string | null;
  createdAt: string;
}

interface NfeItem {
  code: string;
  ncm: string | null;
  total: number;
  description: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  suggestedStockItemId: string | null;
}

interface NfePreview {
  accessKey: string;
  number: string;
  issuedOn: string;
  supplierCnpj: string;
  supplierName: string;
  supplierTradeName: string | null;
  supplierId: string | null;
  total: number;
  alreadyImported: boolean;
  items: NfeItem[];
  supplierState?: string | null;
  recipientState?: string | null;
  isInterstate?: boolean;
}

interface Line {
  stockItemId: string;
  description: string;
  quantity: string;
  unitCost: string;
  invoiceUnit?: string;
}

interface Props {
  http: AdminHttp;
  establishmentId: string;
  menu: PublicMenu | null;
  canImportNfe: boolean;
  /** XML vindo da Central Fiscal (nota baixada pelo CNPJ): já abre a entrada conferindo. */
  initialXml?: string | null;
  onOpenFiscal?: (draft?: ManualDraft) => void;
}

const movementLabels: Record<Movement["type"], string> = {
  Purchase: "Entrada",
  SaleDeduction: "Baixa por pedido",
  SaleReversal: "Estorno de pedido",
  Adjustment: "Ajuste",
  Loss: "Perda",
};

type StockAction = "count" | "entry" | "Adjustment" | "Loss";

const actionLabels: Record<StockAction, string> = {
  count: "Contagem de inventário",
  entry: "Entrada avulsa",
  Adjustment: "Ajuste",
  Loss: "Perda",
};

function level(item: StockItem) {
  if (item.quantityOnHand <= 0) {
    return 0;
  }
  const reference = Math.max(item.minimumQuantity * 2, item.quantityOnHand, 1);
  return Math.min(100, (item.quantityOnHand / reference) * 100);
}

function Stock({ http, establishmentId, items, reload, onImport, canImportNfe }: Props & { items: StockItem[]; reload: () => Promise<void>; onImport: () => void }) {
  const action = useAction(reload);
  const [filter, setFilter] = useState<"all" | "low" | "negative">("all");
  const [search, setSearch] = useState("");
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<{ item: StockItem; kind: StockAction } | null>(null);

  const active = items.filter((item) => item.isActive);
  const low = active.filter((item) => item.isLow && item.quantityOnHand >= 0);
  const negative = active.filter((item) => item.quantityOnHand < 0);
  const value = active.reduce((sum, item) => sum + Math.max(0, item.quantityOnHand) * item.averageCostInCents, 0);
  const query = search.trim().toLowerCase();
  const shown = active
    .filter((item) => (filter === "low" ? item.isLow : filter === "negative" ? item.quantityOnHand < 0 : true))
    .filter((item) => !query || item.name.toLowerCase().includes(query))
    .sort((a, b) => Number(b.quantityOnHand < 0) - Number(a.quantityOnHand < 0) || Number(b.isLow) - Number(a.isLow) || a.name.localeCompare(b.name));

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const element = event.currentTarget;
    const form = new FormData(element);
    const done = await action.run(() =>
      http.post(`/establishments/${establishmentId}/stock-items`, {
        name: text(form, "name"),
        unit: text(form, "unit"),
        minimumQuantity: toDecimal(form.get("minimum")),
      }),
    );

    if (done) {
      element.reset();
      setCreating(false);
    }
  }

  async function apply(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editing) {
      return;
    }
    const form = new FormData(event.currentTarget);
    const amount = toDecimal(form.get("quantity"));
    const notes = text(form, "notes");
    const { item, kind } = editing;
    // A count sets the real quantity: the system records the difference as an adjustment.
    const quantityChange =
      kind === "count" ? amount - item.quantityOnHand : kind === "Loss" ? -Math.abs(amount) : kind === "entry" ? Math.abs(amount) : amount;

    if (quantityChange === 0) {
      setEditing(null);
      return;
    }

    const done = await action.run(() =>
      http.post(`/stock-items/${item.id}/adjustments`, {
        type: kind === "Loss" ? "Loss" : "Adjustment",
        quantity: quantityChange,
        notes: notes ?? (kind === "count" ? `Contagem: ${quantity(amount)} ${item.unit}` : kind === "entry" ? "Entrada avulsa" : null),
      }),
    );

    if (done) {
      setEditing(null);
    }
  }

  return (
    <>
      <div className="kpi-strip">
        <div className="kpi"><small>Insumos</small><strong>{active.length}</strong></div>
        <div className={`kpi ${low.length ? "is-warn" : ""}`}><small>No mínimo ou abaixo</small><strong>{low.length}</strong></div>
        <div className={`kpi ${negative.length ? "is-alert" : ""}`}><small>Saldo negativo</small><strong>{negative.length}</strong></div>
        <div className="kpi"><small>Valor em estoque (custo médio)</small><strong>{formatBRL(Math.round(value))}</strong></div>
      </div>

      <div className="board-toolbar">
        <div className="origin-filter">
          {(
            [
              ["all", "Todos", active.length],
              ["low", "Baixo", low.length + negative.length],
              ["negative", "Negativo", negative.length],
            ] as const
          ).map(([key, label, count]) => (
            <button className={`origin-chip ${filter === key ? "is-active" : ""}`} key={key} onClick={() => setFilter(key)} type="button">
              {label}<b>{count}</b>
            </button>
          ))}
        </div>
        <div className="toolbar-actions">
          <input className="board-search" onChange={(event) => setSearch(event.target.value)} placeholder="Buscar insumo" type="search" value={search} />
          {canImportNfe ? <button className="secondary-button" onClick={onImport} type="button">Entrada por NF-e (XML)</button> : null}
          <button className="secondary-button" onClick={() => setCreating(true)} type="button">+ Insumo rápido</button>
        </div>
      </div>

      <ErrorText message={action.error} />
      {active.length === 0 ? <Empty>Cadastre os insumos para controlar o estoque. A baixa acontece sozinha quando o pedido é confirmado.</Empty> : null}

      <div className="stock-list">
        {shown.map((item) => {
          const state = item.quantityOnHand < 0 ? "negative" : item.isLow ? "low" : "ok";
          return (
            <article className={`stock-row is-${state}`} key={item.id}>
              <div className="stock-name">
                <strong>{item.name}</strong>
                <small>Mínimo {quantity(item.minimumQuantity)} {item.unit} · {item.averageCostInCents > 0 ? `custo médio ${formatBRL(item.averageCostInCents)}/${item.unit}` : "sem custo (lance uma entrada com valor)"}</small>
              </div>
              <div className="stock-level">
                <div className="stock-bar"><i style={{ width: `${level(item)}%` }} /></div>
                <span>
                  <b>{quantity(item.quantityOnHand)}</b> {item.unit}
                </span>
              </div>
              <span className={`stock-state is-${state}`}>{state === "negative" ? "Negativo" : state === "low" ? "Repor" : "Ok"}</span>
              <div className="stock-actions">
                <button onClick={() => setEditing({ item, kind: "entry" })} type="button">Entrada</button>
                <button onClick={() => setEditing({ item, kind: "count" })} type="button">Contar</button>
                <button onClick={() => setEditing({ item, kind: "Adjustment" })} type="button">Ajustar</button>
                <button className="danger" onClick={() => setEditing({ item, kind: "Loss" })} type="button">Perda</button>
              </div>
            </article>
          );
        })}
      </div>

      {creating ? (
        <>
          <button aria-label="Fechar" className="drawer-backdrop" onClick={() => setCreating(false)} type="button" />
          <aside className="drawer">
            <header className="drawer-head">
              <h2>Novo insumo</h2>
              <button className="drawer-close" onClick={() => setCreating(false)} type="button">×</button>
            </header>
            <form className="drawer-form" onSubmit={create}>
              <Input label="Nome *" name="name" placeholder="Ex.: Pão brioche" required />
              <Input label="Unidade *" name="unit" placeholder="un, kg, L, g" required />
              <Input inputMode="decimal" label="Estoque mínimo (avisa quando chegar)" name="minimum" />
              <p className="muted">Depois ligue o insumo aos produtos na Ficha técnica: cada venda confirmada baixa o estoque.</p>
              <button className="primary-button" disabled={action.busy} type="submit">Cadastrar</button>
            </form>
          </aside>
        </>
      ) : null}

      {editing ? (
        <>
          <button aria-label="Fechar" className="drawer-backdrop" onClick={() => setEditing(null)} type="button" />
          <aside className="drawer">
            <header className="drawer-head">
              <div>
                <small className="muted">{actionLabels[editing.kind]}</small>
                <h2>{editing.item.name}</h2>
                <small className="muted">Saldo atual: {quantity(editing.item.quantityOnHand)} {editing.item.unit}</small>
              </div>
              <button className="drawer-close" onClick={() => setEditing(null)} type="button">×</button>
            </header>
            <form className="drawer-form" key={`${editing.item.id}-${editing.kind}`} onSubmit={apply}>
              <Input
                autoFocus
                inputMode="decimal"
                label={
                  editing.kind === "count"
                    ? `Quantidade contada (${editing.item.unit}) *`
                    : editing.kind === "Adjustment"
                      ? `Ajuste em ${editing.item.unit} (negativo para reduzir) *`
                      : `Quantidade (${editing.item.unit}) *`
                }
                name="quantity"
                required
              />
              <Input label="Observação" name="notes" placeholder={editing.kind === "Loss" ? "Ex.: venceu, caiu no chão" : ""} />
              {editing.kind === "count" ? <p className="muted">O sistema registra a diferença entre o contado e o saldo como ajuste, com data e quem contou.</p> : null}
              {editing.kind === "entry" ? <p className="muted">Para entrada com nota fiscal e custo, use a aba Entrada ou a NF-e (XML).</p> : null}
              <button className={`primary-button ${editing.kind === "Loss" ? "danger-button" : ""}`} disabled={action.busy} type="submit">Registrar</button>
            </form>
          </aside>
        </>
      ) : null}
    </>
  );
}

function Purchase({ http, establishmentId, items, reload, canImportNfe, initialXml, onOpenFiscal }: Props & { items: StockItem[]; reload: () => Promise<void> }) {
  const suppliers = useLoad(() => http.get<Array<{ id: string; name: string; tradeName: string | null }>>(`/establishments/${establishmentId}/suppliers`).catch(() => []), [establishmentId]);
  const today = new Date().toISOString().slice(0, 10);
  const [lines, setLines] = useState<Line[]>([{ stockItemId: "", description: "", quantity: "", unitCost: "" }]);
  const [preview, setPreview] = useState<NfePreview | null>(null);
  const [supplierId, setSupplierId] = useState("");
  const [invoice, setInvoice] = useState("");
  const [issuedOn, setIssuedOn] = useState(today);
  const [dueOn, setDueOn] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [nfeXml, setNfeXml] = useState<string | null>(null);
  const action = useAction(reload);

  async function readNfe(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    setMessage(null);

    if (!file) {
      return;
    }

    await previewXml(await file.text());
  }

  useEffect(() => {
    if (initialXml) {
      void previewXml(initialXml);
    }
    // Só a nota que chegou da Central Fiscal; trocar de nota é pelo botão de importar.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialXml]);

  function draftFrom(purpose: 3 | 4): ManualDraft | undefined {
    return preview
      ? {
          purpose,
          referencedKey: preview.accessKey,
          supplierCnpj: preview.supplierCnpj,
          interstate: Boolean(preview.isInterstate),
          items: preview.items.map((item) => ({ code: item.code, description: item.description, ncm: item.ncm, quantity: item.quantity, total: item.total })),
        }
      : undefined;
  }

  async function previewXml(xml: string) {
    setMessage(null);
    try {
      setNfeXml(xml);
      const result = await http.post<NfePreview>(`/establishments/${establishmentId}/nfe/preview`, { xml });
      setPreview(result);
      setSupplierId(result.supplierId ?? "");
      setInvoice(result.number);
      setIssuedOn(result.issuedOn);
      setLines(result.items.map((item) => ({
        stockItemId: item.suggestedStockItemId ?? "",
        description: item.description,
        quantity: String(item.quantity),
        unitCost: item.unitPrice.toFixed(2),
        invoiceUnit: item.unit,
      })));
    } catch (error) {
      setMessage(messageOf(error));
    }
  }

  function update(index: number, change: Partial<Line>) {
    setLines((current) => current.map((line, i) => (i === index ? { ...line, ...change } : line)));
  }

  async function save() {
    setMessage(null);
    const done = await action.run(() =>
      http.post(`/establishments/${establishmentId}/purchases`, {
        supplierId: supplierId || null,
        newSupplier: preview && !supplierId
          ? { name: preview.supplierName, tradeName: preview.supplierTradeName, document: preview.supplierCnpj }
          : null,
        invoiceNumber: invoice || null,
        accessKey: preview?.accessKey ?? null,
        nfeXml: preview ? nfeXml : null,
        issuedOn,
        dueOn: dueOn || null,
        notes: null,
        lines: lines.map((line) => ({
          stockItemId: line.stockItemId,
          description: line.description || items.find((item) => item.id === line.stockItemId)?.name || "Item",
          quantity: toDecimal(line.quantity),
          unitCostInCents: toCents(line.unitCost),
        })),
      }),
    );

    if (done) {
      setPreview(null);
      setLines([{ stockItemId: "", description: "", quantity: "", unitCost: "" }]);
      setInvoice("");
      setSupplierId("");
      setDueOn("");
      setMessage("Entrada lançada no estoque e no contas a pagar.");
    }
  }

  const invalid = lines.some((line) => !line.stockItemId || toDecimal(line.quantity) <= 0) || preview?.alreadyImported;

  return (
    <div className="card module-stack">
      {canImportNfe ? (
        <label className="secondary-button secondary-button-small module-file">
          Importar XML da NF-e
          <input accept=".xml,text/xml" onChange={readNfe} type="file" />
        </label>
      ) : null}
      {onOpenFiscal ? <button className="link-button" onClick={() => onOpenFiscal()} type="button">Abrir Central Fiscal (notas recebidas pelo CNPJ, devolução e ajuste) →</button> : null}
      {preview ? (
        <p className="muted">
          NF {preview.number} · {preview.supplierTradeName ?? preview.supplierName} · total R$ {preview.total.toFixed(2).replace(".", ",")}.
          {" "}Confira os insumos e ajuste a quantidade para a unidade do seu estoque.
        </p>
      ) : null}
      {preview?.alreadyImported ? <p className="form-error">Esta nota já foi lançada.</p> : null}
      {preview?.isInterstate ? (
        <div className="fx-warn">
          <strong>Compra de outro estado ({preview.supplierState} → {preview.recipientState}).</strong> Confira com o contador o DIFAL e o
          crédito de ICMS. Se a escrituração pedir, emita a nota de ajuste referenciando esta nota.
          {onOpenFiscal ? <div className="fx-warn-actions"><button className="secondary-button" onClick={() => onOpenFiscal(draftFrom(3))} type="button">Emitir nota de ajuste</button></div> : null}
        </div>
      ) : null}
      {preview && onOpenFiscal ? <button className="link-button" onClick={() => onOpenFiscal(draftFrom(4))} type="button">Devolver esta compra (NF-e de devolução)</button> : null}
      <div className="module-form module-form-flat">
        <label className="field">
          <span>Fornecedor</span>
          <select onChange={(event) => setSupplierId(event.target.value)} value={supplierId}>
            <option value="">{preview && !preview.supplierId ? "Cadastrar da nota" : "Sem fornecedor"}</option>
            {suppliers.data?.map((supplier) => <option key={supplier.id} value={supplier.id}>{supplier.tradeName ?? supplier.name}</option>)}
          </select>
        </label>
        <Input label="Nº da nota" name="invoice" onChange={(event) => setInvoice(event.target.value)} value={invoice} />
        <Input label="Emissão" name="issuedOn" onChange={(event) => setIssuedOn(event.target.value)} type="date" value={issuedOn} />
        <Input label="Vencimento" name="dueOn" onChange={(event) => setDueOn(event.target.value)} type="date" value={dueOn} />
      </div>
      {lines.map((line, index) => (
        <div className="module-line module-line-purchase" key={index}>
          <select onChange={(event) => update(index, { stockItemId: event.target.value })} value={line.stockItemId}>
            <option value="">{line.description ? `Vincular: ${line.description}` : "Insumo"}</option>
            {items.map((item) => <option key={item.id} value={item.id}>{item.name} ({item.unit})</option>)}
          </select>
          <input aria-label="Quantidade" inputMode="decimal" onChange={(event) => update(index, { quantity: event.target.value })} placeholder={line.invoiceUnit ? `Qtd (${line.invoiceUnit})` : "Qtd"} value={line.quantity} />
          <input aria-label="Custo unitário em reais" inputMode="decimal" onChange={(event) => update(index, { unitCost: event.target.value })} placeholder="Custo un. (R$)" value={line.unitCost} />
          <button className="link-button" onClick={() => setLines((current) => current.filter((_, i) => i !== index))} type="button">Remover</button>
        </div>
      ))}
      <div className="module-actions">
        <button className="secondary-button secondary-button-small" onClick={() => setLines((current) => [...current, { stockItemId: "", description: "", quantity: "", unitCost: "" }])} type="button">Adicionar linha</button>
        <button className="primary-button" disabled={action.busy || Boolean(invalid) || lines.length === 0} onClick={save} type="button">Lançar entrada</button>
      </div>
      <ErrorText message={action.error} />
      {message ? <p className="muted">{message}</p> : null}
    </div>
  );
}

function Movements({ http, establishmentId, items }: Props & { items: StockItem[] }) {
  const list = useLoad(() => http.get<Movement[]>(`/establishments/${establishmentId}/stock-movements`), [establishmentId]);
  const names = new Map(items.map((item) => [item.id, item]));

  if (list.data?.length === 0) {
    return <Empty>Nenhuma movimentação ainda.</Empty>;
  }

  return (
    <div className="card admin-table-wrap">
      <ErrorText message={list.error} />
      <table className="admin-table">
        <thead><tr><th>Data</th><th>Insumo</th><th>Tipo</th><th className="num">Quantidade</th><th>Origem</th><th>Por</th></tr></thead>
        <tbody>
          {list.data?.map((movement) => (
            <tr key={movement.id}>
              <td>{formatDateTime(movement.createdAt)}</td>
              <td>{names.get(movement.stockItemId)?.name ?? "—"}</td>
              <td>{movementLabels[movement.type]}</td>
              <td className="num">{quantity(movement.quantity)} {names.get(movement.stockItemId)?.unit}</td>
              <td>{movement.notes ?? "—"}</td>
              <td>{movement.createdBy ?? "—"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function Inventory(props: Props) {
  type View = "stock" | "purchase" | "movements";
  const [view, setView] = useState<View>(props.initialXml ? "purchase" : "stock");
  const stock = useLoad(() => props.http.get<StockItem[]>(`/establishments/${props.establishmentId}/stock-items`), [props.establishmentId]);
  const items = stock.data ?? [];
  
  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Relatório" title="Estoque" hint="Posição, entradas e movimentações. Produtos, ficha técnica e fornecedores ficam nos cadastros." />
      <SubTabs
        onChange={setView}
        options={[
          { value: "stock", label: "Posição e contagem" },
          { value: "purchase", label: "Entrada de mercadoria" },
          { value: "movements", label: "Movimentações" },
        ]}
        value={view}
      />
      <ErrorText message={stock.error} />
      {view === "stock" ? <Stock {...props} items={items} onImport={() => setView("purchase")} reload={stock.reload} /> : null}
      {view === "purchase" ? <Purchase {...props} items={items.filter((item) => item.isActive)} reload={stock.reload} /> : null}
      {view === "movements" ? <Movements {...props} items={items} /> : null}
    </section>
  );
}
