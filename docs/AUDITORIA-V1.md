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

## Registro de execução

As fases seguintes são registradas abaixo, cada uma com o que mudou e como foi validado.
