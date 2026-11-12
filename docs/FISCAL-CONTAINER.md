# Camada fiscal do ZEST em container próprio

A mesma imagem da API roda em dois papéis:

| Papel (`App__Role`) | O que atende |
|---|---|
| `all` (padrão, container `api`) | Tudo: painel, cardápio, pedidos e também a camada fiscal |
| `fiscal` (container `fiscal`) | Só `/api/fiscal/v1` (API fiscal), `/api/accountant` (portal de parceiros), `/api/platform/accountants` e `/health`. Envia os webhooks fiscais. Não migra o banco. |

Enquanto o container `fiscal` não estiver ligado, nada muda: o `api` continua atendendo tudo.

## Ligar

1. No `.env`, acrescente `TNR_FISCAL_WORKER_NA_API=false`. Assim só o container fiscal envia os webhooks, e nunca os dois juntos.
2. Suba os dois:
   ```
   cd "/home/veloso/Veloso/VelosoSolution/To -No-Rango" && docker compose -f docker-compose.yml -f docker-compose.prod.yml --profile fiscal up -d --build --no-deps api fiscal
   ```
3. No túnel (`~/.cloudflared/tonorango/config.yml`), coloque esta regra **antes** da regra de `/api`:
   ```yaml
     - hostname: to-no-rango.velososolution.com.br
       path: ^/api/(fiscal/v1|accountant|platform/accountants)(/.*)?$
       service: http://fiscal:8080
   ```
   Depois reinicie o túnel: `docker compose -f docker-compose.yml -f docker-compose.prod.yml restart tunnel`.
4. Confira: `curl -s https://to-no-rango.velososolution.com.br/api/fiscal/v1/me` deve responder 401 (sem chave).

O endereço público não muda: quem integra continua chamando o mesmo domínio.

## Voltar atrás

Tire a regra do túnel, apague `TNR_FISCAL_WORKER_NA_API` do `.env` e rode `docker compose ... stop fiscal` e depois `up -d --no-deps api`.
