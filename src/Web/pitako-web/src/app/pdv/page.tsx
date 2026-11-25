"use client";

import { useState } from "react";
import { StoreBrandShell } from "@/components/StoreBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

export default function PdvCashierPage() {
  const [terminalCode, setTerminalCode] = useState("PDV-01");
  const [openingBalance, setOpeningBalance] = useState("200.00");
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [grossGrams, setGrossGrams] = useState("450");
  const [tareGrams, setTareGrams] = useState("30");
  const [pricePerKg, setPricePerKg] = useState("64.90");
  const [manualReason, setManualReason] = useState("Contingência de balcão");
  const [statusMsg, setStatusMsg] = useState<string | null>(null);

  const netKg = Math.max(0, (Number(grossGrams) - Number(tareGrams)) / 1000);
  const weighedTotal = (netKg * Number(pricePerKg)).toFixed(2);

  async function handleOpenRegister(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) {
      setStatusMsg("Autentique-se em /login para operar o caixa.");
      return;
    }

    const res = await apiFetch<{ id: string }>(
      "/api/v1/pdv/sessions/open",
      {
        method: "POST",
        body: JSON.stringify({ terminalCode, openingBalance: Number(openingBalance) })
      },
      session.accessToken
    );

    if (res.ok && res.data) {
      setSessionId(res.data.id);
      setStatusMsg(`Caixa ${terminalCode} aberto (sessão ${res.data.id}).`);
    } else {
      setStatusMsg(res.errorMessage || "Erro ao abrir caixa.");
    }
  }

  async function handleManualWeighing() {
    const session = loadSession();
    if (!session) {
      setStatusMsg("Autentique-se em /login para registrar pesagem.");
      return;
    }

    const res = await apiFetch(
      "/api/v1/weighing/manual",
      {
        method: "POST",
        body: JSON.stringify({
          productName: "Açaí por Quilo",
          pricePerKg: Number(pricePerKg),
          grossGrams: Number(grossGrams),
          manualTareGrams: Number(tareGrams),
          reason: manualReason
        })
      },
      session.accessToken
    );

    setStatusMsg(
      res.ok
        ? `Pesagem auditada registrada: ${netKg.toFixed(3)} kg — R$ ${weighedTotal}`
        : res.errorMessage || "Erro ao registrar pesagem."
    );
  }

  return (
    <StoreBrandShell activeSection="/pdv">
      {statusMsg && (
        <div className="mb-6 rounded-xl border border-slate-300 bg-white p-4 text-sm text-slate-800 shadow-sm">
          {statusMsg}
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <h1 className="text-lg font-bold">Frente de Caixa (PDV)</h1>
          <form onSubmit={handleOpenRegister} className="mt-4 space-y-3">
            <input
              value={terminalCode}
              onChange={(e) => setTerminalCode(e.target.value)}
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
              placeholder="Terminal (PDV-01)"
            />
            <input
              type="number"
              step="0.01"
              value={openingBalance}
              onChange={(e) => setOpeningBalance(e.target.value)}
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
            />
            <button
              type="submit"
              style={{ backgroundColor: "var(--store-primary)" }}
              className="w-full rounded-lg py-2 text-sm font-semibold text-white"
            >
              {sessionId ? "Sessão Ativa" : "Abrir Caixa"}
            </button>
          </form>
        </section>

        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="text-lg font-bold">Balança Toledo / Contingência Manual</h2>
          <div className="mt-4 space-y-3 text-sm">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="text-xs text-slate-500">Peso Bruto (g)</label>
                <input
                  type="number"
                  value={grossGrams}
                  onChange={(e) => setGrossGrams(e.target.value)}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2"
                />
              </div>
              <div>
                <label className="text-xs text-slate-500">Tara (g)</label>
                <input
                  type="number"
                  value={tareGrams}
                  onChange={(e) => setTareGrams(e.target.value)}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2"
                />
              </div>
            </div>
            <div>
              <label className="text-xs text-slate-500">Valor por kg (R$)</label>
              <input
                type="number"
                step="0.01"
                value={pricePerKg}
                onChange={(e) => setPricePerKg(e.target.value)}
                className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2"
              />
            </div>
            <input
              value={manualReason}
              onChange={(e) => setManualReason(e.target.value)}
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-xs"
              placeholder="Justificativa obrigatória"
            />
            <div className="rounded-lg bg-slate-100 p-3 font-mono text-xs">
              Peso líquido: {netKg.toFixed(3)} kg | Subtotal: R$ {weighedTotal}
            </div>
            <button
              type="button"
              onClick={handleManualWeighing}
              className="w-full rounded-lg bg-slate-800 py-2 font-semibold text-white hover:bg-slate-900"
            >
              Registrar Pesagem Manual Auditada
            </button>
          </div>
        </section>

        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="text-lg font-bold">Prévia do Cupom Não Fiscal</h2>
          <div className="mt-3 rounded-lg border border-dashed border-slate-400 bg-amber-50/70 p-4 font-mono text-xs leading-relaxed text-slate-800">
            <p className="text-center font-bold">[LOGO DA LOJA]</p>
            <p className="text-center font-bold">AÇAÍ TROPICAL EXPRESS</p>
            <p className="text-center">CNPJ: 11.222.333/0001-81</p>
            <p className="my-2 border-y border-dashed border-slate-400 py-1 text-center font-semibold">
              DOCUMENTO NÃO FISCAL — CONFERÊNCIA
            </p>
            <p>1x Açaí por Quilo ({netKg.toFixed(3)}kg) ... R$ {weighedTotal}</p>
            <p className="mt-2 border-t border-dashed border-slate-400 pt-1 font-bold">
              TOTAL ............................ R$ {weighedTotal}
            </p>
          </div>
        </section>
      </div>
    </StoreBrandShell>
  );
}
