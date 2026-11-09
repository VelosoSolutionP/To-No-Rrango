import type { Metadata } from "next";
import { CourierClient } from "@/components/orders/CourierClient";

export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Entrega · To-No-Rango",
  robots: { index: false, follow: false },
};

export default async function CourierPage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;

  return <CourierClient token={token} />;
}
