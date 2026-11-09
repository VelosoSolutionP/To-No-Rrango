"use client";

import { type ChangeEvent, type FormEvent, useEffect, useState } from "react";
import { formatBRL, formatDateTime, type AdminHttp } from "@/lib/admin-api";
import type { PublicMenu } from "@/lib/types";
import { Suppliers } from "./Parties";
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
}

const movementLabels: Record<Movement["type"], string> = {
  Purchase: "Entrada",
  SaleDeduction: "Baixa por pedido",
  SaleReversal: "Estorno de pedido",
  Adjustment: "Ajuste",
  Loss: "Perda",
};

function Stock({ http, establishmentId, items, reload }: Props & { items: StockItem[]; reload: () => Promise<void> }) {
  const action = useAction(reload);

  async function submit(event: FormEvent<HTMLFormElement>) {
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
    }
  }

  function adjust(item: StockItem, type: "Adjustment" | "Loss") {
    const answer = window.prompt(
      type === "Loss" ? `Quantidade perdida de ${item.name} (${item.unit}):` : `Ajuste de ${item.name} (${item.unit}). Use negativo para reduzir:`,
    );

    if (!answer) {
      return;
    }

    const amount = toDecimal(answer);
    void action.run(() =>
      http.post(`/stock-items/${item.id}/adjustments`, {
        type,
        quantity: type === "Loss" ? -Math.abs(amount) : amount,
        notes: null,
      }),
    );
  }

  return (
    <>
      <form className="card module-form" onSubmit={submit}>
        <Input label="Insumo *" name="name" required />
        <Input label="Unidade *" name="unit" placeholder="un, kg, L" required />
        <Input inputMode="decimal" label="Estoque mínimo" name="minimum" />
        <button className="primary-button" disabled={action.busy} type="submit">Adicionar insumo</button>
      </form>
      <ErrorText message={action.error} />
      {items.length === 0 ? <Empty>Cadastre os insumos para controlar o estoque.</Empty> : (
        <div className="card admin-table-wrap">
          <table className="admin-table">
            <thead><tr><th>Insumo</th><th className="num">Saldo</th><th className="num">Mínimo</th><th className="num">Custo médio</th><th>Situação</th><th /></tr></thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td>{item.name}</td>
                  <td className="num">{quantity(item.quantityOnHand)} {item.unit}</td>
                  <td className="num">{quantity(item.minimumQuantity)}</td>
                  <td className="num">{formatBRL(item.averageCostInCents)}</td>
                  <td>{item.quantityOnHand < 0 ? <span className="table-flag">⚠ Saldo negativo</span> : item.isLow ? <span className="table-flag">⚠ Baixo</span> : "Ok"}</td>
                  <td className="table-actions">
                    <button className="link-button" onClick={() => adjust(item, "Adjustment")} type="button">Ajustar</button>
                    <button className="link-button" onClick={() => adjust(item, "Loss")} type="button">Perda</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

function Recipes({ http, menu, items }: Props & { items: StockItem[] }) {
  const menuItems = menu?.categories.flatMap((category) => category.items) ?? [];
  const [menuItemId, setMenuItemId] = useState("");
  const [lines, setLines] = useState<Array<{ stockItemId: string; quantity: string }>>([]);
  const [saved, setSaved] = useState(false);
  const action = useAction();

  useEffect(() => {
    setSaved(false);

    if (!menuItemId) {
      setLines([]);
      return;
    }

    http
      .get<Array<{ stockItemId: string; quantity: number }>>(`/items/${menuItemId}/recipe`)
      .then((recipe) => setLines(recipe.map((line) => ({ stockItemId: line.stockItemId, quantity: String(Number(line.quantity)) }))))
      .catch(() => setLines([]));
  }, [http, menuItemId]);

  async function save() {
    setSaved(false);
    const done = await action.run(() =>
      http.put(`/items/${menuItemId}/recipe`, lines.map((line) => ({ stockItemId: line.stockItemId, quantity: toDecimal(line.quantity) }))),
    );
    setSaved(done);
  }

  return (
    <div className="card module-stack">
      <label className="field">
        <span>Item do cardápio</span>
        <select onChange={(event) => setMenuItemId(event.target.value)} value={menuItemId}>
          <option value="">Selecione</option>
          {menuItems.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
        </select>
      </label>
      {menuItemId ? (
        <>
          <p className="muted">Quanto de cada insumo sai do estoque quando uma unidade é confirmada.</p>
          {lines.map((line, index) => (
            <div className="module-line" key={index}>
              <select
                onChange={(event) => setLines((current) => current.map((row, i) => (i === index ? { ...row, stockItemId: event.target.value } : row)))}
                value={line.stockItemId}
              >
                <option value="">Insumo</option>
                {items.map((item) => <option key={item.id} value={item.id}>{item.name} ({item.unit})</option>)}
              </select>
              <input
                aria-label="Quantidade"
                inputMode="decimal"
                onChange={(event) => setLines((current) => current.map((row, i) => (i === index ? { ...row, quantity: event.target.value } : row)))}
                value={line.quantity}
              />
              <button className="link-button" onClick={() => setLines((current) => current.filter((_, i) => i !== index))} type="button">Remover</button>
            </div>
          ))}
          <div className="module-actions">
            <button className="secondary-button secondary-button-small" onClick={() => setLines((current) => [...current, { stockItemId: "", quantity: "" }])} type="button">Adicionar insumo</button>
            <button className="primary-button" disabled={action.busy || lines.some((line) => !line.stockItemId || toDecimal(line.quantity) <= 0)} onClick={save} type="button">Salvar ficha técnica</button>
            {saved ? <span className="muted">Ficha salva.</span> : null}
          </div>
          <ErrorText message={action.error} />
        </>
      ) : null}
    </div>
  );
}

function Purchase({ http, establishmentId, items, reload, canImportNfe }: Props & { items: StockItem[]; reload: () => Promise<void> }) {
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

    try {
      const xml = await file.text();
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
      {preview ? (
        <p className="muted">
          NF {preview.number} · {preview.supplierTradeName ?? preview.supplierName} · total R$ {preview.total.toFixed(2).replace(".", ",")}.
          {" "}Confira os insumos e ajuste a quantidade para a unidade do seu estoque.
        </p>
      ) : null}
      {preview?.alreadyImported ? <p className="form-error">Esta nota já foi lançada.</p> : null}
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
  type View = "stock" | "recipes" | "purchase" | "movements" | "suppliers";
  const [view, setView] = useState<View>("stock");
  const stock = useLoad(() => props.http.get<StockItem[]>(`/establishments/${props.establishmentId}/stock-items`), [props.establishmentId]);
  const items = stock.data ?? [];
  const low = items.filter((item) => item.isLow).length;

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Gestão" title="Estoque" hint={low > 0 ? `⚠ ${low} insumo(s) no limite mínimo.` : "A baixa acontece quando o pedido é confirmado e volta se ele for cancelado."} />
      <SubTabs
        onChange={setView}
        options={[
          { value: "stock", label: "Insumos" },
          { value: "recipes", label: "Ficha técnica" },
          { value: "purchase", label: "Entrada" },
          { value: "movements", label: "Movimentações" },
          { value: "suppliers", label: "Fornecedores" },
        ]}
        value={view}
      />
      <ErrorText message={stock.error} />
      {view === "stock" ? <Stock {...props} items={items} reload={stock.reload} /> : null}
      {view === "recipes" ? <Recipes {...props} items={items.filter((item) => item.isActive)} /> : null}
      {view === "purchase" ? <Purchase {...props} items={items.filter((item) => item.isActive)} reload={stock.reload} /> : null}
      {view === "movements" ? <Movements {...props} items={items} /> : null}
      {view === "suppliers" ? <Suppliers establishmentId={props.establishmentId} http={props.http} /> : null}
    </section>
  );
}
