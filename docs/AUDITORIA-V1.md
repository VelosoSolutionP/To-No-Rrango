# TO NO RANGO — Auditoria da V1 (Missão 0007, Fase 1)

Data: 2026-11-10. Base: branch `landing-e-deploy` (commit a91268d), 198 testes passando, `tsc` limpo.

Legenda: **OK** funciona e tem teste · **Parcial** existe mas incompleto · **Ausente** não encontrado · **Externo** depende de credencial ou serviço.

## Decisões do dono que valem sobre o documento

- A IA roda só dentro do ZEST. O To-No-Rango atende por regras e fala com o ZEST por API (pedido e resposta). Áudio é transcrito no ZEST (whisper local, já existe em `ZEST/backend/canais.mjs`) e chega aqui como texto.
- Recebimento só por Mercado Pago por enquanto; dinheiro e maquininha continuam para quem usa.
- Fiscal é módulo à parte, para Lucro Presumido para cima.

## Matriz de situação

| Área | Funcionalidade | Situação | Evidência | Ação |
|---|---|---|---|---|
| WhatsApp | Evolution API: conexão, QR, webhook, envio/recebimento | Externo | `WhatsAppService.cs`, webhook `/api/webhooks/evolution/{id}/{token}`; testado só contra simulador | Validar com instância real |
| WhatsApp | Fila de envio com novas tentativas | OK | `OutboxWorker.cs` (Attempts, NextAttemptAt) | Preservar |
| WhatsApp | Histórico de conversas e passagem para humano | OK | `Conversation`, `HandoffPause` 2h, palavra *atendente* | Preservar |
| WhatsApp | Venda pela conversa (categoria, item, adicionais, quantidade, carrinho, entrega/retirada, CEP, pagamento, troco, nome, confirmação) | OK | `ZestSalesFlow.cs`, `ZestSalesFlowTests.cs` | Preservar |
| WhatsApp | Pesquisa de produto por nome na conversa | Parcial | só navegação por número | Completar |
| WhatsApp | Combos e promoções na conversa | Ausente | — | Implementar |
| WhatsApp | Link do cardápio digital na conversa | Parcial | link existe no pedido; não no menu inicial | Completar |
| WhatsApp | Cliente já cadastrado: reaproveitar nome e endereço | Ausente | fluxo sempre pede CEP e nome | Implementar |
| WhatsApp | Ponto de referência no endereço | Ausente | — | Implementar |
| WhatsApp | Consultar andamento do pedido pela conversa | Parcial | aviso de status sai por mensagem; consulta ativa não | Completar |
| Áudio | Receber e transcrever áudio | Ausente aqui | `AudioNotSupported` em `WhatsAppService.cs`; transcrição existe no ZEST | Conector ZEST |
| Áudio | Resposta por voz | Ausente | — | Fica no ZEST (TTS) |
| Horário | Horário de funcionamento | Ausente | `Establishment` não tem horário | Implementar |
| Horário | Pedido fora do horário (aguardando confirmação / agendado) | Ausente | — | Implementar |
| Cardápio | Categorias, itens, imagens, descrição, preço, adicionais, disponibilidade | OK | `MenuService`, `MenuItemTests` | Preservar |
| Cardápio | Combos | Parcial | backend `MenuCombo` + `ComboParts` no DTO; sem tela no painel nem destaque no cardápio | Completar |
| Cardápio | Promoções (preço de/por) | Ausente | — | Implementar |
| Cardápio | Pesquisa e filtros | Ausente | `MenuClient.tsx` sem busca | Implementar |
| Cardápio | Carrinho, observação por item, entrega/retirada, pagamento | OK | `CartDrawer`, `CheckoutModal` | Preservar |
| Pagamentos | Pix, cartão (brick), link checkout, webhook, estorno | Externo | `PaymentService`, `MercadoPagoCheckoutTests`; E2E só contra simulador; nenhuma loja com credencial | Validar com credencial real |
| Pagamentos | Pago na entrega só por registro manual | OK | `/orders/{id}/paid` | Preservar |
| Cozinha | Kanban por status, tempo decorrido, atualização periódica | OK | `Kitchen.tsx` | Preservar |
| Cozinha | Setor/estação (bar × cozinha), atraso destacado, status por item | Ausente | — | Implementar |
| Acompanhamento | Status geral em tempo real (SSE) | OK | `/orders/{code}/events` | Preservar |
| Acompanhamento | Status individual por produto | Ausente | `OrderItem` sem status | Implementar |
| Entregas | Entregador, link de rastreio, mapa real com GPS, linha do tempo | OK | `DeliveryTracking.cs`, `DeliveryTrackingTests.cs` | Testar na rua |
| Avaliações | Restaurante e entregador | Ausente | — | Implementar |
| PDV | Caixa (abertura, fechamento, sangria, suprimento, conferência) | Ausente | — | Implementar |
| PDV | Venda de balcão | Ausente | — | Implementar |
| PDV/Fiscal | NFC-e via ACBrMonitor | Parcial / Externo | `FiscalService.IssueAsync`, `NfceIniBuilder`; sem cancelamento, inutilização nem contingência; nunca passou pela SEFAZ | Precisa certificado A1 e homologação |
| Fiscal | NF-e de entrada (importar XML) | Parcial | `NfeImportService` (prévia) + compras | Preservar |
| Fiscal | NF-e de saída (modelo 55) | Ausente | — | Depende de ACBr + certificado |
| Fiscal | SINTEGRA, EFD ICMS/IPI, EFD Contribuições | Parcial | `FiscalFiles.cs`, `FiscalTests.cs`; sem validação no PVA | Validar no PVA da Receita |
| Estoque | Itens, ficha técnica, baixa por venda, estorno, ajuste, perda, compras, mínimo e alerta | OK | `InventoryService`, `OrderLifecycle` | Preservar |
| Estoque | Inventário (contagem) | Parcial | ajuste manual existe; contagem em lote não | Completar |
| Financeiro | Contas a pagar/receber, lançamento automático do pedido e da compra, painel | OK | `FinanceService` | Preservar |
| ZEST IA | Base de conhecimento, perguntas sem resposta | Parcial | `ZestBrain`, `KnowledgeEntry` | Conector com ZEST |
| Layout | Navegação do painel | Parcial | abas atuais: Operação/Gestão/Ajustes | Reorganizar para Cardápio, Cozinha, Entregas, PDV, Fiscal, Estoque, Relatórios, Configurações |

