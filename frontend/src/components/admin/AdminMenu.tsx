"use client";

import { useState } from "react";
import { formatBRL } from "@/lib/admin-api";
import type {
  CategoryPayload,
  MenuCategory,
  MenuItem,
  MenuItemPayload,
  PublicMenu,
} from "@/lib/types";
import { CategoryModal } from "./CategoryModal";
import { ItemModal } from "./ItemModal";

interface AdminMenuProps {
  menu: PublicMenu | null;
  loading: boolean;
  error: string | null;
  busy: boolean;
  onRefresh: () => Promise<void>;
  onSaveCategory: (categoryId: string | null, payload: CategoryPayload) => Promise<void>;
  onSetCategoryActive: (category: MenuCategory, isActive: boolean) => Promise<void>;
  onSaveItem: (itemId: string | null, payload: MenuItemPayload) => Promise<void>;
  onSetItemActive: (item: MenuItem, isActive: boolean) => Promise<void>;
  onSetItemAvailability: (item: MenuItem, isAvailable: boolean) => Promise<void>;
}

export function AdminMenu({
  menu,
  loading,
  error,
  busy,
  onRefresh,
  onSaveCategory,
  onSetCategoryActive,
  onSaveItem,
  onSetItemActive,
  onSetItemAvailability,
}: AdminMenuProps) {
  const [categoryModal, setCategoryModal] = useState<{ category: MenuCategory | null } | null>(null);
  const [itemModal, setItemModal] = useState<{ item: MenuItem | null; categoryId: string } | null>(
    null,
  );

  const categories = menu?.categories ?? [];

  async function saveCategory(payload: CategoryPayload) {
    try {
      await onSaveCategory(categoryModal?.category?.id ?? null, payload);
      setCategoryModal(null);
    } catch {
      // O erro é exibido pelo modal via prop.
    }
  }

  async function saveItem(payload: MenuItemPayload) {
    try {
      await onSaveItem(itemModal?.item?.id ?? null, payload);
      setItemModal(null);
    } catch {
      // O erro é exibido pelo modal via prop.
    }
  }

  return (
    <section className="admin-section">
      <div className="admin-section-heading">
        <div>
          <p className="eyebrow">Catálogo</p>
          <h2>Cardápio</h2>
          <p className="muted">Categorias, itens, preços e opções de personalização.</p>
        </div>
        <div className="admin-heading-actions">
          <button className="secondary-button secondary-button-small" onClick={onRefresh} type="button">
            Atualizar
          </button>
          <button
            className="primary-button"
            onClick={() => setCategoryModal({ category: null })}
            type="button"
          >
            Nova categoria
          </button>
        </div>
      </div>

      {error ? <p className="form-error">{error}</p> : null}

      {loading && !menu ? (
        <div className="admin-empty card">
          <span className="loading-dot" />
          <p>Carregando cardápio...</p>
        </div>
      ) : categories.length === 0 ? (
        <div className="admin-empty card">
          <h3>Seu cardápio começa aqui</h3>
          <p className="muted">Crie uma categoria para organizar os itens do estabelecimento.</p>
          <button
            className="primary-button"
            onClick={() => setCategoryModal({ category: null })}
            type="button"
          >
            Criar primeira categoria
          </button>
        </div>
      ) : (
        <div className="admin-category-list">
          {categories.map((category) => (
            <section
              className={`admin-category card ${category.isActive ? "" : "is-inactive"}`}
              key={category.id}
            >
              <header className="admin-category-head">
                <div>
                  <div className="admin-category-title">
                    <h3>{category.name}</h3>
                    {!category.isActive ? <span className="admin-badge danger">Inativa</span> : null}
                    <span className="pill">{category.items.length}</span>
                  </div>
                  {category.description ? <p className="muted">{category.description}</p> : null}
                </div>
                <div className="admin-category-actions">
                  <button
                    className="secondary-button secondary-button-small"
                    onClick={() => setCategoryModal({ category })}
                    type="button"
                  >
                    Editar
                  </button>
                  <button
                    className="secondary-button secondary-button-small"
                    disabled={busy}
                    onClick={() => {
                      void onSetCategoryActive(category, !category.isActive).catch(() => undefined);
                    }}
                    type="button"
                  >
                    {category.isActive ? "Desativar" : "Ativar"}
                  </button>
                  <button
                    className="primary-button"
                    disabled={!category.isActive}
                    onClick={() => setItemModal({ item: null, categoryId: category.id })}
                    type="button"
                  >
                    Novo item
                  </button>
                </div>
              </header>

              {category.items.length === 0 ? (
                <p className="admin-inline-empty">Nenhum item nesta categoria.</p>
              ) : (
                <div className="admin-item-list">
                  {category.items.map((item) => (
                    <article
                      className={`admin-item-row ${item.isActive ? "" : "is-inactive"}`}
                      key={item.id}
                    >
                      <div className="admin-item-info">
                        <div className="admin-item-title">
                          <strong>{item.name}</strong>
                          <span>{formatBRL(item.priceInCents)}</span>
                          {!item.isActive ? (
                            <span className="admin-badge danger">Inativo</span>
                          ) : null}
                          {!item.isAvailable ? (
                            <span className="admin-badge warning">Indisponível</span>
                          ) : null}
                        </div>
                        {item.description ? <p className="muted">{item.description}</p> : null}
                        {item.optionGroups.length > 0 ? (
                          <div className="admin-option-summary">
                            {item.optionGroups.map((group) => (
                              <span key={group.id}>
                                {group.name} · {group.minSelections}–{group.maxSelections}
                              </span>
                            ))}
                          </div>
                        ) : null}
                      </div>

                      <div className="admin-item-actions">
                        <button
                          className="secondary-button secondary-button-small"
                          onClick={() => setItemModal({ item, categoryId: category.id })}
                          type="button"
                        >
                          Editar
                        </button>
                        <button
                          className="secondary-button secondary-button-small"
                          disabled={busy || !item.isActive}
                          onClick={() => {
                            void onSetItemAvailability(item, !item.isAvailable).catch(
                              () => undefined,
                            );
                          }}
                          type="button"
                        >
                          {item.isAvailable ? "Pausar" : "Disponibilizar"}
                        </button>
                        <button
                          className="link-button"
                          disabled={busy}
                          onClick={() => {
                            void onSetItemActive(item, !item.isActive).catch(() => undefined);
                          }}
                          type="button"
                        >
                          {item.isActive ? "Desativar" : "Ativar"}
                        </button>
                      </div>
                    </article>
                  ))}
                </div>
              )}
            </section>
          ))}
        </div>
      )}

      {categoryModal ? (
        <CategoryModal
          busy={busy}
          category={categoryModal.category}
          error={error}
          onClose={() => setCategoryModal(null)}
          onSave={saveCategory}
        />
      ) : null}

      {itemModal ? (
        <ItemModal
          busy={busy}
          categories={categories}
          defaultCategoryId={itemModal.categoryId}
          error={error}
          item={itemModal.item}
          onClose={() => setItemModal(null)}
          onSave={saveItem}
        />
      ) : null}
    </section>
  );
}
