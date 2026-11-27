"use client";

import { Bell, CheckCircle2, CreditCard, Plus, QrCode, Receipt, Utensils } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { QrCodeMatrix } from "@/components/QrCodeMatrix";
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
  const [activeCatId, setActiveCatId] = useState("all");
  const [modalProduct, setModalProduct] = useState<MenuProduct | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [splitCount, setSplitCount] = useState(1);
  const [pixData, setPixData] = useState<{ pixBrCode?: string | null; amountToPay?: number; pixKeyDisplay?: string; alreadyPaid?: boolean } | null>(null);

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

  async function handleTablePay(method: string, confirmImmediatePayment: boolean) {
    const res = await apiFetch<{
      alreadyPaid?: boolean;
      amountToPay?: number;
      remainingBalance?: number;
      pixBrCode?: string | null;
      pixKeyDisplay?: string;
      message?: string;
    }>(`/api/v1/public/qr/${params.slug}/${params.token}/pay`, {
      method: "POST",
      body: JSON.stringify({ paymentMethod: method, splitCount, confirmImmediatePayment })
    });
    if (res.ok && res.data) {
      setPixData(res.data);
      if (res.data.message) setFeedback(res.data.message);
      await loadTableData();
    } else {
      setFeedback(res.errorMessage || "Erro ao processar pagamento na mesa.");
    }
  }

  const visibleProducts = (menu?.products ?? []).filter((p) => activeCatId === "all" || p.categoryId === activeCatId);

  return (
    <StoreBrandShell theme={theme} hideOpsNav>
      <div className="mx-auto max-w-5xl space-y-6">
        <section className="rounded-3xl border border-slate-200 bg-white p-6 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <span className="rounded-full bg-indigo-100 px-3 py-1 text-xs font-extrabold text-indigo-800">
                Mesa {summary?.tableCode || params.token} · Comanda {summary?.tab.tabCode || "Ativa"}
              </span>
              <h1 className="mt-2 text-xl font-extrabold text-slate-900">Cardápio Digital & Pagamento na Mesa</h1>
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
          <section className="space-y-4 lg:col-span-2">
            <div className="flex items-center justify-between">
              <h2 className="flex items-center gap-2 text-base font-extrabold text-slate-900"><Utensils className="h-4 w-4" /> Cardápio da Mesa</h2>
            </div>
            <div className="flex gap-2 overflow-x-auto pb-1">
              <button
                type="button"
                onClick={() => setActiveCatId("all")}
                style={activeCatId === "all" ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
                className="shrink-0 rounded-full border bg-white px-3.5 py-1.5 text-xs font-bold"
              >
                Tudo
              </button>
              {(menu?.categories ?? []).map((c) => (
                <button
                  key={c.id}
                  type="button"
                  onClick={() => setActiveCatId(c.id)}
                  style={activeCatId === c.id ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
                  className="shrink-0 rounded-full border bg-white px-3.5 py-1.5 text-xs font-bold"
                >
                  {c.name}
                </button>
              ))}
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              {visibleProducts.map((p) => (
                <button key={p.id} type="button" onClick={() => setModalProduct(p)} className="flex items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-3.5 text-left shadow-sm hover:border-slate-300">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-extrabold text-slate-900">{p.name}</p>
                    {p.description && <p className="mt-0.5 line-clamp-1 text-[11px] text-slate-500">{p.description}</p>}
                    <p className="mt-1 font-mono text-xs font-bold text-emerald-700">{formatBrl(p.effectivePrice)}</p>
                    <span className="mt-1.5 inline-flex items-center gap-1 text-[11px] font-bold text-indigo-700"><Plus className="h-3 w-3" /> Pedir agora</span>
                  </div>
                  <ProductMediaFrame src={p.primaryImageUrl} alt={p.name} className="h-16 w-16 shrink-0 rounded-xl" iconSize={20} />
                </button>
              ))}
            </div>
          </section>

          <aside className="space-y-4 rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
            <h3 className="text-base font-extrabold text-slate-900">Conta da Mesa</h3>
            <div className="space-y-2 text-xs">
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
              <div className="space-y-1.5 border-t pt-3 text-xs">
                <div className="flex justify-between text-slate-600"><span>Consumo</span><span className="font-mono">{formatBrl(summary.tab.itemsSubtotal)}</span></div>
                <div className="flex justify-between text-slate-600"><span>Serviço (10%)</span><span className="font-mono">{formatBrl(summary.tab.serviceChargeAmount)}</span></div>
                <div className="flex justify-between text-base font-extrabold text-slate-900"><span>Saldo Atual</span><span className="font-mono">{formatBrl(summary.tab.remainingBalance)}</span></div>
              </div>
            )}

            {summary && summary.tab.remainingBalance > 0 && (
              <div className="space-y-3 border-t border-slate-200 pt-4 text-xs">
                <div className="flex items-center justify-between">
                  <span className="font-bold text-slate-700">Dividir conta:</span>
                  <select
                    value={splitCount}
                    onChange={(e) => setSplitCount(Number(e.target.value))}
                    className="rounded-lg border border-slate-200 px-2 py-1 font-bold"
                  >
                    {[1, 2, 3, 4, 5, 6].map((n) => (
                      <option key={n} value={n}>{n}x ({formatBrl(summary.tab.remainingBalance / n)} cada)</option>
                    ))}
                  </select>
                </div>

                <div className="grid grid-cols-2 gap-2">
                  <button
                    type="button"
                    onClick={() => void handleTablePay("PIX", false)}
                    className="inline-flex items-center justify-center gap-1 rounded-xl bg-emerald-600 py-2.5 font-extrabold text-white hover:bg-emerald-700"
                  >
                    <QrCode className="h-3.5 w-3.5" /> Pagar PIX QR
                  </button>
                  <button
                    type="button"
                    onClick={() => void handleTablePay("Cartao", true)}
                    style={{ backgroundColor: "var(--store-primary)" }}
                    className="inline-flex items-center justify-center gap-1 rounded-xl py-2.5 font-extrabold text-white hover:opacity-90"
                  >
                    <CreditCard className="h-3.5 w-3.5" /> Quitar Cartão
                  </button>
                </div>
              </div>
            )}

            {pixData?.pixBrCode && !pixData.alreadyPaid && (
              <div className="rounded-2xl border border-emerald-200 bg-emerald-50/70 p-4 text-center text-xs space-y-2.5">
                <p className="font-extrabold text-emerald-900">
                  PIX Gerado: {formatBrl(pixData.amountToPay ?? 0)}
                </p>
                <div className="flex justify-center">
                  <QrCodeMatrix value={pixData.pixBrCode} size={150} alt="QR Code PIX Mesa" />
                </div>
                <button
                  type="button"
                  onClick={() => void handleTablePay("PIX", true)}
                  className="inline-flex w-full items-center justify-center gap-1.5 rounded-xl bg-emerald-600 py-2 font-extrabold text-white hover:bg-emerald-700"
                >
                  <CheckCircle2 className="h-4 w-4" /> Confirmar PIX Pago
                </button>
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
