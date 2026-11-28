"use client";

import { Bell, CheckCircle2, ChevronLeft, ChevronRight, CreditCard, Menu, Plus, QrCode, Receipt, Utensils, X } from "lucide-react";
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

const MESA_ITEMS_PER_PAGE = 6;

export default function QrTableSelfServicePage({ params }: { params: { slug: string; token: string } }) {
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: `Mesa · ${params.slug}`,
    primaryColorHex: "#4C1D95",
    secondaryColorHex: "#FAF5FF",
    accentColorHex: "#F59E0B"
  });
  const [menu, setMenu] = useState<PublicMenuResponse | null>(null);
  const [summary, setSummary] = useState<QrSummaryDto | null>(null);
  const [activeCatId, setActiveCatId] = useState("all");
  const [menuOpen, setMenuOpen] = useState(false);
  const [page, setPage] = useState(1);
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
      if (typeof document !== "undefined") document.title = `${storeRes.data.tradeName} | Autoatendimento na Mesa`;
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

  useEffect(() => {
    if (!feedback) return;
    const t = setTimeout(() => setFeedback(null), 2600);
    return () => clearTimeout(t);
  }, [feedback]);

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
  const totalPages = Math.max(1, Math.ceil(visibleProducts.length / MESA_ITEMS_PER_PAGE));
  const safePage = Math.min(page, totalPages);
  const paginatedProducts = visibleProducts.slice((safePage - 1) * MESA_ITEMS_PER_PAGE, safePage * MESA_ITEMS_PER_PAGE);
  const activeCatLabel =
    activeCatId === "all"
      ? `Tudo (${menu?.products.length ?? 0})`
      : menu?.categories.find((c) => c.id === activeCatId)?.name || "Cardápio";

  return (
    <StoreBrandShell theme={theme} hideOpsNav>
      <div className="mx-auto w-full max-w-5xl space-y-4 overflow-x-hidden sm:space-y-6">
        <section className="rounded-3xl border border-slate-200 bg-white p-4 shadow-sm sm:p-6">
          <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center sm:justify-between">
            <div className="min-w-0">
              <span className="inline-block rounded-full bg-indigo-100 px-3 py-1 text-xs font-extrabold text-indigo-800">
                Mesa {summary?.tableCode || params.token} · Comanda {summary?.tab.tabCode || "Ativa"}
              </span>
              <h1 className="mt-2 text-lg font-extrabold text-slate-900 sm:text-xl">Cardápio Digital &amp; Pagamento na Mesa</h1>
            </div>
            <div className="grid grid-cols-2 gap-2 sm:flex sm:flex-wrap">
              <button type="button" onClick={() => callWaiter(1, "Chamar Garçom")} style={{ backgroundColor: "var(--store-primary)" }} className="inline-flex items-center justify-center gap-1.5 rounded-xl px-3 py-2.5 text-xs font-bold text-white sm:px-4">
                <Bell className="h-4 w-4 shrink-0" /> Chamar Garçom
              </button>
              <button type="button" onClick={() => callWaiter(2, "Pedir a Conta")} style={{ backgroundColor: "var(--store-accent)" }} className="inline-flex items-center justify-center gap-1.5 rounded-xl px-3 py-2.5 text-xs font-bold text-white sm:px-4">
                <Receipt className="h-4 w-4 shrink-0" /> Pedir a Conta
              </button>
            </div>
          </div>

          {feedback && <p className="mt-4 rounded-xl bg-emerald-50 p-3 text-xs font-bold text-emerald-800">{feedback}</p>}
        </section>

        <div className="grid gap-5 lg:grid-cols-3 lg:gap-6">
          <section className="min-w-0 space-y-4 lg:col-span-2">
            <div className="rounded-2xl border border-slate-200 bg-white p-3 shadow-sm">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <button
                  type="button"
                  onClick={() => setMenuOpen((prev) => !prev)}
                  aria-expanded={menuOpen}
                  aria-label="Abrir menu de categorias da mesa"
                  style={{ backgroundColor: "var(--store-primary)" }}
                  className="inline-flex items-center gap-2 rounded-xl px-3.5 py-2 text-xs font-extrabold text-white shadow-sm"
                >
                  {menuOpen ? <X className="h-4 w-4" /> : <Menu className="h-4 w-4" />}
                  <span>Menu / Categorias</span>
                </button>
                <div className="flex items-center gap-2 text-xs">
                  <span className="max-w-[175px] truncate rounded-full bg-slate-100 px-3 py-1 font-bold text-slate-800 sm:max-w-none">
                    {activeCatLabel}
                  </span>
                </div>
              </div>

              {menuOpen && (
                <div className="mt-3 grid grid-cols-1 gap-2 border-t border-slate-100 pt-3 sm:grid-cols-2">
                  <button
                    type="button"
                    onClick={() => {
                      setActiveCatId("all");
                      setPage(1);
                      setMenuOpen(false);
                    }}
                    style={activeCatId === "all" ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
                    className={`rounded-xl border px-3 py-2.5 text-left text-xs font-bold ${
                      activeCatId === "all" ? "border-transparent shadow-sm" : "border-slate-200 bg-slate-50 text-slate-800"
                    }`}
                  >
                    Tudo ({menu?.products.length ?? 0})
                  </button>
                  {(menu?.categories ?? []).map((c) => (
                    <button
                      key={c.id}
                      type="button"
                      onClick={() => {
                        setActiveCatId(c.id);
                        setPage(1);
                        setMenuOpen(false);
                      }}
                      style={activeCatId === c.id ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
                      className={`rounded-xl border px-3 py-2.5 text-left text-xs font-bold ${
                        activeCatId === c.id ? "border-transparent shadow-sm" : "border-slate-200 bg-slate-50 text-slate-800"
                      }`}
                    >
                      {c.name}
                    </button>
                  ))}
                  <a
                    href="#conta-mesa"
                    onClick={() => setMenuOpen(false)}
                    className="rounded-xl border border-indigo-200 bg-indigo-50 px-3 py-2.5 text-left text-xs font-extrabold text-indigo-800 sm:col-span-2"
                  >
                    Ir para Conta da Mesa &amp; Pagamento ↓
                  </a>
                </div>
              )}
            </div>

            <div className="flex items-center justify-between px-1">
              <h2 className="flex items-center gap-2 text-base font-extrabold text-slate-900">
                <Utensils className="h-4 w-4" /> Cardápio da Mesa ({visibleProducts.length})
              </h2>
              {totalPages > 1 && (
                <span className="text-xs font-bold text-slate-500">
                  Página {safePage} de {totalPages}
                </span>
              )}
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              {paginatedProducts.map((p) => (
                <button key={p.id} type="button" onClick={() => setModalProduct(p)} className="flex w-full min-w-0 items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-3.5 text-left shadow-sm hover:border-slate-300">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-extrabold text-slate-900">{p.name}</p>
                    {p.description && <p className="mt-0.5 line-clamp-2 text-[11px] text-slate-500">{p.description}</p>}
                    <p className="mt-1 font-mono text-xs font-bold text-emerald-700">{formatBrl(p.effectivePrice)}</p>
                    <span className="mt-1.5 inline-flex items-center gap-1 text-[11px] font-bold text-indigo-700"><Plus className="h-3 w-3" /> Pedir agora</span>
                  </div>
                  <ProductMediaFrame src={p.primaryImageUrl} alt={p.name} className="h-16 w-16 shrink-0 rounded-xl" iconSize={20} />
                </button>
              ))}
            </div>

            {totalPages > 1 && (
              <div className="flex items-center justify-between gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-2.5 text-xs shadow-sm">
                <button
                  type="button"
                  disabled={safePage <= 1}
                  onClick={() => setPage((prev) => Math.max(1, prev - 1))}
                  className="inline-flex items-center gap-1 rounded-xl border border-slate-200 px-3 py-1.5 font-bold text-slate-700 hover:bg-slate-50 disabled:opacity-40"
                >
                  <ChevronLeft className="h-3.5 w-3.5" /> Anterior
                </button>
                <span className="font-bold text-slate-600">
                  {safePage} / {totalPages}
                </span>
                <button
                  type="button"
                  disabled={safePage >= totalPages}
                  onClick={() => setPage((prev) => Math.min(totalPages, prev + 1))}
                  className="inline-flex items-center gap-1 rounded-xl border border-slate-200 px-3 py-1.5 font-bold text-slate-700 hover:bg-slate-50 disabled:opacity-40"
                >
                  Próxima <ChevronRight className="h-3.5 w-3.5" />
                </button>
              </div>
            )}
          </section>

          <aside id="conta-mesa" className="min-w-0 space-y-4 rounded-3xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5">
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
