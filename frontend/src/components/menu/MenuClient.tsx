"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { api, formatBRL } from "@/lib/api";
import {
  cartCount,
  cartLineKey,
  cartSubtotal,
  type CartLine,
  type CartOption,
} from "@/lib/cart";
import type { CheckoutOptions, MenuCategory, MenuItem, OrderTracking, PublicMenu } from "@/lib/types";
import { CartDrawer } from "./CartDrawer";
import { CheckoutModal } from "./CheckoutModal";
import { ItemModal } from "./ItemModal";

interface MenuClientProps {
  menu: PublicMenu;
}

type MenuFilter = "all" | "promo" | "combo" | "available";
type MenuSort = "default" | "cheap" | "expensive";

const filterLabels: Record<MenuFilter, string> = {
  all: "Tudo",
  promo: "Promoções",
  combo: "Combos",
  available: "Disponíveis agora",
};

function plain(value: string) {
  return value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
}

function isPromo(item: MenuItem) {
  return (item.compareAtPriceInCents ?? 0) > item.priceInCents;
}

function isCombo(item: MenuItem) {
  return (item.comboParts?.length ?? 0) > 0;
}

function formatOpening(value: string) {
  const date = new Date(value);
  const today = new Date();
  const tomorrow = new Date(today);
  tomorrow.setDate(today.getDate() + 1);
  const time = date.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });

  if (date.toDateString() === today.toDateString()) {
    return `hoje às ${time}`;
  }

  if (date.toDateString() === tomorrow.toDateString()) {
    return `amanhã às ${time}`;
  }

  return `${date.toLocaleDateString("pt-BR", { weekday: "long" })} às ${time}`;
}

