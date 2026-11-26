"use client";

import { CheckCircle2, CreditCard, QrCode, RefreshCw, ShoppingBag, Trash2, UtensilsCrossed } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { QrCodeMatrix } from "@/components/QrCodeMatrix";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { ModifierSelectionPayload, ProductCustomizationModal } from "@/components/storefront/ProductCustomizationModal";
import { formatBrl, ProductMediaFrame } from "@/components/storefront/ProductMediaFrame";
import { apiFetch, loadSession } from "@/lib/api";
import { MenuProduct, PublicMenuResponse } from "@/types/menu";

interface TotemSessionDto {
  id: string;
  sessionToken: string;
  fulfillmentChoice: string;
  status: string;
  itemsSubtotal: number;
  lines: Array<{
    id: string;
    productId?: string | null;
    itemTitle: string;
    customizationSummary: string;
    unitPrice: number;
    quantity: number;
    lineSubtotal: number;
    customerNotes?: string | null;
  }>;
}

interface TotemReceiptDto {
  pickupCallCode: string;
  dispatchedOrderNumber: string;
  itemsSubtotal: number;
  paymentChoice?: string | null;
  pixBrCode?: string | null;
}

export default function SelfServiceTotemPage() {
  const [slug, setSlug] = useState("acai-demo");
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: "Totem de Autoatendimento",
    primaryColorHex: "#4C1D95",
    secondaryColorHex: "#FAF5FF",
    accentColorHex: "#F59E0B"
  });
  const [menu, setMenu] = useState<PublicMenuResponse | null>(null);
  const [totemSession, setTotemSession] = useState<TotemSessionDto | null>(null);
  const [receipt, setReceipt] = useState<TotemReceiptDto | null>(null);
  const [customerName, setCustomerName] = useState("");
  const [activeCatId, setActiveCatId] = useState("all");
  const [modalProduct, setModalProduct] = useState<MenuProduct | null>(null);
  const [paymentChoice, setPaymentChoice] = useState<1 | 2 | 3>(2);
  const [idleSeconds, setIdleSeconds] = useState(120);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const loadStoreCatalog = useCallback(async (targetSlug: string) => {
    const [storeRes, menuRes] = await Promise.all([
      apiFetch<{
        tradeName: string;
        formattedCnpj: string;
        branding: { logoUrl?: string; primaryColorHex: string; secondaryColorHex: string; accentColorHex: string };
      }>(`/api/v1/public/store/${targetSlug}/`),
      apiFetch<PublicMenuResponse>(`/api/v1/public/store/${targetSlug}/menu`)
    ]);

    if (storeRes.ok && storeRes.data) {
      if (typeof document !== "undefined") document.title = `${storeRes.data.tradeName} — Totem`;
      setTheme({
        tradeName: storeRes.data.tradeName,
        formattedCnpj: storeRes.data.formattedCnpj,
        logoUrl: storeRes.data.branding.logoUrl,
        primaryColorHex: storeRes.data.branding.primaryColorHex,
        secondaryColorHex: storeRes.data.branding.secondaryColorHex,
        accentColorHex: storeRes.data.branding.accentColorHex
      });
    }
    if (menuRes.ok && menuRes.data) setMenu(menuRes.data);
  }, []);

  useEffect(() => {
    const querySlug = typeof window !== "undefined" ? new URLSearchParams(window.location.search).get("loja") : null;
    const sessionSlug = loadSession()?.storeSlug;
    const target = (querySlug || sessionSlug || "acai-demo").trim().toLowerCase();
    setSlug(target);
    void loadStoreCatalog(target);
  }, [loadStoreCatalog]);

  useEffect(() => {
    const timer = setInterval(() => {
      setIdleSeconds((s) => {
        if (s <= 1) {
          if (totemSession && !receipt) {
            setTotemSession(null);
          }
          return 120;
        }
        return s - 1;
      });
    }, 1000);
    return () => clearInterval(timer);
  }, [totemSession, receipt]);

  async function startKioskSession(choice: 1 | 2) {
    setBusy(true);
    setErrorMsg(null);
    setReceipt(null);
    const res = await apiFetch<{ sessionToken: string; fulfillmentChoice: string }>(`/api/v1/public/totem/${slug}/start`, {
      method: "POST",
      body: JSON.stringify({ choice, customerName: customerName.trim() || "Cliente Totem" })
    });
    setBusy(false);
    if (res.ok && res.data) {
      setIdleSeconds(120);
      setTotemSession({
        id: res.data.sessionToken,
        sessionToken: res.data.sessionToken,
        fulfillmentChoice: res.data.fulfillmentChoice,
        status: "Draft",
        itemsSubtotal: 0,
        lines: []
      });
    } else {
      setErrorMsg(res.errorMessage || "Não foi possível iniciar sessão no totem.");
    }
  }

  async function handleAddProduct(payload: {
    productId: string;
    variationId?: string;
    modifiers: ModifierSelectionPayload[];
    quantity: number;
    notes?: string;
  }) {
    if (!totemSession) return;
    setIdleSeconds(120);
    const res = await apiFetch<TotemSessionDto>(`/api/v1/public/totem/${slug}/sessions/${totemSession.sessionToken}/items`, {
      method: "POST",
      body: JSON.stringify({
        kind: "catalog",
        productId: payload.productId,
        variationId: payload.variationId || null,
        modifiers: payload.modifiers,
        quantity: payload.quantity,
        notes: payload.notes
      })
    });
    if (!res.ok || !res.data) throw new Error(res.errorMessage || "Erro ao adicionar item no totem.");
    setTotemSession(res.data);
    setModalProduct(null);
  }

  async function handleRemoveLine(lineId: string) {
    if (!totemSession) return;
    const res = await apiFetch<TotemSessionDto>(`/api/v1/public/totem/${slug}/sessions/${totemSession.sessionToken}/lines/${lineId}`, {
      method: "DELETE"
    });
    if (res.ok && res.data) setTotemSession(res.data);
  }

  async function handleConfirmOrder() {
    if (!totemSession || totemSession.lines.length === 0) return;
    setBusy(true);
    setErrorMsg(null);
    const res = await apiFetch<TotemReceiptDto>(`/api/v1/public/totem/${slug}/sessions/${totemSession.sessionToken}/checkout`, {
      method: "POST",
      body: JSON.stringify({ paymentChoice, idempotencyKey: `totem-${totemSession.sessionToken}` })
    });
    setBusy(false);
    if (res.ok && res.data) {
      setReceipt(res.data);
      setTotemSession(null);
    } else {
      setErrorMsg(res.errorMessage || "Erro ao finalizar pedido no totem.");
    }
  }

  const visibleProducts = (menu?.products ?? []).filter((p) => activeCatId === "all" || p.categoryId === activeCatId);

  return (
    <StoreBrandShell theme={theme} hideOpsNav>
      <div onClick={() => setIdleSeconds(120)} className="mx-auto max-w-6xl space-y-6">
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-slate-200 bg-white px-5 py-3 shadow-sm text-xs">
          <div className="flex items-center gap-2 font-bold text-slate-700">
            <UtensilsCrossed className="h-4 w-4" />
            <span>Autoatendimento Touch · Loja: <code>{slug}</code></span>
          </div>
          <span className="font-mono text-slate-500">Reinício por inatividade: {idleSeconds}s</span>
        </div>

        {errorMsg && (
          <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-bold text-rose-800">{errorMsg}</div>
        )}

        {/* TELA 3: Senha Emitida / Sucesso + PIX QR Code */}
        {receipt ? (
          <section className="mx-auto max-w-xl rounded-3xl border border-emerald-200 bg-white p-8 text-center shadow-lg">
            <CheckCircle2 className="mx-auto h-14 w-14 text-emerald-600" />
            <h1 className="mt-3 text-2xl font-extrabold text-slate-900">Pedido Enviado para a Cozinha!</h1>
            <p className="mt-1 text-sm text-slate-600">Pedido <strong>{receipt.dispatchedOrderNumber}</strong> · Total <strong>{formatBrl(receipt.itemsSubtotal)}</strong></p>

            <div className="mt-5 rounded-2xl bg-slate-900 py-5 text-white">
              <p className="text-xs uppercase tracking-widest text-slate-300">Sua Senha no Painel</p>
              <p className="mt-1 font-mono text-4xl font-black tracking-wider">{receipt.pickupCallCode}</p>
            </div>

            {receipt.pixBrCode && (
              <div className="mt-6 rounded-2xl border border-slate-200 bg-slate-50 p-5">
                <p className="mb-3 flex items-center justify-center gap-1.5 text-sm font-extrabold text-slate-900">
                  <QrCode className="h-4 w-4" /> Escaneie o PIX para Pagar Agora
                </p>
                <div className="flex justify-center">
                  <QrCodeMatrix value={receipt.pixBrCode} size={190} alt="QR Code PIX Totem" />
                </div>
              </div>
            )}

            <button
              type="button"
              onClick={() => {
                setReceipt(null);
                setCustomerName("");
              }}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="mt-6 inline-flex items-center gap-2 rounded-2xl px-8 py-4 text-base font-bold text-white shadow"
            >
              <RefreshCw className="h-5 w-5" /> Iniciar Novo Atendimento
            </button>
          </section>
        ) : !totemSession ? (
          /* TELA 1: Boas-vindas Comer Aqui ou Levar */
          <section className="mx-auto max-w-2xl rounded-3xl border border-slate-200 bg-white p-10 text-center shadow-lg">
            <h1 className="text-3xl font-extrabold text-slate-900">Toque para iniciar seu pedido</h1>
            <p className="mt-2 text-sm text-slate-600">Escolha como prefere retirar, personalize seus ingredientes e receba sua senha na hora.</p>

            <input
              value={customerName}
              onChange={(e) => setCustomerName(e.target.value)}
              placeholder="Seu primeiro nome (opcional)"
              className="mx-auto mt-6 block w-full max-w-sm rounded-2xl border border-slate-300 px-4 py-3 text-center text-base"
            />

            <div className="mt-6 grid gap-4 sm:grid-cols-2">
              <button
                type="button"
                disabled={busy}
                onClick={() => startKioskSession(1)}
                style={{ backgroundColor: "var(--store-primary)" }}
                className="rounded-2xl py-7 text-xl font-extrabold text-white shadow-md transition hover:opacity-95 disabled:opacity-50"
              >
                Comer Aqui
              </button>
              <button
                type="button"
                disabled={busy}
                onClick={() => startKioskSession(2)}
                style={{ backgroundColor: "var(--store-accent)" }}
                className="rounded-2xl py-7 text-xl font-extrabold text-white shadow-md transition hover:opacity-95 disabled:opacity-50"
              >
                Levar / Retirada
              </button>
            </div>
          </section>
        ) : (
          /* TELA 2: Catálogo Touch + Sacola do Totem */
          <div className="grid gap-6 lg:grid-cols-3">
            <div className="space-y-4 lg:col-span-2">
              <div className="flex gap-2 overflow-x-auto pb-1">
                <button
                  type="button"
                  onClick={() => setActiveCatId("all")}
                  style={activeCatId === "all" ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
                  className="shrink-0 rounded-full border bg-white px-4 py-2 text-xs font-bold"
                >
                  Tudo
                </button>
                {(menu?.categories ?? []).map((c) => (
                  <button
                    key={c.id}
                    type="button"
                    onClick={() => setActiveCatId(c.id)}
                    style={activeCatId === c.id ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
                    className="shrink-0 rounded-full border bg-white px-4 py-2 text-xs font-bold"
                  >
                    {c.name}
                  </button>
                ))}
              </div>

              <div className="grid gap-3.5 sm:grid-cols-2">
                {visibleProducts.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => setModalProduct(p)}
                    className="flex items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm hover:border-slate-400"
                  >
                    <div className="min-w-0 flex-1">
                      <h3 className="text-base font-extrabold text-slate-900">{p.name}</h3>
                      {p.description && <p className="mt-0.5 line-clamp-2 text-xs text-slate-500">{p.description}</p>}
                      <p className="mt-2 font-mono text-sm font-extrabold text-slate-900">{formatBrl(p.effectivePrice)}</p>
                    </div>
                    <ProductMediaFrame src={p.primaryImageUrl} alt={p.name} className="h-20 w-20 shrink-0 rounded-xl" iconSize={24} />
                  </button>
                ))}
              </div>
            </div>

            <aside className="flex flex-col justify-between rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
              <div>
                <h2 className="flex items-center gap-2 text-lg font-extrabold text-slate-900">
                  <ShoppingBag className="h-5 w-5" /> Seu Pedido ({totemSession.lines.length})
                </h2>
                <div className="mt-3 space-y-2.5 text-xs">
                  {totemSession.lines.map((l) => (
                    <div key={l.id} className="flex items-start justify-between gap-2 rounded-xl bg-slate-50 p-3">
                      <div className="min-w-0 flex-1">
                        <p className="font-bold text-slate-900">{l.quantity}x {l.itemTitle}</p>
                        {l.customizationSummary && <p className="text-slate-500">{l.customizationSummary}</p>}
                        <p className="mt-1 font-mono font-bold text-slate-900">{formatBrl(l.lineSubtotal)}</p>
                      </div>
                      <button type="button" onClick={() => handleRemoveLine(l.id)} className="text-rose-600"><Trash2 className="h-4 w-4" /></button>
                    </div>
                  ))}
                </div>
              </div>

              <div className="mt-6 space-y-4 border-t pt-4">
                <div className="flex justify-between text-lg font-extrabold text-slate-900">
                  <span>Total</span>
                  <span className="font-mono">{formatBrl(totemSession.itemsSubtotal)}</span>
                </div>

                <div>
                  <p className="mb-2 flex items-center gap-1.5 text-xs font-bold text-slate-700"><CreditCard className="h-3.5 w-3.5" /> Forma de Pagamento</p>
                  <div className="grid grid-cols-3 gap-1.5 text-[11px] font-bold">
                    {[
                      { code: 2 as const, label: "PIX na Tela" },
                      { code: 1 as const, label: "Cartão Totem" },
                      { code: 3 as const, label: "Pagar Caixa" }
                    ].map((opt) => (
                      <button
                        key={opt.code}
                        type="button"
                        onClick={() => setPaymentChoice(opt.code)}
                        className={`rounded-xl border py-2 ${paymentChoice === opt.code ? "border-slate-900 bg-slate-900 text-white" : "border-slate-200"}`}
                      >
                        {opt.label}
                      </button>
                    ))}
                  </div>
                </div>

                <button
                  type="button"
                  disabled={busy || totemSession.lines.length === 0}
                  onClick={handleConfirmOrder}
                  style={{ backgroundColor: "var(--store-primary)" }}
                  className="w-full rounded-2xl py-4 text-base font-extrabold text-white shadow disabled:opacity-40"
                >
                  {busy ? "Emitindo Senha..." : "Finalizar & Emitir Senha"}
                </button>
              </div>
            </aside>
          </div>
        )}
      </div>

      <ProductCustomizationModal
        product={modalProduct}
        allModifierGroups={menu?.modifierGroups ?? []}
        onClose={() => setModalProduct(null)}
        onConfirm={handleAddProduct}
      />
    </StoreBrandShell>
  );
}
