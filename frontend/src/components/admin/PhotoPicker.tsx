"use client";

import { type FormEvent, useEffect, useState } from "react";
import type { AdminHttp } from "@/lib/admin-api";

export interface PhotoOption {
  id: string;
  previewUrl: string;
  imageUrl: string;
  tags: string;
  author: string;
  pageUrl: string;
}

export interface PhotoSearchResult {
  configured: boolean;
  query: string;
  photos: PhotoOption[];
  busy?: boolean;
}

export const searchPhotos = (http: AdminHttp, base: string, text: string) =>
  http.get<PhotoSearchResult>(`${base}/photos/search?q=${encodeURIComponent(text)}`);

/** Copies a bank photo into the store's own media (bank links expire in 24 h) and returns our address. */
export const importPhoto = async (http: AdminHttp, base: string, photo: PhotoOption) =>
  (await http.post<{ url: string }>(`${base}/photos/import`, { url: photo.imageUrl })).url;

/** Real photos (never drawings) to choose from, searched by the product name. */
export function PhotoPicker({ http, base, initialQuery, onPick, onClose }: { http: AdminHttp; base: string; initialQuery: string; onPick: (url: string) => void; onClose: () => void }) {
  const [query, setQuery] = useState(initialQuery);
  const [result, setResult] = useState<PhotoSearchResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [saving, setSaving] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function search(text: string) {
    if (!text.trim()) return;
    setBusy(true);
    setError(null);
    try {
      setResult(await searchPhotos(http, base, text));
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "A busca de fotos não respondeu.");
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    void search(initialQuery);
    const onKey = (event: KeyboardEvent) => event.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function pick(photo: PhotoOption) {
    setSaving(photo.id);
    setError(null);
    try {
      onPick(await importPhoto(http, base, photo));
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Não consegui usar esta foto.");
    } finally {
      setSaving(null);
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    void search(query);
  }

  return (
    <div className="modal-backdrop vx-dialog-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onClose()} role="presentation">
      <section aria-label="Buscar foto real" className="modal photo-picker" role="dialog">
        <header>
          <h2>Foto real do produto</h2>
          <button aria-label="Fechar" className="modal-close" onClick={onClose} type="button">×</button>
        </header>
        <form onSubmit={submit}>
          <input autoFocus onChange={(event) => setQuery(event.target.value)} placeholder="Ex.: cerveja lata, pizza calabresa" value={query} />
          <button className="secondary-button" disabled={busy} type="submit">{busy ? "Buscando…" : "Buscar"}</button>
        </form>
        {result && !result.configured ? <p className="form-error">A busca de fotos ainda não está ligada neste servidor (falta a chave do Pixabay).</p> : null}
        {error ? <p className="form-error">{error}</p> : null}
        {result?.busy ? <p className="form-error">O banco de fotos pediu uma pausa. Espere alguns segundos e clique em Buscar.</p> : null}
        {result?.configured && !result.busy && result.photos.length === 0 && !busy ? <p className="muted">Nada encontrado. Tente outro nome, mais simples (ex.: só “hambúrguer”).</p> : null}
        <div className="photo-grid">
          {result?.photos.map((photo) => (
            <button className={saving === photo.id ? "is-saving" : ""} disabled={Boolean(saving)} key={photo.id} onClick={() => void pick(photo)} title={photo.tags} type="button">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img alt={photo.tags} loading="lazy" src={photo.previewUrl} />
              {saving === photo.id ? <span>Salvando…</span> : null}
            </button>
          ))}
        </div>
        <small className="muted">Fotos reais do Pixabay, de uso comercial livre. Clique na que combina com o seu produto.</small>
      </section>
    </div>
  );
}
