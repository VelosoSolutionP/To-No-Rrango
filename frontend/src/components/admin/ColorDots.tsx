"use client";

import { useEffect, useState } from "react";

/** As mesmas cores do ZEST, mais o laranja com lilás do To No Rango (padrão). Ver admin-hud.css. */
const COLORS = [
  { id: "laranja", label: "Laranja com lilás (padrão)", swatch: "linear-gradient(135deg,#ff6a1a 55%,#a98bff 55%)" },
  { id: "verde", label: "Verde", swatch: "#9cc46a" },
  { id: "lilas", label: "Lilás com laranja", swatch: "linear-gradient(135deg,#b9a0f5 60%,#fb923c 60%)" },
  { id: "grafite", label: "Grafite", swatch: "#a1a1aa" },
  { id: "azul", label: "Azul com âmbar", swatch: "linear-gradient(135deg,#5b9dff 60%,#fbbf24 60%)" },
  { id: "fusao", label: "Fusão", swatch: "conic-gradient(#b9a0f5,#5b9dff,#9cc46a,#fb923c,#b9a0f5)" },
  { id: "carvao", label: "Carvão com laranja", swatch: "linear-gradient(135deg,#1b1b1e 55%,#ff782f 55%)" },
] as const;

const KEY = "tnr-cor";

function read(): string {
  try {
    return localStorage.getItem(KEY) || "laranja";
  } catch {
    return "laranja";
  }
}

function apply(color: string) {
  if (color === "laranja") document.documentElement.removeAttribute("data-cor");
  else document.documentElement.setAttribute("data-cor", color);
}

/** Bolinhas de cor do painel: passar o mouse mostra a cor, clicar salva neste aparelho. */
export function ColorDots() {
  const [chosen, setChosen] = useState("laranja");

  useEffect(() => {
    const saved = read();
    setChosen(saved);
    apply(saved);
  }, []);

  const choose = (color: string) => {
    setChosen(color);
    apply(color);
    try {
      localStorage.setItem(KEY, color);
    } catch {
      /* navegador sem armazenamento: vale só nesta visita */
    }
  };

  return (
    <div aria-label="Cor do painel" className="vx-cores" onMouseLeave={() => apply(chosen)} role="group">
      {COLORS.map((color) => (
        <button
          aria-label={`Cor ${color.label}`}
          aria-pressed={chosen === color.id}
          className={chosen === color.id ? "on" : ""}
          key={color.id}
          onClick={() => choose(color.id)}
          onMouseEnter={() => apply(color.id)}
          style={{ ["--c" as string]: color.swatch }}
          title={color.label}
          type="button"
        />
      ))}
    </div>
  );
}
