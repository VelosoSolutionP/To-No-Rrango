"use client";

import { useEffect, useRef, useState } from "react";

/** Total life of a Pix QR code: matches PaymentService.PixLifetime on the API. */
const LIFETIME_MS = 30 * 60 * 1000;

function left(expiresAt: string) {
  return Math.max(0, new Date(expiresAt).getTime() - Date.now());
}

/** Countdown under the Pix QR code; calls onExpire once when it reaches zero. */
export function PixCountdown({ expiresAt, onExpire }: { expiresAt: string | null; onExpire: () => void }) {
  const [ms, setMs] = useState(() => (expiresAt ? left(expiresAt) : LIFETIME_MS));
  const fired = useRef(false);

  useEffect(() => {
    if (!expiresAt) return;
    fired.current = false;
    const tick = () => {
      const remaining = left(expiresAt);
      setMs(remaining);
      if (remaining === 0 && !fired.current) {
        fired.current = true;
        onExpire();
      }
    };
    tick();
    const timer = window.setInterval(tick, 1000);
    return () => window.clearInterval(timer);
  }, [expiresAt, onExpire]);

  if (!expiresAt) return null;

  const minutes = Math.floor(ms / 60000);
  const seconds = Math.floor((ms % 60000) / 1000);
  const share = Math.min(1, ms / LIFETIME_MS);
  const tone = ms <= 60_000 ? "is-ending" : ms <= 5 * 60_000 ? "is-short" : "";

  return (
    <div aria-live="polite" className={`pix-countdown ${tone}`} role="timer">
      <span>Este QR Code expira em</span>
      <strong>{String(minutes).padStart(2, "0")}:{String(seconds).padStart(2, "0")}</strong>
      <i aria-hidden="true"><b style={{ width: `${share * 100}%` }} /></i>
    </div>
  );
}
