"use client";

import { ImageUpload } from "./ImageUpload";
import { FormEvent, useEffect, useState } from "react";
import type {
  MenuCategory,
  MenuItem,
  MenuItemPayload,
  MenuOptionGroupPayload,
} from "@/lib/types";

interface ItemModalProps {
  item: MenuItem | null;
  categories: MenuCategory[];
  defaultCategoryId: string;
  busy: boolean;
  error: string | null;
  onClose: () => void;
  onSave: (payload: MenuItemPayload) => Promise<void>;
}

interface OptionDraft {
  key: string;
  name: string;
  price: string;
  isAvailable: boolean;
  sortOrder: string;
}

interface GroupDraft {
  key: string;
  name: string;
  minSelections: string;
  maxSelections: string;
  isRequired: boolean;
  sortOrder: string;
  options: OptionDraft[];
}

function createKey(): string {
  return Math.random().toString(36).slice(2, 10);
}

function moneyToInput(cents: number): string {
  return String(cents / 100);
}

function inputToCents(value: string): number {
  const parsed = Number(value.replace(",", "."));
  return Number.isFinite(parsed) ? Math.round(parsed * 100) : Number.NaN;
}

function emptyOption(): OptionDraft {
  return { key: createKey(), name: "", price: "0", isAvailable: true, sortOrder: "0" };
}

function emptyGroup(): GroupDraft {
  return {
    key: createKey(),
    name: "",
    minSelections: "0",
    maxSelections: "1",
    isRequired: false,
    sortOrder: "0",
    options: [emptyOption()],
  };
}

function toDrafts(item: MenuItem | null): GroupDraft[] {
  if (!item) {
    return [];
  }

  return item.optionGroups.map((group) => ({
    key: createKey(),
    name: group.name,
    minSelections: String(group.minSelections),
    maxSelections: String(group.maxSelections),
    isRequired: group.isRequired,
    sortOrder: String(group.sortOrder),
    options: group.options.map((option) => ({
      key: createKey(),
      name: option.name,
      price: moneyToInput(option.priceInCents),
      isAvailable: option.isAvailable,
      sortOrder: String(option.sortOrder),
    })),
  }));
}

