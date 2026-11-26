"use client";

import Link from "next/link";
import { Store } from "lucide-react";
import { useEffect, useState } from "react";
import { apiFetch, loadSession } from "@/lib/api";

export interface StoreBrandTheme {
  tradeName: string;
  formattedCnpj?: string;
  logoUrl?: string | null;
  primaryColorHex: string;
  secondaryColorHex: string;
  accentColorHex: string;
}

const FALLBACK_STORE: StoreBrandTheme = {
  tradeName: "Estabelecimento",
  formattedCnpj: "",
  logoUrl: null,
  primaryColorHex: "#1E293B",
  secondaryColorHex: "#F8FAFC",
  accentColorHex: "#0F766E"
};

/**
 * Moldura visual exclusiva do estabelecimento cliente (whitelabel).
 * Aplica exclusivamente cores, logo e CNPJ da loja — zero marca PITAKO.
 */
export function StoreBrandShell({
  theme,
  activeSection,
  hideOpsNav = false,
  children
}: {
  theme?: StoreBrandTheme;
  activeSection?: string;
  hideOpsNav?: boolean;
  children: React.ReactNode;
}) {
  const [loadedTheme, setLoadedTheme] = useState<StoreBrandTheme | null>(null);

  useEffect(() => {
    if (theme) return;
    const session = loadSession();
    if (!session || session.scope !== "Establishment") return;

    let cancelled = false;
    apiFetch<{
      tradeName?: string;
      legalName?: string;
      cnpj?: string;
      draft?: {
        logoUrl?: string | null;
        primaryColorHex?: string;
        secondaryColorHex?: string;
        accentColorHex?: string;
      };
    }>("/api/v1/store/branding", {}, session.accessToken).then((res) => {
      if (cancelled || !res.ok || !res.data) return;
      const d = res.data;
      setLoadedTheme({
        tradeName: d.tradeName || d.legalName || "Estabelecimento",
        formattedCnpj: d.cnpj || "",
        logoUrl: d.draft?.logoUrl ?? null,
        primaryColorHex: d.draft?.primaryColorHex || "#1E293B",
        secondaryColorHex: d.draft?.secondaryColorHex || "#F8FAFC",
        accentColorHex: d.draft?.accentColorHex || "#0F766E"
      });
    });

    return () => {
      cancelled = true;
    };
  }, [theme]);

  const resolvedTheme = theme ?? loadedTheme ?? FALLBACK_STORE;

  useEffect(() => {
    if (typeof document !== "undefined" && resolvedTheme.tradeName && resolvedTheme.tradeName !== "Estabelecimento") {
      document.title = resolvedTheme.tradeName;
    }
  }, [resolvedTheme.tradeName]);

  const styleVars = {
    "--store-primary": resolvedTheme.primaryColorHex,
    "--store-secondary": resolvedTheme.secondaryColorHex,
    "--store-accent": resolvedTheme.accentColorHex
  } as React.CSSProperties;

  const navItems = [
    { href: "/admin", label: "Painel Loja" },
    { href: "/pdv", label: "PDV & Balança" },
    { href: "/pedidos", label: "Kanban Pedidos" },
    { href: "/salao", label: "Salão & Mesas" },
    { href: "/financeiro", label: "Financeiro & DRE" }
  ];

  return (
    <div style={styleVars} className="min-h-screen bg-slate-50 text-slate-900">
      <header style={{ backgroundColor: "var(--store-primary)" }} className="text-white shadow">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-4 px-6 py-4">
          <div className="flex items-center gap-3">
            {resolvedTheme.logoUrl ? (
              <img
                src={resolvedTheme.logoUrl}
                alt={`Logo ${resolvedTheme.tradeName}`}
                className="h-10 w-10 rounded-lg bg-white/10 object-contain p-1"
              />
            ) : (
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-white/15">
                <Store className="h-5 w-5" />
              </div>
            )}
            <div>
              <p className="text-base font-bold leading-tight">{resolvedTheme.tradeName}</p>
              {resolvedTheme.formattedCnpj && (
                <p className="text-xs opacity-80">CNPJ {resolvedTheme.formattedCnpj}</p>
              )}
            </div>
          </div>

          {!hideOpsNav && (
            <nav className="flex flex-wrap items-center gap-2 text-sm">
              {navItems.map((item) => (
                <Link
                  key={item.href}
                  href={item.href}
                  className={`rounded-lg px-3 py-1.5 transition ${
                    activeSection === item.href
                      ? "bg-white/25 font-semibold text-white"
                      : "text-white/85 hover:bg-white/15 hover:text-white"
                  }`}
                >
                  {item.label}
                </Link>
              ))}
            </nav>
          )}
        </div>
      </header>
      <main className="mx-auto max-w-7xl px-6 py-8">{children}</main>
    </div>
  );
}
