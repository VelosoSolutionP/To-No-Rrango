"use client";

import { type ChangeEvent, useMemo, useState } from "react";
import type { AdminHttp } from "@/lib/admin-api";
import type { PublicMenu } from "@/lib/types";
import { FormDrawer, StatusChip } from "../DataTable";
import { prepare } from "../ImageUpload";
import { ErrorMessage } from "./shared";

interface Item {
  id: string;
  name: string;
  imageUrl: string | null;
}

interface Pair {
  file: File;
  preview: string;
  itemId: string | null;
  state: "waiting" | "sending" | "done" | "failed";
  message?: string;
}

/** "Pizza Margherita Grande.jpg" and "pizza-margherita-grande.png" both become "pizza margherita grande". */
export function normalize(value: string) {
  return value
    .replace(/\.[a-z0-9]{2,5}$/i, "")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .trim();
}

/** Exact name first; then the longest product name the file name starts with, or that starts with it. */
export function matchItem(fileName: string, items: Item[]): string | null {
  const name = normalize(fileName);
  const exact = items.find((item) => normalize(item.name) === name);
  if (exact) return exact.id;
  const close = items
    .map((item) => ({ item, key: normalize(item.name) }))
    .filter(({ key }) => key.length >= 3 && (name.startsWith(key) || key.startsWith(name)))
    .sort((a, b) => b.key.length - a.key.length)[0];
  return close?.item.id ?? null;
}

/**
 * Many photos at once (owner, 14/11/2026): name each file like the product and every card
 * of the checkout, the menu and the catalog gets its real picture.
 */
export function BulkPhotos({ http, base, menu, onClose, onDone }: { http: AdminHttp; base: string; menu: PublicMenu | null; onClose: () => void; onDone: () => Promise<void> }) {
  const items: Item[] = useMemo(
    () => (menu?.categories ?? []).flatMap((category) => category.items.map((item) => ({ id: item.id, name: item.name, imageUrl: item.imageUrl ?? null }))),
    [menu],
  );
  const [pairs, setPairs] = useState<Pair[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function pick(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? []).filter((file) => file.type.startsWith("image/"));
    setPairs(files.map((file) => ({ file, preview: URL.createObjectURL(file), itemId: matchItem(file.name, items), state: "waiting" })));
    setError(null);
  }

  function choose(index: number, itemId: string) {
    setPairs((current) => current.map((pair, at) => (at === index ? { ...pair, itemId: itemId || null } : pair)));
  }

  async function apply() {
    setBusy(true);
    setError(null);
    let failures = 0;

    for (let index = 0; index < pairs.length; index++) {
      const pair = pairs[index];
      if (!pair.itemId || pair.state === "done") continue;
      setPairs((current) => current.map((item, at) => (at === index ? { ...item, state: "sending" } : item)));

      try {
        const uploaded = await http.upload<{ url: string }>(`${base}/media`, await prepare(pair.file));
        await http.patch(`/items/${pair.itemId}/image`, { imageUrl: uploaded.url });
        setPairs((current) => current.map((item, at) => (at === index ? { ...item, state: "done" } : item)));
      } catch (failure) {
        failures++;
        const message = failure instanceof Error ? failure.message : "Falhou.";
        setPairs((current) => current.map((item, at) => (at === index ? { ...item, state: "failed", message } : item)));
      }
    }

    await onDone();
    setBusy(false);
    if (failures) setError(`${failures} foto(s) não foram enviadas. As outras já estão nos produtos.`);
  }

  const ready = pairs.filter((pair) => pair.itemId && pair.state !== "done").length;
  const finished = pairs.length > 0 && pairs.every((pair) => pair.state === "done" || !pair.itemId);

  return (
    <FormDrawer onClose={onClose} subtitle="Produtos" title="Fotos em lote" wide>
      <div className="drawer-form">
        <p className="muted">
          Dê a cada foto o nome do produto (ex.: <code>Pizza Margherita Grande.jpg</code>) e selecione todas de uma vez. O sistema liga cada foto ao produto
          pelo nome; confira e troque o que precisar antes de aplicar.
        </p>
        <label className="secondary-button bulk-photos-pick">
          {pairs.length ? "Escolher outras fotos" : "Escolher fotos"}
          <input accept="image/*" disabled={busy} hidden multiple onChange={pick} type="file" />
        </label>

        {pairs.length ? (
          <ul className="bulk-photos">
            {pairs.map((pair, index) => (
              <li className={`is-${pair.state}`} key={pair.preview}>
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img alt="" src={pair.preview} />
                <div>
                  <small>{pair.file.name}</small>
                  <select disabled={busy || pair.state === "done"} onChange={(event) => choose(index, event.target.value)} value={pair.itemId ?? ""}>
                    <option value="">Sem produto (ignorar)</option>
                    {items.map((item) => (
                      <option key={item.id} value={item.id}>{item.name}{item.imageUrl ? " · já tem foto" : ""}</option>
                    ))}
                  </select>
                  {pair.state === "failed" ? <small className="form-error">{pair.message}</small> : null}
                </div>
                {pair.state === "done" ? <StatusChip tone="ok">Aplicada</StatusChip> : pair.state === "sending" ? <StatusChip tone="warn">Enviando…</StatusChip> : pair.itemId ? <StatusChip tone="ok">Achou</StatusChip> : <StatusChip tone="muted">Sem par</StatusChip>}
              </li>
            ))}
          </ul>
        ) : null}

        <ErrorMessage message={error} />
        <div className="drawer-actions">
          {finished ? (
            <button className="primary-button" onClick={onClose} type="button">Pronto</button>
          ) : (
            <button className="primary-button" disabled={busy || ready === 0} onClick={() => void apply()} type="button">
              {busy ? "Enviando…" : `Aplicar ${ready} foto(s)`}
            </button>
          )}
        </div>
      </div>
    </FormDrawer>
  );
}
