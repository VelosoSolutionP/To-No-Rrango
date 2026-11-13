import { redirect } from "next/navigation";

/**
 * Sem site próprio (13/11/2026, dono: "mata o site e deixa só o sistema"): a divulgação e o acesso
 * ficam centralizados no portal da Veloso Solution. Quem cai na raiz vai direto para o sistema,
 * que entra pelo Veloso ID.
 */
export default function Home() {
  redirect("/admin");
}
