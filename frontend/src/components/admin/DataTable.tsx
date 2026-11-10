"use client";

import { type ReactNode, useMemo, useState } from "react";
import { Icon } from "./Icon";

export interface Column<T> {
  key: string;
  title: string;
  /** Value used for sorting and search; defaults to the rendered text. */
  value?: (row: T) => string | number | null | undefined;
  render?: (row: T) => ReactNode;
  align?: "left" | "right" | "center";
  width?: string;
  hideOnMobile?: boolean;
}

export interface RowAction<T> {
  icon: string;
  label: string;
  onClick: (row: T) => void;
  tone?: "danger" | "accent";
  hidden?: (row: T) => boolean;
}

export interface Filter<T> {
  key: string;
  label: string;
  test: (row: T) => boolean;
}

/**
 * The one table every register uses: search, quick filters, sortable columns, row actions as
 * icons, pagination and a "new" button above. Same look in customers, suppliers, products…
 */
export function DataTable<T>({
  rows,
  columns,
  actions = [],
  filters = [],
  rowKey,
  searchPlaceholder = "Pesquisar…",
  onCreate,
  createLabel = "Novo",
  toolbar,
  empty = "Nada cadastrado ainda.",
  pageSize = 25,
  onRowClick,
  loading,
}: {
  rows: T[];
  columns: Column<T>[];
  actions?: RowAction<T>[];
  filters?: Filter<T>[];
  rowKey: (row: T) => string;
  searchPlaceholder?: string;
  onCreate?: () => void;
  createLabel?: string;
  toolbar?: ReactNode;
  empty?: string;
  pageSize?: number;
  onRowClick?: (row: T) => void;
  loading?: boolean;
}) {
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<string>("all");
  const [sort, setSort] = useState<{ key: string; dir: 1 | -1 } | null>(null);
  const [page, setPage] = useState(0);

  const plain = (value: unknown) =>
    String(value ?? "")
      .normalize("NFD")
      .replace(/[̀-ͯ]/g, "")
      .toLowerCase();

  const filtered = useMemo(() => {
    const words = plain(query).split(/\s+/).filter(Boolean);
    const active = filters.find((item) => item.key === filter);
    let list = rows.filter((row) => (active ? active.test(row) : true));

    if (words.length) {
      list = list.filter((row) => {
        const haystack = columns.map((column) => plain(column.value ? column.value(row) : "")).join(" ");
        return words.every((word) => haystack.includes(word));
      });
    }

    if (sort) {
      const column = columns.find((item) => item.key === sort.key);
      if (column?.value) {
        list = [...list].sort((a, b) => {
          const x = column.value!(a);
          const y = column.value!(b);
          if (typeof x === "number" && typeof y === "number") return (x - y) * sort.dir;
          return String(x ?? "").localeCompare(String(y ?? ""), "pt-BR", { numeric: true }) * sort.dir;
        });
      }
    }

    return list;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows, query, filter, sort, columns, filters]);

  const pages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const current = Math.min(page, pages - 1);
  const shown = filtered.slice(current * pageSize, current * pageSize + pageSize);

  return (
    <div className="dt">
      <div className="dt-bar">
        <label className="dt-search">
          <Icon name="search" size={18} />
          <input
            aria-label="Pesquisar"
            onChange={(event) => {
              setQuery(event.target.value);
              setPage(0);
            }}
            placeholder={searchPlaceholder}
            type="search"
            value={query}
          />
          {query ? <button aria-label="Limpar" onClick={() => setQuery("")} type="button">×</button> : null}
        </label>
        {filters.length ? (
          <div className="dt-filters" role="group">
            <button className={filter === "all" ? "is-active" : ""} onClick={() => setFilter("all")} type="button">
              Todos <b>{rows.length}</b>
            </button>
            {filters.map((item) => (
              <button className={filter === item.key ? "is-active" : ""} key={item.key} onClick={() => { setFilter(item.key); setPage(0); }} type="button">
                {item.label} <b>{rows.filter(item.test).length}</b>
              </button>
            ))}
          </div>
        ) : null}
        <div className="dt-tools">
          {toolbar}
          {onCreate ? (
            <button className="primary-button dt-new" onClick={onCreate} type="button">
              <Icon name="plus" size={18} /> {createLabel}
            </button>
          ) : null}
        </div>
      </div>

      <div className="dt-wrap">
        <table className="dt-table">
          <thead>
            <tr>
              {columns.map((column) => (
                <th
                  aria-sort={sort?.key === column.key ? (sort.dir === 1 ? "ascending" : "descending") : undefined}
                  className={`${column.align ? `is-${column.align}` : ""} ${column.hideOnMobile ? "hide-mobile" : ""} ${column.value ? "is-sortable" : ""}`}
                  key={column.key}
                  onClick={() => column.value && setSort((prev) => (prev?.key === column.key ? { key: column.key, dir: prev.dir === 1 ? -1 : 1 } : { key: column.key, dir: 1 }))}
                  style={column.width ? { width: column.width } : undefined}
                >
                  {column.title}
                  {sort?.key === column.key ? <span className="dt-sort">{sort.dir === 1 ? "▲" : "▼"}</span> : null}
                </th>
              ))}
              {actions.length ? <th className="is-right dt-actions-head">Ações</th> : null}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td className="dt-empty" colSpan={columns.length + 1}>Carregando…</td></tr>
            ) : shown.length === 0 ? (
              <tr><td className="dt-empty" colSpan={columns.length + 1}>{rows.length === 0 ? empty : "Nada encontrado com essa busca."}</td></tr>
            ) : (
              shown.map((row) => (
                <tr className={onRowClick ? "is-clickable" : ""} key={rowKey(row)} onClick={() => onRowClick?.(row)}>
                  {columns.map((column) => (
                    <td className={`${column.align ? `is-${column.align}` : ""} ${column.hideOnMobile ? "hide-mobile" : ""}`} data-title={column.title} key={column.key}>
                      {column.render ? column.render(row) : String(column.value?.(row) ?? "—")}
                    </td>
                  ))}
                  {actions.length ? (
                    <td className="is-right dt-actions">
                      {actions
                        .filter((action) => !action.hidden?.(row))
                        .map((action) => (
                          <button
                            aria-label={action.label}
                            className={`dt-icon ${action.tone ? `is-${action.tone}` : ""}`}
                            data-tip={action.label}
                            key={action.label}
                            onClick={(event) => {
                              event.stopPropagation();
                              action.onClick(row);
                            }}
                            title={action.label}
                            type="button"
                          >
                            <Icon name={action.icon} size={17} />
                          </button>
                        ))}
                    </td>
                  ) : null}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      <div className="dt-foot">
        <span>
          {filtered.length} registro(s){filtered.length !== rows.length ? ` de ${rows.length}` : ""}
        </span>
        {pages > 1 ? (
          <div className="dt-pages">
            <button disabled={current === 0} onClick={() => setPage(current - 1)} type="button">‹</button>
            <span>{current + 1} / {pages}</span>
            <button disabled={current >= pages - 1} onClick={() => setPage(current + 1)} type="button">›</button>
          </div>
        ) : null}
      </div>
    </div>
  );
}

/** Side panel used by every register to create and edit. */
export function FormDrawer({ title, subtitle, onClose, children, wide }: { title: string; subtitle?: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  return (
    <>
      <button aria-label="Fechar" className="drawer-backdrop" onClick={onClose} type="button" />
      <aside className={`drawer ${wide ? "drawer-wide" : ""}`}>
        <header className="drawer-head">
          <div>
            {subtitle ? <small className="muted">{subtitle}</small> : null}
            <h2>{title}</h2>
          </div>
          <button aria-label="Fechar" className="drawer-close" onClick={onClose} type="button">×</button>
        </header>
        {children}
      </aside>
    </>
  );
}

export function StatusChip({ tone, children }: { tone: "ok" | "warn" | "bad" | "muted" | "info" | "accent"; children: ReactNode }) {
  return <span className={`chip chip-${tone}`}>{children}</span>;
}