export function ItemModal({
  item,
  categories,
  defaultCategoryId,
  busy,
  error,
  onClose,
  onSave,
}: ItemModalProps) {
  const [categoryId, setCategoryId] = useState(item?.categoryId ?? defaultCategoryId);
  const [name, setName] = useState(item?.name ?? "");
  const [description, setDescription] = useState(item?.description ?? "");
  const [imageUrl, setImageUrl] = useState(item?.imageUrl ?? "");
  const [price, setPrice] = useState(moneyToInput(item?.priceInCents ?? 0));
  const [sortOrder, setSortOrder] = useState(String(item?.sortOrder ?? 0));
  const [isAvailable, setIsAvailable] = useState(item?.isAvailable ?? true);
  const [groups, setGroups] = useState<GroupDraft[]>(() => toDrafts(item));

  useEffect(() => {
    setCategoryId(item?.categoryId ?? defaultCategoryId);
    setName(item?.name ?? "");
    setDescription(item?.description ?? "");
    setImageUrl(item?.imageUrl ?? "");
    setPrice(moneyToInput(item?.priceInCents ?? 0));
    setSortOrder(String(item?.sortOrder ?? 0));
    setIsAvailable(item?.isAvailable ?? true);
    setGroups(toDrafts(item));
  }, [defaultCategoryId, item]);

  function updateGroup(key: string, patch: Partial<GroupDraft>) {
    setGroups((current) =>
      current.map((group) => (group.key === key ? { ...group, ...patch } : group)),
    );
  }

  function updateOption(groupKey: string, optionKey: string, patch: Partial<OptionDraft>) {
    setGroups((current) =>
      current.map((group) =>
        group.key === groupKey
          ? {
              ...group,
              options: group.options.map((option) =>
                option.key === optionKey ? { ...option, ...patch } : option,
              ),
            }
          : group,
      ),
    );
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const priceInCents = inputToCents(price);
    if (Number.isNaN(priceInCents)) {
      return;
    }

    const optionGroups: MenuOptionGroupPayload[] = groups
      .filter((group) => group.name.trim())
      .map((group, groupIndex) => {
        let minSelections = Number(group.minSelections) || 0;
        let maxSelections = Number(group.maxSelections) || 0;

        if (group.isRequired && minSelections < 1) {
          minSelections = 1;
        }

        if (maxSelections < minSelections) {
          maxSelections = minSelections;
        }

        return {
          name: group.name.trim(),
          minSelections,
          maxSelections,
          isRequired: group.isRequired,
          sortOrder: Number(group.sortOrder) || groupIndex,
          options: group.options
            .filter((option) => option.name.trim())
            .map((option, optionIndex) => ({
              name: option.name.trim(),
              priceInCents: inputToCents(option.price) || 0,
              isAvailable: option.isAvailable,
              sortOrder: Number(option.sortOrder) || optionIndex,
            })),
        };
      });

    await onSave({
      categoryId,
      name: name.trim(),
      description: description.trim() || null,
      imageUrl: imageUrl.trim() || null,
      priceInCents,
      sortOrder: Number(sortOrder) || 0,
      isAvailable,
      optionGroups,
    });
  }

  return (
    <div
      className="modal-backdrop"
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <section className="modal admin-modal admin-item-modal">
        <button aria-label="Fechar" className="modal-close" onClick={onClose} type="button">
          ×
        </button>
        <p className="eyebrow">{item ? "Editar item" : "Novo item"}</p>
        <h2>{item ? item.name : "Adicione ao cardápio"}</h2>

        <form className="admin-form" onSubmit={handleSubmit}>
          <div className="admin-form-grid">
            <label className="field">
              <span>Categoria</span>
              <select
                onChange={(event) => setCategoryId(event.target.value)}
                required
                value={categoryId}
              >
                {categories.map((category) => (
                  <option key={category.id} value={category.id}>
                    {category.name}
                  </option>
                ))}
              </select>
            </label>

            <label className="field">
              <span>Preço (R$)</span>
              <input
                min={0}
                onChange={(event) => setPrice(event.target.value)}
                required
                step="0.01"
                type="number"
                value={price}
              />
            </label>
          </div>

          <label className="field">
            <span>Nome</span>
            <input onChange={(event) => setName(event.target.value)} required value={name} />
          </label>

          <label className="field">
            <span>Descrição</span>
            <textarea
              onChange={(event) => setDescription(event.target.value)}
              rows={3}
              value={description}
            />
          </label>

          <ImageUpload hint="Foto quadrada ou deitada, com boa luz, vende mais." label="Foto do produto" onChange={setImageUrl} value={imageUrl} />

          <div className="admin-form-grid">
            <label className="field">
              <span>Ordem</span>
              <input
                min={0}
                onChange={(event) => setSortOrder(event.target.value)}
                type="number"
                value={sortOrder}
              />
            </label>
          </div>

          <label className="admin-check">
            <input
              checked={isAvailable}
              onChange={(event) => setIsAvailable(event.target.checked)}
              type="checkbox"
            />
            Disponível para pedidos
          </label>

          <div className="admin-subheading">
            <div>
              <h3>Grupos de opções</h3>
              <p className="muted">Tamanhos, adicionais e escolhas obrigatórias.</p>
            </div>
            <button
              className="secondary-button secondary-button-small"
              onClick={() => setGroups((current) => [...current, emptyGroup()])}
              type="button"
            >
              Adicionar grupo
            </button>
          </div>

          {groups.length === 0 ? (
            <p className="admin-inline-empty">Sem grupos. O item será vendido direto.</p>
          ) : null}

          <div className="admin-option-groups">
            {groups.map((group, groupIndex) => (
              <fieldset className="admin-option-group" key={group.key}>
                <legend>Grupo {groupIndex + 1}</legend>
                <button
                  className="link-button"
                  onClick={() =>
                    setGroups((current) => current.filter((entry) => entry.key !== group.key))
                  }
                  type="button"
                >
                  Remover grupo
                </button>

                <div className="admin-form-grid">
                  <label className="field">
                    <span>Nome do grupo</span>
                    <input
                      onChange={(event) => updateGroup(group.key, { name: event.target.value })}
                      placeholder="Tamanho"
                      value={group.name}
                    />
                  </label>
                  <label className="field">
                    <span>Mínimo</span>
                    <input
                      min={0}
                      onChange={(event) =>
                        updateGroup(group.key, { minSelections: event.target.value })
                      }
                      type="number"
                      value={group.minSelections}
                    />
                  </label>
                  <label className="field">
                    <span>Máximo</span>
                    <input
                      min={0}
                      onChange={(event) =>
                        updateGroup(group.key, { maxSelections: event.target.value })
                      }
                      type="number"
                      value={group.maxSelections}
                    />
                  </label>
                  <label className="field">
                    <span>Ordem</span>
                    <input
                      min={0}
                      onChange={(event) =>
                        updateGroup(group.key, { sortOrder: event.target.value })
                      }
                      type="number"
                      value={group.sortOrder}
                    />
                  </label>
                </div>

                <label className="admin-check">
                  <input
                    checked={group.isRequired}
                    onChange={(event) =>
                      updateGroup(group.key, { isRequired: event.target.checked })
                    }
                    type="checkbox"
                  />
                  Escolha obrigatória
                </label>

                <div className="admin-option-list">
                  {group.options.map((option) => (
                    <div className="admin-option-row" key={option.key}>
                      <input
                        aria-label="Nome da opção"
                        onChange={(event) =>
                          updateOption(group.key, option.key, { name: event.target.value })
                        }
                        placeholder="Nome"
                        value={option.name}
                      />
                      <input
                        aria-label="Preço da opção"
                        min={0}
                        onChange={(event) =>
                          updateOption(group.key, option.key, { price: event.target.value })
                        }
                        placeholder="Preço"
                        step="0.01"
                        type="number"
                        value={option.price}
                      />
                      <label className="admin-check admin-check-inline">
                        <input
                          checked={option.isAvailable}
                          onChange={(event) =>
                            updateOption(group.key, option.key, {
                              isAvailable: event.target.checked,
                            })
                          }
                          type="checkbox"
                        />
                        Disponível
                      </label>
                      <button
                        className="link-button"
                        onClick={() =>
                          updateGroup(group.key, {
                            options: group.options.filter((entry) => entry.key !== option.key),
                          })
                        }
                        type="button"
                      >
                        Remover
                      </button>
                    </div>
                  ))}
                </div>

                <button
                  className="secondary-button secondary-button-small"
                  onClick={() =>
                    updateGroup(group.key, { options: [...group.options, emptyOption()] })
                  }
                  type="button"
                >
                  Adicionar opção
                </button>
              </fieldset>
            ))}
          </div>

          {error ? <p className="form-error">{error}</p> : null}

          <div className="admin-form-actions">
            <button className="secondary-button" onClick={onClose} type="button">
              Cancelar
            </button>
            <button className="primary-button" disabled={busy} type="submit">
              {busy ? "Salvando..." : "Salvar item"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
