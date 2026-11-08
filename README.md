# To-No-Rango

Plataforma comercial de cardápio digital, pedidos, pagamentos, atendimento automatizado, controle de estoque e funcionalidades fiscais da **Veloso Solution**.

- **Domínio:** https://to-no-rango.velososolution.com.br
- **Repositório:** https://github.com/VelosoSolutionP/To-No-Rrango.git

## Fase atual

| Fase | Escopo | Estado |
|---|---|---|
| 1 — Fundação | Clean Architecture .NET 10, EF Core, Redis, health, Docker, frontend, testes | **Concluída** |
| 2 — Portal e contratos | Contrato único, vigência, módulos, limites, bloqueio/renovação | **Entregue no To-No-Rango**; o Portal ainda precisa publicar o contrato no endpoint |
| 3 — Estabelecimentos e catálogo | Estabelecimento, categorias, itens, grupos/opções, cardápio público | **Backend + frontend público concluídos** |
| 4 — Pedidos e tracking | Carrinho/checkout, criação de pedido, status, rastreio público | **Backend + frontend público concluídos** |
| 5 — Operação | Login JWT, cozinha, Mercado Pago, clientes/fornecedores, estoque, compras, financeiro, relatórios | **Entregue** |
| 6 — ZEST | Venda completa pelo WhatsApp, conexão por QR Code, atendimento e conhecimento por estabelecimento, LLM opcional | **Texto entregue**; áudio pendente |
| 7 — Fiscal | Importação de NF-e de entrada | **Entrada entregue**; emissão via ACBr, SPED e SINTEGRA pendentes |

## Stack

**Backend** (`src/`): .NET 10, C#, ASP.NET Core Web API, Entity Framework Core, PostgreSQL, Redis, Clean Architecture.

**Frontend** (`frontend/`): Next.js 15, React 19, TypeScript.

## Estrutura

```
To -No-Rango/
├── src/
│   ├── ToNoRango.Domain/          # Agregados e regras de domínio
│   ├── ToNoRango.Application/     # Casos de uso
│   ├── ToNoRango.Infrastructure/  # EF Core, Redis, tenant context, cache
│   ├── ToNoRango.Api/             # Endpoints, health, OpenAPI, Dockerfile
│   └── ToNoRango.SharedKernel/    # Primitivos, resultados, eventos, abstrações
├── tests/
│   └── ToNoRango.Tests/           # Testes unitários
├── frontend/                       # Next.js
├── scripts/seed-dev.sh             # Seed idempotente de demonstração
├── docker-compose.yml
└── ToNoRango.sln
```

## Como executar

Copie `.env.example` para `.env` e preencha as chaves antes de subir os serviços.

### Docker Compose (recomendado)

```bash
docker compose up -d --build postgres redis api
```

- API: `http://localhost:8080`
- Health: `http://localhost:8080/health/live` e `/health/ready`
- OpenAPI/Scalar: `http://localhost:8080/scalar/v1`
- Migrations são aplicadas automaticamente em `Development`.

Popular dados de demonstração:

```bash
./scripts/seed-dev.sh
```

O script detecta a API em `8080`, `5099` ou `5000`, cria estabelecimento, categoria, itens e um pedido de exemplo. É idempotente para o slug `acai-do-veloso`.

### Backend local

```bash
ASPNETCORE_ENVIRONMENT=Development \
  Jwt__Key="$TNR_JWT_KEY" Security__EncryptionKey="$TNR_ENCRYPTION_KEY" \
  dotnet run --project src/ToNoRango.Api/ToNoRango.Api.csproj \
  --urls http://127.0.0.1:5099
```

Requer PostgreSQL e Redis:

```bash
docker compose up -d postgres redis
```

### Frontend local

```bash
cd frontend
npm install
NEXT_PUBLIC_API_URL=http://127.0.0.1:5099 npm run dev
```

Rotas públicas:

- `/` — landing
- `/menu/acai-do-veloso` — cardápio
- `/pedido/{code}` — rastreio do pedido
- `/admin` — painel administrativo (pedidos, cardápio e estabelecimento)

## Acesso ao painel

O painel usa e-mail e senha (JWT de 10 horas). O seed cria `dono@demo.local` / `demo-tonorango`.

| Perfil | Acesso |
|---|---|
| Dono | Tudo, incluindo usuários |
| Gerente | Tudo, exceto usuários |
| Atendente | Pedidos, cozinha, cadastros e atendimento |
| Cozinha | Pedidos e cozinha, sem registrar pagamento |

