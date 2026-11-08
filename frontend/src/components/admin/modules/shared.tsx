"use client";

import { type DependencyList, type ReactNode, useCallback, useEffect, useState } from "react";
import { ApiError } from "@/lib/api";

export function messageOf(error: unknown): string {
  if (error instanceof ApiError) {
    return error.detail ?? error.message;
  }

  return error instanceof Error ? error.message : "Ocorreu um erro inesperado.";
}

export function toNumber(value: FormDataEntryValue | string | null): number {
  return Number(String(value ?? "").trim().replace(/\./g, "").replace(",", ".")) || 0;
}

/** Quantities typed with a dot or a comma as the decimal separator. */
export function toDecimal(value: FormDataEntryValue | string | null): number {
  return Number(String(value ?? "").trim().replace(",", ".")) || 0;
}

export function toCents(value: FormDataEntryValue | string | null): number {
  return Math.round(toDecimal(value) * 100);
}

export function text(form: FormData, name: string): string | null {
  const value = String(form.get(name) ?? "").trim();
  return value.length > 0 ? value : null;
}

export function quantity(value: number): string {
  return new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 3 }).format(value);
}

export function useLoad<T>(load: () => Promise<T>, deps: DependencyList) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const reload = useCallback(async () => {
    try {
      setData(await load());
      setError(null);
    } catch (loadError) {
      setError(messageOf(loadError));
    } finally {
      setLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  useEffect(() => {
    void reload();
  }, [reload]);

  return { data, error, loading, reload };
}

/** Runs a mutation, keeps the message for display and reloads on success. */
export function useAction(onDone?: () => void | Promise<void>) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<unknown>): Promise<boolean> {
    setBusy(true);
    setError(null);

    try {
      await action();
      await onDone?.();
      return true;
    } catch (actionError) {
      setError(messageOf(actionError));
      return false;
    } finally {
      setBusy(false);
    }
  }

  return { busy, error, run };
}

export function ModuleHeading({ eyebrow, title, hint, children }: { eyebrow: string; title: string; hint?: string; children?: ReactNode }) {
  return (
    <div className="admin-section-heading">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h2>{title}</h2>
        {hint ? <p className="muted">{hint}</p> : null}
      </div>
      {children}
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return (
    <div className="admin-empty card">
      <p className="muted">{children}</p>
    </div>
  );
}

export function SubTabs<T extends string>({ value, onChange, options }: { value: T; onChange: (value: T) => void; options: Array<{ value: T; label: string }> }) {
  return (
    <div className="admin-filter-row">
      {options.map((option) => (
        <button
          className={`admin-filter ${value === option.value ? "is-active" : ""}`}
          key={option.value}
          onClick={() => onChange(option.value)}
          type="button"
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

export function Input({ label, name, wide, ...props }: { label: string; name: string; wide?: boolean } & React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <label className={`field ${wide ? "field-wide" : ""}`}>
      <span>{label}</span>
      <input name={name} {...props} />
    </label>
  );
}

export function Check({ label, name, defaultChecked }: { label: string; name: string; defaultChecked?: boolean }) {
  return (
    <label className="module-check">
      <input defaultChecked={defaultChecked} name={name} type="checkbox" />
      <span>{label}</span>
    </label>
  );
}

export function ErrorMessage({ message }: { message: string | null }) {
  return message ? <p className="form-error">{message}</p> : null;
}
