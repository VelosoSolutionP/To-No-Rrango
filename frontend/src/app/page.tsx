import Link from "next/link";
import { SlugForm } from "@/components/home/SlugForm";

const modules = [
  { index: "01", title: "Cardápio digital", description: "Fotos, categorias, adicionais e pedido em poucos toques, direto no celular do cliente." },
  { index: "02", title: "Venda no WhatsApp", description: "A ZEST atende, monta o pedido, calcula a entrega e envia o Pix dentro da conversa." },
  { index: "03", title: "Cozinha e entrega", description: "Fila ao vivo, status automático e o cliente acompanhando cada etapa." },
  { index: "04", title: "Gestão completa", description: "Estoque com ficha técnica, compras, financeiro e relatórios conectados aos pedidos." },
];

export default function Home() {
  return (
    <main className="lp">
      <nav className="lp-nav">
        <Link className="zx-brand" href="/">
          <span className="zx-brand-mark">TR</span>
          <div>
            <strong>TO-NO-RANGO</strong>
            <small>by ZEST</small>
          </div>
        </Link>
        <Link className="primary-button" href="/admin">Acessar painel</Link>
      </nav>

      <section className="lp-hero">
        <div>
          <p className="zx-tag"><i />Alimentação · Inteligência · Gestão</p>
          <h1>Seu delivery <span>vendendo sozinho.</span></h1>
          <p>Cardápio digital, pedidos, pagamentos e uma IA própria para o seu negócio, atendendo e vendendo pelo WhatsApp a qualquer hora.</p>
          <div className="lp-actions"><SlugForm /></div>
        </div>
        <div className="lp-orb"><div className="zx-orb" aria-hidden="true"><span>Z</span></div></div>
      </section>

      <section className="lp-grid">
        {modules.map((module) => (
          <article className="zx-card" key={module.index}>
            <p className="zx-index">{module.index}</p>
            <h2>{module.title}</h2>
            <p>{module.description}</p>
          </article>
        ))}
      </section>
    </main>
  );
}
