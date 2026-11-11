# To No Rango — Diagnóstico (Etapa 1 do Escopo Mestre `milagre.md`)

Data: 2026-11-11. Base: branch `landing-e-deploy`, commit 94919ee, publicado em produção. Nenhum código alterado nesta etapa.

## 1. O que existe

| Camada | Tecnologia | Onde |
|---|---|---|
| Backend | .NET 10, ASP.NET Core minimal API, Clean Architecture (Domain, Application, Infrastructure, Api, SharedKernel) | `src/` |
| Banco | PostgreSQL 18 + EF Core, um schema por módulo; Redis para cache | `src/ToNoRango.Infrastructure/Persistence` |
| Frontend | Next.js 15, React 19, TypeScript, CSS próprio (`globals.css`, ~4.850 linhas), sem biblioteca de componentes | `frontend/` |
| Testes | xunit v3, 231 testes passando (rodar pelo executável `tests/ToNoRango.Tests/bin/Debug/net10.0/ToNoRango.Tests`; `dotnet test` não descobre os testes) | `tests/` |
| Infra | Docker Compose (api, web, postgres, redis, evolution, whisper, tunnel Cloudflare) | `docker-compose*.yml` |

Domínios no backend: Catalog, Orders, Payments, Delivery, Feedback, Pdv, Inventory, Finance, Fiscal, Parties (clientes/fornecedores), Messaging, Zest, Contracts, Identity, Establishments, Media.

## 2. Telas existentes

**Públicas**
| Rota | Tela |
|---|---|
| `/` | Landing e busca da loja |
| `/menu/[slug]` | Cardápio digital: categorias, busca, filtros, combos, promoções, carrinho, checkout |
| `/pedido/[code]` | Acompanhamento do pedido em tempo real (SSE), status por item, pagamento, avaliação |
| `/entrega/[token]` | Tela do entregador: compartilha GPS real |

**Painel `/admin`** (entrada pelo Veloso ID)
| Grupo | Aba | Componente | Situação |
|---|---|---|---|
| Operação | Visão geral | `Overview.tsx` | Só fila por status; sem faturamento, mesas, atrasos, estoque crítico |
| Operação | Pedidos | `OrdersBoard.tsx` | Kanban, pagamento manual, estorno, envio ao entregador |
| Operação | Cozinha | `Kitchen.tsx` | Kanban por setor (cozinha/bar), item a item, atraso em vermelho |
| Operação | PDV | `Pdv.tsx` | Caixa, balcão, mesas com comanda, garçom, retirada, entrega |
| Operação | Distribuição | `Distribution.tsx`, `LiveDeliveries.tsx` | Entregadores, mapa com GPS real |
| Cadastros | Produtos | `Products.tsx` | Cadastro central (venda, ficha técnica, compra, alíquotas) |
| Cadastros | Cardápio | `AdminMenu.tsx`, `ItemModal.tsx` | Categorias, itens, adicionais, combos, promoção |
| Cadastros | Clientes / Fornecedores | `Parties.tsx` | Tabela com busca; CPF/CNPJ validado, observações |
| Gestão | Estoque | `Inventory.tsx` | Relatório, movimentações, inventário |
| Gestão | Financeiro | `Finance.tsx` | Títulos a pagar/receber, fluxo de caixa |
| Gestão | Fiscal | `Fiscal.tsx` | Existe, mas travado no menu ("em levantamento") |
| Gestão | Relatórios | `Reports.tsx`, `Ratings.tsx` | Existe, mas travado no menu |
| Inteligência | ZEST IA | `ZestIa.tsx`, `Zest.tsx` | WhatsApp (QR, resposta automática, conversas), lacunas, conhecimento, chave do conector |
| Ajustes | Configurações | `AdminEstablishment.tsx`, `Settings.tsx` | Loja, horário, entrega, pagamentos, usuários |

Estrutura do painel hoje: lateral `vx-side` (marca, menu só com texto, usuário e Sair) e cabeçalho `vx-top` (título, seletor de loja, pílula "em andamento", Ver cardápio).

## 3. Integrações

| Integração | Situação | Evidência |
|---|---|---|
| WhatsApp / Evolution API | Pronta; número particular do dono conectado em 2026-11-11, falta o número de teste da loja | `WhatsAppService.cs`, `OutboxWorker.cs`, `ZestSalesFlow.cs` |
| Mercado Pago (Pix, cartão, checkout por link, webhook, estorno) | Pronta no código; só testada contra simulador; nenhuma loja com credencial | `PaymentService`, `MercadoPagoCheckoutTests` |
| Veloso ID / Portal | Entrada no painel pelo Portal; contrato por módulos | `VelosoIdEndpoints.cs`, `Contracts/` |
| Conector ZEST | `POST /api/zest/{loja}/messages` com `X-Zest-Key` | `ZestConnectorService` |
| SEFAZ (Hercules.NET, sem ACBr) e ACBrMonitor | Código pronto, nunca transmitido (sem certificado A1) | `Infrastructure/Fiscal` |
| SINTEGRA, EFD ICMS/IPI, EFD Contribuições | Geração pronta, sem validação no PVA | `FiscalFiles.cs` |
| Mapa (OpenStreetMap) | Pronto, GPS real do entregador | `DeliveryMap.tsx` |
| Transcrição (whisper) e voz (piper) | Rodando dentro do To No Rango | `docker-compose.yml` (whisper), `Api/Dockerfile` (piper), `Speech.cs` |

