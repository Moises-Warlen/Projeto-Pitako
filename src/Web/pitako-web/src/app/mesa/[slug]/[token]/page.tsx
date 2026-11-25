"use client";

import { useEffect, useState } from "react";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { apiFetch } from "@/lib/api";

export default function QrTableSelfServicePage({
  params
}: {
  params: { slug: string; token: string };
}) {
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: `Mesa — ${params.slug}`,
    primaryColorHex: "#4C1D95",
    secondaryColorHex: "#FAF5FF",
    accentColorHex: "#F59E0B"
  });
  const [feedback, setFeedback] = useState<string | null>(null);

  useEffect(() => {
    apiFetch<{
      tradeName: string;
      formattedCnpj: string;
      branding: { logoUrl?: string; primaryColorHex: string; secondaryColorHex: string; accentColorHex: string };
    }>(`/api/v1/public/store/${params.slug}`).then((res) => {
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
  }, [params.slug]);

  async function callWaiter(callType: number, label: string) {
    const res = await apiFetch(
      `/api/v1/public/qr/${params.slug}/${params.token}/waiter-call`,
      {
        method: "POST",
        body: JSON.stringify({ callType, notes: label })
      }
    );
    setFeedback(res.ok ? `Chamado enviado em tempo real: ${label}.` : res.errorMessage || "Erro ao chamar garçom.");
  }

  return (
    <StoreBrandShell theme={theme} hideOpsNav>
      <div className="mx-auto max-w-lg rounded-2xl border border-slate-200 bg-white p-6 text-center shadow-sm">
        <h1 className="text-xl font-bold text-slate-900">Atendimento na Mesa (QR Code)</h1>
        <p className="mt-1 text-xs text-slate-500">
          Token da mesa: <code>{params.token}</code> — Identidade 100% exclusiva do estabelecimento.
        </p>

        <div className="mt-6 grid gap-3 sm:grid-cols-2">
          <button
            type="button"
            onClick={() => callWaiter(1, "Chamar Garçom")}
            style={{ backgroundColor: "var(--store-primary)" }}
            className="rounded-xl py-3 text-sm font-semibold text-white"
          >
            Chamar Garçom
          </button>
          <button
            type="button"
            onClick={() => callWaiter(2, "Pedir a Conta")}
            style={{ backgroundColor: "var(--store-accent)" }}
            className="rounded-xl py-3 text-sm font-semibold text-white"
          >
            Pedir a Conta
          </button>
        </div>

        {feedback && (
          <p className="mt-4 rounded-lg bg-slate-100 p-3 text-xs font-medium text-slate-800">{feedback}</p>
        )}
      </div>
    </StoreBrandShell>
  );
}
