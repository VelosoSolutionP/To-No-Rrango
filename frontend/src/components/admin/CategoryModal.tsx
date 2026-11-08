"use client";

import { FormEvent, useEffect, useState } from "react";
import type { CategoryPayload, MenuCategory } from "@/lib/types";

interface CategoryModalProps {
  category: MenuCategory | null;
  busy: boolean;
  error: string | null;
  onClose: () => void;
  onSave: (payload: CategoryPayload) => Promise<void>;
}

export function CategoryModal({ category, busy, error, onClose, onSave }: CategoryModalProps) {
  const [name, setName] = useState(category?.name ?? "");
  const [description, setDescription] = useState(category?.description ?? "");
  const [sortOrder, setSortOrder] = useState(String(category?.sortOrder ?? 0));

  useEffect(() => {
    setName(category?.name ?? "");
    setDescription(category?.description ?? "");
    setSortOrder(String(category?.sortOrder ?? 0));
  }, [category]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await onSave({
      name: name.trim(),
      description: description.trim() || null,
      sortOrder: Number(sortOrder) || 0,
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
      <section className="modal admin-modal">
        <button aria-label="Fechar" className="modal-close" onClick={onClose} type="button">
          ×
        </button>
        <p className="eyebrow">{category ? "Editar categoria" : "Nova categoria"}</p>
        <h2>{category ? category.name : "Organize o cardápio"}</h2>

        <form className="admin-form" onSubmit={handleSubmit}>
          <label className="field">
            <span>Nome</span>
            <input
              onChange={(event) => setName(event.target.value)}
              required
              value={name}
            />
          </label>

          <label className="field">
            <span>Descrição</span>
            <textarea
              onChange={(event) => setDescription(event.target.value)}
              rows={3}
              value={description}
            />
          </label>

          <label className="field">
            <span>Ordem</span>
            <input
              min={0}
              onChange={(event) => setSortOrder(event.target.value)}
              type="number"
              value={sortOrder}
            />
          </label>

          {error ? <p className="form-error">{error}</p> : null}

          <div className="admin-form-actions">
            <button className="secondary-button" onClick={onClose} type="button">
              Cancelar
            </button>
            <button className="primary-button" disabled={busy} type="submit">
              {busy ? "Salvando..." : "Salvar categoria"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
