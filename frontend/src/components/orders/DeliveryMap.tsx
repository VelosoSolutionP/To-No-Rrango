"use client";

import "leaflet/dist/leaflet.css";
import type { Map as LeafletMap, Marker } from "leaflet";
import { useEffect, useRef, useState } from "react";
import { API_BASE_URL } from "@/lib/api";

interface GeoPoint {
  latitude: number;
  longitude: number;
}

interface TrackingData {
  isActive: boolean;
  courier: GeoPoint | null;
  destination: GeoPoint | null;
  updatedAt: string | null;
  distanceKm: number | null;
  etaMinutes: number | null;
  stage: TrackingStage | null;
}

type TrackingStage = "Left" | "Midway" | "Close" | "Arrived";

const journey: Array<{ stage: TrackingStage; label: string }> = [
  { stage: "Left", label: "Saiu" },
  { stage: "Midway", label: "No meio do caminho" },
  { stage: "Close", label: "A 2 min" },
  { stage: "Arrived", label: "Chegou" },
];

const refreshMs = 10_000;
const staleSeconds = 90;

function icon(leaflet: typeof import("leaflet"), label: string, className: string) {
  return leaflet.divIcon({ className: "", html: `<span class="map-pin ${className}">${label}</span>`, iconSize: [36, 36], iconAnchor: [18, 18] });
}

export function DeliveryMap({ code, dispatched }: { code: string; dispatched: boolean }) {
  const [data, setData] = useState<TrackingData | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const host = useRef<HTMLDivElement>(null);
  const map = useRef<LeafletMap | null>(null);
  const courierPin = useRef<Marker | null>(null);
  const destinationPin = useRef<Marker | null>(null);

  useEffect(() => {
    let alive = true;

    async function load() {
      try {
        const response = await fetch(`${API_BASE_URL}/api/public/orders/${encodeURIComponent(code)}/tracking`, { cache: "no-store" });

        if (response.ok && alive) {
          setData((await response.json()) as TrackingData);
          setNow(Date.now());
        }
      } catch {
        // O mapa é um extra da página: uma falha de rede só espera a próxima tentativa.
      }
    }

    void load();
    const timer = window.setInterval(load, refreshMs);

    return () => {
      alive = false;
      window.clearInterval(timer);
    };
  }, [code]);

  const courier = data?.courier ?? null;
  const destination = data?.destination ?? null;
  const hasPoint = Boolean(data?.isActive && (courier || destination));

  useEffect(() => {
    if (!hasPoint || !host.current) {
      return;
    }

    let cancelled = false;

    void import("leaflet").then(({ default: leaflet }) => {
      if (cancelled || !host.current) {
        return;
      }

      const first = !map.current;

      if (!map.current) {
        map.current = leaflet.map(host.current, { scrollWheelZoom: false });
        leaflet.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
          attribution: "&copy; OpenStreetMap",
          maxZoom: 19,
        }).addTo(map.current);
      }

      if (courier) {
        const point: [number, number] = [courier.latitude, courier.longitude];
        courierPin.current = courierPin.current?.setLatLng(point) ?? leaflet.marker(point, { icon: icon(leaflet, "🛵", "map-pin-courier") }).addTo(map.current);
      }

      if (destination && !destinationPin.current) {
        destinationPin.current = leaflet.marker([destination.latitude, destination.longitude], { icon: icon(leaflet, "🏠", "map-pin-home") }).addTo(map.current);
      }

      if (first) {
        const points = [courier, destination].filter((item): item is GeoPoint => item !== null).map((item): [number, number] => [item.latitude, item.longitude]);

        if (points.length > 1) {
          map.current.fitBounds(points, { padding: [40, 40], maxZoom: 16 });
        } else {
          map.current.setView(points[0], 15);
        }
      }
    });

    return () => {
      cancelled = true;
    };
  }, [hasPoint, courier, destination]);

  useEffect(() => () => {
    map.current?.remove();
    map.current = null;
  }, []);

  if (!data?.isActive) {
    return null;
  }

  const age = data.updatedAt ? Math.max(0, Math.round((now - new Date(data.updatedAt).getTime()) / 1000)) : null;
  const isStale = age !== null && age > staleSeconds;
  const arrived = data.stage === "Arrived";
  const reached = journey.findIndex((step) => step.stage === data.stage);

  return (
    <section className="tracking-card delivery-map-card">
      <h2>Sua entrega no mapa</h2>
      {dispatched && reached >= 0 ? (
        <ol className="journey">
          {journey.map((step, index) => (
            <li className={`${index < reached ? "is-done" : ""} ${index === reached ? "is-current" : ""}`} key={step.stage}>
              <i aria-hidden="true" />
              <span>{step.label}</span>
            </li>
          ))}
        </ol>
      ) : null}
      {dispatched && arrived ? (
        <p className="delivery-eta"><strong>O entregador chegou</strong></p>
      ) : data.etaMinutes !== null && !isStale ? (
        <p className="delivery-eta">
          <strong>Chega em cerca de {data.etaMinutes} min</strong>
          {data.distanceKm !== null ? <span>Faltam cerca de {data.distanceKm.toLocaleString("pt-BR")} km</span> : null}
        </p>
      ) : null}
      {hasPoint ? <div className="delivery-map" ref={host} /> : null}
      <p className="muted delivery-updated">
        {age === null ? "Aguardando o entregador iniciar o trajeto." : isStale ? "O sinal do entregador caiu. Mostrando a última posição." : `Atualizado há ${age} segundos.`}
      </p>
    </section>
  );
}
