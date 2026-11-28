"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { StoreBrandShell } from "@/components/StoreBrandShell";
import { apiFetch as rawApiFetch, loadSession } from "@/lib/api";

async function authedApi<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = loadSession()?.accessToken;
  const res = await rawApiFetch<T>(path, options, token);
  if (!res.ok) {
    throw new Error(res.errorMessage || `Erro HTTP ${res.status}`);
  }
  return res.data as T;
}

interface PdvSessionDto {
  id: string;
  terminalCode: string;
  openedAtUtc: string;
  openingBalance: number;
  expectedCashBalance: number;
  movementsCount: number;
}

interface PdvSaleSummaryDto {
  id: string;
  receiptNumber: string;
  grossAmount: number;
  discountAmount: number;
  netAmount: number;
  status: string;
  settledAtUtc: string;
  payments: { method: string; amount: number; change: number }[];
}

interface CatalogProductQuick {
  id: string;
  name: string;
  basePrice: number;
  status: string;
  variations?: { id: string; variationName: string; price: number; isActive: boolean }[];
}

interface StoreBrandingInfo {
  legalName?: string;
  tradeName?: string;
  cnpj?: string;
  address?: string;
  draft?: { logoUrl?: string | null };
}

interface BasketLine {
  id: string;
  title: string;
  detail: string;
  quantity: number;
  unitPrice: number;
  total: number;
}

interface PaymentAllocationDraft {
  id: string;
  method: number;
  amount: string;
  tenderedAmount?: string;
}

interface ReceiptData {
  receiptNumber: string;
  settledAtUtc: string;
  lines: BasketLine[];
  grossAmount: number;
  discountAmount: number;
  netTotalAmount: number;
  payments: { methodLabel: string; allocated: number; tendered: number; change: number }[];
  totalChangeGiven: number;
}

const PAYMENT_METHODS = [
  { id: 1, label: "Dinheiro" },
  { id: 2, label: "PIX" },
  { id: 3, label: "Cartão Débito" },
  { id: 4, label: "Cartão Crédito" },
  { id: 5, label: "Vale Refeição" },
];

