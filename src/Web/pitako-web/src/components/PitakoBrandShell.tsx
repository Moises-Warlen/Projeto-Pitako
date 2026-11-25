"use client";

import Link from "next/link";
import { ShieldCheck, Layers, LogIn } from "lucide-react";

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
  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="border-b border-zinc-800 bg-zinc-950/95">
        <div className="mx-auto flex max-w-7xl items-center justify-between px-6 py-4">
          <div className="flex items-center gap-4">
            <Link href="/" className="flex items-center gap-3">
              <div
                className="flex h-10 items-center justify-center rounded border border-dashed border-red-600/70 bg-red-600/10 px-3 text-xs font-bold tracking-wider text-red-400"
                title="Espaço reservado para a logo oficial PITAKO"
              >
                LOGO PITAKO [Aguardando arquivo oficial]
              </div>
              <span className="text-lg font-bold tracking-tight text-white">
                PITAKO <span className="text-red-500">SaaS</span>
              </span>
            </Link>
          </div>
          <nav className="flex flex-wrap items-center gap-3 text-sm">
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
      </header>
      <main className="mx-auto max-w-7xl px-6 py-8">
        <div className="mb-8 border-b border-zinc-800 pb-5">
          <h1 className="text-2xl font-bold text-white">{title}</h1>
          {subtitle && <p className="mt-1 text-sm text-zinc-400">{subtitle}</p>}
        </div>
        {children}
      </main>
    </div>
  );
}