export function MenuClient({ menu }: MenuClientProps) {
  const { establishment } = menu;
  const [cart, setCart] = useState<CartLine[]>([]);
  const [selectedItem, setSelectedItem] = useState<MenuItem | null>(null);
  const [cartOpen, setCartOpen] = useState(false);
  const [checkoutOpen, setCheckoutOpen] = useState(false);
  const [createdOrder, setCreatedOrder] = useState<OrderTracking | null>(null);
  const [options, setOptions] = useState<CheckoutOptions | null>(null);

  const count = cartCount(cart);
  const subtotal = cartSubtotal(cart);
  const hasOverlay = selectedItem !== null || cartOpen || checkoutOpen || createdOrder !== null;

  useEffect(() => {
    document.body.style.overflow = hasOverlay ? "hidden" : "";
    return () => {
      document.body.style.overflow = "";
    };
  }, [hasOverlay]);

  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<MenuFilter>("all");
  const [sort, setSort] = useState<MenuSort>("default");
  const [hoursOpen, setHoursOpen] = useState(false);
  const [tableLabel, setTableLabel] = useState<string | null>(null);

  // Smart table: the QR Code on the table opens /menu/loja?mesa=7.
  useEffect(() => {
    const table = new URLSearchParams(window.location.search).get("mesa")?.trim();
    setTableLabel(table ? table.slice(0, 20) : null);
  }, []);

  const allCategories = useMemo(
    () => menu.categories.filter((category) => category.items.length > 0 || category.isActive),
    [menu.categories],
  );

  const availableFilters = useMemo(() => {
    const items = allCategories.flatMap((category) => category.items);
    return (Object.keys(filterLabels) as MenuFilter[]).filter(
      (key) => (key !== "promo" || items.some(isPromo)) && (key !== "combo" || items.some(isCombo)),
    );
  }, [allCategories]);

  const searching = search.trim().length > 0 || filter !== "all" || sort !== "default";

  const categories = useMemo<MenuCategory[]>(() => {
    const query = plain(search.trim());
    const words = query.split(/\s+/).filter(Boolean);

    const matches = (item: MenuItem) =>
      words.every((word) => plain(`${item.name} ${item.description ?? ""} ${(item.comboParts ?? []).map((part) => part.name).join(" ")}`).includes(word)) &&
      (filter !== "promo" || isPromo(item)) &&
      (filter !== "combo" || isCombo(item)) &&
      (filter !== "available" || item.isAvailable);

    const ordered = (items: MenuItem[]) =>
      sort === "default"
        ? items
        : [...items].sort((a, b) => (sort === "cheap" ? a.priceInCents - b.priceInCents : b.priceInCents - a.priceInCents));

    return allCategories
      .map((category) => ({ ...category, items: ordered(category.items.filter(matches)) }))
      .filter((category) => !searching || category.items.length > 0);
  }, [allCategories, filter, search, searching, sort]);

  const closed = establishment.isOpenNow === false;

  useEffect(() => {
    api.getCheckoutOptions(establishment.slug).then(setOptions).catch(() => setOptions(null));
  }, [establishment.slug]);

  function addToCart(item: MenuItem, quantity: number, notes: string, options: CartOption[]) {
    const optionIds = options.map((selected) => selected.option.id);
    const key = `${cartLineKey(item.id, optionIds)}:${notes}`;

    setCart((current) => {
      const existing = current.find((line) => line.key === key);

      if (existing) {
        return current.map((line) =>
          line.key === key ? { ...line, quantity: Math.min(99, line.quantity + quantity) } : line,
        );
      }

      return [...current, { key, item, quantity, notes, options }];
    });

    setSelectedItem(null);
  }

  function updateQuantity(key: string, quantity: number) {
    if (quantity <= 0) {
      setCart((current) => current.filter((line) => line.key !== key));
      return;
    }

    setCart((current) =>
      current.map((line) => (line.key === key ? { ...line, quantity: Math.min(99, quantity) } : line)),
    );
  }

  function openCheckout() {
    setCartOpen(false);
    setCheckoutOpen(true);
  }

  function handleOrderCreated(order: OrderTracking) {
    setCheckoutOpen(false);
    setCart([]);
    setCreatedOrder(order);
  }

  return (
    <div className="menu-shell">
      <header className="store-hero">
        {establishment.coverUrl ? (
          <img alt="" className="store-cover" src={establishment.coverUrl} />
        ) : (
          <div className="store-cover store-cover-fallback" />
        )}
        <div className="store-hero-content">
          <div className="store-logo">
            {establishment.logoUrl ? (
              <img alt={establishment.name} src={establishment.logoUrl} />
            ) : (
              <span>{establishment.name.slice(0, 1).toUpperCase()}</span>
            )}
          </div>

          <div className="store-title">
            <button className={`store-status ${closed ? "is-closed" : ""}`} onClick={() => setHoursOpen((value) => !value)} type="button">
              <span className="status-dot" />{" "}
              {!establishment.isActive
                ? "Fechado"
                : closed
                  ? establishment.nextOpeningAt
                    ? `Fechado agora · abre ${formatOpening(establishment.nextOpeningAt)}`
                    : "Fechado agora"
                  : "Aberto · recebendo pedidos"}
            </button>
            {hoursOpen && establishment.hoursText ? <pre className="store-hours">{establishment.hoursText}</pre> : null}
            <h1>{establishment.name}</h1>
            {establishment.description ? <p>{establishment.description}</p> : null}
            <div className="store-meta">
              {establishment.isDeliveryEnabled ? (
                <span>{options?.hasDeliveryZones ? "Entrega com taxa por bairro" : `Entrega ${formatBRL(establishment.deliveryFeeInCents)}`}</span>
              ) : null}
              {establishment.isPickupEnabled ? <span>Retirada grátis</span> : null}
              {establishment.minimumOrderInCents > 0 ? (
                <span>Mínimo {formatBRL(establishment.minimumOrderInCents)}</span>
              ) : null}
            </div>
          </div>
        </div>
      </header>

      {tableLabel ? <div className="menu-table-banner">🍽️ Você está na <strong>mesa {tableLabel}</strong>. Peça por aqui e receba na mesa.</div> : null}

      {closed && establishment.isActive ? (
        <div className="menu-closed-banner">
          {establishment.acceptsOrdersWhenClosed === false
            ? "A loja está fechada e não recebe pedidos agora. Veja o horário acima."
            : `A loja está fechada. Você pode pedir agora: o pedido fica agendado${establishment.nextOpeningAt ? ` para ${formatOpening(establishment.nextOpeningAt)}` : ""} e a loja confirma quando abrir.`}
        </div>
      ) : null}

      <div className="menu-tools">
        <label className="menu-search">
          <span aria-hidden="true">⌕</span>
          <input
            aria-label="Buscar no cardápio"
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Buscar no cardápio"
            type="search"
            value={search}
          />
        </label>
        <div className="menu-filters" role="group" aria-label="Filtros">
          {availableFilters.map((key) => (
            <button
              aria-pressed={filter === key}
              className={`menu-filter ${filter === key ? "is-active" : ""}`}
              key={key}
              onClick={() => setFilter(key)}
              type="button"
            >
              {filterLabels[key]}
            </button>
          ))}
          <select aria-label="Ordenar" className="menu-sort" onChange={(event) => setSort(event.target.value as MenuSort)} value={sort}>
            <option value="default">Ordem da loja</option>
            <option value="cheap">Menor preço</option>
            <option value="expensive">Maior preço</option>
          </select>
        </div>
      </div>

      <nav className="category-nav" aria-label="Categorias do cardápio">
        <div className="category-nav-inner">
          {categories.map((category) => (
            <a href={`#categoria-${category.id}`} key={category.id}>
              {category.name}
            </a>
          ))}
        </div>
      </nav>

      <main className="menu-main">
        {categories.length === 0 && searching ? (
          <div className="empty-state card">
            <h2>Nada encontrado</h2>
            <p className="muted">Tente outra palavra ou limpe os filtros.</p>
            <button
              className="secondary-button"
              onClick={() => {
                setSearch("");
                setFilter("all");
                setSort("default");
              }}
              type="button"
            >
              Limpar busca
            </button>
          </div>
        ) : categories.length === 0 ? (
          <div className="empty-state card">
            <h2>Cardápio em preparação</h2>
            <p className="muted">O estabelecimento ainda não publicou itens.</p>
          </div>
        ) : (
          categories.map((category) => (
            <section className="category-section" id={`categoria-${category.id}`} key={category.id}>
              <div className="category-heading">
                <div>
                  <h2>{category.name}</h2>
                  {category.description ? <p className="muted">{category.description}</p> : null}
                </div>
                <span className="pill">{category.items.length} itens</span>
              </div>

              <div className="items-grid">
                {category.items.map((item) => (
                  <article
                    className={`item-card ${item.isAvailable ? "" : "is-unavailable"}`}
                    key={item.id}
                    onClick={() => {
                      if (item.isAvailable) {
                        setSelectedItem(item);
                      }
                    }}
                  >
                    {item.imageUrl ? <img alt="" aria-hidden="true" className="item-card-glow" src={item.imageUrl} /> : null}
                    <div className="item-card-image">
                      {item.imageUrl ? (
                        <img alt={item.name} src={item.imageUrl} />
                      ) : (
                        <span>{item.name.slice(0, 1).toUpperCase()}</span>
                      )}
                      {!item.isAvailable ? <em>Indisponível</em> : isPromo(item) ? <em className="is-promo">Promoção</em> : isCombo(item) ? <em className="is-combo">Combo</em> : null}
                      <button
                        aria-label={`${item.optionGroups.length > 0 ? "Personalizar" : "Adicionar"} ${item.name}`}
                        className="add-button"
                        disabled={!item.isAvailable}
                        onClick={(event) => {
                          event.stopPropagation();

                          if (item.optionGroups.length > 0) {
                            setSelectedItem(item);
                            return;
                          }

                          addToCart(item, 1, "", []);
                        }}
                        type="button"
                      >
                        +
                      </button>
                    </div>

                    <div className="item-card-body">
                      <div>
                        <h3>{item.name}</h3>
                        {item.description ? <p className="muted">{item.description}</p> : null}
                        {isCombo(item) ? (
                          <p className="item-combo-parts">
                            {item.comboParts!.map((part) => `${part.quantity}× ${part.name}`).join(" + ")}
                          </p>
                        ) : null}
                      </div>

                      <div className="item-card-footer">
                        {isPromo(item) ? <s className="item-price-before">{formatBRL(item.compareAtPriceInCents!)}</s> : null}
                        <strong>{formatBRL(item.priceInCents)}</strong>
                        {item.comboSavingsInCents ? <span className="item-saving">economize {formatBRL(item.comboSavingsInCents)}</span> : null}
                      </div>
                    </div>
                  </article>
                ))}
              </div>
            </section>
          ))
        )}
      </main>

      {count > 0 ? (
        <button className="floating-cart" onClick={() => setCartOpen(true)} type="button">
          <span className="floating-cart-count">{count}</span>
          <span>Ver carrinho</span>
          <strong>{formatBRL(subtotal)}</strong>
        </button>
      ) : null}

      {selectedItem ? (
        <ItemModal
          item={selectedItem}
          onAdd={(quantity, notes, options) => addToCart(selectedItem, quantity, notes, options)}
          onClose={() => setSelectedItem(null)}
        />
      ) : null}

      {cartOpen ? (
        <CartDrawer
          establishment={establishment}
          lines={cart}
          onCheckout={openCheckout}
          onClose={() => setCartOpen(false)}
          onRemove={(key) => setCart((current) => current.filter((line) => line.key !== key))}
          onUpdateQuantity={updateQuantity}
        />
      ) : null}

      {checkoutOpen ? (
        <CheckoutModal
          tableLabel={tableLabel}
          establishment={establishment}
          lines={cart}
          onClose={() => setCheckoutOpen(false)}
          onSuccess={handleOrderCreated}
        />
      ) : null}

      {createdOrder ? (
        <div className="modal-backdrop" role="presentation">
          <section aria-modal="true" className="modal success-modal" role="dialog">
            <div className="success-icon">✓</div>
            <p className="eyebrow">Pedido confirmado</p>
            <h2>Seu pedido foi enviado!</h2>
            <p className="muted">
              Acompanhe o preparo em tempo real usando o código abaixo.
            </p>
            <strong className="order-code">{createdOrder.code}</strong>
            <div className="success-actions">
              <Link className="primary-button" href={`/pedido/${createdOrder.code}`}>
                Acompanhar pedido
              </Link>
              <button className="secondary-button" onClick={() => setCreatedOrder(null)} type="button">
                Voltar ao cardápio
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </div>
  );
}
