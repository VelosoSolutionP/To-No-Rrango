import Link from "next/link";
import { Icon } from "@/components/admin/Icon";
import { LandingMotion } from "@/components/home/LandingMotion";
import { SlugForm } from "@/components/home/SlugForm";
import { COMPANY_URL, FEATURED_MENU_PATH, WHATSAPP_URL } from "@/lib/site";
import "./landing.css";

const pillars = [
  {
    icon: "menu",
    tag: "Cardápio",
    title: "Cardápio digital",
    lead: "Bonito no celular, sem instalar nada.",
    text: "Fotos, categorias e adicionais em um link só seu. O cliente monta o pedido em poucos toques.",
  },
  {
    icon: "whatsapp",
    tag: "Atendimento",
    title: "Venda no WhatsApp",
    lead: "A ZEST conversa e fecha o pedido.",
    text: "Responde o cliente, monta o pedido, calcula a entrega e envia o Pix dentro da conversa.",
  },
  {
    icon: "kitchen",
    tag: "Operação",
    title: "Cozinha e entrega",
    lead: "Fila ao vivo do preparo à porta.",
    text: "Cada pedido cai na fila, muda de status e o cliente acompanha todas as etapas pelo link.",
  },
  {
    icon: "reports",
    tag: "Gestão",
    title: "Gestão completa",
    lead: "Venda, estoque e caixa conversando.",
    text: "Estoque com ficha técnica, compras, financeiro e relatórios ligados aos pedidos.",
  },
];

const zestPoints = [
  { title: "Responde com os dados da loja", text: "Preço, taxa de entrega e situação do pedido saem do seu cardápio, sem inventar nada." },
  { title: "Conexão pelo seu número", text: "Liga por número ou QR Code e desconecta em um clique, direto no painel." },
  { title: "Só atende quando você mandar", text: "A resposta automática começa desligada e fica nas suas mãos." },
  { title: "Proteção contra conversa em loop", text: "Se outro robô responder sem parar, a ZEST identifica e pausa o atendimento." },
];

const steps = [
  { title: "Monte o cardápio", text: "Cadastre produtos, fotos, adicionais e taxas de entrega por bairro." },
  { title: "Divulgue seu link", text: "Coloque na bio, no status e nos grupos. O cliente abre e já pede." },
  { title: "Receba o pagamento", text: "Pix e cartão pelo Mercado Pago, direto na conta da sua loja." },
  { title: "Prepare e acompanhe", text: "A cozinha vê a fila, o cliente vê o status e você vê o resultado." },
];

const features = [
  { icon: "finance", title: "Pix e cartão", text: "Cobrança confirmada automaticamente." },
  { icon: "orders", title: "Pedido ao vivo", text: "Status atualizado sem recarregar." },
  { icon: "kitchen", title: "Fila da cozinha", text: "Tela própria para o preparo." },
  { icon: "inventory", title: "Ficha técnica", text: "Baixa de insumos a cada venda." },
  { icon: "fiscal", title: "Compras por XML", text: "Importe a NF-e do fornecedor." },
  { icon: "finance", title: "Financeiro", text: "Entradas e saídas em um lugar." },
  { icon: "distribution", title: "Entrega por bairro", text: "Taxa calculada no pedido." },
  { icon: "customers", title: "Clientes e fornecedores", text: "Cadastro com validação de CPF e CNPJ." },
  { icon: "reports", title: "Relatórios", text: "O que vende mais e quanto entrou." },
];

const questions = [
  { q: "Meu cliente precisa instalar alguma coisa?", a: "Não. O cardápio abre no navegador do celular, a partir do link da sua loja." },
  { q: "Como eu recebo o dinheiro das vendas?", a: "Você conecta a sua conta do Mercado Pago no painel. Pix e cartão caem direto nela." },
  { q: "Posso usar meu número de WhatsApp?", a: "Sim. Você conecta o número pelo painel e pode desconectar quando quiser." },
  { q: "O WhatsApp já sai respondendo sozinho?", a: "Não. O atendimento automático só começa depois que a conexão estiver pronta e você ligar." },
  { q: "Controla estoque e financeiro também?", a: "Sim. Os pedidos alimentam o estoque pela ficha técnica, o financeiro e os relatórios." },
];

