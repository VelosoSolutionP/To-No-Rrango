import type { Metadata } from "next";
import { AdminClient } from "@/components/admin/AdminClient";

export const metadata: Metadata = {
  title: "Painel | To-No-Rango",
  description: "Painel administrativo do To-No-Rango",
};

export default function AdminPage() {
  return <AdminClient />;
}
