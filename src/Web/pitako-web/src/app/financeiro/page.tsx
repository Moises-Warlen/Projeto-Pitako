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

interface DreResponse {
  grossRevenue: number;
  deductionsAndFees: number;
  netRevenue: number;
  cogsAmount: number;
  grossProfit: number;
  operatingExpenses: number;
  operatingResult: number;
}

interface CashFlowResponse {
  realizedInflows: number;
  realizedOutflows: number;
  netRealizedCashFlow: number;
  pendingReceivables: number;
  pendingPayables: number;
  projectedNetBalance: number;
}

interface FinanceEntryDto {
  id: string;
  entryCode: string;
  kind: number;
  kindName: string;
  dreCategory: number;
  dreCategoryName: string;
  counterpartyName: string;
  description: string;
  referenceDocument?: string | null;
  originalAmount: number;
  feeOrAdjustmentAmount: number;
  settledAmount: number;
  status: number;
  statusName: string;
  dueDateUtc: string;
  settledAtUtc?: string | null;
  paymentMethodName?: string | null;
}

interface GatewayConfigDto {
  id: string;
  provider: string;
  merchantAccountCode: string;
  defaultMdrPercent: number;
  settlementDays: number;
  isActive: boolean;
}

const DRE_CATEGORIES = [
  { id: 1, label: "1. Receita Bruta de Vendas" },
  { id: 2, label: "2. Taxas Marketplace / Gateway" },
  { id: 3, label: "3. CMV / Insumos e Estoque" },
  { id: 4, label: "4. Folha e Equipe" },
  { id: 5, label: "5. Aluguel e Utilidades" },
  { id: 6, label: "6. Marketing e Outras Despesas" },
];

const GATEWAY_PROVIDERS = [
  { id: 1, label: "Stone" },
  { id: 2, label: "Rede" },
  { id: 3, label: "Cielo" },
  { id: 4, label: "Getnet" },
  { id: 5, label: "Mercado Pago" },
  { id: 6, label: "PagBank" },
  { id: 7, label: "PIX Direto" },
];

