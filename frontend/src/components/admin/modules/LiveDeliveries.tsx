"use client";

import "leaflet/dist/leaflet.css";
import type { Map as LeafletMap, Marker } from "leaflet";
import { useEffect, useMemo, useRef, useState } from "react";
import type { AdminHttp } from "@/lib/admin-api";
import { Empty, ErrorMessage, ModuleHeading, messageOf } from "./shared";

interface GeoPoint {
  latitude: number;
  longitude: number;
}

interface LiveDelivery {
  orderId: string;
  orderCode: string;
  customerName: string;
  destination: string;
  courier: GeoPoint | null;
  destinationPoint: GeoPoint | null;
  updatedAt: string | null;
  distanceKm: number | null;
  etaMinutes: number | null;
}

const refreshMs = 10_000;
const staleSeconds = 90;

function ageOf(delivery: LiveDelivery, now: number) {
  return delivery.updatedAt ? Math.max(0, Math.round((now - new Date(delivery.updatedAt).getTime()) / 1000)) : null;
}

function statusOf(delivery: LiveDelivery, now: number) {
  const age = ageOf(delivery, now);

  if (age === null) {
    return { headline: "Aguardando", detail: "Entregador ainda não iniciou" };
  }

  if (age > staleSeconds) {
    return { headline: "Sem sinal", detail: "Mostrando a última posição" };
  }

  return {
    headline: delivery.etaMinutes !== null ? `Cerca de ${delivery.etaMinutes} min` : "Em rota",
    detail: delivery.distanceKm !== null ? `${delivery.distanceKm.toLocaleString("pt-BR")} km · há ${age}s` : `Atualizado há ${age}s`,
  };
}

function pin(leaflet: typeof import("leaflet"), label: string, className: string) {
  return leaflet.divIcon({ className: "", html: `<span class="map-pin ${className}">${label}</span>`, iconSize: [36, 36], iconAnchor: [18, 18] });
}

export function LiveDeliveries({ http, establishmentId }: { http: AdminHttp; establishmentId: string }) {
  const [deliveries, setDeliveries] = useState<LiveDelivery[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const host = useRef<HTMLDivElement>(null);
  const map = useRef<LeafletMap | null>(null);
  const couriers = useRef(new Map<string, Marker>());
  const homes = useRef(new Map<string, Marker>());
  const framed = useRef("");

  useEffect(() => {
    let alive = true;

    async function load() {
      try {
        const data = await http.get<LiveDelivery[]>(`/establishments/${establishmentId}/live-deliveries`);

        if (alive) {
          setDeliveries(data);
          setNow(Date.now());
          setError(null);
        }
      } catch (failure) {
        if (alive) {
          setError(messageOf(failure));
        }
      }
    }

    void load();
    const timer = window.setInterval(load, refreshMs);

    return () => {
      alive = false;
      window.clearInterval(timer);
    };
  }, [http, establishmentId]);

  const located = useMemo(() => (deliveries ?? []).filter((item) => item.courier || item.destinationPoint), [deliveries]);
  const hasMap = located.length > 0;

  useEffect(() => {
    if (!hasMap) {
      map.current?.remove();
      map.current = null;
      couriers.current.clear();
      homes.current.clear();
      framed.current = "";
      return;
    }

    let cancelled = false;

    void import("leaflet").then(({ default: leaflet }) => {
      if (cancelled || !host.current) {
        return;
      }

      if (!map.current) {
        map.current = leaflet.map(host.current, { scrollWheelZoom: false });
        leaflet.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", { attribution: "&copy; OpenStreetMap", maxZoom: 19 }).addTo(map.current);
      }

      const current = map.current;
      const moving = new Set<string>();
      const destinations = new Set<string>();

      for (const delivery of located) {
        const age = ageOf(delivery, Date.now());
        const stale = age !== null && age > staleSeconds;

        if (delivery.courier) {
          moving.add(delivery.orderCode);
          const point: [number, number] = [delivery.courier.latitude, delivery.courier.longitude];
          const icon = pin(leaflet, "🛵", stale ? "map-pin-courier map-pin-stale" : "map-pin-courier");
          const existing = couriers.current.get(delivery.orderCode);

          if (existing) {
            existing.setLatLng(point).setIcon(icon);
          } else {
            couriers.current.set(
              delivery.orderCode,
              leaflet.marker(point, { icon }).bindTooltip(delivery.orderCode.slice(-8), { permanent: true, direction: "top", offset: [0, -18] }).addTo(current),
            );
          }
        }

        if (delivery.destinationPoint) {
          destinations.add(delivery.orderCode);

          if (!homes.current.has(delivery.orderCode)) {
            homes.current.set(
              delivery.orderCode,
              leaflet.marker([delivery.destinationPoint.latitude, delivery.destinationPoint.longitude], { icon: pin(leaflet, "🏠", "map-pin-home") }).addTo(current),
            );
          }
        }
      }

      for (const [markers, keep] of [[couriers.current, moving], [homes.current, destinations]] as const) {
        for (const [code, marker] of markers) {
          if (!keep.has(code)) {
            marker.remove();
            markers.delete(code);
          }
        }
      }

      // Só reenquadra quando entra ou sai entrega, para não desfazer o zoom de quem está olhando.
      const frame = [...moving].sort().join() + "|" + [...destinations].sort().join();

      if (frame !== framed.current) {
        framed.current = frame;
        const points = located.flatMap((item) => [item.courier, item.destinationPoint]).filter((item): item is GeoPoint => item !== null).map((item): [number, number] => [item.latitude, item.longitude]);

        if (points.length > 1) {
          current.fitBounds(points, { padding: [50, 50], maxZoom: 16 });
        } else {
          current.setView(points[0], 15);
        }
      }
    });

    return () => {
      cancelled = true;
    };
  }, [hasMap, located]);

  useEffect(() => () => {
    map.current?.remove();
    map.current = null;
  }, []);

  function focus(delivery: LiveDelivery) {
    const point = delivery.courier ?? delivery.destinationPoint;

    if (point) {
      map.current?.setView([point.latitude, point.longitude], 16);
    }
  }

  return (
    <section className="admin-section">
      <ModuleHeading eyebrow="Operação" title="Rastreio" hint="Entregas em andamento no mapa. Atualiza sozinho a cada 10 segundos." />
      <ErrorMessage message={error} />
      {deliveries === null && !error ? <Empty>Carregando entregas...</Empty> : null}
      {deliveries?.length === 0 ? <Empty>Nenhuma entrega sendo rastreada agora. Em Pedidos, abra uma entrega e clique em Rastrear entrega.</Empty> : null}
      {deliveries && deliveries.length > 0 ? (
        <div className="vx-live">
          <div className="card vx-order-list">
            {deliveries.map((delivery) => {
              const status = statusOf(delivery, now);

              return (
                <button className="vx-order-row" key={delivery.orderId} onClick={() => focus(delivery)} type="button">
                  <strong>{delivery.orderCode.slice(-8)}</strong>
                  <strong>{status.headline}</strong>
                  <small>{delivery.customerName}</small>
                  <small>{status.detail}</small>
                </button>
              );
            })}
          </div>
          {hasMap ? <div className="card delivery-map vx-live-map" ref={host} /> : <Empty>O mapa aparece quando o primeiro entregador iniciar o trajeto.</Empty>}
        </div>
      ) : null}
    </section>
  );
}
