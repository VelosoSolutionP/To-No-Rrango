"use client";

import { type FormEvent, useEffect, useMemo, useState } from "react";
import { formatBRL, type AdminHttp } from "@/lib/admin-api";
import type { MenuItem, PublicMenu } from "@/lib/types";
import { DataTable, FormDrawer, StatusChip } from "../DataTable";
import { ImageUpload } from "../ImageUpload";
import { ErrorMessage, ModuleHeading, SubTabs, quantity, text, toCents, toDecimal, useAction, useLoad } from "./shared";

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

interface Purchase {
  id: string;
  supplierId: string | null;
  invoiceNumber: string | null;
  issuedOn: string;
  totalInCents: number;
  createdAt: string;
  lines: Array<{ stockItemId: string; description: string; quantity: number; unitCostInCents: number; totalInCents: number }>;
}

interface Supplier {
  id: string;
  name: string;
  tradeName: string | null;
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

interface RecipeLine {
  stockItemId: string;
  quantity: number;
}

type Row = MenuItem & { categoryName: string };

const money = (cents: number) => (cents / 100).toFixed(2).replace(".", ",");

function initials(name: string) {
  const words = name.split(/[\s-]+/).filter(Boolean);
  return (words.length > 1 ? words[0][0] + words[1][0] : name.slice(0, 2)).toUpperCase();
}

export function Products({
  http,
  establishmentId,
  menu,
  modules,
  onMenuChanged,
}: {
  http: AdminHttp;
  establishmentId: string;
  menu: PublicMenu | null;
  modules: string[];
  onMenuChanged: () => Promise<void>;
}) {
  const hasStock = modules.includes("estoque");
  const hasFiscal = modules.includes("fiscal_nfce");
  const base = `/establishments/${establishmentId}`;
  const stock = useLoad(() => (hasStock ? http.get<StockItem[]>(`${base}/stock-items`) : Promise.resolve([])), [base, hasStock]);
  const purchases = useLoad(() => (hasStock ? http.get<Purchase[]>(`${base}/purchases`) : Promise.resolve([])), [base, hasStock]);
  const suppliers = useLoad(() => (hasStock ? http.get<Supplier[]>(`${base}/suppliers`).catch(() => []) : Promise.resolve([])), [base, hasStock]);
  const fiscal = useLoad(
    () => (hasFiscal ? http.get<{ products: FiscalProduct[] }>(`${base}/fiscal`).then((data) => data.products).catch(() => []) : Promise.resolve([])),
    [base, hasFiscal],
  );
  const action = useAction(async () => {
    await Promise.all([onMenuChanged(), stock.reload(), fiscal.reload()]);
  });
  const [view, setView] = useState<"sale" | "inputs">("sale");
  const [editing, setEditing] = useState<Row | "new" | null>(null);
  const [editingInput, setEditingInput] = useState<StockItem | "new" | null>(null);

  const rows: Row[] = useMemo(
    () => (menu?.categories ?? []).flatMap((category) => category.items.map((item) => ({ ...item, categoryName: category.name }))),
    [menu],
  );
  const fiscalOf = useMemo(() => new Map((fiscal.data ?? []).map((item) => [item.menuItemId, item])), [fiscal.data]);
  const supplierName = useMemo(() => new Map((suppliers.data ?? []).map((item) => [item.id, item.tradeName ?? item.name])), [suppliers.data]);

  /** Last purchase of each input: supplier and unit cost. */
  const lastPurchase = useMemo(() => {
    const map = new Map<string, { supplier: string; cost: number; on: string }>();
    for (const purchase of [...(purchases.data ?? [])].sort((a, b) => a.createdAt.localeCompare(b.createdAt))) {
      for (const line of purchase.lines) {
        map.set(line.stockItemId, { supplier: purchase.supplierId ? supplierName.get(purchase.supplierId) ?? "—" : "—", cost: line.unitCostInCents, on: purchase.issuedOn });
      }
    }
    return map;
  }, [purchases.data, supplierName]);

  const fiscalReady = (id: string) => {
    const tax = fiscalOf.get(id);
    return Boolean(tax?.ncm && tax.cfop && tax.icmsCst && tax.pisCofinsCst);
  };

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Cadastros" title="Produtos" hint="O cadastro que liga venda, composição, compra, fornecedor e tributação." />
      <div className="kpi-strip">
        <div className="kpi"><small>Produtos de venda</small><strong>{rows.filter((row) => row.isActive).length}</strong></div>
        <div className="kpi"><small>Em promoção</small><strong>{rows.filter((row) => (row.compareAtPriceInCents ?? 0) > row.priceInCents).length}</strong></div>
        <div className={`kpi ${rows.some((row) => !row.isAvailable && row.isActive) ? "is-warn" : ""}`}><small>Esgotados</small><strong>{rows.filter((row) => !row.isAvailable && row.isActive).length}</strong></div>
        {hasFiscal ? <div className={`kpi ${rows.some((row) => row.isActive && !fiscalReady(row.id)) ? "is-warn" : ""}`}><small>Sem tributação</small><strong>{rows.filter((row) => row.isActive && !fiscalReady(row.id)).length}</strong></div> : null}
        {hasStock ? <div className="kpi"><small>Insumos</small><strong>{(stock.data ?? []).filter((item) => item.isActive).length}</strong></div> : null}
      </div>
      {hasStock ? (
        <SubTabs
          onChange={setView}
          options={[
            { value: "sale", label: "Produtos de venda" },
            { value: "inputs", label: "Insumos e revenda" },
          ]}
          value={view}
        />
      ) : null}
      <ErrorMessage message={action.error ?? stock.error} />

      {view === "sale" ? (
        <DataTable
          actions={[
            { icon: "edit", label: "Editar", onClick: (row) => setEditing(row) },
            {
              icon: "play",
              label: "Voltar a vender",
              hidden: (row) => row.isAvailable || !row.isActive,
              onClick: (row) => void action.run(() => http.patch(`/items/${row.id}/availability`, { isAvailable: true })),
            },
            {
              icon: "pause",
              label: "Marcar esgotado",
              hidden: (row) => !row.isAvailable || !row.isActive,
              onClick: (row) => void action.run(() => http.patch(`/items/${row.id}/availability`, { isAvailable: false })),
            },
            { icon: "power", label: "Reativar", hidden: (row) => row.isActive, onClick: (row) => void action.run(() => http.patch(`/items/${row.id}/active`, { isActive: true })) },
            { icon: "trash", label: "Inativar", tone: "danger", hidden: (row) => !row.isActive, onClick: (row) => window.confirm(`Inativar ${row.name}? Ele sai do cardápio e do PDV.`) && void action.run(() => http.patch(`/items/${row.id}/active`, { isActive: false })) },
          ]}
          columns={[
            {
              key: "name",
              title: "Produto",
              value: (row) => `${row.name} ${row.description ?? ""}`,
              render: (row) => (
                <span className="cell-main">
                  {row.imageUrl ? <img alt="" className="avatar avatar-img" src={row.imageUrl} /> : <span className="avatar">{initials(row.name)}</span>}
                  <div>
                    <strong>{row.name}</strong>
                    <small>{row.categoryName}{row.comboParts?.length ? " · combo" : ""}</small>
                  </div>
                </span>
              ),
            },
            { key: "code", title: "Código / EAN", value: (row) => `${row.sku ?? ""} ${row.barcode ?? ""}`, render: (row) => (row.sku || row.barcode ? <>{row.sku ?? "—"}<small>{row.barcode ?? ""}</small></> : "—"), hideOnMobile: true },
            {
              key: "price",
              title: "Preço",
              align: "right",
              value: (row) => row.priceInCents,
              render: (row) => ((row.compareAtPriceInCents ?? 0) > row.priceInCents ? <><s className="muted">{formatBRL(row.compareAtPriceInCents!)}</s> <b>{formatBRL(row.priceInCents)}</b></> : formatBRL(row.priceInCents)),
            },
            ...(hasFiscal
              ? [{
                  key: "fiscal",
                  title: "Tributação",
                  value: (row: Row) => (fiscalReady(row.id) ? "ok" : "pendente"),
                  render: (row: Row) => (fiscalReady(row.id) ? <StatusChip tone="ok">NCM {fiscalOf.get(row.id)?.ncm}</StatusChip> : <StatusChip tone="warn">Pendente</StatusChip>),
                  hideOnMobile: true,
                }]
              : []),
            {
              key: "status",
              title: "Situação",
              value: (row) => (!row.isActive ? "inativo" : !row.isAvailable ? "esgotado" : "à venda"),
              render: (row) => (!row.isActive ? <StatusChip tone="muted">Inativo</StatusChip> : !row.isAvailable ? <StatusChip tone="warn">Esgotado</StatusChip> : <StatusChip tone="ok">À venda</StatusChip>),
            },
          ]}
          createLabel="Novo produto"
          empty="Cadastre o primeiro produto."
          filters={[
            { key: "promo", label: "Promoção", test: (row) => (row.compareAtPriceInCents ?? 0) > row.priceInCents },
            { key: "out", label: "Esgotados", test: (row) => row.isActive && !row.isAvailable },
            ...(hasFiscal ? [{ key: "tax", label: "Sem tributação", test: (row: Row) => row.isActive && !fiscalReady(row.id) }] : []),
            { key: "inactive", label: "Inativos", test: (row) => !row.isActive },
          ]}
          onCreate={() => setEditing("new")}
          onRowClick={(row) => setEditing(row)}
          rowKey={(row) => row.id}
          rows={rows}
          searchPlaceholder="Pesquisar produto, categoria, código ou código de barras…"
        />
      ) : (
        <DataTable
          actions={[
            { icon: "edit", label: "Editar", onClick: (row) => setEditingInput(row) },
            { icon: "power", label: "Reativar", hidden: (row) => row.isActive, onClick: (row) => void action.run(() => http.put(`/stock-items/${row.id}`, { name: row.name, unit: row.unit, minimumQuantity: row.minimumQuantity, isActive: true })) },
            { icon: "trash", label: "Inativar", tone: "danger", hidden: (row) => !row.isActive, onClick: (row) => window.confirm(`Inativar ${row.name}?`) && void action.run(() => http.put(`/stock-items/${row.id}`, { name: row.name, unit: row.unit, minimumQuantity: row.minimumQuantity, isActive: false })) },
          ]}
          columns={[
            { key: "name", title: "Insumo", value: (row) => row.name, render: (row) => <span className="cell-main"><span className="avatar">{initials(row.name)}</span><div><strong>{row.name}</strong><small>unidade {row.unit}</small></div></span> },
            { key: "qty", title: "Saldo", align: "right", value: (row) => row.quantityOnHand, render: (row) => <b className={row.quantityOnHand < 0 ? "is-negative" : ""}>{quantity(row.quantityOnHand)} {row.unit}</b> },
            { key: "min", title: "Mínimo", align: "right", value: (row) => row.minimumQuantity, render: (row) => `${quantity(row.minimumQuantity)} ${row.unit}`, hideOnMobile: true },
            { key: "cost", title: "Custo médio", align: "right", value: (row) => row.averageCostInCents, render: (row) => (row.averageCostInCents ? formatBRL(row.averageCostInCents) : "—") },
            { key: "supplier", title: "Último fornecedor", value: (row) => lastPurchase.get(row.id)?.supplier ?? "", render: (row) => lastPurchase.get(row.id) ? <>{lastPurchase.get(row.id)!.supplier}<small>{formatBRL(lastPurchase.get(row.id)!.cost)} em {lastPurchase.get(row.id)!.on.split("-").reverse().join("/")}</small></> : "—", hideOnMobile: true },
            {
              key: "status",
              title: "Situação",
              value: (row) => (!row.isActive ? "inativo" : row.quantityOnHand < 0 ? "negativo" : row.isLow ? "repor" : "ok"),
              render: (row) => (!row.isActive ? <StatusChip tone="muted">Inativo</StatusChip> : row.quantityOnHand < 0 ? <StatusChip tone="bad">Negativo</StatusChip> : row.isLow ? <StatusChip tone="warn">Repor</StatusChip> : <StatusChip tone="ok">Ok</StatusChip>),
            },
          ]}
          createLabel="Novo insumo"
          empty="Cadastre os insumos (matéria-prima e produtos de revenda)."
          filters={[
            { key: "low", label: "Repor", test: (row) => row.isActive && row.isLow },
            { key: "inactive", label: "Inativos", test: (row) => !row.isActive },
          ]}
          onCreate={() => setEditingInput("new")}
          onRowClick={(row) => setEditingInput(row)}
          rowKey={(row) => row.id}
          rows={stock.data ?? []}
          searchPlaceholder="Pesquisar insumo…"
        />
      )}

      {editing ? (
        <ProductDrawer
          base={base}
          busy={action.busy}
          error={action.error}
          fiscal={editing === "new" ? null : fiscalOf.get(editing.id) ?? null}
          hasFiscal={hasFiscal}
          hasStock={hasStock}
          http={http}
          lastPurchase={lastPurchase}
          menu={menu}
          onClose={() => setEditing(null)}
          product={editing === "new" ? null : editing}
          run={action.run}
          stock={stock.data ?? []}
        />
      ) : null}

      {editingInput ? (
        <FormDrawer onClose={() => setEditingInput(null)} subtitle="Insumo" title={editingInput === "new" ? "Novo insumo" : editingInput.name}>
          <form
            className="drawer-form"
            onSubmit={async (event) => {
              event.preventDefault();
              const form = new FormData(event.currentTarget);
              const payload = { name: text(form, "name"), unit: text(form, "unit"), minimumQuantity: toDecimal(form.get("minimum")), isActive: editingInput === "new" ? true : editingInput.isActive };
              const done = await action.run(() => (editingInput === "new" ? http.post(`${base}/stock-items`, payload) : http.put(`/stock-items/${editingInput.id}`, payload)));
              if (done) setEditingInput(null);
            }}
          >
            <label className="field"><span>Nome *</span><input defaultValue={editingInput === "new" ? "" : editingInput.name} name="name" required /></label>
            <div className="field-row">
              <label className="field"><span>Unidade *</span><input defaultValue={editingInput === "new" ? "" : editingInput.unit} name="unit" placeholder="un, kg, L, g" required /></label>
              <label className="field"><span>Estoque mínimo</span><input defaultValue={editingInput === "new" ? "" : String(editingInput.minimumQuantity).replace(".", ",")} inputMode="decimal" name="minimum" /></label>
            </div>
            <p className="muted">Saldo, contagem e entradas ficam em Estoque. Compras com nota entram pela NF-e (XML).</p>
            <div className="drawer-foot">
              <button className="secondary-button" onClick={() => setEditingInput(null)} type="button">Cancelar</button>
              <button className="primary-button" disabled={action.busy} type="submit">Salvar</button>
            </div>
          </form>
        </FormDrawer>
      ) : null}
    </section>
  );
}

