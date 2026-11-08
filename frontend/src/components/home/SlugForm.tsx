"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { FEATURED_MENU_SLUG, toMenuSlug } from "@/lib/site";

export function SlugForm() {
  const router = useRouter();
  const [name, setName] = useState("");
  const [isOpening, startOpening] = useTransition();

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const slug = toMenuSlug(name) || FEATURED_MENU_SLUG;
    startOpening(() => router.push(`/menu/${slug}`));
  }

  return (
    <form className="slug-form" onSubmit={handleSubmit}>
      <label>
        <span>Já tem um cardápio? Digite o nome</span>
        <input
          autoComplete="off"
          onChange={(event) => setName(event.target.value)}
          placeholder="ex.: Açaí do Veloso"
          value={name}
        />
      </label>
      <button className="primary-button" disabled={isOpening} type="submit">
        {isOpening ? "Abrindo…" : "Abrir cardápio"}
      </button>
    </form>
  );
}
