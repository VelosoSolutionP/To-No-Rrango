import type { Metadata } from "next";
import { Inter, JetBrains_Mono, Sora } from "next/font/google";
import "./globals.css";

const body = Inter({ subsets: ["latin"], variable: "--font-body" });
const display = Sora({ subsets: ["latin"], variable: "--font-display" });
const mono = JetBrains_Mono({ subsets: ["latin"], variable: "--font-mono" });

export const metadata: Metadata = {
  title: "To-No-Rango",
  description: "Cardápio digital, pedidos e vendas no WhatsApp com a ZEST.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html className={`${body.variable} ${display.variable} ${mono.variable}`} lang="pt-BR" suppressHydrationWarning>
      <body>{children}</body>
    </html>
  );
}