/* ------------------------------------------------------------------------------------------------ */

function ProductDrawer({
  product,
  menu,
  stock,
  fiscal,
  hasFiscal,
  hasStock,
  lastPurchase,
  http,
  base,
  busy,
  error,
  run,
  onClose,
}: {
  product: Row | null;
  menu: PublicMenu | null;
  stock: StockItem[];
  fiscal: FiscalProduct | null;
  hasFiscal: boolean;
  hasStock: boolean;
  lastPurchase: Map<string, { supplier: string; cost: number; on: string }>;
  http: AdminHttp;
  base: string;
  busy: boolean;
  error: string | null;
  run: (action: () => Promise<unknown>) => Promise<boolean>;
  onClose: () => void;
}) {
  const [tab, setTab] = useState<"general" | "price" | "fiscal" | "recipe" | "supply">("general");
  const [imageUrl, setImageUrl] = useState(product?.imageUrl ?? "");
  const [price, setPrice] = useState(product ? money(product.priceInCents) : "");
  const [recipe, setRecipe] = useState<RecipeLine[]>([]);
  const [recipeLoaded, setRecipeLoaded] = useState(false);
  const [pick, setPick] = useState("");
  const categories = menu?.categories ?? [];

  useEffect(() => {
    if (product && hasStock) {
      http.get<RecipeLine[]>(`/items/${product.id}/recipe`).then((lines) => {
        setRecipe(lines);
        setRecipeLoaded(true);
      }).catch(() => setRecipeLoaded(true));
    }
  }, [hasStock, http, product]);

  const costOf = (line: RecipeLine) => (stock.find((item) => item.id === line.stockItemId)?.averageCostInCents ?? 0) * line.quantity;
  const cost = Math.round(recipe.reduce((sum, line) => sum + costOf(line), 0));
  const priceCents = toCents(price);
  const margin = priceCents > 0 && cost > 0 ? ((priceCents - cost) / priceCents) * 100 : null;

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const compare = text(form, "compareAt");
    const payload = {
      categoryId: String(form.get("categoryId")),
      name: text(form, "name"),
      description: text(form, "description"),
      imageUrl: imageUrl || null,
      priceInCents: toCents(form.get("price")),
      sortOrder: product?.sortOrder ?? 0,
      isAvailable: form.get("isAvailable") === "on",
      optionGroups: product ? null : [],
      compareAtPriceInCents: compare ? toCents(compare) : null,
      sku: text(form, "sku"),
      barcode: text(form, "barcode"),
      unit: text(form, "unit"),
    };

    const done = await run(async () => {
      const saved = product ? await http.put<MenuItem>(`/items/${product.id}`, payload) : await http.post<MenuItem>(`${base}/items`, payload);

      if (hasStock && recipeLoaded) {
        await http.put(`/items/${saved.id}/recipe`, recipe.filter((line) => line.quantity > 0));
      }

      if (hasFiscal && text(form, "ncm")) {
        await http.put(`${base}/fiscal/products/${saved.id}`, {
          ncm: text(form, "ncm"),
          cfop: text(form, "cfop"),
          cest: text(form, "cest"),
          icmsCst: String(form.get("icmsCst")),
          icmsRate: toDecimal(form.get("icmsRate")),
          pisCofinsCst: String(form.get("pisCofinsCst")),
          pisRate: toDecimal(form.get("pisRate")),
          cofinsRate: toDecimal(form.get("cofinsRate")),
        });
      }
    });

    if (done) onClose();
  }

  const tabs = [
    ["general", "Geral"],
    ["price", "Preço e margem"],
    ...(hasFiscal ? [["fiscal", "Fiscal"]] : []),
    ...(hasStock ? [["recipe", "Ficha técnica"], ["supply", "Compras e estoque"]] : []),
  ] as Array<[typeof tab, string]>;

  return (
    <FormDrawer onClose={onClose} subtitle={product ? product.categoryName : "Novo produto"} title={product ? product.name : "Novo produto"} wide>
      <div className="drawer-tabs">
        {tabs.map(([value, label]) => (
          <button className={tab === value ? "is-active" : ""} key={value} onClick={() => setTab(value)} type="button">{label}</button>
        ))}
      </div>
      <form className="drawer-form" onSubmit={save}>
        <div hidden={tab !== "general"}>
          <div className="drawer-form">
            <label className="field"><span>Nome *</span><input defaultValue={product?.name ?? ""} maxLength={120} name="name" required /></label>
            <div className="field-row">
              <label className="field">
                <span>Categoria *</span>
                <select defaultValue={product?.categoryId ?? categories[0]?.id} name="categoryId" required>
                  {categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}
                </select>
              </label>
              <label className="field"><span>Unidade</span><input defaultValue={product?.unit ?? "UN"} maxLength={6} name="unit" /></label>
            </div>
            <div className="field-row">
              <label className="field"><span>Código interno (SKU)</span><input defaultValue={product?.sku ?? ""} maxLength={30} name="sku" /></label>
              <label className="field"><span>Código de barras (EAN)</span><input defaultValue={product?.barcode ?? ""} inputMode="numeric" maxLength={14} name="barcode" placeholder="leitor no PDV" /></label>
            </div>
            <label className="field"><span>Descrição</span><textarea defaultValue={product?.description ?? ""} maxLength={500} name="description" /></label>
            <ImageUpload hint="Foto quadrada ou deitada, com boa luz, vende mais." label="Foto" onChange={setImageUrl} value={imageUrl} />
            <label className="module-check"><input defaultChecked={product?.isAvailable ?? true} name="isAvailable" type="checkbox" /> <span>Disponível para venda</span></label>
          </div>
        </div>

        <div hidden={tab !== "price"}>
          <div className="drawer-form">
            <div className="field-row">
              <label className="field"><span>Preço de venda (R$) *</span><input inputMode="decimal" name="price" onChange={(event) => setPrice(event.target.value)} required value={price} /></label>
              <label className="field"><span>Promoção: preço &quot;de&quot; (R$)</span><input defaultValue={product?.compareAtPriceInCents ? money(product.compareAtPriceInCents) : ""} inputMode="decimal" name="compareAt" placeholder="vazio = sem promoção" /></label>
            </div>
            <div className="kpi-strip">
              <div className="kpi"><small>Custo pela ficha técnica</small><strong>{cost ? formatBRL(cost) : "—"}</strong></div>
              <div className={`kpi ${margin !== null && margin < 30 ? "is-warn" : margin !== null ? "is-ok" : ""}`}><small>Margem bruta</small><strong>{margin === null ? "—" : `${margin.toFixed(1).replace(".", ",")}%`}</strong></div>
              <div className="kpi"><small>Lucro por unidade</small><strong>{cost ? formatBRL(priceCents - cost) : "—"}</strong></div>
            </div>
            {!cost ? <p className="muted">Monte a ficha técnica e lance entradas com custo para ver a margem.</p> : null}
          </div>
        </div>

        {hasFiscal ? (
          <div hidden={tab !== "fiscal"}>
            <div className="drawer-form">
              <p className="muted">Preencha com o contador. Sem estes dados o produto não entra em nota.</p>
              <div className="field-row">
                <label className="field"><span>NCM *</span><input defaultValue={fiscal?.ncm ?? ""} inputMode="numeric" maxLength={8} name="ncm" /></label>
                <label className="field"><span>CFOP *</span><input defaultValue={fiscal?.cfop ?? "5102"} inputMode="numeric" maxLength={4} name="cfop" /></label>
              </div>
              <div className="field-row">
                <label className="field"><span>CEST</span><input defaultValue={fiscal?.cest ?? ""} inputMode="numeric" maxLength={7} name="cest" /></label>
                <label className="field">
                  <span>CST ICMS</span>
                  <select defaultValue={fiscal?.icmsCst ?? "00"} name="icmsCst">
                    <option value="00">00 · Tributada integralmente</option>
                    <option value="40">40 · Isenta</option>
                    <option value="41">41 · Não tributada</option>
                    <option value="60">60 · ICMS cobrado por ST</option>
                  </select>
                </label>
              </div>
              <div className="field-row">
                <label className="field"><span>Alíquota ICMS (%)</span><input defaultValue={fiscal?.icmsRate ?? ""} inputMode="decimal" name="icmsRate" /></label>
                <label className="field">
                  <span>CST PIS/COFINS</span>
                  <select defaultValue={fiscal?.pisCofinsCst ?? "01"} name="pisCofinsCst">
                    {["01", "02", "04", "06", "07", "08", "09"].map((code) => <option key={code} value={code}>{code}</option>)}
                  </select>
                </label>
              </div>
              <div className="field-row">
                <label className="field"><span>Alíquota PIS (%)</span><input defaultValue={fiscal?.pisRate ?? "0,65"} inputMode="decimal" name="pisRate" /></label>
                <label className="field"><span>Alíquota COFINS (%)</span><input defaultValue={fiscal?.cofinsRate ?? "3"} inputMode="decimal" name="cofinsRate" /></label>
              </div>
            </div>
          </div>
        ) : null}

        {hasStock ? (
          <div hidden={tab !== "recipe"}>
            <div className="drawer-form">
              <p className="muted">O que cada unidade vendida consome. A baixa acontece sozinha quando o pedido é confirmado.</p>
              <ul className="recipe-list">
                {recipe.length === 0 ? <li className="muted">Nenhum insumo ligado.</li> : null}
                {recipe.map((line) => {
                  const item = stock.find((candidate) => candidate.id === line.stockItemId);
                  return (
                    <li key={line.stockItemId}>
                      <strong>{item?.name ?? "Insumo removido"}</strong>
                      <input
                        aria-label="Quantidade"
                        inputMode="decimal"
                        onChange={(event) => setRecipe((current) => current.map((entry) => (entry.stockItemId === line.stockItemId ? { ...entry, quantity: toDecimal(event.target.value) } : entry)))}
                        defaultValue={String(line.quantity).replace(".", ",")}
                      />
                      <span>{item?.unit}</span>
                      <span className="muted">{formatBRL(Math.round(costOf(line)))}</span>
                      <button className="dt-icon is-danger" onClick={() => setRecipe((current) => current.filter((entry) => entry.stockItemId !== line.stockItemId))} type="button">×</button>
                    </li>
                  );
                })}
              </ul>
              <div className="field-row">
                <select onChange={(event) => setPick(event.target.value)} value={pick}>
                  <option value="">Adicionar insumo…</option>
                  {stock.filter((item) => item.isActive && !recipe.some((line) => line.stockItemId === item.id)).map((item) => (
                    <option key={item.id} value={item.id}>{item.name} ({item.unit})</option>
                  ))}
                </select>
                <button className="secondary-button" disabled={!pick} onClick={() => { setRecipe((current) => [...current, { stockItemId: pick, quantity: 1 }]); setPick(""); }} type="button">Adicionar</button>
              </div>
            </div>
          </div>
        ) : null}

        {hasStock ? (
          <div hidden={tab !== "supply"}>
            <div className="drawer-form">
              {recipe.length === 0 ? <p className="muted">Ligue insumos na ficha técnica para ver saldo e compras.</p> : null}
              <ul className="recipe-list">
                {recipe.map((line) => {
                  const item = stock.find((candidate) => candidate.id === line.stockItemId);
                  const last = lastPurchase.get(line.stockItemId);
                  const servings = item && line.quantity > 0 ? Math.floor(item.quantityOnHand / line.quantity) : null;
                  return (
                    <li key={line.stockItemId}>
                      <strong>{item?.name}</strong>
                      <span>{item ? `${quantity(item.quantityOnHand)} ${item.unit}` : "—"}</span>
                      <span className="muted">{servings !== null ? `rende ${Math.max(0, servings)} un.` : ""}</span>
                      <span className="muted">{last ? `${last.supplier} · ${formatBRL(last.cost)}` : "sem compra"}</span>
                    </li>
                  );
                })}
              </ul>
            </div>
          </div>
        ) : null}

        <ErrorMessage message={error} />
        <div className="drawer-foot">
          <button className="secondary-button" onClick={onClose} type="button">Cancelar</button>
          <button className="primary-button" disabled={busy || categories.length === 0} type="submit">Salvar produto</button>
        </div>
      </form>
    </FormDrawer>
  );
}