## 4. Funcionando · parcial · pendente

**Funcionando (preservar):** cardápio e checkout; pedidos e Kanban; cozinha item a item; acompanhamento em tempo real; venda pelo WhatsApp por texto e áudio; horário e pedido agendado; PDV com caixa, sangria, suprimento e fechamento conferido; mesas com comanda; rastreio com GPS; avaliações; estoque com ficha técnica e baixa automática; financeiro de títulos; contratos por módulo.

**Parcial:**
- Dashboard: não responde faturamento, mesas ocupadas, atrasos, estoque crítico, pendências (5.1).
- Mesas: abrir, comanda, garçom e fechar existem; faltam transferir mesa, dividir conta, juntar comandas, estado "aguardando atendimento/fechamento" e associar cliente (5.3).
- Cozinha: colunas atuais são Novos, Confirmados, Em preparo, Prontos; falta "Entregues" e a origem do pedido em destaque (5.5).
- Clientes: falta histórico de compras na ficha, preferências e fidelização (5.7).
- Relatórios: faltam desempenho por funcionário e cancelamentos; aba travada (5.12).
- Cabeçalho e menu: faltam notificações, estado da conexão do WhatsApp, acesso ao perfil, ícones no menu e recolher no computador (4.1).
- Financeiro: falta conciliação (5.9).

**Pendente por dependência externa:** número de teste do WhatsApp; credencial Mercado Pago; certificado A1 + CSC para homologar na SEFAZ; PVA da Receita; teste do rastreio na rua.

## 5. Conflitos entre o que existe e o Escopo Mestre

1. **IA hospedada no To No Rango (seção 7).** O whisper (transcrição) roda num contêiner do To No Rango, o piper (voz) roda dentro da API, e o `ZestBrain` é um motor de regras (não é IA, mas leva o nome ZEST). O escopo proíbe IA nos produtos. **Decisão do dono (2026-11-11):** isso passa para o Cadu, bot do ZEST para alimentos e bebidas, cardápio digital e pedidos no WhatsApp. O recado foi registrado no mapa do 0007 e nas responsabilidades do Cadu, ainda como "descoberto", aguardando validação. O To No Rango continua com whisper e piper até o Cadu assumir pelo conector `POST /api/zest/{loja}/messages`.
2. **Fiscal dentro do To No Rango (seção 6).** Emissão, certificados, SINTEGRA e SPED estão no mesmo backend e banco. O escopo pede Central Fiscal como produto separado, com acesso pelo Veloso ID.
3. **ODV (6.2).** A sigla não aparece em nenhum lugar do código nem dos documentos do repositório.
4. **Escolha de produto após o login (6.1).** O Veloso ID leva direto ao painel; não há tela de escolha entre To No Rango e Central Fiscal.

## 6. Não alterar

Integração WhatsApp (`WhatsAppService`, `ZestSalesFlow`, `OutboxWorker`, webhook), Mercado Pago (`PaymentService` e webhook), cardápio público e checkout, cálculo de valores do pedido, contratos por módulo, migrações e dados existentes.

## 7. Plano de reconstrução visual, por tela (Etapa 2, aguardando autorização)

Ordem proposta, do que o cliente mais vê ao que menos vê:

| # | Tela | O que muda |
|---|---|---|
| 1 | Estrutura do painel | Lateral com ícones, recolhível e mais estreita; cabeçalho compacto com loja, usuário, notificações, estado do WhatsApp e acesso ao perfil |
| 2 | Visão geral | Responder as 7 perguntas da 5.1 com dados reais, por prioridade |
| 3 | Mesas | Grade de mesas com estado, tempo, valor parcial, garçom; transferir, dividir, juntar |
| 4 | PDV | Venda em poucos cliques, balcão/mesa/retirada/delivery na mesma tela |
| 5 | Cozinha | Legível à distância, coluna Entregues, origem do pedido |
| 6 | Cardápio público | Vitrine com fotos em destaque, celular primeiro |
| 7 | Pedidos e Delivery | Mesmo padrão visual do Kanban, mapa só com dado real |
| 8 | Cadastros, Estoque, Financeiro | Padrão de tabela único com paginação |
| 9 | Relatórios | Filtros por período e funcionário |

Para cada tela a Etapa 2 entrega: captura atual, proposta desenhada, medidas, hierarquia, componentes, comportamento e versão celular/tablet.
