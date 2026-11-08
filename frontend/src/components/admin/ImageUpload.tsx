"use client";

import { type ChangeEvent, type DragEvent, useRef, useState } from "react";
import { useAdmin } from "./AdminContext";

const MAX_SIDE = 1600;

/** Shrinks big phone photos in the browser, so a 12 MB picture still uploads in seconds. */
async function prepare(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file).catch(() => null);

  if (!bitmap) {
    throw new Error("Não consegui ler esta imagem. Use JPG, PNG ou WebP.");
  }

  const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext("2d")!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);

  return new Promise((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error("Não consegui processar a imagem."))), "image/jpeg", 0.86),
  );
}

interface ImageUploadProps {
  label: string;
  hint?: string;
  value: string;
  shape?: "photo" | "logo" | "cover";
  onChange: (url: string) => void;
}

export function ImageUpload({ label, hint, value, shape = "photo", onChange }: ImageUploadProps) {
  const { http, establishmentId } = useAdmin();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);

  async function send(file: File | undefined) {
    if (!file || !http || !establishmentId) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const blob = await prepare(file);
      const result = await http.upload<{ url: string }>(`/establishments/${establishmentId}/media`, blob);
      onChange(result.url);
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : "Não foi possível enviar a foto.");
    } finally {
      setBusy(false);
    }
  }

  function onDrop(event: DragEvent<HTMLButtonElement>) {
    event.preventDefault();
    setDragging(false);
    void send(event.dataTransfer.files[0]);
  }

  return (
    <div className="upload">
      <span className="upload-label">{label}</span>
      <button
        className={`upload-drop upload-${shape} ${dragging ? "is-dragging" : ""}`}
        disabled={busy}
        onClick={() => input.current?.click()}
        onDragLeave={() => setDragging(false)}
        onDragOver={(event) => {
          event.preventDefault();
          setDragging(true);
        }}
        onDrop={onDrop}
        type="button"
      >
        {value ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img alt="" src={value} />
        ) : (
          <span className="upload-empty">
            <strong>{busy ? "Enviando..." : "Toque para escolher a foto"}</strong>
            <small>ou arraste o arquivo para cá</small>
          </span>
        )}
        {value && busy ? <span className="upload-veil">Enviando...</span> : null}
      </button>
      <input
        accept="image/jpeg,image/png,image/webp"
        hidden
        onChange={(event: ChangeEvent<HTMLInputElement>) => {
          void send(event.target.files?.[0]);
          event.target.value = "";
        }}
        ref={input}
        type="file"
      />
      {hint ? <small className="upload-hint">{hint}</small> : null}
      {value ? (
        <div className="upload-actions">
          <button className="link-button" onClick={() => input.current?.click()} type="button">Trocar foto</button>
          <button className="link-button" onClick={() => onChange("")} type="button">Remover</button>
        </div>
      ) : null}
      {error ? <p className="form-error">{error}</p> : null}
    </div>
  );
}
