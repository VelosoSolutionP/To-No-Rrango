import type { Metadata } from "next";
import { MenuClient } from "@/components/menu/MenuClient";
import { api } from "@/lib/api";

export const dynamic = "force-dynamic";

interface MenuPageProps {
  params: Promise<{ slug: string }>;
}

export async function generateMetadata({ params }: MenuPageProps): Promise<Metadata> {
  const { slug } = await params;

  try {
    const menu = await api.getPublicMenu(slug);
    return {
      title: `${menu.establishment.name} · To-No-Rango`,
      description: menu.establishment.description ?? "Cardápio digital To-No-Rango.",
    };
  } catch {
    return { title: "Cardápio · To-No-Rango" };
  }
}

export default async function MenuPage({ params }: MenuPageProps) {
  const { slug } = await params;

  try {
    const menu = await api.getPublicMenu(slug);
    return <MenuClient menu={menu} />;
  } catch {
    return (
      <main className="menu-main">
        <div className="empty-state card">
          <p className="eyebrow">Cardápio indisponível</p>
          <h1>Não encontramos este estabelecimento</h1>
          <p className="muted">
            Confira o link ou tente novamente em instantes. Se o problema continuar, fale com o
            estabelecimento.
          </p>
          <a className="secondary-button" href="/">
            Voltar ao início
          </a>
        </div>
      </main>
    );
  }
}
