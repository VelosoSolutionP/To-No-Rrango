"use client";

import { formatBRL } from "@/lib/api";
import { cartCount, cartLineTotal, cartLineUnitPrice, cartSubtotal, type CartLine } from "@/lib/cart";
import type { Establishment } from "@/lib/types";

interface CartDrawerProps {
  establishment: Establishment;
  lines: CartLine[];
  onCheckout: () => void;
  onClose: () => void;
  onRemove: (key: string) => void;
  onUpdateQuantity: (key: string, quantity: number) => void;
}

export function CartDrawer({
  establishment,
  lines,
  onCheckout,
  onClose,
  onRemove,
  onUpdateQuantity,
}: CartDrawerProps) {
  const subtotal = cartSubtotal(lines);
  const count = cartCount(lines);
  const belowMinimum = subtotal < establishment.minimumOrderInCents;

  return (
    <div className="drawer-backdrop" role="presentation" onMouseDown={onClose}>
      <aside
        aria-label="Carrinho"
        className="cart-drawer"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header className="drawer-header">
          <div>
            <p className="eyebrow">Seu pedido</p>
            <h2>
              Carrinho <span className="pill">{count}</span>
            </h2>
          </div>
          <button aria-label="Fechar carrinho" className="modal-close" onClick={onClose} type="button">
            ×
          </button>
        </header>

        {lines.length === 0 ? (
          <div className="empty-state">
            <span className="empty-icon">🛒</span>
            <h3>Seu carrinho está vazio</h3>
            <p className="muted">Escolha seus itens favoritos no cardápio e monte o pedido.</p>
            <button className="secondary-button" onClick={onClose} type="button">
              Ver cardápio
            </button>
          </div>
        ) : (
          <>
            <div className="cart-lines">
              {lines.map((line) => (
                <article className="cart-line" key={line.key}>
                  <div className="cart-line-main">
                    <div>
                      <h3>{line.item.name}</h3>
                      {line.options.length > 0 ? (
                        <p className="cart-line-options">
                          {line.options
                            .map((selected) => `${selected.groupName}: ${selected.option.name}`)
                            .join(" · ")}
                        </p>
                      ) : null}
                      {line.notes ? <p className="cart-line-notes">“{line.notes}”</p> : null}
                      <p className="cart-line-unit">{formatBRL(cartLineUnitPrice(line))} / un.</p>
                    </div>
                    <strong>{formatBRL(cartLineTotal(line))}</strong>
                  </div>

                  <div className="cart-line-actions">
                    <div className="quantity-control quantity-control-small">
                      <button
                        aria-label={`Diminuir ${line.item.name}`}
                        onClick={() => onUpdateQuantity(line.key, line.quantity - 1)}
                        type="button"
                      >
                        −
                      </button>
                      <strong>{line.quantity}</strong>
                      <button
                        aria-label={`Aumentar ${line.item.name}`}
                        onClick={() => onUpdateQuantity(line.key, line.quantity + 1)}
                        type="button"
                      >
                        +
                      </button>
                    </div>
                    <button className="link-button" onClick={() => onRemove(line.key)} type="button">
                      Remover
                    </button>
                  </div>
                </article>
              ))}
            </div>

            <footer className="drawer-footer">
              <div className="summary-row">
                <span>Subtotal</span>
                <strong>{formatBRL(subtotal)}</strong>
              </div>
              <div className="summary-row muted">
                <span>Taxa de entrega</span>
                <span>
                  {establishment.isDeliveryEnabled
                    ? `a partir de ${formatBRL(establishment.deliveryFeeInCents)}`
                    : "somente retirada"}
                </span>
              </div>

              {belowMinimum ? (
                <p className="minimum-warning">
                  Faltam {formatBRL(establishment.minimumOrderInCents - subtotal)} para atingir o
                  pedido mínimo.
                </p>
              ) : null}

              <button
                className="primary-button primary-button-full"
                disabled={belowMinimum}
                onClick={onCheckout}
                type="button"
              >
                Finalizar pedido
              </button>
            </footer>
          </>
        )}
      </aside>
    </div>
  );
}
