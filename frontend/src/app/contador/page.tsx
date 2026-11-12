import type { Metadata } from "next";
import { AccountantPortal } from "@/components/accountant/AccountantPortal";

export const metadata: Metadata = {
  title: "Portal do contador | To-No-Rango",
  description: "Dados fiscais das empresas atendidas pelo escritório",
};

export default function AccountantPage() {
  return <AccountantPortal />;
}