export default function Home() {
  return (
    <main className="lx">
      <LandingMotion />

      <header className="lx-nav">
        <div className="lx-nav-bar">
          <Link className="zx-brand" href="/">
            <span className="zx-brand-mark">TR</span>
            <div>
              <strong>TO-NO-RANGO</strong>
              <small>by ZEST</small>
            </div>
          </Link>
          <nav aria-label="Seções da página">
            <a href="#plataforma">Plataforma</a>
            <a href="#zest">ZEST</a>
            <a href="#como-funciona">Como funciona</a>
            <a href="#recursos">Recursos</a>
            <a href="#duvidas">Dúvidas</a>
          </nav>
          <div className="lx-nav-actions">
            <Link className="secondary-button" href={FEATURED_MENU_PATH}>Cardápio</Link>
            <Link className="primary-button" href="/admin">Acessar painel</Link>
          </div>
        </div>
      </header>

      <section className="lx-hero lx-wrap">
        <div className="lx-hero-copy">
          <p className="zx-tag"><i />Alimentação · Inteligência · Gestão</p>
          <h1>Seu delivery <span>vendendo sozinho.</span></h1>
          <p className="lx-lead">
            Cardápio digital, pedidos, pagamentos e atendimento automático, vendendo pelo WhatsApp a qualquer hora.
          </p>
          <SlugForm />
          <div className="lx-hero-links">
            <a href={WHATSAPP_URL} rel="noreferrer" target="_blank">Quero para o meu negócio <span aria-hidden="true">→</span></a>
            <a href="#como-funciona">Ver como funciona <span aria-hidden="true">→</span></a>
          </div>
        </div>
        <div className="lx-stage" aria-hidden="true">
          <i className="lx-ring lx-ring-a" />
          <i className="lx-ring lx-ring-b" />
          <i className="lx-spark lx-spark-a" />
          <i className="lx-spark lx-spark-b" />
          <div className="lx-symbol"><span>TR</span></div>
          <i className="lx-base" />
          <p className="lx-chip lx-chip-a"><b>01</b> Cardápio</p>
          <p className="lx-chip lx-chip-b"><b>02</b> ZEST no WhatsApp</p>
          <p className="lx-chip lx-chip-c"><b>03</b> Gestão</p>
        </div>
      </section>

      <section aria-label="O que a plataforma conecta" className="lx-strip">
        <div className="lx-wrap">
          <p>Uma operação conectada</p>
          <ul>
            <li>Cardápio</li>
            <li>WhatsApp</li>
            <li>Pagamentos</li>
            <li>Cozinha</li>
            <li>Gestão</li>
          </ul>
        </div>
      </section>

      <section className="lx-section lx-wrap" id="plataforma">
        <div className="lx-head" data-reveal>
          <p className="lx-eyebrow">01 / Plataforma</p>
          <h2>Do primeiro clique à entrega. <span>Tudo no mesmo lugar.</span></h2>
          <p>O To-No-Rango reúne o que um negócio de alimentação usa todo dia, sem planilha paralela e sem pular de sistema.</p>
        </div>
        <div className="lx-pillars">
          {pillars.map((pillar, index) => (
            <article className="lx-card" data-glow data-reveal key={pillar.title} style={{ "--i": index } as React.CSSProperties}>
              <div className="lx-card-top">
                <span className="lx-icon"><Icon name={pillar.icon} /></span>
                <small>0{index + 1}</small>
              </div>
              <p className="lx-eyebrow">{pillar.tag}</p>
              <h3>{pillar.title}</h3>
              <strong>{pillar.lead}</strong>
              <p>{pillar.text}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="lx-band" id="zest">
        <div className="lx-wrap lx-split">
          <div className="lx-phone" data-reveal>
            <header>
              <span className="lx-phone-mark">Z</span>
              <div><strong>Atendimento ZEST</strong><small>Exemplo de conversa</small></div>
            </header>
            <div className="lx-chat">
              <p className="lx-msg lx-in">Oi, tem açaí hoje?</p>
              <p className="lx-msg lx-out">Tem sim! Vou te mostrar o cardápio. Qual tamanho você prefere?</p>
              <p className="lx-msg lx-in">O de 500 ml, para entregar no Centro</p>
              <p className="lx-msg lx-out">Anotado! Já calculei a entrega do seu bairro e somei ao pedido.</p>
              <p className="lx-msg lx-out lx-pay"><b>Pix gerado</b> Pague pelo código e eu confirmo seu pedido aqui mesmo.</p>
            </div>
          </div>
          <div data-reveal>
            <p className="lx-eyebrow">02 / WhatsApp</p>
            <h2>Uma atendente que <span>conhece o seu cardápio.</span></h2>
            <p className="lx-copy">O atendimento usa os seus produtos e preços para responder no WhatsApp, enquanto você e a equipe cuidam do preparo.</p>
            <ul className="lx-points">
              {zestPoints.map((point) => (
                <li key={point.title}>
                  <i aria-hidden="true" />
                  <div><strong>{point.title}</strong><p>{point.text}</p></div>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </section>

      <section className="lx-section lx-wrap" id="como-funciona">
        <div className="lx-head lx-head-center" data-reveal>
          <p className="lx-eyebrow">03 / Como funciona</p>
          <h2>Do cadastro ao pedido <span>em quatro passos.</span></h2>
        </div>
        <ol className="lx-steps" data-reveal>
          {steps.map((step, index) => (
            <li key={step.title} style={{ "--i": index } as React.CSSProperties}>
              <span>{index + 1}</span>
              <h3>{step.title}</h3>
              <p>{step.text}</p>
            </li>
          ))}
        </ol>
      </section>

      <section className="lx-band" id="recursos">
        <div className="lx-wrap">
          <div className="lx-head" data-reveal>
            <p className="lx-eyebrow">04 / Recursos</p>
            <h2>O cardápio vende. <span>O painel organiza.</span></h2>
            <p>Cada venda atualiza o restante da operação.</p>
          </div>
          <div className="lx-features">
            {features.map((feature, index) => (
              <article className="lx-feature" data-glow data-reveal key={feature.title} style={{ "--i": index % 3 } as React.CSSProperties}>
                <span className="lx-icon"><Icon name={feature.icon} /></span>
                <div><h3>{feature.title}</h3><p>{feature.text}</p></div>
              </article>
            ))}
          </div>
        </div>
      </section>

      <section className="lx-section lx-wrap lx-faq-wrap" id="duvidas">
        <div className="lx-head" data-reveal>
          <p className="lx-eyebrow">05 / Dúvidas</p>
          <h2>Perguntas <span>frequentes.</span></h2>
        </div>
        <div className="lx-faq" data-reveal>
          {questions.map((item) => (
            <details key={item.q}>
              <summary>{item.q}</summary>
              <p>{item.a}</p>
            </details>
          ))}
        </div>
      </section>

      <section className="lx-cta">
        <div className="lx-wrap" data-reveal>
          <div>
            <p className="lx-eyebrow">06 / Próximo passo</p>
            <h2>Seu próximo pedido <span>pode vir daqui.</span></h2>
            <p>Conte como funciona o seu negócio e a gente coloca o seu cardápio no ar.</p>
          </div>
          <div className="lx-cta-actions">
            <a className="primary-button" href={WHATSAPP_URL} rel="noreferrer" target="_blank">Falar no WhatsApp</a>
            <Link className="secondary-button" href={FEATURED_MENU_PATH}>Abrir cardápio</Link>
          </div>
        </div>
      </section>

      <footer className="lx-footer">
        <div className="lx-wrap lx-footer-grid">
          <div>
            <Link className="zx-brand" href="/">
              <span className="zx-brand-mark">TR</span>
              <div>
                <strong>TO-NO-RANGO</strong>
                <small>by ZEST</small>
              </div>
            </Link>
            <p>Cardápio digital, pedidos, pagamentos e gestão para negócios de alimentação.</p>
          </div>
          <nav aria-label="Produto">
            <h3>Produto</h3>
            <a href="#plataforma">Plataforma</a>
            <a href="#zest">ZEST no WhatsApp</a>
            <a href="#recursos">Recursos</a>
            <Link href={FEATURED_MENU_PATH}>Cardápio</Link>
          </nav>
          <nav aria-label="Veloso Solution">
            <h3>Veloso Solution</h3>
            <a href={COMPANY_URL}>Conheça a empresa</a>
            <a href={`${COMPANY_URL}/privacidade/`}>Privacidade</a>
            <a href={`${COMPANY_URL}/termos/`}>Termos de uso</a>
          </nav>
          <nav aria-label="Contato e acesso">
            <h3>Contato e acesso</h3>
            <a href={WHATSAPP_URL} rel="noreferrer" target="_blank">WhatsApp</a>
            <Link href="/admin">Acessar painel</Link>
          </nav>
        </div>
        <div className="lx-wrap lx-footer-end">
          <small>© 2026 Veloso Solution · Contagem/MG</small>
          <small>Feito para quem vive de vender comida.</small>
        </div>
      </footer>
    </main>
  );
}
