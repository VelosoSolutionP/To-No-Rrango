"use client";

import { useEffect, useMemo, useState } from "react";
import type { MenuItem, MenuOption, MenuOptionGroup } from "@/lib/types";
import { formatBRL } from "@/lib/api";
import type { CartOption } from "@/lib/cart";

interface ItemModalProps {
  item: MenuItem;
  onClose: () => void;
  onAdd: (quantity: number, notes: string, options: CartOption[]) => void;
}

function defaultSelections(groups: MenuOptionGroup[]): Record<string, string[]> {
  return Object.fromEntries(groups.map((group) => [group.id, []]));
}

export function ItemModal({ item, onClose, onAdd }: ItemModalProps) {
  const [selected, setSelected] = useState<Record<string, string[]>>(() =>
    defaultSelections(item.optionGroups),
  );
  const [quantity, setQuantity] = useState(1);
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const selectedOptions = useMemo<CartOption[]>(() => {
    return item.optionGroups.flatMap((group) =>
      group.options
        .filter((option) => selected[group.id]?.includes(option.id))
        .map((option) => ({ groupName: group.name, option })),
    );
  }, [item.optionGroups, selected]);

  const unitPrice =
    item.priceInCents +
    selectedOptions.reduce((total, selectedOption) => total + selectedOption.option.priceInCents, 0);

  function toggleOption(group: MenuOptionGroup, option: MenuOption) {
    if (!option.isAvailable) {
      return;
    }

    setError(null);
    setSelected((current) => {
      const chosen = current[group.id] ?? [];

      if (group.maxSelections === 1) {
        return { ...current, [group.id]: chosen.includes(option.id) ? [] : [option.id] };
      }

      if (chosen.includes(option.id)) {
        return { ...current, [group.id]: chosen.filter((id) => id !== option.id) };
      }

      if (chosen.length >= group.maxSelections) {
        setError(`Escolha no máximo ${group.maxSelections} opções em "${group.name}".`);
        return current;
      }

      return { ...current, [group.id]: [...chosen, option.id] };
    });
  }

  function handleAdd() {
    for (const group of item.optionGroups) {
      const count = selected[group.id]?.length ?? 0;

      if (count < group.minSelections) {
        setError(`Escolha pelo menos ${group.minSelections} opção(ões) em "${group.name}".`);
        return;
      }

      if (count > group.maxSelections) {
        setError(`Escolha no máximo ${group.maxSelections} opção(ões) em "${group.name}".`);
        return;
      }
    }

    onAdd(quantity, notes.trim(), selectedOptions);
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section
        aria-modal="true"
        className="modal item-modal"
        role="dialog"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <button aria-label="Fechar" className="modal-close" onClick={onClose} type="button">
          ×
        </button>

        {item.imageUrl ? <img alt="" aria-hidden="true" className="item-card-glow" src={item.imageUrl} /> : null}

        {item.imageUrl ? (
          <img alt={item.name} className="item-modal-image" src={item.imageUrl} />
        ) : (
          <div className="item-modal-image item-modal-image-placeholder">{item.name.slice(0, 1)}</div>
        )}

        <div className="item-modal-body">
          <div>
            <p className="eyebrow">Personalize seu pedido</p>
            <h2>{item.name}</h2>
            {item.description ? <p className="muted">{item.description}</p> : null}
            {item.comboParts && item.comboParts.length > 0 ? (
              <ul className="item-combo-list">
                {item.comboParts.map((part) => (
                  <li key={part.menuItemId}>
                    {part.quantity}× {part.name}
                  </li>
                ))}
              </ul>
            ) : null}
            <p className="item-price">
              {(item.compareAtPriceInCents ?? 0) > item.priceInCents ? (
                <s className="item-price-before">{formatBRL(item.compareAtPriceInCents!)}</s>
              ) : null}
              {formatBRL(item.priceInCents)}
              {item.comboSavingsInCents ? <span className="item-saving">economize {formatBRL(item.comboSavingsInCents)}</span> : null}
            </p>
          </div>

          {item.optionGroups.map((group) => (
            <fieldset className="option-group" key={group.id}>
              <legend>
                <span>{group.name}</span>
                <small>
                  {group.isRequired ? "Obrigatório" : "Opcional"} ·{" "}
                  {group.minSelections === group.maxSelections
                    ? `escolha ${group.maxSelections}`
                    : `até ${group.maxSelections}`}
                </small>
              </legend>

              <div className="option-list">
                {group.options.map((option) => {
                  const checked = selected[group.id]?.includes(option.id) ?? false;
                  const inputType = group.maxSelections === 1 ? "radio" : "checkbox";

                  return (
                    <label
                      className={`option-row ${checked ? "is-checked" : ""} ${
                        option.isAvailable ? "" : "is-disabled"
                      }`}
                      key={option.id}
                    >
                      <input
                        checked={checked}
                        disabled={!option.isAvailable}
                        name={group.id}
                        onChange={() => toggleOption(group, option)}
                        type={inputType}
                      />
                      <span>{option.name}</span>
                      <strong>
                        {option.priceInCents > 0 ? `+ ${formatBRL(option.priceInCents)}` : "Grátis"}
                      </strong>
                    </label>
                  );
                })}
              </div>
            </fieldset>
          ))}

          <label className="field">
            <span>Observações para a cozinha</span>
            <textarea
              maxLength={500}
              onChange={(event) => setNotes(event.target.value)}
              placeholder="Ex.: sem cebola, ponto da carne, embalagem separada..."
              rows={3}
              value={notes}
            />
          </label>

          {error ? <p className="form-error">{error}</p> : null}

          <div className="item-modal-actions">
            <div className="quantity-control" aria-label="Quantidade">
              <button
                aria-label="Diminuir quantidade"
                onClick={() => setQuantity((current) => Math.max(1, current - 1))}
                type="button"
              >
                −
              </button>
              <strong>{quantity}</strong>
              <button
                aria-label="Aumentar quantidade"
                onClick={() => setQuantity((current) => Math.min(99, current + 1))}
                type="button"
              >
                +
              </button>
            </div>

            <button className="primary-button" onClick={handleAdd} type="button">
              Adicionar · {formatBRL(unitPrice * quantity)}
            </button>
          </div>
        </div>
      </section>
    </div>
  );
}
