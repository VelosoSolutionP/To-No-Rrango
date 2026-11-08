#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${API_URL:-}" ]]; then
  for candidate in http://127.0.0.1:8080 http://127.0.0.1:5099 http://localhost:5000; do
    if curl -sf "$candidate/health/live" >/dev/null; then
      API_URL="$candidate"
      break
    fi
  done
fi

BASE_URL="${API_URL:-http://127.0.0.1:8080}"
TENANT_ID="${TENANT_ID:-11111111-1111-1111-1111-111111111111}"
ADMIN_KEY="${ADMIN_KEY:-dev-admin-key}"
SLUG="${SLUG:-acai-do-veloso}"
OWNER_EMAIL="${OWNER_EMAIL:-dono@demo.local}"
OWNER_PASSWORD="${OWNER_PASSWORD:-demo-tonorango}"

auth=(-H "Content-Type: application/json" -H "X-Admin-Key: $ADMIN_KEY" -H "X-Tenant-Id: $TENANT_ID")

provision_access() {
  local establishment_id="$1"

  curl -sf -X PUT "$BASE_URL/api/platform/tenants/$TENANT_ID/contract" "${auth[@]}" -d "{
    \"customerId\": \"$TENANT_ID\",
    \"establishmentId\": \"$establishment_id\",
    \"startsOn\": \"2026-01-01\",
    \"expiresOn\": null,
    \"status\": \"Vigente\",
    \"modules\": [
      {\"code\": \"cardapio\"}, {\"code\": \"pedidos\"}, {\"code\": \"pagamentos\"}, {\"code\": \"clientes\"},
      {\"code\": \"estoque\"}, {\"code\": \"financeiro\"}, {\"code\": \"relatorios\"}, {\"code\": \"fiscal\"},
      {\"code\": \"whatsapp\"}, {\"code\": \"ia\"}
    ]
  }" >/dev/null

  # 409 means the demo user already exists.
  curl -s -o /dev/null -X POST "$BASE_URL/api/platform/tenants/$TENANT_ID/users" "${auth[@]}" -d "{
    \"name\": \"Dono Demo\",
    \"email\": \"$OWNER_EMAIL\",
    \"password\": \"$OWNER_PASSWORD\",
    \"role\": \"Owner\"
  }"

  echo "Painel: $OWNER_EMAIL / $OWNER_PASSWORD"
}

existing_id=$(curl -s "$BASE_URL/api/admin/establishments" "${auth[@]}" | jq -r --arg slug "$SLUG" '.[]? | select(.slug == $slug) | .id')

if [[ -n "$existing_id" ]]; then
  provision_access "$existing_id"
  echo "Seed já existe para $SLUG."
  exit 0
fi

establishment=$(curl -sf -X POST "$BASE_URL/api/admin/establishments" "${auth[@]}" -d "{
  \"tenantId\": \"$TENANT_ID\",
  \"name\": \"Açaí do Veloso\",
  \"slug\": \"$SLUG\",
  \"description\": \"Açaí cremoso, lanches e combos feitos na hora.\",
  \"phone\": \"(11) 99999-9999\",
  \"whatsApp\": \"5511999999999\",
  \"addressLine\": \"Rua das Palmeiras, 120\",
  \"city\": \"São Paulo\",
  \"state\": \"SP\",
  \"postalCode\": \"01000-000\",
  \"isPickupEnabled\": true,
  \"isDeliveryEnabled\": true,
  \"isDineInEnabled\": false,
  \"deliveryFeeInCents\": 500,
  \"minimumOrderInCents\": 1500,
  \"primaryColor\": \"#F97316\",
  \"secondaryColor\": \"#8B5CF6\"
}")

establishment_id=$(jq -r .id <<<"$establishment")
provision_access "$establishment_id"

category=$(curl -sf -X POST "$BASE_URL/api/admin/establishments/$establishment_id/categories" "${auth[@]}" -d '{
  "name": "Destaques",
  "description": "Os favoritos da casa.",
  "sortOrder": 0
}')
category_id=$(jq -r .id <<<"$category")