## Dependências externas (não dá para concluir sem elas)

1. Credencial Mercado Pago de uma loja e uma compra real de valor baixo.
2. Instância Evolution real conectada a um número de teste.
3. Certificado A1, CSC da SEFAZ e ACBrMonitor rodando, para NFC-e/NF-e em homologação.
4. PVA da Receita para validar SINTEGRA e SPED.
5. Celular na rua para testar o rastreio.

## Registro de execução — 2026-11-10

### 1. Já existia e foi preservado
Cardápio, carrinho, checkout, Mercado Pago (Pix, cartão, link), webhook, venda pela conversa, fila de envio com novas tentativas, passagem para humano, Kanban por status, rastreio com GPS real, estoque com ficha técnica, financeiro, NFC-e via ACBr, SINTEGRA e SPED. Nenhuma rota, tela ou tabela foi removida; a migração `V1Operacao` só acrescenta.

### 2. Corrigido
- Trabalho de combos e áudio deixado pela metade por outro agente (não compilava): terminado.
- Pedido de balcão sem telefone não cria cliente nem tenta mandar WhatsApp.

### 3. Implementado
| Funcionalidade | Onde | Validação |
|---|---|---|
| Promoção (preço de/por) | `MenuItem.CompareAtPriceInCents`, cardápio, WhatsApp, painel | teste de domínio, E2E API, navegador |
| Combos (cadastro, exibição, economia) | `MenuCombo`, `ItemModal` do painel, cardápio | E2E API (criar, trocar linhas, recusar combo de si mesmo), navegador |
| Busca, filtros (promoções, combos, disponíveis) e ordenação no cardápio | `MenuClient.tsx` | navegador (celular 390px) |
| Ponto de referência no endereço | web e WhatsApp | teste do fluxo |
| WhatsApp: busca, promoções, combos, link do cardápio, endereço salvo do cliente, horário | `ZestSalesFlow`, `ZestBrain` | 8 testes novos + simulação pela API |
| Horário de funcionamento e pedido fora do horário (agendado, espera confirmação, ou recusado se a loja quiser) | `OpeningSchedule`, `Establishment`, configurações | 3 testes + E2E API |
| Setor por categoria (cozinha/bar) e status por item, pedido fica pronto quando todos os itens ficam prontos | `Order.AdvanceItem`, Kanban | 3 testes + E2E API + clique no navegador |
| Cozinha: filtro por setor, atraso (25 min) em vermelho, agendado, toque no item | `Kitchen.tsx` | navegador |
| Entregador gravado no pedido ao enviar o link | `Order.AssignCourier` | E2E API |
| Avaliação da loja e do entregador, médias, problemas recorrentes | `OrderRating`, `RatingService`, página do pedido, Relatórios | 2 testes + E2E API + navegador |
| PDV: abertura, suprimento, sangria, venda de balcão/mesa/entrega com troco, desconto só gerente com motivo, receber pedido do cardápio, cancelar venda, fechamento com conferência e diferença justificada, histórico | `CashSession`, `PdvService`, aba PDV | 2 testes + 19 checagens E2E + navegador |
| Áudio: transcrição com whisper local e resposta em voz com piper (pt_BR) | `Speech.cs`, `WhatsAppService` | E2E com simulador da Evolution: nota de voz → texto → venda → texto e nota de voz enviados |
| Perguntas sem resposta (lacunas) e tela para responder | `KnowledgeGap`, aba ZEST IA | E2E |

