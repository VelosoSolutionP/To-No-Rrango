"use client";

import { useEffect } from "react";

/**
 * Efeitos da página inicial. O conteúdo vem visível do servidor; só depois que
 * este código roda é que os blocos ficam prontos para aparecer ao rolar.
 */
export function LandingMotion() {
  useEffect(() => {
    const root = document.querySelector<HTMLElement>(".lx");
    if (!root) return;

    const nav = root.querySelector<HTMLElement>(".lx-nav");
    const updateNav = () => nav?.toggleAttribute("data-scrolled", window.scrollY > 12);
    updateNav();
    window.addEventListener("scroll", updateNav, { passive: true });

    const trackPointer = (event: PointerEvent) => {
      const card = (event.target as Element).closest<HTMLElement>("[data-glow]");
      if (!card) return;
      const box = card.getBoundingClientRect();
      card.style.setProperty("--x", `${event.clientX - box.left}px`);
      card.style.setProperty("--y", `${event.clientY - box.top}px`);
    };
    root.addEventListener("pointermove", trackPointer, { passive: true });

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) continue;
          entry.target.setAttribute("data-seen", "");
          observer.unobserve(entry.target);
        }
      },
      { rootMargin: "0px 0px -8% 0px", threshold: 0.12 },
    );
    root.querySelectorAll("[data-reveal]").forEach((element) => observer.observe(element));
    root.setAttribute("data-motion", "");

    return () => {
      window.removeEventListener("scroll", updateNav);
      root.removeEventListener("pointermove", trackPointer);
      observer.disconnect();
    };
  }, []);

  return null;
}
