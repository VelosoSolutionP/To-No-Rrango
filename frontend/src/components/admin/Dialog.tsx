"use client";

import { type FormEvent, useEffect, useRef, useState } from "react";

/**
 * The panel's own confirm and prompt, in place of the browser's alert boxes (owner, 14/11/2026).
 * Call `confirmDialog` / `promptDialog` from anywhere; `<DialogHost />` (mounted once) draws them.
 */
type Tone = "danger" | "default";

interface ConfirmOptions {
  title: string;
  message?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: Tone;
}

interface PromptOptions extends ConfirmOptions {
  label?: string;
  placeholder?: string;
  minLength?: number;
  maxLength?: number;
  multiline?: boolean;
}

type Request =
  | { kind: "confirm"; options: ConfirmOptions; resolve: (value: boolean) => void }
  | { kind: "prompt"; options: PromptOptions; resolve: (value: string | null) => void };

let show: ((request: Request) => void) | null = null;

export function confirmDialog(options: ConfirmOptions): Promise<boolean> {
  return new Promise((resolve) => (show ? show({ kind: "confirm", options, resolve }) : resolve(window.confirm(options.title))));
}

export function promptDialog(options: PromptOptions): Promise<string | null> {
  return new Promise((resolve) => (show ? show({ kind: "prompt", options, resolve }) : resolve(window.prompt(options.title))));
}

export function DialogHost() {
  const [request, setRequest] = useState<Request | null>(null);
  const [text, setText] = useState("");
  const field = useRef<HTMLInputElement & HTMLTextAreaElement>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    show = (next) => {
      setText("");
      setRequest(next);
    };
    return () => {
      show = null;
    };
  }, []);

  useEffect(() => {
    if (!request) return;
    (request.kind === "prompt" ? field.current : confirmButton.current)?.focus();
    const onKey = (event: KeyboardEvent) => event.key === "Escape" && close(false);
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [request]);

  if (!request) return null;

  const { options } = request;
  const min = request.kind === "prompt" ? request.options.minLength ?? 0 : 0;
  const tooShort = request.kind === "prompt" && text.trim().length < Math.max(min, 1);

  function close(ok: boolean) {
    if (!request) return;
    if (request.kind === "confirm") request.resolve(ok);
    else request.resolve(ok ? text.trim() : null);
    setRequest(null);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!tooShort) close(true);
  }

  const prompt = request.kind === "prompt" ? request.options : null;

  return (
    <div className="modal-backdrop vx-dialog-backdrop" onMouseDown={(event) => event.target === event.currentTarget && close(false)} role="presentation">
      <form aria-labelledby="vx-dialog-title" aria-modal="true" className={`modal vx-dialog ${options.tone === "danger" ? "is-danger" : ""}`} onSubmit={submit} role="dialog">
        <i aria-hidden="true" className="vx-dialog-icon">{options.tone === "danger" ? "!" : "?"}</i>
        <h2 id="vx-dialog-title">{options.title}</h2>
        {options.message ? <p>{options.message}</p> : null}
        {prompt ? (
          <label className="vx-dialog-field">
            {prompt.label ? <span>{prompt.label}</span> : null}
            {prompt.multiline ? (
              <textarea maxLength={prompt.maxLength} onChange={(event) => setText(event.target.value)} placeholder={prompt.placeholder} ref={field} rows={3} value={text} />
            ) : (
              <input maxLength={prompt.maxLength} onChange={(event) => setText(event.target.value)} placeholder={prompt.placeholder} ref={field} value={text} />
            )}
            {min > 1 ? <small className={text.trim().length >= min ? "is-ok" : ""}>{text.trim().length}/{min} letras no mínimo</small> : null}
          </label>
        ) : null}
        <div className="vx-dialog-actions">
          <button className="secondary-button" onClick={() => close(false)} type="button">{options.cancelLabel ?? "Voltar"}</button>
          <button className={options.tone === "danger" ? "danger-button" : "primary-button"} disabled={tooShort} ref={confirmButton} type="submit">
            {options.confirmLabel ?? "Confirmar"}
          </button>
        </div>
      </form>
    </div>
  );
}
