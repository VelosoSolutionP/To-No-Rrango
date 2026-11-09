"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { API_BASE_URL } from "@/lib/api";

interface CourierSession {
  isActive: boolean;
  orderCode: string | null;
  storeName: string | null;
  destination: string | null;
}

type Phase = "loading" | "ready" | "tracking" | "ended";

const sendEveryMs = 15_000;

export function CourierClient({ token }: { token: string }) {
  const [session, setSession] = useState<CourierSession | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [problem, setProblem] = useState<string | null>(null);
  const [sentAt, setSentAt] = useState<Date | null>(null);
  const watch = useRef<number | null>(null);
  const wakeLock = useRef<WakeLockSentinel | null>(null);
  const lastSent = useRef(0);
  const base = `${API_BASE_URL}/api/public/tracking/${encodeURIComponent(token)}`;

  const stopSensors = useCallback(() => {
    if (watch.current !== null) {
      navigator.geolocation.clearWatch(watch.current);
      watch.current = null;
    }

    void wakeLock.current?.release().catch(() => undefined);
    wakeLock.current = null;
  }, []);

  const keepScreenOn = useCallback(async () => {
    try {
      wakeLock.current = (await navigator.wakeLock?.request("screen")) ?? null;
    } catch {
      setProblem("Não deu para manter a tela acesa. Deixe a tela ligada durante a entrega.");
    }
  }, []);

  useEffect(() => {
    fetch(base, { cache: "no-store" })
      .then((response) => (response.ok ? (response.json() as Promise<CourierSession>) : Promise.reject(new Error())))
      .then((data) => {
        setSession(data);
        setPhase(data.isActive ? "ready" : "ended");
      })
      .catch(() => setProblem("Não foi possível abrir esta entrega. Verifique a internet e recarregue a página."));

    return stopSensors;
  }, [base, stopSensors]);

  useEffect(() => {
    function onVisible() {
      if (phase === "tracking" && document.visibilityState === "visible") {
        void keepScreenOn();
      }
    }

    document.addEventListener("visibilitychange", onVisible);
    return () => document.removeEventListener("visibilitychange", onVisible);
  }, [keepScreenOn, phase]);

  async function send(latitude: number, longitude: number) {
    if (Date.now() - lastSent.current < sendEveryMs) {
      return;
    }

    lastSent.current = Date.now();

    try {
      const response = await fetch(`${base}/position`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ latitude, longitude }),
      });

      if (!response.ok) {
        return;
      }

      const data = (await response.json()) as CourierSession;

      if (!data.isActive) {
        stopSensors();
        setPhase("ended");
        return;
      }

      setSentAt(new Date());
      setProblem(null);
    } catch {
      setProblem("Sem internet agora. A posição volta a ser enviada quando o sinal retornar.");
    }
  }

  function start() {
    if (!navigator.geolocation) {
      setProblem("Este celular não permite compartilhar a localização pelo navegador.");
      return;
    }

    setProblem(null);
    lastSent.current = 0;
    watch.current = navigator.geolocation.watchPosition(
      (position) => {
        setPhase("tracking");
        void send(position.coords.latitude, position.coords.longitude);
      },
      (error) => setProblem(error.code === error.PERMISSION_DENIED
        ? "Permita o acesso à localização nas configurações do navegador e tente de novo."
        : "Não foi possível obter a localização. Vá para um lugar aberto."),
      { enableHighAccuracy: true, maximumAge: 5_000, timeout: 30_000 },
    );
    void keepScreenOn();
  }

  async function finish() {
    stopSensors();
    setPhase("ended");
    await fetch(`${base}/finish`, { method: "POST" }).catch(() => undefined);
  }

  return (
    <main className="tracking-page courier-page">
      <section className="tracking-card">
        <p className="eyebrow">{session?.storeName ?? "Entrega"}</p>
        {phase === "ended" ? (
          <>
            <h1>Rastreio encerrado</h1>
            <p className="muted">Sua localização não está mais sendo compartilhada. Pode fechar esta página.</p>
          </>
        ) : (
          <>
            <h1>Pedido {session?.orderCode ?? ""}</h1>
            {session?.destination ? <p><strong>Entregar em:</strong> {session.destination}</p> : null}
            {phase === "tracking" ? (
              <>
                <p className="courier-live"><i />Compartilhando sua localização</p>
                <p className="muted">Deixe esta página aberta e a tela ligada até concluir a entrega.{sentAt ? ` Último envio às ${sentAt.toLocaleTimeString("pt-BR")}.` : ""}</p>
                <button className="secondary-button" onClick={() => void finish()} type="button">Encerrar rastreio</button>
              </>
            ) : phase === "ready" ? (
              <>
                <p className="muted">O cliente verá sua posição no mapa só durante esta entrega.</p>
                <button className="primary-button" onClick={start} type="button">Iniciar entrega</button>
              </>
            ) : null}
          </>
        )}
        {problem ? <p className="form-error">{problem}</p> : null}
      </section>
    </main>
  );
}
