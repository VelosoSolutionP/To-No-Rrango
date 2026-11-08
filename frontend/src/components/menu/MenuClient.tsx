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
import type { CheckoutOptions, MenuItem, OrderTracking, PublicMenu } from "@/lib/types";
import { CartDrawer } from "./CartDrawer";
import { CheckoutModal } from "./CheckoutModal";
import { ItemModal } from "./ItemModal";

interface MenuClientProps {
  menu: PublicMenu;
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

  const categories = useMemo(
    () => menu.categories.filter((category) => category.items.length > 0 || category.isActive),
    [menu.categories],
  );

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
            <span className="store-status">
              <span className="status-dot" /> {establishment.isActive ? "Recebendo pedidos" : "Fechado"}
            </span>
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
        {categories.length === 0 ? (
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
                      {!item.isAvailable ? <em>Indisponível</em> : null}
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
                      </div>

                      <div className="item-card-footer">
                        <strong>{formatBRL(item.priceInCents)}</strong>
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
