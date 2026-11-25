"use client";

import { useEffect, useState } from "react";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { apiFetch } from "@/lib/api";

export default function SelfServiceTotemPage() {
  const [idleSeconds, setIdleSeconds] = useState(120);
  const [ticketCode, setTicketCode] = useState<string | null>(null);
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: "Totem de Autoatendimento",
    primaryColorHex: "#4C1D95",
    secondaryColorHex: "#FAF5FF",
    accentColorHex: "#F59E0B"
  });

  useEffect(() => {
    apiFetch<{
      tradeName: string;
      formattedCnpj: string;
      branding: { logoUrl?: string; primaryColorHex: string; secondaryColorHex: string; accentColorHex: string };
    }>("/api/v1/public/store/acai-demo").then((res) => {
      if (res.ok && res.data) {
        setTheme({
          tradeName: res.data.tradeName,
          formattedCnpj: res.data.formattedCnpj,
          logoUrl: res.data.branding.logoUrl,
          primaryColorHex: res.data.branding.primaryColorHex,
          secondaryColorHex: res.data.branding.secondaryColorHex,
          accentColorHex: res.data.branding.accentColorHex
        });
      }
    });
  }, []);

  useEffect(() => {
    const timer = setInterval(() => {
      setIdleSeconds((s) => (s <= 1 ? 120 : s - 1));
    }, 1000);
    return () => clearInterval(timer);
  }, []);

  return (
    <StoreBrandShell theme={theme} hideOpsNav>
      <div
        onClick={() => setIdleSeconds(120)}
        className="mx-auto max-w-2xl rounded-3xl border border-slate-200 bg-white p-10 text-center shadow-lg"
      >
        <p className="text-xs font-semibold uppercase tracking-wider text-slate-400">
          Sessão Touch · Reinício automático por inatividade em {idleSeconds}s
        </p>
        <h1 className="mt-3 text-3xl font-extrabold text-slate-900">Toque para iniciar seu pedido</h1>
        <p className="mt-2 text-sm text-slate-600">
          Consumo no local ou retirada no balcão com emissão imediata de senha.
        </p>

        <div className="mt-8 grid gap-4 sm:grid-cols-2">
          <button
            type="button"
            onClick={() => setTicketCode("T-0042")}
            style={{ backgroundColor: "var(--store-primary)" }}
            className="rounded-2xl py-6 text-lg font-bold text-white shadow"
          >
            Comer Aqui
          </button>
          <button
            type="button"
            onClick={() => setTicketCode("T-0043")}
            style={{ backgroundColor: "var(--store-accent)" }}
            className="rounded-2xl py-6 text-lg font-bold text-white shadow"
          >
            Levar / Retirada
          </button>
        </div>

        {ticketCode && (
          <div className="mt-6 rounded-2xl border border-emerald-300 bg-emerald-50 p-4 text-emerald-900">
            Senha de exemplo de interface pronta para vinculação com <code>/api/v1/totem/sessions</code>:{" "}
            <strong className="font-mono">{ticketCode}</strong>
          </div>
        )}
      </div>
    </StoreBrandShell>
  );
}
