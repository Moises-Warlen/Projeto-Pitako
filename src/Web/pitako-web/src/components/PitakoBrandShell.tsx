"use client";

import Link from "next/link";
import { useState } from "react";
import { ShieldCheck, Layers, LogIn, Menu, X } from "lucide-react";

/**
 * Identidade visual exclusiva da plataforma PITAKO (vermelho, branco e preto).
 * Nunca deve ser utilizada em cardápios públicos, QR Code de mesa, Totem ou comprovantes das lojas clientes.
 */
export function PitakoBrandShell({
  title,
  subtitle,
  children
}: {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
}) {
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <div className="min-h-screen w-full max-w-full overflow-x-hidden bg-zinc-950 text-zinc-100">
      <header className="border-b border-zinc-800 bg-zinc-950/95">
        <div className="mx-auto flex max-w-7xl items-center justify-between gap-3 px-3 py-3 sm:px-6 sm:py-4">
          <div className="flex items-center gap-3">
            <Link href="/" className="flex items-center">
              <img
                src="/pitako-logo.jpg"
                alt="Logo Oficial PITAKO"
                className="h-9 w-auto rounded-lg object-contain shadow-sm sm:h-10"
              />
            </Link>
          </div>
          <button
            type="button"
            onClick={() => setMenuOpen((prev) => !prev)}
            aria-label="Abrir menu PITAKO"
            className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-zinc-800 bg-zinc-900 text-zinc-200 md:hidden"
          >
            {menuOpen ? <X className="h-4 w-4" /> : <Menu className="h-4 w-4" />}
          </button>
          <nav className="hidden flex-wrap items-center gap-3 text-sm md:flex">
            <Link href="/master" className="flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-zinc-300 hover:bg-zinc-900 hover:text-white">
              <ShieldCheck className="h-4 w-4 text-red-500" />
              Painel Master
            </Link>
            <Link href="/admin" className="flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-zinc-300 hover:bg-zinc-900 hover:text-white">
              <Layers className="h-4 w-4 text-red-500" />
              Painel Loja
            </Link>
            <Link href="/login" className="flex items-center gap-1.5 rounded-lg bg-red-600 px-3.5 py-1.5 font-medium text-white hover:bg-red-700">
              <LogIn className="h-4 w-4" />
              Entrar
            </Link>
          </nav>
        </div>
        {menuOpen && (
          <nav className="flex flex-col gap-1.5 border-t border-zinc-800 bg-zinc-900/90 px-3 py-3 text-sm md:hidden">
            <Link href="/master" onClick={() => setMenuOpen(false)} className="flex items-center gap-2 rounded-lg px-3 py-2 text-zinc-200 hover:bg-zinc-800">
              <ShieldCheck className="h-4 w-4 text-red-500" /> Painel Master
            </Link>
            <Link href="/admin" onClick={() => setMenuOpen(false)} className="flex items-center gap-2 rounded-lg px-3 py-2 text-zinc-200 hover:bg-zinc-800">
              <Layers className="h-4 w-4 text-red-500" /> Painel Loja
            </Link>
            <Link href="/login" onClick={() => setMenuOpen(false)} className="flex items-center justify-center gap-2 rounded-lg bg-red-600 px-3 py-2 font-semibold text-white">
              <LogIn className="h-4 w-4" /> Entrar
            </Link>
          </nav>
        )}
      </header>
      <main className="mx-auto w-full max-w-7xl px-3 py-5 sm:px-6 sm:py-8">
        <div className="mb-6 border-b border-zinc-800 pb-4 sm:mb-8 sm:pb-5">
          <h1 className="text-xl font-bold text-white sm:text-2xl">{title}</h1>
          {subtitle && <p className="mt-1 text-xs text-zinc-400 sm:text-sm">{subtitle}</p>}
        </div>
        {children}
      </main>
    </div>
  );
}
