"use client";

import { Bell, Plus, Receipt, Utensils } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { ModifierSelectionPayload, ProductCustomizationModal } from "@/components/storefront/ProductCustomizationModal";
import { formatBrl, ProductMediaFrame } from "@/components/storefront/ProductMediaFrame";
import { apiFetch } from "@/lib/api";
import { MenuProduct, PublicMenuResponse } from "@/types/menu";

interface QrSummaryDto {
  sessionToken: string;
  tableCode: string;
  tab: {
    id: string;
    tabCode: string;
    itemsSubtotal: number;
    serviceChargeAmount: number;
    totalAmount: number;
    remainingBalance: number;
    items: Array<{ id: string; itemTitle: string; customizationSummary: string; quantity: number; lineSubtotal: number }>;
  };
}

export default function QrTableSelfServicePage({ params }: { params: { slug: string; token: string } }) {
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: `Mesa — ${params.slug}`,
    primaryColorHex: "#4C1D95",
    secondaryColorHex: "#FAF5FF",
    accentColorHex: "#F59E0B"
  });
  const [menu, setMenu] = useState<PublicMenuResponse | null>(null);
  const [summary, setSummary] = useState<QrSummaryDto | null>(null);
  const [modalProduct, setModalProduct] = useState<MenuProduct | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);

  const loadTableData = useCallback(async () => {
    const [storeRes, sumRes, menuRes] = await Promise.all([
      apiFetch<{
        tradeName: string;
        formattedCnpj: string;
        branding: { logoUrl?: string; primaryColorHex: string; secondaryColorHex: string; accentColorHex: string };
      }>(`/api/v1/public/store/${params.slug}/`),
      apiFetch<QrSummaryDto>(`/api/v1/public/qr/${params.slug}/${params.token}/summary`),
      apiFetch<PublicMenuResponse>(`/api/v1/public/store/${params.slug}/menu`)
    ]);

    if (storeRes.ok && storeRes.data) {
      if (typeof document !== "undefined") document.title = `${storeRes.data.tradeName} — Autoatendimento na Mesa`;
      setTheme({
        tradeName: storeRes.data.tradeName,
        formattedCnpj: storeRes.data.formattedCnpj,
        logoUrl: storeRes.data.branding.logoUrl,
        primaryColorHex: storeRes.data.branding.primaryColorHex,
        secondaryColorHex: storeRes.data.branding.secondaryColorHex,
        accentColorHex: storeRes.data.branding.accentColorHex
      });
    }
    if (sumRes.ok && sumRes.data) setSummary(sumRes.data);
    if (menuRes.ok && menuRes.data) setMenu(menuRes.data);
  }, [params.slug, params.token]);

  useEffect(() => {
    void loadTableData();
  }, [loadTableData]);

  async function callWaiter(callType: number, label: string) {
    const targetToken = summary?.sessionToken || params.token;
    const res = await apiFetch(`/api/v1/public/qr/${params.slug}/${targetToken}/waiter-call`, {
      method: "POST",
      body: JSON.stringify({ callType, notes: label })
    });
    setFeedback(res.ok ? `Chamado enviado ao salão: ${label}.` : res.errorMessage || "Erro ao chamar garçom.");
  }

  async function addProductToTable(payload: {
    productId: string;
    variationId?: string;
    modifiers: ModifierSelectionPayload[];
    quantity: number;
    notes?: string;
  }) {
    const res = await apiFetch<QrSummaryDto>(`/api/v1/public/qr/${params.slug}/${params.token}/items`, {
      method: "POST",
      body: JSON.stringify({
        productId: payload.productId,
        variationId: payload.variationId || null,
        modifiers: payload.modifiers,
        quantity: payload.quantity,
        notes: payload.notes
      })
    });
    if (!res.ok || !res.data) throw new Error(res.errorMessage || "Não foi possível lançar na mesa.");
    setSummary(res.data);
    setModalProduct(null);
    setFeedback("Pedido enviado direto para sua mesa e cozinha!");
  }

  return (
    <StoreBrandShell theme={theme} hideOpsNav>
      <div className="mx-auto max-w-4xl space-y-6">
        <section className="rounded-3xl border border-slate-200 bg-white p-6 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <span className="rounded-full bg-indigo-100 px-3 py-1 text-xs font-extrabold text-indigo-800">
                Mesa {summary?.tableCode || params.token} · Comanda {summary?.tab.tabCode || "Ativa"}
              </span>
              <h1 className="mt-2 text-xl font-extrabold text-slate-900">Atendimento Digital na Mesa</h1>
            </div>
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={() => callWaiter(1, "Chamar Garçom")} style={{ backgroundColor: "var(--store-primary)" }} className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2.5 text-xs font-bold text-white">
                <Bell className="h-4 w-4" /> Chamar Garçom
              </button>
              <button type="button" onClick={() => callWaiter(2, "Pedir a Conta")} style={{ backgroundColor: "var(--store-accent)" }} className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2.5 text-xs font-bold text-white">
                <Receipt className="h-4 w-4" /> Pedir a Conta
              </button>
            </div>
          </div>

          {feedback && <p className="mt-4 rounded-xl bg-emerald-50 p-3 text-xs font-bold text-emerald-800">{feedback}</p>}
        </section>

        <div className="grid gap-6 lg:grid-cols-3">
          {/* Cardápio para pedir na mesa */}
          <section className="space-y-3 lg:col-span-2">
            <h2 className="flex items-center gap-2 text-base font-extrabold text-slate-900"><Utensils className="h-4 w-4" /> Fazer Novo Pedido para a Mesa</h2>
            <div className="grid gap-3 sm:grid-cols-2">
              {(menu?.products ?? []).map((p) => (
                <button key={p.id} type="button" onClick={() => setModalProduct(p)} className="flex items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-3.5 text-left shadow-sm hover:border-slate-300">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-extrabold text-slate-900">{p.name}</p>
                    <p className="mt-0.5 font-mono text-xs font-bold text-slate-700">{formatBrl(p.effectivePrice)}</p>
                    <span className="mt-2 inline-flex items-center gap-1 text-[11px] font-bold text-indigo-700"><Plus className="h-3 w-3" /> Pedir agora</span>
                  </div>
                  <ProductMediaFrame src={p.primaryImageUrl} alt={p.name} className="h-16 w-16 shrink-0 rounded-xl" iconSize={20} />
                </button>
              ))}
            </div>
          </section>

          {/* Resumo da Comanda da Mesa */}
          <aside className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
            <h3 className="text-base font-extrabold text-slate-900">Conta da Mesa</h3>
            <div className="mt-3 space-y-2 text-xs">
              {(summary?.tab.items ?? []).length === 0 ? (
                <p className="text-slate-500">Nenhum item lançado ainda nesta comanda.</p>
              ) : (
                summary?.tab.items.map((it) => (
                  <div key={it.id} className="flex justify-between gap-2 border-b border-slate-100 pb-2">
                    <span>{it.quantity}x {it.itemTitle}</span>
                    <span className="font-mono font-bold">{formatBrl(it.lineSubtotal)}</span>
                  </div>
                ))
              )}
            </div>
            {summary && (
              <div className="mt-4 space-y-1 border-t pt-3 text-xs">
                <div className="flex justify-between text-slate-600"><span>Consumo</span><span className="font-mono">{formatBrl(summary.tab.itemsSubtotal)}</span></div>
                <div className="flex justify-between text-slate-600"><span>Serviço (10%)</span><span className="font-mono">{formatBrl(summary.tab.serviceChargeAmount)}</span></div>
                <div className="flex justify-between text-base font-extrabold text-slate-900"><span>Saldo Atual</span><span className="font-mono">{formatBrl(summary.tab.remainingBalance)}</span></div>
              </div>
            )}
          </aside>
        </div>
      </div>

      <ProductCustomizationModal
        product={modalProduct}
        allModifierGroups={menu?.modifierGroups ?? []}
        onClose={() => setModalProduct(null)}
        onConfirm={addProductToTable}
      />
    </StoreBrandShell>
  );
}
