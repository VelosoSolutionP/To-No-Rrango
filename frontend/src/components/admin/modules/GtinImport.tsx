"use client";

import { type FormEvent, useCallback, useEffect, useState } from "react";
import type { AdminHttp } from "@/lib/admin-api";
import type { PublicMenu } from "@/lib/types";
import { FormDrawer, StatusChip } from "../DataTable";
import { ErrorMessage, text, toDecimal } from "./shared";

export interface GtinLookup {
  code: string;
  name: string;
  brand: string | null;
  ncm: string | null;
  cest: string | null;
  imageUrl: string | null;
  existingItemId: string | null;
  existingItemName: string | null;
}

interface ImportLine {
  code: string;
  status: "created" | "not_found" | "skipped" | "invalid" | "failed";
  name: string | null;
  message: string | null;
}

interface ImportJob {
  id: string;
  state: "queued" | "running" | "done" | "cancelled";
  total: number;
  done: number;
  created: number;
  notFound: number;
  skipped: number;
  invalid: number;
  failed: number;
  secondsLeft: number;
  lines: ImportLine[];
}

const lineTone: Record<ImportLine["status"], "ok" | "warn" | "muted" | "bad"> = {
  created: "ok",
  not_found: "warn",
  skipped: "muted",
  invalid: "bad",
  failed: "bad",
};

const lineLabel: Record<ImportLine["status"], string> = {
  created: "Criado",
  not_found: "Não achado",
  skipped: "Já existe",
  invalid: "Código errado",
  failed: "Falhou",
};

function remaining(seconds: number) {
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.round(seconds / 60);
  return minutes < 60 ? `${minutes} min` : `${Math.floor(minutes / 60)} h ${minutes % 60} min`;
}

/** Codes pasted in any shape: one per line, separated by commas, or straight from a spreadsheet column. */
function parseCodes(raw: string) {
  return [...new Set(raw.split(/[\s,;]+/).map((code) => code.replace(/\D/g, "")).filter(Boolean))];
}