item=$(curl -sf -X POST "$BASE_URL/api/admin/establishments/$establishment_id/items" "${auth[@]}" -d "{
  \"categoryId\": \"$category_id\",
  \"name\": \"Açaí 500ml\",
  \"description\": \"Açaí cremoso com banana, granola e leite condensado.\",
  \"imageUrl\": null,
  \"priceInCents\": 1800,
  \"sortOrder\": 0,
  \"isAvailable\": true,
  \"optionGroups\": [
    {
      \"name\": \"Tamanho\",
      \"minSelections\": 1,
      \"maxSelections\": 1,
      \"isRequired\": true,
      \"sortOrder\": 0,
      \"options\": [
        { \"name\": \"500ml\", \"priceInCents\": 0, \"isAvailable\": true, \"sortOrder\": 0 },
        { \"name\": \"700ml\", \"priceInCents\": 600, \"isAvailable\": true, \"sortOrder\": 1 }
      ]
    },
    {
      \"name\": \"Adicionais\",
      \"minSelections\": 0,
      \"maxSelections\": 2,
      \"isRequired\": false,
      \"sortOrder\": 1,
      \"options\": [
        { \"name\": \"Granola extra\", \"priceInCents\": 200, \"isAvailable\": true, \"sortOrder\": 0 },
        { \"name\": \"Morango\", \"priceInCents\": 300, \"isAvailable\": true, \"sortOrder\": 1 },
        { \"name\": \"Paçoca\", \"priceInCents\": 150, \"isAvailable\": true, \"sortOrder\": 2 }
      ]
    }
  ]
}")

curl -sf -X POST "$BASE_URL/api/admin/establishments/$establishment_id/items" "${auth[@]}" -d "{
  \"categoryId\": \"$category_id\",
  \"name\": \"Combo do Veloso\",
  \"description\": \"Açaí 500ml + pão de queijo + suco natural.\",
  \"imageUrl\": null,
  \"priceInCents\": 2900,
  \"sortOrder\": 1,
  \"isAvailable\": true,
  \"optionGroups\": []
}" >/dev/null

menu=$(curl -sf "$BASE_URL/api/public/establishments/$SLUG/menu")
item_id=$(jq -r '.categories[0].items[] | select(.name=="Açaí 500ml") | .id' <<<"$menu")
size_700=$(jq -r '.categories[0].items[] | select(.name=="Açaí 500ml") | .optionGroups[] | select(.name=="Tamanho") | .options[] | select(.name=="700ml") | .id' <<<"$menu")
morango=$(jq -r '.categories[0].items[] | select(.name=="Açaí 500ml") | .optionGroups[] | select(.name=="Adicionais") | .options[] | select(.name=="Morango") | .id' <<<"$menu")

order=$(curl -sf -X POST "$BASE_URL/api/public/establishments/$SLUG/orders" -H "Content-Type: application/json" -d "{
  \"customerName\": \"Ana Silva\",
  \"customerPhone\": \"11999999999\",
  \"fulfillmentType\": \"Delivery\",
  \"paymentMethod\": \"Pix\",
  \"notes\": \"Sem granola\",
  \"idempotencyKey\": \"seed-order-1\",
  \"deliveryAddress\": {
    \"street\": \"Rua das Flores\",
    \"number\": \"10\",
    \"complement\": null,
    \"district\": \"Centro\",
    \"city\": \"São Paulo\",
    \"state\": \"SP\",
    \"postalCode\": \"01000-000\"
  },
  \"items\": [
    {
      \"menuItemId\": \"$item_id\",
      \"quantity\": 1,
      \"notes\": null,
      \"optionIds\": [\"$size_700\", \"$morango\"]
    }
  ]
}")

echo "Estabelecimento: $establishment_id"
echo "Categoria: $category_id"
echo "Item: $(jq -r .id <<<"$item")"
echo "Pedido: $(jq -r .code <<<"$order")"
