"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function SlugForm() {
  const router = useRouter();
  const [slug, setSlug] = useState("");

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const value = slug.trim().toLowerCase();

    if (value) {
      router.push(`/menu/${encodeURIComponent(value)}`);
    }
  }

  return (
    <form className="slug-form" onSubmit={handleSubmit}>
      <label>
        <span>Já tem um cardápio? Digite o nome</span>
        <input
          autoComplete="off"
          onChange={(event) => setSlug(event.target.value)}
          placeholder="ex.: acai-do-veloso"
          value={slug}
        />
      </label>
      <button className="primary-button" type="submit">
        Abrir cardápio
      </button>
    </form>
  );
}
