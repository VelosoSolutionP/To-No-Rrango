import type { Metadata } from "next";
import { OrderTrackingClient } from "@/components/orders/OrderTrackingClient";

export const dynamic = "force-dynamic";

interface TrackingPageProps {
  params: Promise<{ code: string }>;
}

export const metadata: Metadata = {
  title: "Acompanhar pedido · To-No-Rango",
  description: "Acompanhe o preparo e a entrega do seu pedido em tempo real.",
};

export default async function TrackingPage({ params }: TrackingPageProps) {
  const { code } = await params;

  return <OrderTrackingClient code={decodeURIComponent(code)} />;
}