export function GtinImport({
  http,
  base,
  menu,
  hasFiscal,
  onClose,
  onImported,
}: {
  http: AdminHttp;
  base: string;
  menu: PublicMenu | null;
  hasFiscal: boolean;
  onClose: () => void;
  onImported: () => Promise<void>;
}) {
  const [job, setJob] = useState<ImportJob | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [codes, setCodes] = useState("");
  const [withTax, setWithTax] = useState(false);
  const categories = menu?.categories ?? [];
  const running = job?.state === "queued" || job?.state === "running";
  const parsed = parseCodes(codes);
  const minutes = Math.ceil((parsed.length * 2 * 31) / 60);

  const refresh = useCallback(async () => {
    const current = await http.get<ImportJob | undefined>(`${base}/gtin/imports/current`).catch(() => undefined);
    setJob(current ?? null);
    setLoaded(true);
    return current ?? null;
  }, [base, http]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    if (!running) return;
    let created = job?.created ?? 0;
    const timer = window.setInterval(async () => {
      const current = await refresh();
      if (current && current.created !== created) {
        created = current.created;
        await onImported();
      }
    }, 5000);
    return () => window.clearInterval(timer);
  }, [job?.created, onImported, refresh, running]);

  async function start(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setBusy(true);
    setError(null);
    try {
      const started = await http.post<ImportJob>(`${base}/gtin/imports`, {
        categoryId: String(form.get("categoryId")),
        codes: parsed,
        withImages: form.get("withImages") === "on",
        fiscal: withTax
          ? {
              cfop: text(form, "cfop"),
              icmsCst: String(form.get("icmsCst")),
              icmsRate: toDecimal(form.get("icmsRate")),
              pisCofinsCst: String(form.get("pisCofinsCst")),
              pisRate: toDecimal(form.get("pisRate")),
              cofinsRate: toDecimal(form.get("cofinsRate")),
            }
          : null,
      });
      setJob(started);
      setCodes("");
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Não foi possível começar a importação.");
    } finally {
      setBusy(false);
    }
  }

  async function cancel() {
    const current = await http.post<ImportJob>(`${base}/gtin/imports/current/cancel`).catch(() => null);
    if (current) setJob(current);
  }

  const showForm = loaded && !running;

  return (
    <FormDrawer onClose={onClose} subtitle="Produtos embalados" title="Importar por código de barras" wide>
      {job ? (
        <div className="drawer-form">
          <div className="kpi-strip">
            <div className="kpi"><small>Andamento</small><strong>{job.done}/{job.total}</strong></div>
            <div className="kpi is-ok"><small>Criados</small><strong>{job.created}</strong></div>
            <div className={`kpi ${job.notFound ? "is-warn" : ""}`}><small>Não achados</small><strong>{job.notFound}</strong></div>
            <div className="kpi"><small>Já existiam</small><strong>{job.skipped}</strong></div>
            {job.invalid ? <div className="kpi is-warn"><small>Código errado</small><strong>{job.invalid}</strong></div> : null}
            {job.failed ? <div className="kpi is-warn"><small>Falharam</small><strong>{job.failed}</strong></div> : null}
          </div>
          {running ? (
            <p className="muted">
              Importando em segundo plano. Pode fechar esta janela. A base libera 2 consultas por minuto; faltam cerca de {remaining(job.secondsLeft)}.
            </p>
          ) : (
            <p className="muted">
              {job.state === "cancelled" ? "Importação cancelada." : "Importação concluída."} Os produtos entram inativos e sem preço, fora do cardápio e do PDV. Use o filtro Inativos, defina o preço de cada um e reative.
            </p>
          )}
          {running ? (
            <div className="drawer-foot">
              <button className="secondary-button" onClick={() => void cancel()} type="button">Cancelar importação</button>
            </div>
          ) : null}
          <ul className="gtin-lines">
            {job.lines.map((line) => (
              <li key={line.code}>
                <StatusChip tone={lineTone[line.status]}>{lineLabel[line.status]}</StatusChip>
                <div>
                  <strong>{line.name ?? line.code}</strong>
                  <small>{line.name ? `${line.code} · ` : ""}{line.message ?? ""}</small>
                </div>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {showForm ? (
        <form className="drawer-form" onSubmit={start}>
          {job ? <h3>Nova importação</h3> : null}
          <p className="muted">
            Para mercadoria com código de barras: bebidas, mercearia, produtos de revenda. Nome, marca, NCM, CEST e foto vêm da base; prato feito não está nela.
          </p>
          <label className="field">
            <span>Categoria em que os produtos entram *</span>
            <select defaultValue={categories[0]?.id} name="categoryId" required>
              {categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}
            </select>
          </label>
          <label className="field">
            <span>Códigos de barras *</span>
            <textarea
              name="codes"
              onChange={(event) => setCodes(event.target.value)}
              placeholder={"Um por linha, ou cole a coluna da planilha\n7891000100103\n7896116900029"}
              rows={8}
              value={codes}
            />
          </label>
          {parsed.length ? (
            <p className="muted">{parsed.length} código(s). Com foto, leva cerca de {minutes} min; sem foto, metade.</p>
          ) : null}
          <label className="module-check"><input defaultChecked name="withImages" type="checkbox" /> <span>Trazer a foto de cada produto</span></label>
          {hasFiscal ? (
            <>
              <label className="module-check">
                <input checked={withTax} onChange={(event) => setWithTax(event.target.checked)} type="checkbox" />{" "}
                <span>Gravar NCM e CEST da base com esta tributação</span>
              </label>
              {withTax ? (
                <>
                  <p className="muted">Confira com o contador: a mesma tributação vale para todos os produtos desta importação.</p>
                  <div className="field-row">
                    <label className="field"><span>CFOP *</span><input defaultValue="5102" inputMode="numeric" maxLength={4} name="cfop" required /></label>
                    <label className="field">
                      <span>CST ICMS</span>
                      <select defaultValue="00" name="icmsCst">
                        <option value="00">00 · Tributada integralmente</option>
                        <option value="40">40 · Isenta</option>
                        <option value="41">41 · Não tributada</option>
                        <option value="60">60 · ICMS cobrado por ST</option>
                      </select>
                    </label>
                  </div>
                  <div className="field-row">
                    <label className="field"><span>Alíquota ICMS (%)</span><input defaultValue="" inputMode="decimal" name="icmsRate" /></label>
                    <label className="field">
                      <span>CST PIS/COFINS</span>
                      <select defaultValue="01" name="pisCofinsCst">
                        {["01", "02", "04", "06", "07", "08", "09"].map((code) => <option key={code} value={code}>{code}</option>)}
                      </select>
                    </label>
                  </div>
                  <div className="field-row">
                    <label className="field"><span>Alíquota PIS (%)</span><input defaultValue="0,65" inputMode="decimal" name="pisRate" /></label>
                    <label className="field"><span>Alíquota COFINS (%)</span><input defaultValue="3" inputMode="decimal" name="cofinsRate" /></label>
                  </div>
                </>
              ) : null}
            </>
          ) : null}
          <ErrorMessage message={error} />
          <div className="drawer-foot">
            <button className="secondary-button" onClick={onClose} type="button">Fechar</button>
            <button className="primary-button" disabled={busy || parsed.length === 0 || categories.length === 0} type="submit">Importar</button>
          </div>
        </form>
      ) : null}
    </FormDrawer>
  );
}
