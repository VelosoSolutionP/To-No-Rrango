/** Cardápio aberto pelos atalhos da página inicial. */
export const FEATURED_MENU_SLUG = "acai-do-veloso";
export const FEATURED_MENU_PATH = `/menu/${FEATURED_MENU_SLUG}`;

export const WHATSAPP_URL = "https://wa.me/553175536010";
export const COMPANY_URL = "https://velososolution.com.br";

/** Transforma o nome digitado no endereço do cardápio: "Açaí do Veloso" vira "acai-do-veloso". */
export function toMenuSlug(name: string): string {
  return name
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}