| Conector ZEST: chave por loja (guardada só como hash), rota `POST /api/zest/{loja}/messages` com `X-Zest-Key`, texto ou áudio, respostas em texto, imagem (Pix) e voz, histórico no painel, mensagem repetida ignorada | `ZestConnectorService`, aba ZEST IA | E2E em cópia do banco: venda completa por texto até o pedido, áudio com resposta em voz, atendente (18/18) |

### 4. Refatorado
Menu do painel: Operação (Visão geral, Pedidos, Cozinha, Entregas, PDV), Gestão (Cardápio, Estoque, Clientes, Distribuição, Financeiro, Fiscal, Relatórios), Inteligência (ZEST IA), Ajustes (Configurações).

### 5. Depende de credencial ou serviço externo
- Mercado Pago: credencial de uma loja e compra real.
- Evolution: instância real (testado só com simulador).
- Áudio em produção: resolvido no Docker. O serviço `whisper` (whisper.cpp, modelo base, compilado sem AVX porque o servidor não tem) sobe com o sistema, e a imagem da API já traz piper com a voz pt_BR e ffmpeg. Testado nos contêineres: transcrição em 18 s para 3,5 s de fala; voz gerada em ogg/opus.
- Fiscal: emissão direta na SEFAZ (sem ACBr) com a biblioteca gratuita Hercules.NET.NFe.NFCe (LGPL-2.1): NFC-e, NF-e de saída (55), cancelamento, inutilização, contingência offline da NFC-e com envio posterior, teste de comunicação. Certificado A1 e CSC por loja, guardados cifrados. Testado sem a SEFAZ: notas assinadas com certificado de teste, validadas nos esquemas oficiais (XSD 4.00) e assinatura conferida; API testada em cópia do banco (11/11). **Falta transmitir de verdade** com certificado A1 e CSC reais em homologação. O ACBr continua funcionando para loja sem certificado enviado.

### 6. Testado
222 testes automatizados; E2E da API (52 de 53 checagens; a que falhou era do próprio teste), E2E no navegador (17 de 18; a falha era de tempo do teste e foi confirmada à mão), E2E de áudio (7 de 7). Tudo numa cópia descartável do banco.

### 7. Ainda depende de homologação
NFC-e/NF-e na SEFAZ, SINTEGRA e SPED no PVA, rastreio em celular na rua, Mercado Pago e Evolution reais.
