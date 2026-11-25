"use client";

import { useEffect, useState } from "react";
import { StoreBrandShell } from "@/components/StoreBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

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
  realizedNetBalance: number;
  projectedPendingReceivables: number;
  projectedPendingPayables: number;
  projectedFinalBalance: number;
}

export default function FinancePage() {
  const [dre, setDre] = useState<DreResponse | null>(null);
  const [cashFlow, setCashFlow] = useState<CashFlowResponse | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") {
      setMsg("Faça login em /login com perfil Financeiro/Gerente para consultar Fluxo de Caixa e DRE.");
      return;
    }

    Promise.all([
      apiFetch<DreResponse>("/api/v1/finance/dre", {}, session.accessToken),
      apiFetch<CashFlowResponse>("/api/v1/finance/cash-flow", {}, session.accessToken)
    ]).then(([dreRes, cfRes]) => {
      if (dreRes.ok && dreRes.data) setDre(dreRes.data);
      if (cfRes.ok && cfRes.data) setCashFlow(cfRes.data);
      if (!dreRes.ok || !cfRes.ok) {
        setMsg(dreRes.errorMessage || cfRes.errorMessage || "Erro ao consultar financeiro.");
      } else {
        setMsg(null);
      }
    });
  }, []);

  return (
    <StoreBrandShell activeSection="/financeiro">
      <div className="mb-6">
        <h1 className="text-xl font-bold text-slate-900">Financeiro, Fluxo de Caixa, DRE & Gateways PCI-Safe</h1>
        <p className="text-xs text-slate-500">
          Conformidade PCI: somente token de gateway e 4 últimos dígitos autorizados; PAN completo é bloqueado no backend.
        </p>
      </div>

      {msg && (
        <div className="mb-5 rounded-xl border border-slate-300 bg-white p-4 text-sm text-slate-700">
          {msg}
        </div>
      )}

      <div className="grid gap-6 md:grid-cols-2">
        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="mb-3 text-base font-bold text-slate-900">DRE Gerencial Consolidada</h2>
          {dre ? (
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between"><dt>Receita Bruta</dt><dd className="font-mono">R$ {dre.grossRevenue.toFixed(2)}</dd></div>
              <div className="flex justify-between text-rose-700"><dt>(-) Taxas / Deduções</dt><dd className="font-mono">R$ {dre.deductionsAndFees.toFixed(2)}</dd></div>
              <div className="flex justify-between font-semibold"><dt>Receita Líquida</dt><dd className="font-mono">R$ {dre.netRevenue.toFixed(2)}</dd></div>
              <div className="flex justify-between text-rose-700"><dt>(-) CMV / Insumos</dt><dd className="font-mono">R$ {dre.cogsAmount.toFixed(2)}</dd></div>
              <div className="flex justify-between font-semibold"><dt>Lucro Bruto</dt><dd className="font-mono">R$ {dre.grossProfit.toFixed(2)}</dd></div>
              <div className="flex justify-between text-rose-700"><dt>(-) Despesas Operacionais</dt><dd className="font-mono">R$ {dre.operatingExpenses.toFixed(2)}</dd></div>
              <div className="flex justify-between border-t pt-2 text-base font-bold"><dt>Resultado Operacional</dt><dd className="font-mono">R$ {dre.operatingResult.toFixed(2)}</dd></div>
            </dl>
          ) : (
            <p className="text-sm text-slate-500">Aguardando autenticação financeira.</p>
          )}
        </section>

        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="mb-3 text-base font-bold text-slate-900">Fluxo de Caixa Realizado e Projetado</h2>
          {cashFlow ? (
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between text-emerald-700"><dt>Entradas Realizadas</dt><dd className="font-mono">R$ {cashFlow.realizedInflows.toFixed(2)}</dd></div>
              <div className="flex justify-between text-rose-700"><dt>Saídas Realizadas</dt><dd className="font-mono">R$ {cashFlow.realizedOutflows.toFixed(2)}</dd></div>
              <div className="flex justify-between font-semibold"><dt>Saldo Realizado</dt><dd className="font-mono">R$ {cashFlow.realizedNetBalance.toFixed(2)}</dd></div>
              <div className="flex justify-between"><dt>A Receber em Aberto</dt><dd className="font-mono">R$ {cashFlow.projectedPendingReceivables.toFixed(2)}</dd></div>
              <div className="flex justify-between"><dt>A Pagar em Aberto</dt><dd className="font-mono">R$ {cashFlow.projectedPendingPayables.toFixed(2)}</dd></div>
              <div className="flex justify-between border-t pt-2 text-base font-bold"><dt>Saldo Projetado</dt><dd className="font-mono">R$ {cashFlow.projectedFinalBalance.toFixed(2)}</dd></div>
            </dl>
          ) : (
            <p className="text-sm text-slate-500">Aguardando autenticação financeira.</p>
          )}
        </section>
      </div>
    </StoreBrandShell>
  );
}