function brl(val: number): string {
  return val.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function readMoney(value: unknown): number {
  if (typeof value === "number") return value;
  if (value && typeof value === "object" && "amount" in value) {
    return Number((value as { amount?: number }).amount ?? 0);
  }
  return 0;
}

export default function PdvPage() {
  const [authenticated, setAuthenticated] = useState(false);
  const [terminalCode, setTerminalCode] = useState("PDV-01");
  const [openingBalance, setOpeningBalance] = useState("200.00");
  const [activeSession, setActiveSession] = useState<PdvSessionDto | null>(null);
  const [sessionSales, setSessionSales] = useState<PdvSaleSummaryDto[]>([]);
  const [branding, setBranding] = useState<StoreBrandingInfo | null>(null);
  const [catalogProducts, setCatalogProducts] = useState<CatalogProductQuick[]>([]);
  const [catalogSearch, setCatalogSearch] = useState("");

  // Weighing / scale
  const [weighProductName, setWeighProductName] = useState("Açaí por Quilo");
  const [grossGrams, setGrossGrams] = useState(450);
  const [tareGrams, setTareGrams] = useState(30);
  const [pricePerKg, setPricePerKg] = useState(64.9);

  // Basket & payment
  const [basket, setBasket] = useState<BasketLine[]>([]);
  const [discountStr, setDiscountStr] = useState("0");
  const [payments, setPayments] = useState<PaymentAllocationDraft[]>([
    { id: "pay-1", method: 2, amount: "" },
  ]);
  const [lastReceipt, setLastReceipt] = useState<ReceiptData | null>(null);

  // Cash movements & closing
  const [movementAmount, setMovementAmount] = useState("50.00");
  const [movementReason, setMovementReason] = useState("Troco / Reposição de caixa");
  const [closeCountedCash, setCloseCountedCash] = useState("");
  const [closeReport, setCloseReport] = useState<{
    terminalCode: string;
    openingBalance: number;
    expectedCashBalance: number;
    countedCashBalance: number;
    cashVariance: number;
  } | null>(null);

  const [msg, setMsg] = useState<{ type: "ok" | "err" | "info"; text: string } | null>(null);

  useEffect(() => {
    if (!msg || !authenticated) return;
    const t = setTimeout(() => setMsg(null), 2500);
    return () => clearTimeout(t);
  }, [msg, authenticated]);

  const netKg = useMemo(() => Math.max(0, (grossGrams - tareGrams) / 1000), [grossGrams, tareGrams]);
  const weighedTotal = useMemo(() => Number((netKg * pricePerKg).toFixed(2)), [netKg, pricePerKg]);

  const basketGross = useMemo(
    () => Number(basket.reduce((sum, item) => sum + item.total, 0).toFixed(2)),
    [basket]
  );
  const discountVal = useMemo(() => Math.max(0, Number(discountStr.replace(",", ".")) || 0), [discountStr]);
  const basketNet = useMemo(() => Math.max(0, Number((basketGross - discountVal).toFixed(2))), [basketGross, discountVal]);

  const loadPdvState = useCallback(async () => {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") {
      setAuthenticated(false);
      setMsg({
        type: "info",
        text: "Faça login em /login com perfil de Caixa/Gerente para abrir o PDV e registrar vendas reais.",
      });
      return;
    }
    setAuthenticated(true);
    try {
      const [pdvRes, brandRes, catRes] = await Promise.all([
        authedApi<{ activeSession: PdvSessionDto | null; sales: PdvSaleSummaryDto[] }>("/api/v1/pdv/sessions/open"),
        authedApi<StoreBrandingInfo>("/api/v1/store/branding").catch(() => null),
        authedApi<{ products: CatalogProductQuick[] }>("/api/v1/catalog/products").catch(() => ({ products: [] })),
      ]);
      setActiveSession(pdvRes.activeSession);
      setSessionSales(pdvRes.sales ?? []);
      if (pdvRes.activeSession) {
        setCloseCountedCash(pdvRes.activeSession.expectedCashBalance.toFixed(2));
      }
      if (brandRes) setBranding(brandRes);
      setCatalogProducts((catRes.products ?? []).filter((p) => p.status !== "Inactive"));
    } catch (e) {
      setMsg({ type: "err", text: e instanceof Error ? e.message : "Erro ao carregar estado do PDV." });
    }
  }, []);

  useEffect(() => {
    void loadPdvState();
  }, [loadPdvState]);

  const filteredProducts = useMemo(() => {
    const q = catalogSearch.trim().toLowerCase();
    return catalogProducts
      .filter((p) => !q || p.name.toLowerCase().includes(q))
      .slice(0, 12);
  }, [catalogProducts, catalogSearch]);

  async function handleOpenRegister() {
    try {
      const created = await authedApi<{
        id: string;
        terminalCode: string;
        openedAtUtc: string;
        openingBalance: { amount: number } | number;
        expectedCashBalance: { amount: number } | number;
      }>("/api/v1/pdv/sessions/open", {
        method: "POST",
        body: JSON.stringify({
          terminalCode: terminalCode.trim() || "PDV-01",
          openingBalance: Number(openingBalance.replace(",", ".")) || 0,
        }),
      });
      setCloseReport(null);
      setMsg({ type: "ok", text: `Caixa ${created.terminalCode} aberto com sucesso!` });
      await loadPdvState();
    } catch (err) {
      setMsg({ type: "err", text: err instanceof Error ? err.message : "Erro ao abrir caixa." });
    }
  }

  async function handleCashMovement(kind: "supply" | "withdrawal") {
    if (!activeSession) return;
    try {
      await authedApi(`/api/v1/pdv/sessions/${activeSession.id}/${kind}`, {
        method: "POST",
        body: JSON.stringify({
          amount: Number(movementAmount.replace(",", ".")) || 0,
          reason: movementReason.trim() || (kind === "supply" ? "Suprimento" : "Sangria"),
        }),
      });
      setMsg({
        type: "ok",
        text: kind === "supply" ? "Suprimento registrado no caixa." : "Sangria registrada no caixa.",
      });
      await loadPdvState();
    } catch (err) {
      setMsg({ type: "err", text: err instanceof Error ? err.message : "Falha na movimentação." });
    }
  }

  async function handleCloseSession() {
    if (!activeSession) return;
    try {
      const report = await authedApi<{
        terminalCode: string;
        openingBalance: unknown;
        expectedCashBalance: unknown;
        countedCashBalance: unknown;
        cashVariance: unknown;
      }>(`/api/v1/pdv/sessions/${activeSession.id}/close`, {
        method: "POST",
        body: JSON.stringify({
          countedCashAmount: Number(closeCountedCash.replace(",", ".")) || 0,
          notes: "Fechamento conferido no PDV",
        }),
      });
      setCloseReport({
        terminalCode: report.terminalCode,
        openingBalance: readMoney(report.openingBalance),
        expectedCashBalance: readMoney(report.expectedCashBalance),
        countedCashBalance: readMoney(report.countedCashBalance),
        cashVariance: readMoney(report.cashVariance),
      });
      setMsg({ type: "ok", text: "Caixa fechado e conferência gravada com auditoria." });
      await loadPdvState();
    } catch (err) {
      setMsg({ type: "err", text: err instanceof Error ? err.message : "Erro ao fechar caixa." });
    }
  }

  async function handleWeighAndAdd() {
    if (weighedTotal <= 0) {
      setMsg({ type: "err", text: "Peso líquido precisa ser maior que zero." });
      return;
    }
    try {
      if (authenticated) {
        await authedApi("/api/v1/weighing/manual", {
          method: "POST",
          body: JSON.stringify({
            productName: weighProductName,
            pricePerKg,
            grossGrams,
            reason: "Pesagem de balcão no PDV",
            manualTareGrams: tareGrams,
          }),
        });
      }
      setBasket((prev) => [
        ...prev,
        {
          id: `weigh-${Date.now()}`,
          title: `${weighProductName} (${netKg.toFixed(3)}kg)`,
          detail: `Bruto ${grossGrams}g · Tara ${tareGrams}g · R$ ${pricePerKg.toFixed(2)}/kg`,
          quantity: 1,
          unitPrice: weighedTotal,
          total: weighedTotal,
        },
      ]);
      setMsg({ type: "ok", text: `Pesagem de ${netKg.toFixed(3)}kg (${brl(weighedTotal)}) lançada na venda.` });
    } catch (err) {
      setMsg({ type: "err", text: err instanceof Error ? err.message : "Erro ao registrar pesagem." });
    }
  }

  function addCatalogItem(title: string, unitPrice: number) {
    setBasket((prev) => {
      const idx = prev.findIndex((i) => i.title === title && i.unitPrice === unitPrice);
      if (idx >= 0) {
        const clone = [...prev];
        const qty = clone[idx].quantity + 1;
        clone[idx] = { ...clone[idx], quantity: qty, total: Number((qty * unitPrice).toFixed(2)) };
        return clone;
      }
      return [
        ...prev,
        {
          id: `cat-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
          title,
          detail: "Item de balcão / catálogo",
          quantity: 1,
          unitPrice,
          total: unitPrice,
        },
      ];
    });
  }

  function updateLineQty(id: string, delta: number) {
    setBasket((prev) =>
      prev
        .map((line) => {
          if (line.id !== id) return line;
          const nextQty = line.quantity + delta;
          if (nextQty <= 0) return null;
          return { ...line, quantity: nextQty, total: Number((nextQty * line.unitPrice).toFixed(2)) };
        })
        .filter(Boolean) as BasketLine[]
    );
  }

  async function handleFinalizeSale() {
    if (!activeSession) {
      setMsg({ type: "err", text: "Abra uma sessão de caixa antes de finalizar a venda." });
      return;
    }
    if (basket.length === 0) {
      setMsg({ type: "err", text: "Adicione pelo menos um item ou pesagem ao cupom." });
      return;
    }

    const allocatedPayments = payments.map((p, idx) => {
      const parsed = Number(p.amount.replace(",", "."));
      const amt = parsed > 0 ? parsed : idx === 0 && payments.length === 1 ? basketNet : 0;
      const tendered =
        p.method === 1 && p.tenderedAmount
          ? Math.max(amt, Number(p.tenderedAmount.replace(",", ".")) || amt)
          : amt;
      return {
        method: p.method,
        allocatedAmount: amt,
        tenderedAmount: tendered,
      };
    });

    const sumPaid = allocatedPayments.reduce((s, p) => s + p.allocatedAmount, 0);
    if (Math.abs(sumPaid - basketNet) > 0.01) {
      setMsg({
        type: "err",
        text: `Soma dos pagamentos (${brl(sumPaid)}) difere do total líquido (${brl(basketNet)}). Ajuste ou deixe 1 forma em branco para preencher automático.`,
      });
      return;
    }

    try {
      const currentLines = [...basket];
      const result = await authedApi<{
        receipt: {
          receiptNumber: string;
          settledAtUtc: string;
          grossAmount: unknown;
          discountAmount: unknown;
          netTotalAmount: unknown;
          totalChangeGiven: unknown;
          payments: { method: number; allocated: unknown; tendered: unknown; change: unknown }[];
        };
      }>("/api/v1/pdv/sales", {
        method: "POST",
        body: JSON.stringify({
          cashRegisterSessionId: activeSession.id,
          grossAmount: basketGross,
          discountAmount: discountVal,
          discountReason: discountVal > 0 ? "Desconto aplicado no caixa" : null,
          payments: allocatedPayments,
        }),
      });

      const r = result.receipt;
      setLastReceipt({
        receiptNumber: r.receiptNumber,
        settledAtUtc: r.settledAtUtc,
        lines: currentLines,
        grossAmount: readMoney(r.grossAmount),
        discountAmount: readMoney(r.discountAmount),
        netTotalAmount: readMoney(r.netTotalAmount),
        totalChangeGiven: readMoney(r.totalChangeGiven),
        payments: (r.payments ?? []).map((p) => ({
          methodLabel: PAYMENT_METHODS.find((m) => m.id === p.method)?.label ?? `Método ${p.method}`,
          allocated: readMoney(p.allocated),
          tendered: readMoney(p.tendered),
          change: readMoney(p.change),
        })),
      });

      setBasket([]);
      setDiscountStr("0");
      setPayments([{ id: `pay-${Date.now()}`, method: 2, amount: "" }]);
      setMsg({ type: "ok", text: `Venda concluída! Cupom ${r.receiptNumber} emitido.` });
      await loadPdvState();
    } catch (err) {
      setMsg({ type: "err", text: err instanceof Error ? err.message : "Erro ao finalizar venda no PDV." });
    }
  }

  const storeHeaderName = branding?.tradeName || branding?.legalName || "ESTABLISHMENT PDV";
  const storeLogoUrl = branding?.draft?.logoUrl;

  return (
    <StoreBrandShell activeSection="/pdv">
      <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Frente de Caixa (PDV) & Balança</h1>
          <p className="text-xs text-slate-500">
            Abertura/fechamento de caixa, pesagem com tara, múltiplos meios de pagamento e cupom não fiscal em tempo real.
          </p>
        </div>
        {!authenticated ? (
          <Link
            href="/login"
            className="rounded-lg bg-slate-900 px-4 py-2 text-xs font-bold text-white hover:bg-slate-800"
          >
            Entrar para operar Caixa (/login)
          </Link>
        ) : (
          <div className="flex flex-wrap items-center gap-2 text-xs">
            {activeSession ? (
              <span className="rounded-full bg-emerald-100 px-3 py-1 font-bold text-emerald-800">
                Caixa Aberto: {activeSession.terminalCode} · Saldo Dinheiro {brl(activeSession.expectedCashBalance)}
              </span>
            ) : (
              <div className="flex items-center gap-2">
                <input
                  value={terminalCode}
                  onChange={(e) => setTerminalCode(e.target.value)}
                  className="w-24 rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs"
                  placeholder="PDV-01"
                />
                <input
                  value={openingBalance}
                  onChange={(e) => setOpeningBalance(e.target.value)}
                  className="w-24 rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-mono"
                  placeholder="200.00"
                />
                <button
                  onClick={() => void handleOpenRegister()}
                  className="rounded-lg bg-slate-900 px-3 py-1.5 text-xs font-bold text-white hover:bg-slate-800"
                >
                  Abrir Caixa
                </button>
              </div>
            )}
          </div>
        )}
      </div>

      {msg && (
        <div
          className={`mb-4 rounded-xl border p-3 text-xs font-medium ${
            msg.type === "ok"
              ? "border-emerald-200 bg-emerald-50 text-emerald-900"
              : msg.type === "err"
                ? "border-rose-200 bg-rose-50 text-rose-900"
                : "border-amber-200 bg-amber-50 text-amber-900"
          }`}
        >
          {msg.text}
        </div>
      )}

      {closeReport && (
        <div className="mb-5 rounded-xl border border-indigo-200 bg-indigo-50 p-4 text-xs text-indigo-950">
          <div className="font-bold">Relatório de Fechamento · {closeReport.terminalCode}</div>
          <div className="mt-1 flex flex-wrap gap-4 font-mono">
            <span>Abertura: {brl(closeReport.openingBalance)}</span>
            <span>Esperado em Dinheiro: {brl(closeReport.expectedCashBalance)}</span>
            <span>Contado: {brl(closeReport.countedCashBalance)}</span>
            <span className={closeReport.cashVariance === 0 ? "text-emerald-700 font-bold" : "text-rose-700 font-bold"}>
              Quebra/Diferença: {brl(closeReport.cashVariance)}
            </span>
          </div>
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-12">
        {/* Left column: Weighing + Quick Catalog */}
        <div className="space-y-6 lg:col-span-5">
          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="mb-3 text-base font-bold text-slate-900">Balança Toledo / Contingência Manual</h2>
            <div className="grid grid-cols-2 gap-3 text-xs">
              <label className="col-span-2 block">
                <span className="font-semibold text-slate-700">Produto na Balança</span>
                <input
                  value={weighProductName}
                  onChange={(e) => setWeighProductName(e.target.value)}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2"
                />
              </label>
              <label className="block">
                <span className="font-semibold text-slate-700">Peso Bruto (g)</span>
                <input
                  type="number"
                  value={grossGrams}
                  onChange={(e) => setGrossGrams(Number(e.target.value))}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 font-mono"
                />
              </label>
              <label className="block">
                <span className="font-semibold text-slate-700">Tara (g)</span>
                <input
                  type="number"
                  value={tareGrams}
                  onChange={(e) => setTareGrams(Number(e.target.value))}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 font-mono"
                />
              </label>
              <label className="col-span-2 block">
                <span className="font-semibold text-slate-700">Valor por kg (R$)</span>
                <input
                  type="number"
                  step="0.01"
                  value={pricePerKg}
                  onChange={(e) => setPricePerKg(Number(e.target.value))}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 font-mono"
                />
              </label>
            </div>
            <div className="mt-3 flex items-center justify-between rounded-lg bg-slate-100 p-3 text-xs font-semibold text-slate-800">
              <span>Peso líquido: {netKg.toFixed(3)} kg</span>
              <span className="font-mono text-sm text-emerald-700">{brl(weighedTotal)}</span>
            </div>
            <button
              onClick={() => void handleWeighAndAdd()}
              className="mt-3 w-full rounded-lg bg-emerald-600 px-4 py-2 text-xs font-bold text-white hover:bg-emerald-700"
            >
              + Registrar Pesagem e Jogar no Cupom
            </button>
          </section>

          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="mb-3 flex items-center justify-between gap-2">
              <h2 className="text-base font-bold text-slate-900">Catálogo Rápido de Balcão</h2>
              <input
                value={catalogSearch}
                onChange={(e) => setCatalogSearch(e.target.value)}
                placeholder="Buscar produto..."
                className="w-40 rounded-lg border border-slate-300 px-2.5 py-1 text-xs"
              />
            </div>
            {filteredProducts.length === 0 ? (
              <p className="text-xs text-slate-500">
                {authenticated ? "Nenhum produto encontrado." : "Entre no painel para carregar o catálogo da loja."}
              </p>
            ) : (
              <div className="grid max-h-64 grid-cols-1 gap-2 overflow-y-auto pr-1 sm:grid-cols-2">
                {filteredProducts.map((p) => {
                  const firstVar = p.variations?.find((v) => v.isActive);
                  const displayPrice = firstVar ? firstVar.price : p.basePrice;
                  const label = firstVar ? `${p.name} (${firstVar.variationName})` : p.name;
                  return (
                    <button
                      key={p.id}
                      onClick={() => addCatalogItem(label, displayPrice)}
                      className="flex flex-col justify-between rounded-lg border border-slate-200 p-2.5 text-left hover:border-slate-900 hover:bg-slate-50"
                    >
                      <span className="line-clamp-2 text-xs font-semibold text-slate-900">{label}</span>
                      <span className="mt-1 font-mono text-xs font-bold text-emerald-700">{brl(displayPrice)}</span>
                    </button>
                  );
                })}
              </div>
            )}
          </section>

          {authenticated && activeSession && (
            <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
              <h2 className="mb-3 text-base font-bold text-slate-900">Movimentação & Fechamento de Caixa</h2>
              <div className="grid grid-cols-2 gap-2 text-xs">
                <input
                  value={movementAmount}
                  onChange={(e) => setMovementAmount(e.target.value)}
                  placeholder="Valor R$"
                  className="rounded-lg border border-slate-300 px-2.5 py-2 font-mono"
                />
                <input
                  value={movementReason}
                  onChange={(e) => setMovementReason(e.target.value)}
                  placeholder="Motivo"
                  className="rounded-lg border border-slate-300 px-2.5 py-2"
                />
                <button
                  onClick={() => void handleCashMovement("supply")}
                  className="rounded-lg border border-emerald-600 py-2 font-bold text-emerald-700 hover:bg-emerald-50"
                >
                  + Suprimento
                </button>
                <button
                  onClick={() => void handleCashMovement("withdrawal")}
                  className="rounded-lg border border-rose-600 py-2 font-bold text-rose-700 hover:bg-rose-50"
                >
                  - Sangria
                </button>
              </div>
              <div className="mt-3 flex gap-2 border-t border-slate-100 pt-3 text-xs">
                <input
                  value={closeCountedCash}
                  onChange={(e) => setCloseCountedCash(e.target.value)}
                  placeholder="Dinheiro contado"
                  className="w-36 rounded-lg border border-slate-300 px-2.5 py-2 font-mono"
                />
                <button
                  onClick={() => void handleCloseSession()}
                  className="flex-1 rounded-lg bg-rose-700 py-2 font-bold text-white hover:bg-rose-800"
                >
                  Fechar Caixa & Conferir
                </button>
              </div>
            </section>
          )}
        </div>

        {/* Center column: Active Sale Basket + Multi-Payment */}
        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-4">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-base font-bold text-slate-900">Cupom em Andamento</h2>
            {basket.length > 0 && (
              <button
                onClick={() => setBasket([])}
                className="text-xs font-semibold text-rose-600 hover:underline"
              >
                Limpar
              </button>
            )}
          </div>

          {basket.length === 0 ? (
            <p className="rounded-lg border border-dashed border-slate-200 p-6 text-center text-xs text-slate-500">
              Pese na balança ou clique em um produto ao lado para montar a venda.
            </p>
          ) : (
            <ul className="divide-y divide-slate-100 text-xs">
              {basket.map((line) => (
                <li key={line.id} className="flex items-center justify-between gap-2 py-2.5">
                  <div>
                    <div className="font-semibold text-slate-900">{line.title}</div>
                    <div className="text-[11px] text-slate-500">{line.detail}</div>
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => updateLineQty(line.id, -1)}
                      className="h-6 w-6 rounded border border-slate-300 font-bold"
                    >
                      -
                    </button>
                    <span className="w-4 text-center font-mono">{line.quantity}</span>
                    <button
                      onClick={() => updateLineQty(line.id, 1)}
                      className="h-6 w-6 rounded border border-slate-300 font-bold"
                    >
                      +
                    </button>
                    <span className="w-20 text-right font-mono font-bold text-slate-900">{brl(line.total)}</span>
                  </div>
                </li>
              ))}
            </ul>
          )}

          <div className="mt-4 space-y-2 border-t border-slate-200 pt-3 text-xs">
            <div className="flex justify-between">
              <span>Subtotal Bruto</span>
              <span className="font-mono font-semibold">{brl(basketGross)}</span>
            </div>
            <div className="flex items-center justify-between">
              <span>Desconto (R$)</span>
              <input
                value={discountStr}
                onChange={(e) => setDiscountStr(e.target.value)}
                className="w-24 rounded border border-slate-300 px-2 py-1 text-right font-mono"
              />
            </div>
            <div className="flex justify-between text-sm font-bold text-slate-900">
              <span>Total Líquido a Pagar</span>
              <span className="font-mono text-emerald-700">{brl(basketNet)}</span>
            </div>
          </div>

          {/* Multi-payment rows */}
          <div className="mt-4 space-y-2 border-t border-slate-100 pt-3 text-xs">
            <div className="flex items-center justify-between">
              <span className="font-bold text-slate-800">Formas de Pagamento</span>
              <button
                type="button"
                onClick={() =>
                  setPayments((prev) => [...prev, { id: `pay-${Date.now()}`, method: 1, amount: "" }])
                }
                className="font-semibold text-indigo-700 hover:underline"
              >
                + Dividir Pagamento
              </button>
            </div>

            {payments.map((p, idx) => (
              <div key={p.id} className="grid grid-cols-12 gap-1.5">
                <select
                  value={p.method}
                  onChange={(e) =>
                    setPayments((prev) =>
                      prev.map((row) => (row.id === p.id ? { ...row, method: Number(e.target.value) } : row))
                    )
                  }
                  className="col-span-5 rounded-lg border border-slate-300 px-2 py-1.5"
                >
                  {PAYMENT_METHODS.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.label}
                    </option>
                  ))}
                </select>
                <input
                  value={p.amount}
                  onChange={(e) =>
                    setPayments((prev) =>
                      prev.map((row) => (row.id === p.id ? { ...row, amount: e.target.value } : row))
                    )
                  }
                  placeholder={payments.length === 1 ? basketNet.toFixed(2) : "Valor"}
                  className="col-span-3 rounded-lg border border-slate-300 px-2 py-1.5 font-mono"
                />
                {p.method === 1 ? (
                  <input
                    value={p.tenderedAmount ?? ""}
                    onChange={(e) =>
                      setPayments((prev) =>
                        prev.map((row) => (row.id === p.id ? { ...row, tenderedAmount: e.target.value } : row))
                      )
                    }
                    placeholder="Recebido"
                    className="col-span-3 rounded-lg border border-slate-300 px-2 py-1.5 font-mono"
                  />
                ) : (
                  <div className="col-span-3 flex items-center justify-center text-[10px] text-slate-400">
                    Exato
                  </div>
                )}
                {idx > 0 && (
                  <button
                    type="button"
                    onClick={() => setPayments((prev) => prev.filter((row) => row.id !== p.id))}
                    className="col-span-1 text-rose-600 font-bold"
                  >
                    ×
                  </button>
                )}
              </div>
            ))}
          </div>

          <button
            onClick={() => void handleFinalizeSale()}
            className="mt-4 w-full rounded-xl bg-slate-900 py-3 text-xs font-bold text-white hover:bg-slate-800"
          >
            Finalizar Venda & Emitir Cupom Não Fiscal
          </button>

          {sessionSales.length > 0 && (
            <div className="mt-5 border-t border-slate-100 pt-3">
              <p className="mb-2 text-xs font-bold text-slate-700">Vendas da Sessão Atual ({sessionSales.length})</p>
              <div className="max-h-36 space-y-1.5 overflow-y-auto text-[11px]">
                {sessionSales.map((s) => (
                  <div key={s.id} className="flex items-center justify-between rounded bg-slate-50 px-2.5 py-1.5">
                    <span className="font-mono font-semibold">{s.receiptNumber}</span>
                    <span className="text-slate-500">
                      {s.payments.map((p) => p.method).join(" + ")}
                    </span>
                    <span className="font-mono font-bold text-emerald-700">{brl(s.netAmount)}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </section>

        {/* Right column: Real Thermal Cupom Não Fiscal */}
        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-3">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-base font-bold text-slate-900">Cupom Não Fiscal</h2>
            {lastReceipt && (
              <button
                onClick={() => window.print()}
                className="rounded-lg border border-slate-300 px-2.5 py-1 text-[11px] font-semibold text-slate-700 hover:bg-slate-50"
              >
                Imprimir
              </button>
            )}
          </div>

          <div className="mx-auto max-w-xs rounded border border-dashed border-slate-300 bg-amber-50/70 p-4 font-mono text-[11px] text-slate-800">
            <div className="flex flex-col items-center text-center">
              {storeLogoUrl ? (
                <img
                  src={storeLogoUrl}
                  alt={storeHeaderName}
                  className="mb-2 h-12 w-12 rounded-full object-cover border border-slate-200"
                />
              ) : (
                <div className="mb-1 text-[10px] font-bold tracking-wider text-slate-500">[LOGO OFICIAL DA LOJA]</div>
              )}
              <div className="font-bold uppercase">{storeHeaderName}</div>
              <div>CNPJ: {branding?.cnpj || "Não informado"}</div>
              {branding?.address && <div className="text-[10px] text-slate-600">{branding.address}</div>}
            </div>

            <hr className="my-2 border-dashed border-slate-400" />
            <div className="text-center font-bold">DOCUMENTO NÃO FISCAL · CONFERÊNCIA</div>
            {lastReceipt && (
              <div className="mt-1 text-center text-[10px] text-slate-600">
                Cupom {lastReceipt.receiptNumber} · {new Date(lastReceipt.settledAtUtc).toLocaleString("pt-BR")}
              </div>
            )}
            <hr className="my-2 border-dashed border-slate-400" />

            {lastReceipt ? (
              <>
                <div className="space-y-1">
                  {lastReceipt.lines.map((l) => (
                    <div key={l.id} className="flex justify-between gap-2">
                      <span className="truncate">
                        {l.quantity}x {l.title}
                      </span>
                      <span className="shrink-0">{brl(l.total)}</span>
                    </div>
                  ))}
                </div>
                <hr className="my-2 border-dashed border-slate-400" />
                <div className="flex justify-between">
                  <span>BRUTO</span>
                  <span>{brl(lastReceipt.grossAmount)}</span>
                </div>
                {lastReceipt.discountAmount > 0 && (
                  <div className="flex justify-between text-rose-700">
                    <span>DESCONTO</span>
                    <span>-{brl(lastReceipt.discountAmount)}</span>
                  </div>
                )}
                <div className="flex justify-between text-xs font-bold">
                  <span>TOTAL LÍQUIDO</span>
                  <span>{brl(lastReceipt.netTotalAmount)}</span>
                </div>
                <hr className="my-2 border-dashed border-slate-400" />
                {lastReceipt.payments.map((p, idx) => (
                  <div key={idx} className="flex justify-between text-[10px]">
                    <span>{p.methodLabel}</span>
                    <span>{brl(p.allocated)}</span>
                  </div>
                ))}
                {lastReceipt.totalChangeGiven > 0 && (
                  <div className="mt-1 flex justify-between font-bold">
                    <span>TROCO</span>
                    <span>{brl(lastReceipt.totalChangeGiven)}</span>
                  </div>
                )}
              </>
            ) : basket.length > 0 ? (
              <>
                <div className="mb-1 text-[10px] text-amber-800">PRÉVIA DO CUPOM ATUAL:</div>
                {basket.map((l) => (
                  <div key={l.id} className="flex justify-between gap-2">
                    <span className="truncate">
                      {l.quantity}x {l.title}
                    </span>
                    <span className="shrink-0">{brl(l.total)}</span>
                  </div>
                ))}
                <hr className="my-2 border-dashed border-slate-400" />
                <div className="flex justify-between font-bold">
                  <span>TOTAL</span>
                  <span>{brl(basketNet)}</span>
                </div>
              </>
            ) : (
              <div className="py-4 text-center text-slate-500">
                Finalize uma venda para imprimir o comprovante não fiscal real.
              </div>
            )}
          </div>
        </section>
      </div>
    </StoreBrandShell>
  );
}