function brl(value?: number | null): string {
  return Number(value ?? 0).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

export default function FinancePage() {
  const [authenticated, setAuthenticated] = useState(false);
  const [dre, setDre] = useState<DreResponse | null>(null);
  const [cashFlow, setCashFlow] = useState<CashFlowResponse | null>(null);
  const [entries, setEntries] = useState<FinanceEntryDto[]>([]);
  const [gateways, setGateways] = useState<GatewayConfigDto[]>([]);
  const [msg, setMsg] = useState<{ type: "info" | "ok" | "err"; text: string } | null>(null);
  const [loading, setLoading] = useState(true);
  const [filterKind, setFilterKind] = useState<"ALL" | "RECEIVABLE" | "PAYABLE" | "OPEN">("ALL");

  // New entry form
  const [kind, setKind] = useState<number>(2);
  const [dreCategory, setDreCategory] = useState<number>(3);
  const [counterpartyName, setCounterpartyName] = useState("");
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState("150.00");
  const [dueDate, setDueDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [referenceDocument, setReferenceDocument] = useState("");

  // Settle modal state
  const [settlingEntry, setSettlingEntry] = useState<FinanceEntryDto | null>(null);
  const [settleNetAmount, setSettleNetAmount] = useState("");
  const [settleFeeAmount, setSettleFeeAmount] = useState("0");
  const [settleMethod, setSettleMethod] = useState("PIX");

  // Gateway form
  const [gwProvider, setGwProvider] = useState<number>(7);
  const [gwMerchant, setGwMerchant] = useState("");
  const [gwSecret, setGwSecret] = useState("");
  const [gwMdr, setGwMdr] = useState("0.99");
  const [gwDays, setGwDays] = useState("1");

  // PCI Charge test
  const [testGatewayId, setTestGatewayId] = useState("");
  const [testRef, setTestRef] = useState("PED-PCI-TESTE");
  const [testAmount, setTestAmount] = useState("45.00");
  const [testBrand, setTestBrand] = useState("VISA");
  const [testLast4, setTestLast4] = useState("4242");
  const [testToken, setTestToken] = useState("tok_gateway_safe_9912");

  const loadFinanceData = useCallback(async () => {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") {
      setAuthenticated(false);
      setLoading(false);
      setMsg({
        type: "info",
        text: "Você precisa entrar com um operador da loja (/login) com permissão Financeira/Gerencial para acessar DRE, Contas e Gateways.",
      });
      return;
    }

    setAuthenticated(true);
    setLoading(true);
    try {
      const [d, cf, list, gws] = await Promise.all([
        authedApi<DreResponse>("/api/v1/finance/dre"),
        authedApi<CashFlowResponse>("/api/v1/finance/cash-flow"),
        authedApi<FinanceEntryDto[]>("/api/v1/finance/entries"),
        authedApi<GatewayConfigDto[]>("/api/v1/finance/gateways"),
      ]);
      setDre(d);
      setCashFlow(cf);
      setEntries(list);
      setGateways(gws);
      if (gws.length > 0 && !testGatewayId) {
        setTestGatewayId(gws[0].id);
      }
      setMsg(null);
    } catch (e) {
      setMsg({ type: "err", text: e instanceof Error ? e.message : "Erro ao carregar dados financeiros." });
    } finally {
      setLoading(false);
    }
  }, [testGatewayId]);

  useEffect(() => {
    void loadFinanceData();
  }, [loadFinanceData]);

  const filteredEntries = useMemo(() => {
    return entries.filter((item) => {
      if (filterKind === "RECEIVABLE") return item.kind === 1;
      if (filterKind === "PAYABLE") return item.kind === 2;
      if (filterKind === "OPEN") return item.status !== 3 && item.status !== 4;
      return true;
    });
  }, [entries, filterKind]);

  async function handleCreateEntry(e: React.FormEvent) {
    e.preventDefault();
    try {
      const parsedAmount = Number(amount.replace(",", "."));
      if (!parsedAmount || parsedAmount <= 0) {
        setMsg({ type: "err", text: "Informe um valor válido maior que zero." });
        return;
      }
      await authedApi("/api/v1/finance/entries", {
        method: "POST",
        body: JSON.stringify({
          kind,
          dreCategory,
          counterpartyName: counterpartyName.trim(),
          description: description.trim(),
          amount: parsedAmount,
          dueDateUtc: new Date(`${dueDate}T12:00:00Z`).toISOString(),
          referenceDocument: referenceDocument.trim() || null,
        }),
      });
      setCounterpartyName("");
      setDescription("");
      setReferenceDocument("");
      setMsg({ type: "ok", text: "Título financeiro lançado com sucesso e refletido no fluxo projetado." });
      await loadFinanceData();
    } catch (err) {
      setMsg({ type: "err", text: err instanceof Error ? err.message : "Falha ao lançar título." });
    }
  }

  async function handleSettleEntry(e: React.FormEvent) {
    e.preventDefault();
    if (!settlingEntry) return;
    try {
      await authedApi(`/api/v1/finance/entries/${settlingEntry.id}/settle`, {
        method: "POST",
        body: JSON.stringify({
          netPaidAmount: Number(settleNetAmount.replace(",", ".")),
          feeAmount: Number(settleFeeAmount.replace(",", ".")) || 0,
          paymentMethod: settleMethod,
        }),
      });
      setMsg({ type: "ok", text: `Baixa realizada no título ${settlingEntry.entryCode}.` });
      setSettlingEntry(null);
      await loadFinanceData();
    } catch (err) {
      setMsg({ type: "err", text: err instanceof Error ? err.message : "Erro ao liquidar título." });
    }
  }

  async function handleConfigureGateway(e: React.FormEvent) {
    e.preventDefault();
    try {
      await authedApi("/api/v1/finance/gateways", {
        method: "POST",
        body: JSON.stringify({
          provider: gwProvider,
          merchantAccountCode: gwMerchant.trim(),
          apiSecret: gwSecret.trim(),
          mdrPercent: Number(gwMdr.replace(",", ".")),
          settlementDays: Number(gwDays) || 1,
        }),
      });
      setGwMerchant("");
      setGwSecret("");
      setMsg({ type: "ok", text: "Gateway configurado com segredo criptografado no servidor." });
      await loadFinanceData();
    } catch (err) {
      setMsg({ type: "err", text: err instanceof Error ? err.message : "Erro ao configurar gateway." });
    }
  }

  async function handleTestPciCharge(e: React.FormEvent) {
    e.preventDefault();
    if (!testGatewayId) {
      setMsg({ type: "err", text: "Cadastre ou selecione um gateway primeiro." });
      return;
    }
    try {
      await authedApi(`/api/v1/finance/gateways/${testGatewayId}/charges`, {
        method: "POST",
        body: JSON.stringify({
          orderOrSaleReference: testRef.trim(),
          grossAmount: Number(testAmount.replace(",", ".")),
          cardBrandOrPix: testBrand.trim(),
          cardLast4Digits: testLast4.trim() || null,
          gatewayTokenReference: testToken.trim(),
        }),
      });
      setMsg({ type: "ok", text: "Transação tokenizada validada no backend com conformidade PCI-Safe." });
      await loadFinanceData();
    } catch (err) {
      setMsg({
        type: "err",
        text: err instanceof Error ? err.message : "Bloqueio ou erro na cobrança do gateway.",
      });
    }
  }

  return (
    <StoreBrandShell activeSection="/financeiro">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Financeiro, Fluxo de Caixa, DRE & Gateways PCI-Safe</h1>
          <p className="text-xs text-slate-500">
            Conformidade PCI: somente token de gateway e 4 últimos dígitos autorizados; PAN completo é bloqueado no backend.
          </p>
        </div>
        {authenticated && (
          <button
            onClick={() => void loadFinanceData()}
            className="rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50"
          >
            Atualizar Painel
          </button>
        )}
      </div>

      {!authenticated && (
        <div className="mb-6 flex flex-wrap items-center justify-between gap-4 rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950">
          <div>
            <p className="font-bold">Acesso financeiro restrito ao operador da loja</p>
            <p className="text-xs text-amber-800">{msg?.text}</p>
          </div>
          <Link
            href="/login"
            className="rounded-lg bg-slate-900 px-4 py-2 text-xs font-semibold text-white hover:bg-slate-800"
          >
            Ir para Login (/login)
          </Link>
        </div>
      )}

      {authenticated && msg && (
        <div
          className={`mb-5 rounded-xl border p-3 text-sm ${
            msg.type === "ok"
              ? "border-emerald-200 bg-emerald-50 text-emerald-900"
              : msg.type === "err"
                ? "border-rose-200 bg-rose-50 text-rose-900"
                : "border-slate-200 bg-white text-slate-700"
          }`}
        >
          {msg.text}
        </div>
      )}

      {/* DRE & Cash Flow */}
      <div className="grid gap-6 md:grid-cols-2">
        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="mb-3 text-base font-bold text-slate-900">DRE Gerencial Consolidada</h2>
          {dre ? (
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between">
                <dt>Receita Bruta</dt>
                <dd className="font-mono">{brl(dre.grossRevenue)}</dd>
              </div>
              <div className="flex justify-between text-rose-700">
                <dt>(-) Taxas / Deduções</dt>
                <dd className="font-mono">{brl(dre.deductionsAndFees)}</dd>
              </div>
              <div className="flex justify-between font-semibold">
                <dt>Receita Líquida</dt>
                <dd className="font-mono">{brl(dre.netRevenue)}</dd>
              </div>
              <div className="flex justify-between text-rose-700">
                <dt>(-) CMV / Insumos</dt>
                <dd className="font-mono">{brl(dre.cogsAmount)}</dd>
              </div>
              <div className="flex justify-between font-semibold">
                <dt>Lucro Bruto</dt>
                <dd className="font-mono">{brl(dre.grossProfit)}</dd>
              </div>
              <div className="flex justify-between text-rose-700">
                <dt>(-) Despesas Operacionais</dt>
                <dd className="font-mono">{brl(dre.operatingExpenses)}</dd>
              </div>
              <div className="flex justify-between border-t border-slate-200 pt-2 text-base font-bold">
                <dt>Resultado Operacional</dt>
                <dd className={`font-mono ${dre.operatingResult >= 0 ? "text-emerald-700" : "text-rose-700"}`}>
                  {brl(dre.operatingResult)}
                </dd>
              </div>
            </dl>
          ) : (
            <p className="text-xs text-slate-500">
              {loading ? "Carregando DRE..." : "Aguardando autenticação financeira."}
            </p>
          )}
        </section>

        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="mb-3 text-base font-bold text-slate-900">Fluxo de Caixa Realizado e Projetado</h2>
          {cashFlow ? (
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between">
                <dt>Entradas Realizadas</dt>
                <dd className="font-mono text-emerald-700">{brl(cashFlow.realizedInflows)}</dd>
              </div>
              <div className="flex justify-between">
                <dt>Saídas Realizadas</dt>
                <dd className="font-mono text-rose-700">{brl(cashFlow.realizedOutflows)}</dd>
              </div>
              <div className="flex justify-between font-semibold">
                <dt>Saldo Realizado</dt>
                <dd className="font-mono">{brl(cashFlow.netRealizedCashFlow)}</dd>
              </div>
              <div className="flex justify-between text-slate-600">
                <dt>(+) Recebíveis em Aberto</dt>
                <dd className="font-mono">{brl(cashFlow.pendingReceivables)}</dd>
              </div>
              <div className="flex justify-between text-slate-600">
                <dt>(-) Contas a Pagar em Aberto</dt>
                <dd className="font-mono">{brl(cashFlow.pendingPayables)}</dd>
              </div>
              <div className="flex justify-between border-t border-slate-200 pt-2 text-base font-bold">
                <dt>Saldo Final Projetado</dt>
                <dd className={`font-mono ${(cashFlow.projectedNetBalance ?? 0) >= 0 ? "text-emerald-700" : "text-rose-700"}`}>
                  {brl(cashFlow.projectedNetBalance)}
                </dd>
              </div>
            </dl>
          ) : (
            <p className="text-xs text-slate-500">
              {loading ? "Carregando Fluxo de Caixa..." : "Aguardando autenticação financeira."}
            </p>
          )}
        </section>
      </div>

      {/* Contas a Pagar / Receber */}
      {authenticated && (
        <div className="mt-6 grid gap-6 lg:grid-cols-3">
          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-2">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-base font-bold text-slate-900">Contas a Pagar & Receber</h2>
              <div className="flex flex-wrap gap-1.5 text-xs">
                {[
                  { key: "ALL", label: `Todos (${entries.length})` },
                  { key: "RECEIVABLE", label: "A Receber" },
                  { key: "PAYABLE", label: "A Pagar" },
                  { key: "OPEN", label: "Em Aberto" },
                ].map((tab) => (
                  <button
                    key={tab.key}
                    onClick={() => setFilterKind(tab.key as typeof filterKind)}
                    className={`rounded-full px-3 py-1 font-semibold ${
                      filterKind === tab.key
                        ? "bg-slate-900 text-white"
                        : "border border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100"
                    }`}
                  >
                    {tab.label}
                  </button>
                ))}
              </div>
            </div>

            {filteredEntries.length === 0 ? (
              <p className="rounded-lg border border-dashed border-slate-200 p-6 text-center text-xs text-slate-500">
                Nenhum lançamento financeiro encontrado para o filtro selecionado.
              </p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead className="border-b border-slate-200 text-slate-500">
                    <tr>
                      <th className="py-2 pr-2">Código</th>
                      <th className="py-2 pr-2">Favorecido / Descrição</th>
                      <th className="py-2 pr-2">Vencimento</th>
                      <th className="py-2 pr-2 text-right">Valor</th>
                      <th className="py-2 pr-2">Status</th>
                      <th className="py-2 text-right">Ação</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {filteredEntries.map((entry) => (
                      <tr key={entry.id}>
                        <td className="py-2.5 pr-2 font-mono font-semibold text-slate-800">{entry.entryCode}</td>
                        <td className="py-2.5 pr-2">
                          <div className="font-semibold text-slate-900">{entry.counterpartyName}</div>
                          <div className="text-[11px] text-slate-500">
                            {entry.kind === 1 ? "Receita" : "Despesa"} · {entry.description}
                          </div>
                        </td>
                        <td className="py-2.5 pr-2 text-slate-600">
                          {new Date(entry.dueDateUtc).toLocaleDateString("pt-BR")}
                        </td>
                        <td
                          className={`py-2.5 pr-2 text-right font-mono font-semibold ${
                            entry.kind === 1 ? "text-emerald-700" : "text-rose-700"
                          }`}
                        >
                          {entry.kind === 1 ? "+" : "-"}
                          {brl(entry.originalAmount)}
                        </td>
                        <td className="py-2.5 pr-2">
                          <span
                            className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${
                              entry.status === 3
                                ? "bg-emerald-100 text-emerald-800"
                                : "bg-amber-100 text-amber-800"
                            }`}
                          >
                            {entry.status === 3 ? "Liquidado" : "Em Aberto"}
                          </span>
                        </td>
                        <td className="py-2.5 text-right">
                          {entry.status !== 3 && entry.status !== 4 ? (
                            <button
                              onClick={() => {
                                setSettlingEntry(entry);
                                setSettleNetAmount((entry.originalAmount - entry.settledAmount).toFixed(2));
                                setSettleFeeAmount("0");
                              }}
                              className="rounded-md bg-emerald-600 px-2.5 py-1 text-[11px] font-semibold text-white hover:bg-emerald-700"
                            >
                              Dar Baixa
                            </button>
                          ) : (
                            <span className="text-[11px] text-slate-400">{entry.paymentMethodName ?? "Pago"}</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="mb-3 text-base font-bold text-slate-900">Novo Lançamento</h2>
            <form onSubmit={handleCreateEntry} className="space-y-3 text-xs">
              <div className="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  onClick={() => {
                    setKind(1);
                    setDreCategory(1);
                  }}
                  className={`rounded-lg border px-3 py-2 font-semibold ${
                    kind === 1 ? "border-emerald-600 bg-emerald-50 text-emerald-800" : "border-slate-200 text-slate-600"
                  }`}
                >
                  + Receita (Receber)
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setKind(2);
                    setDreCategory(3);
                  }}
                  className={`rounded-lg border px-3 py-2 font-semibold ${
                    kind === 2 ? "border-rose-600 bg-rose-50 text-rose-800" : "border-slate-200 text-slate-600"
                  }`}
                >
                  - Despesa (Pagar)
                </button>
              </div>
              <div>
                <label className="mb-1 block font-semibold text-slate-700">Categoria DRE</label>
                <select
                  value={dreCategory}
                  onChange={(e) => setDreCategory(Number(e.target.value))}
                  className="w-full rounded-lg border border-slate-300 px-2.5 py-2"
                >
                  {DRE_CATEGORIES.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.label}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block font-semibold text-slate-700">Fornecedor / Cliente</label>
                <input
                  required
                  value={counterpartyName}
                  onChange={(e) => setCounterpartyName(e.target.value)}
                  placeholder="Ex: Distribuidora Açaí Norte"
                  className="w-full rounded-lg border border-slate-300 px-2.5 py-2"
                />
              </div>
              <div>
                <label className="mb-1 block font-semibold text-slate-700">Descrição</label>
                <input
                  required
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Ex: Compra de polpa 20kg"
                  className="w-full rounded-lg border border-slate-300 px-2.5 py-2"
                />
              </div>
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Valor (R$)</label>
                  <input
                    required
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 px-2.5 py-2 font-mono"
                  />
                </div>
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Vencimento</label>
                  <input
                    required
                    type="date"
                    value={dueDate}
                    onChange={(e) => setDueDate(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 px-2.5 py-2"
                  />
                </div>
              </div>
              <div>
                <label className="mb-1 block font-semibold text-slate-700">Documento / NF (opcional)</label>
                <input
                  value={referenceDocument}
                  onChange={(e) => setReferenceDocument(e.target.value)}
                  placeholder="NF-1234"
                  className="w-full rounded-lg border border-slate-300 px-2.5 py-2"
                />
              </div>
              <button
                type="submit"
                className="w-full rounded-lg bg-slate-900 py-2.5 text-xs font-bold text-white hover:bg-slate-800"
              >
                Lançar Título no Financeiro
              </button>
            </form>
          </section>
        </div>
      )}

      {/* Gateways PCI-Safe */}
      {authenticated && (
        <div className="mt-6 grid gap-6 lg:grid-cols-2">
          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="mb-3 text-base font-bold text-slate-900">Gateways de Pagamento Configurados</h2>
            <div className="mb-4 space-y-2">
              {gateways.length === 0 ? (
                <p className="text-xs text-slate-500">Nenhum gateway configurado ainda.</p>
              ) : (
                gateways.map((gw) => (
                  <div
                    key={gw.id}
                    className="flex items-center justify-between rounded-lg border border-slate-200 px-3 py-2 text-xs"
                  >
                    <div>
                      <span className="font-bold text-slate-900">{gw.provider}</span> · Conta{" "}
                      <span className="font-mono text-slate-600">{gw.merchantAccountCode}</span>
                    </div>
                    <div className="text-right text-[11px] text-slate-600">
                      MDR {gw.defaultMdrPercent}% · D+{gw.settlementDays}
                    </div>
                  </div>
                ))
              )}
            </div>

            <form onSubmit={handleConfigureGateway} className="space-y-2 border-t border-slate-100 pt-3 text-xs">
              <p className="font-semibold text-slate-800">Adicionar / Atualizar Gateway</p>
              <div className="grid grid-cols-2 gap-2">
                <select
                  value={gwProvider}
                  onChange={(e) => setGwProvider(Number(e.target.value))}
                  className="rounded-lg border border-slate-300 px-2.5 py-2"
                >
                  {GATEWAY_PROVIDERS.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.label}
                    </option>
                  ))}
                </select>
                <input
                  required
                  value={gwMerchant}
                  onChange={(e) => setGwMerchant(e.target.value)}
                  placeholder="Merchant Code"
                  className="rounded-lg border border-slate-300 px-2.5 py-2"
                />
              </div>
              <input
                required
                type="password"
                autoComplete="off"
                value={gwSecret}
                onChange={(e) => setGwSecret(e.target.value)}
                placeholder="API Secret (cifrado no backend)"
                className="w-full rounded-lg border border-slate-300 px-2.5 py-2"
              />
              <div className="grid grid-cols-3 gap-2">
                <input
                  value={gwMdr}
                  onChange={(e) => setGwMdr(e.target.value)}
                  placeholder="MDR %"
                  className="rounded-lg border border-slate-300 px-2.5 py-2 font-mono"
                />
                <input
                  value={gwDays}
                  onChange={(e) => setGwDays(e.target.value)}
                  placeholder="Dias D+"
                  className="rounded-lg border border-slate-300 px-2.5 py-2 font-mono"
                />
                <button
                  type="submit"
                  className="rounded-lg bg-slate-900 px-3 py-2 font-bold text-white hover:bg-slate-800"
                >
                  Salvar Gateway
                </button>
              </div>
            </form>
          </section>

          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="mb-1 text-base font-bold text-slate-900">Validação PCI-Safe de Cobrança</h2>
            <p className="mb-3 text-xs text-slate-500">
              Envie token + 4 dígitos finais. Caso um PAN completo (16 dígitos) seja informado, o backend rejeita imediatamente.
            </p>
            <form onSubmit={handleTestPciCharge} className="space-y-2.5 text-xs">
              <div>
                <label className="mb-1 block font-semibold text-slate-700">Gateway Ativo</label>
                <select
                  value={testGatewayId}
                  onChange={(e) => setTestGatewayId(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-2.5 py-2"
                >
                  {gateways.length === 0 && <option value="">Nenhum gateway ativo</option>}
                  {gateways.map((gw) => (
                    <option key={gw.id} value={gw.id}>
                      {gw.provider} ({gw.merchantAccountCode})
                    </option>
                  ))}
                </select>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Referência</label>
                  <input
                    value={testRef}
                    onChange={(e) => setTestRef(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 px-2.5 py-2"
                  />
                </div>
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Valor Bruto (R$)</label>
                  <input
                    value={testAmount}
                    onChange={(e) => setTestAmount(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 px-2.5 py-2 font-mono"
                  />
                </div>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Bandeira / PIX</label>
                  <input
                    value={testBrand}
                    onChange={(e) => setTestBrand(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 px-2.5 py-2"
                  />
                </div>
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">4 Últimos Dígitos</label>
                  <input
                    value={testLast4}
                    onChange={(e) => setTestLast4(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 px-2.5 py-2 font-mono"
                  />
                </div>
              </div>
              <div>
                <label className="mb-1 block font-semibold text-slate-700">Token de Gateway (nunca PAN completo)</label>
                <input
                  value={testToken}
                  onChange={(e) => setTestToken(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-2.5 py-2 font-mono"
                />
              </div>
              <button
                type="submit"
                className="w-full rounded-lg border border-slate-900 bg-white py-2 font-bold text-slate-900 hover:bg-slate-100"
              >
                Executar Cobrança Tokenizada PCI-Safe
              </button>
            </form>
          </section>
        </div>
      )}

      {/* Settle Modal */}
      {settlingEntry && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
          <form
            onSubmit={handleSettleEntry}
            className="w-full max-w-md rounded-2xl bg-white p-6 shadow-xl"
          >
            <h3 className="text-base font-bold text-slate-900">Liquidar Título {settlingEntry.entryCode}</h3>
            <p className="mb-4 text-xs text-slate-500">
              {settlingEntry.counterpartyName} · Original {brl(settlingEntry.originalAmount)}
            </p>
            <div className="space-y-3 text-xs">
              <div>
                <label className="mb-1 block font-semibold text-slate-700">Valor Líquido Pago/Recebido (R$)</label>
                <input
                  required
                  value={settleNetAmount}
                  onChange={(e) => setSettleNetAmount(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 font-mono"
                />
              </div>
              <div>
                <label className="mb-1 block font-semibold text-slate-700">Taxas / Juros / Descontos (R$)</label>
                <input
                  value={settleFeeAmount}
                  onChange={(e) => setSettleFeeAmount(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 font-mono"
                />
              </div>
              <div>
                <label className="mb-1 block font-semibold text-slate-700">Forma de Pagamento</label>
                <select
                  value={settleMethod}
                  onChange={(e) => setSettleMethod(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2"
                >
                  <option value="PIX">PIX</option>
                  <option value="Transferência TED">Transferência TED</option>
                  <option value="Boleto">Boleto</option>
                  <option value="Dinheiro">Dinheiro</option>
                  <option value="Cartão">Cartão</option>
                </select>
              </div>
            </div>
            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setSettlingEntry(null)}
                className="rounded-lg border border-slate-300 px-4 py-2 text-xs font-semibold text-slate-700"
              >
                Cancelar
              </button>
              <button
                type="submit"
                className="rounded-lg bg-emerald-600 px-4 py-2 text-xs font-bold text-white hover:bg-emerald-700"
              >
                Confirmar Baixa
              </button>
            </div>
          </form>
        </div>
      )}
    </StoreBrandShell>
  );
}
