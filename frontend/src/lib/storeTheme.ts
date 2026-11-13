"use client";

import { useEffect, type CSSProperties } from "react";

/**
 * Cores da loja nas telas do cliente (cardápio, acompanhamento do pedido, entregador):
 * o dono escolhe em Configurações (bolinhas ou cor livre) e tudo deriva das duas cores.
 */
const HEX = /^#[0-9a-f]{6}$/i;

function luminance(hex: string) {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
  const lin = (c: number) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
}

export function storeThemeVars(primary?: string | null, secondary?: string | null): Record<string, string> {
  if (!primary || !HEX.test(primary)) return {};
  const second = secondary && HEX.test(secondary) ? secondary : primary;
  return {
    "--brand": primary,
    "--electric": primary,
    "--brand-hover": `color-mix(in srgb, ${primary} 82%, #fff)`,
    "--brand-light": `color-mix(in srgb, ${primary} 55%, #fff)`,
    "--brand-lighter": `color-mix(in srgb, ${primary} 35%, #fff)`,
    "--brand-dark": `color-mix(in srgb, ${primary} 75%, #000)`,
    "--brand-ink": luminance(primary) < 0.3 ? "#ffffff" : "#120a03",
    "--brand-2": second,
    "--brand-2-light": `color-mix(in srgb, ${second} 45%, #fff)`,
    "--violet": `color-mix(in srgb, ${second} 70%, #000)`,
  };
}

export function storeThemeStyle(primary?: string | null, secondary?: string | null): CSSProperties {
  return storeThemeVars(primary, secondary) as CSSProperties;
}

/** Também pinta o fundo da página (html/body ficam fora do componente). */
export function useStoreTheme(primary?: string | null, secondary?: string | null) {
  useEffect(() => {
    const vars = storeThemeVars(primary, secondary);
    const root = document.documentElement;
    for (const [name, value] of Object.entries(vars)) root.style.setProperty(name, value);
    return () => {
      for (const name of Object.keys(vars)) root.style.removeProperty(name);
    };
  }, [primary, secondary]);
}
