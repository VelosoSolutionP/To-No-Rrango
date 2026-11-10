"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { RatingState } from "@/lib/types";

function Stars({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return (
    <div className="rating-row">
      <span>{label}</span>
      <div className="rating-stars" role="radiogroup" aria-label={label}>
        {[1, 2, 3, 4, 5].map((score) => (
          <button
            aria-checked={value === score}
            aria-label={`${score} de 5`}
            className={score <= value ? "is-on" : ""}
            key={score}
            onClick={() => onChange(score)}
            role="radio"
            type="button"
          >
            ★
          </button>
        ))}
      </div>
    </div>
  );
}

/** The review of a delivered order: the store on one side, the courier on the other. */
export function OrderRating({ code, delivered }: { code: string; delivered: boolean }) {
  const [state, setState] = useState<RatingState | null>(null);
  const [scores, setScores] = useState({ service: 0, product: 0, overall: 0, courierService: 0, punctuality: 0, care: 0 });
  const [storeComment, setStoreComment] = useState("");
  const [courierComment, setCourierComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (delivered) {
      api.getRating(code).then(setState).catch(() => setState(null));
    }
  }, [code, delivered]);

  if (!delivered || !state) {
    return null;
  }

  if (state.isRated) {
    return (
      <section className="tracking-card rating-card">
        <h2>Obrigado pela avaliação!</h2>
        <p className="muted">Sua opinião ajuda o estabelecimento a melhorar.</p>
      </section>
    );
  }

  if (!state.canRate) {
    return null;
  }

  const hasCourier = Boolean(state.courierName);
  const set = (key: keyof typeof scores) => (value: number) => setScores((current) => ({ ...current, [key]: value }));
  const storeReady = scores.service > 0 && scores.product > 0 && scores.overall > 0;
  const courierReady = !hasCourier || (scores.courierService > 0 && scores.punctuality > 0 && scores.care > 0);

  async function submit() {
    setBusy(true);
    setError(null);

    try {
      setState(
        await api.rateOrder(code, {
          serviceScore: scores.service,
          productScore: scores.product,
          overallScore: scores.overall,
          storeComment: storeComment.trim() || null,
          courierServiceScore: hasCourier ? scores.courierService : null,
          courierPunctualityScore: hasCourier ? scores.punctuality : null,
          courierCareScore: hasCourier ? scores.care : null,
          courierComment: hasCourier ? courierComment.trim() || null : null,
        }),
      );
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Não foi possível enviar a avaliação.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="tracking-card rating-card">
      <h2>Como foi o seu pedido?</h2>
      <h3>Estabelecimento</h3>
      <Stars label="Atendimento" onChange={set("service")} value={scores.service} />
      <Stars label="Produtos" onChange={set("product")} value={scores.product} />
      <Stars label="Experiência geral" onChange={set("overall")} value={scores.overall} />
      <textarea maxLength={500} onChange={(event) => setStoreComment(event.target.value)} placeholder="Quer contar algo para a loja? (opcional)" value={storeComment} />

      {hasCourier ? (
        <>
          <h3>Entregador · {state.courierName}</h3>
          <Stars label="Atendimento" onChange={set("courierService")} value={scores.courierService} />
          <Stars label="Pontualidade" onChange={set("punctuality")} value={scores.punctuality} />
          <Stars label="Cuidado com o pedido" onChange={set("care")} value={scores.care} />
          <textarea maxLength={500} onChange={(event) => setCourierComment(event.target.value)} placeholder="Comentário sobre a entrega (opcional)" value={courierComment} />
        </>
      ) : null}

      {error ? <p className="form-error">{error}</p> : null}
      <button className="primary-button primary-button-full" disabled={busy || !storeReady || !courierReady} onClick={() => void submit()} type="button">
        {busy ? "Enviando…" : "Enviar avaliação"}
      </button>
    </section>
  );
}