## Endpoints principais

A referência completa fica em `/scalar/v1`.

- **Públicos** (`/api/public`): cardápio, criação e rastreio de pedido, eventos em tempo real (`/orders/{code}/events`), Pix, cartão, cotação de entrega e CEP.
- **Login**: `POST /api/auth/login`.
- **Painel** (`/api/admin`, `Authorization: Bearer`): estabelecimentos, cardápio, pedidos, entrega, pagamentos, clientes, fornecedores, estoque, compras, financeiro, relatórios, WhatsApp e ZEST. O tenant vem do token e cada grupo exige o módulo no contrato.
- **Plataforma** (`/api/platform/tenants/{tenantId}`, `X-Admin-Key`): `PUT /contract` para o Portal publicar módulos, vigência e limites, e `POST /users` para criar o primeiro usuário.
- **Webhooks**: `POST /api/webhooks/mercadopago/{establishmentId}` e `POST /api/webhooks/evolution/{establishmentId}/{token}`.

Códigos de módulo: `cardapio`, `pedidos`, `pagamentos`, `clientes`, `estoque`, `financeiro`, `relatorios`, `fiscal`, `whatsapp`, `ia` e `ia_llm` (adicional pago). Sem contrato vigente, o painel e os novos pedidos ficam bloqueados.

## Regras de operação adotadas

Definições provisórias, porque o escopo ainda as lista como abertas:

- Mercado Pago: cada estabelecimento usa a própria conta; o token fica criptografado.
- Estoque: baixa ao confirmar o pedido pela ficha técnica, estorno ao cancelar, saldo zerado avisa mas não trava a venda. Adicionais ainda não têm ficha técnica.
- Entrega: sem regiões cadastradas vale a taxa fixa; com regiões, só os bairros listados são atendidos.
- Pedido pago online é confirmado automaticamente; pedido em dinheiro ou cartão na entrega é dado como pago ao ser entregue.
- CPF: apenas validação dos dígitos, pois não há serviço gratuito autorizado de consulta. CNPJ usa BrasilAPI e CEP usa ViaCEP.

## Configuração

- `ConnectionStrings:Postgres`, `ConnectionStrings:Redis`.
- `Cache:Enabled`, `Cache:DefaultExpirationSeconds`, `Cache:KeyPrefix`.
- `Cors:AllowedOrigins`.
- `Admin:ApiKey` — chave da plataforma; vazia desabilita o acesso da plataforma.
- `Jwt:Key` (mínimo 32 bytes), `Jwt:Issuer`, `Jwt:Audience` — obrigatórios.
- `Security:EncryptionKey` — 32 bytes em base64, obrigatória: `openssl rand -base64 32`. Trocar essa chave invalida as credenciais salvas.
- `App:PublicWebUrl` — base dos links enviados ao cliente.
- `App:PublicApiUrl` — URL pública da API; sem ela não há notificação do Mercado Pago e o status é consultado pela tela de pagamento.
- `MercadoPago:BaseUrl` — opcional, para testes.
- `Evolution:BaseUrl` e `Evolution:ApiKey` — servidor Evolution e sua chave global; vazios mantêm o WhatsApp desligado.
- `Evolution:WebhookBaseUrl` — endereço que a Evolution usa para chegar nesta API; por padrão usa `App:PublicApiUrl`.
- `Zest:BaseUrl`, `Zest:Email`, `Zest:Password` — leitura das IAs homologadas no Hotel dos Bots (no Compose: `ZEST_EMAIL` e `ZEST_PASSWORD` no `.env`).
- `Llm:BaseUrl`, `Llm:ApiKey`, `Llm:Model` — endpoint compatível com OpenAI (Ollama serve); usado só com o módulo `ia_llm`.
- `Network:TrustForwardedHeaders` — ligar atrás de proxy reverso, para o limite de requisições ver o IP real.

## Testes

```bash
dotnet run --project tests/ToNoRango.Tests/ToNoRango.Tests.csproj --no-build
```

> Neste ambiente o runner MTP não descobre os testes via `dotnet test`; o comando acima executa a suíte diretamente.

## Status

- Testes automatizados: 131 aprovados.
- Pendências: áudio no WhatsApp, emissão de NF-e via ACBr, SPED, SINTEGRA, entregadores, lançamento de pedido no balcão, horário de funcionamento e publicação do contrato pelo Portal.
