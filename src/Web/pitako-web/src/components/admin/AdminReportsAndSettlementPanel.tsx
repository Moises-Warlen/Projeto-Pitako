"use client";

import { BarChart3, Bike, Clock, DollarSign, Flame, Printer, TrendingUp } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "@/lib/api";

interface ExecutiveReportDto {
  windowDays: number;
  financialKpis: {
    netRevenue: number;
    validOrderCount: number;
    deliveryFees: number;
    discounts: number;
    cancelledCount: number;
    cancelledTotal: number;
    returnedCount: number;
    cogs: number;
    grossProfit: number;
    avgTicket: number;
    avgPrepMinutes: number;
  };
  bestSellingProducts: Array<{ name: string; quantity: number; revenue: number; cost: number }>;
  leastSellingProducts: Array<{ name: string; quantity: number; revenue: number }>;
  topCustomizations: Array<{ name: string; count: number }>;
  peakHours: Array<{ hour: string; count: number }>;
  paymentMethods: Array<{ method: string; count: number; total: number }>;
  statusCounts: Array<{ status: string; count: number }>;
  courierSettlement: {
    couriers: Array<{ id: string; fullName: string; phone: string; vehicleType: string; licensePlate?: string | null }>;
    totalDeliveries: number;
    totalDistanceKm: number;
    totalFreightPayable: number;
    cashCollected: number;
    settlementBalance: number;
    runs: Array<{
      id: string;
      runCode: string;
      courierName: string;
      status: string;
      totalEstimatedDistanceKm: number;
      freightCommission: number;
      assignedAtUtc: string;
      stops: Array<{
        orderNumberSnapshot: string;
        customerNameSnapshot: string;
        destinationAddress: string;
        legDistanceKm: number;
        commission: number;
        status: string;
      }>;
    }>;
  };
}

function fmtBrl(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

export function AdminReportsAndSettlementPanel({
  token,
  storeTradeName
}: {
  token: string;
  storeTradeName: string;
}) {
  const [subTab, setSubTab] = useState<"sales" | "settlement">("sales");
  const [days, setDays] = useState("7");
  const [courierId, setCourierId] = useState("");
  const [report, setReport] = useState<ExecutiveReportDto | null>(null);

  const loadReport = useCallback(async () => {
    if (!token) return;
    const qs = new URLSearchParams({ days });
    if (courierId) qs.set("courierId", courierId);
    const res = await apiFetch<ExecutiveReportDto>(`/api/v1/reports/executive?${qs.toString()}`, {}, token);
    if (res.ok && res.data) setReport(res.data);
  }, [courierId, days, token]);

  useEffect(() => {
    void loadReport();
  }, [loadReport]);

  const k = report?.financialKpis;
  const s = report?.courierSettlement;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm print:hidden">
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => setSubTab("sales")}
            style={subTab === "sales" ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
            className={`inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-extrabold ${
              subTab === "sales" ? "" : "border border-slate-200 text-slate-700 hover:bg-slate-50"
            }`}
          >
            <BarChart3 className="h-4 w-4" /> Faturamento & Vendas Gerais
          </button>
          <button
            type="button"
            onClick={() => setSubTab("settlement")}
            style={subTab === "settlement" ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
            className={`inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-extrabold ${
              subTab === "settlement" ? "" : "border border-slate-200 text-slate-700 hover:bg-slate-50"
            }`}
          >
            <Bike className="h-4 w-4" /> Entregas & Acerto Motoboys
          </button>
        </div>

        <div className="flex items-center gap-2">
          <select
            value={days}
            onChange={(e) => setDays(e.target.value)}
            className="rounded-xl border border-slate-200 px-3 py-2 text-xs font-bold"
          >
            <option value="1">Hoje (24h)</option>
            <option value="7">Últimos 7 dias</option>
            <option value="15">Últimos 15 dias</option>
            <option value="30">Últimos 30 dias</option>
          </select>
          <button
            type="button"
            onClick={() => window.print()}
            className="inline-flex items-center gap-1.5 rounded-xl bg-slate-900 px-3.5 py-2 text-xs font-bold text-white hover:opacity-90"
          >
            <Printer className="h-3.5 w-3.5" /> Imprimir Relatório PDF
          </button>
        </div>
      </div>

      {subTab === "sales" && (
        <div className="space-y-6">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Faturamento Líquido ({days}d)</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-emerald-700">{fmtBrl(k?.netRevenue ?? 0)}</p>
              <p className="mt-1 text-xs text-slate-500">{k?.validOrderCount ?? 0} pedidos válidos</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Taxas de Entrega</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-sky-700">{fmtBrl(k?.deliveryFees ?? 0)}</p>
              <p className="mt-1 text-xs text-slate-500">Repasse de frete do período</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Descontos Aplicados</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-amber-700">{fmtBrl(k?.discounts ?? 0)}</p>
              <p className="mt-1 text-xs text-slate-500">Cupons e promoções de entrega</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Pedidos Cancelados</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-rose-700">{k?.cancelledCount ?? 0}</p>
              <p className="mt-1 text-xs text-slate-500">{fmtBrl(k?.cancelledTotal ?? 0)} estornados</p>
            </div>
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <div className="rounded-2xl border border-emerald-200 bg-emerald-50/60 p-5">
              <p className="flex items-center gap-1.5 text-xs font-extrabold uppercase text-emerald-900">
                <DollarSign className="h-4 w-4" /> CMV & Lucro Bruto Estimado (Real via Ficha Técnica)
              </p>
              <div className="mt-3 flex flex-wrap items-baseline justify-between gap-4">
                <div>
                  <p className="font-mono text-2xl font-extrabold text-emerald-700">{fmtBrl(k?.grossProfit ?? 0)}</p>
                  <p className="text-xs text-emerald-800">Lucro Bruto após descontar CMV</p>
                </div>
                <div className="text-right">
                  <p className="font-mono text-lg font-extrabold text-rose-700">{fmtBrl(k?.cogs ?? 0)}</p>
                  <p className="text-xs text-slate-600">Custo das Mercadorias (CMV)</p>
                </div>
              </div>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <p className="flex items-center gap-1 text-xs font-bold text-slate-500">
                  <Clock className="h-4 w-4" /> Tempo Médio Operação
                </p>
                <p className="mt-2 font-mono text-2xl font-extrabold text-slate-900">{k?.avgPrepMinutes ?? 24} min</p>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                <p className="flex items-center gap-1 text-xs font-bold text-slate-500">
                  <TrendingUp className="h-4 w-4" /> Ticket Médio
                </p>
                <p className="mt-2 font-mono text-2xl font-extrabold text-slate-900">{fmtBrl(k?.avgTicket ?? 0)}</p>
              </div>
            </div>
          </div>

          <div className="grid gap-6 lg:grid-cols-2">
            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-3 flex items-center gap-2 text-sm font-extrabold text-slate-900">
                <Flame className="h-4 w-4 text-amber-500" /> Produtos Mais Vendidos
              </h3>
              <div className="space-y-2 text-xs">
                {(report?.bestSellingProducts || []).map((p, idx) => (
                  <div key={p.name} className="flex items-center justify-between rounded-xl bg-slate-50 px-3 py-2">
                    <span className="font-bold text-slate-800">
                      #{idx + 1} {p.name}
                    </span>
                    <span className="font-mono font-extrabold text-emerald-700">
                      {p.quantity}x • {fmtBrl(p.revenue)}
                    </span>
                  </div>
                ))}
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-3 text-sm font-extrabold text-slate-900">Produtos com Menor Saída</h3>
              <div className="space-y-2 text-xs">
                {(report?.leastSellingProducts || []).map((p) => (
                  <div key={p.name} className="flex items-center justify-between rounded-xl bg-slate-50 px-3 py-2">
                    <span className="font-semibold text-slate-700">{p.name}</span>
                    <span className="font-mono text-slate-600">
                      {p.quantity} un • {fmtBrl(p.revenue)}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div className="grid gap-6 md:grid-cols-3">
            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-2 text-sm font-extrabold text-slate-900">Acompanhamentos / Extras Populares</h3>
              <div className="flex flex-wrap gap-1.5">
                {(report?.topCustomizations || []).length === 0 ? (
                  <p className="text-xs text-slate-400">Sem acompanhamentos registrados no período.</p>
                ) : (
                  report!.topCustomizations.map((c) => (
                    <span key={c.name} className="rounded-full bg-slate-100 px-2.5 py-1 text-[11px] font-bold text-slate-800">
                      {c.name} ({c.count}x)
                    </span>
                  ))
                )}
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-2 text-sm font-extrabold text-slate-900">Horários de Pico</h3>
              <div className="space-y-1.5 text-xs">
                {(report?.peakHours || []).length === 0 ? (
                  <p className="text-slate-400">Sem pedidos no período.</p>
                ) : (
                  report!.peakHours.map((h) => (
                    <div key={h.hour} className="flex justify-between rounded-lg bg-slate-50 px-3 py-1.5 font-mono">
                      <span>{h.hour}</span>
                      <strong>{h.count} pedido(s)</strong>
                    </div>
                  ))
                )}
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-2 text-sm font-extrabold text-slate-900">Formas de Pagamento</h3>
              <div className="space-y-1.5 text-xs">
                {(report?.paymentMethods || []).map((pm) => (
                  <div key={pm.method} className="flex justify-between rounded-lg bg-slate-50 px-3 py-1.5">
                    <span className="font-bold">{pm.method} ({pm.count}x)</span>
                    <span className="font-mono font-extrabold">{fmtBrl(pm.total)}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}

      {subTab === "settlement" && (
        <div className="space-y-6">
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div>
              <h3 className="text-base font-extrabold text-slate-900">Folha de Acerto de Motoboys — {storeTradeName}</h3>
              <p className="text-xs text-slate-500">Confira entregas realizadas, distância estimada, dinheiro recebido e saldo líquido.</p>
            </div>
            <select
              value={courierId}
              onChange={(e) => setCourierId(e.target.value)}
              className="rounded-xl border border-slate-200 px-3 py-2 text-xs font-bold print:hidden"
            >
              <option value="">Todos os Entregadores</option>
              {(s?.couriers || []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.fullName} ({c.vehicleType})
                </option>
              ))}
            </select>
          </div>

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Total de Entregas</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-slate-900">{s?.totalDeliveries ?? 0}</p>
              <p className="text-xs text-slate-500">{s?.totalDistanceKm ?? 0} km rodados</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Fretes a Pagar (Comissão)</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-emerald-700">{fmtBrl(s?.totalFreightPayable ?? 0)}</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Dinheiro em Mãos</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-amber-700">{fmtBrl(s?.cashCollected ?? 0)}</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Saldo Acerto Final</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-slate-900">{fmtBrl(s?.settlementBalance ?? 0)}</p>
              <p className="text-[11px] text-slate-500">
                {(s?.settlementBalance ?? 0) >= 0 ? "Loja paga ao motoboy" : "Motoboy devolve ao caixa"}
              </p>
            </div>
          </div>

          <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h4 className="mb-3 text-sm font-extrabold text-slate-900">Romaneios & Corridas Despachadas</h4>
            {(s?.runs || []).length === 0 ? (
              <p className="text-xs text-slate-500">Nenhuma corrida de entrega registrada neste filtro.</p>
            ) : (
              <div className="space-y-3">
                {s!.runs.map((r) => (
                  <div key={r.id} className="rounded-xl border border-slate-200 p-3.5 text-xs">
                    <div className="flex flex-wrap items-center justify-between gap-2 font-extrabold text-slate-900">
                      <span>
                        {r.runCode} — {r.courierName}
                      </span>
                      <span className="font-mono text-emerald-700">
                        {r.totalEstimatedDistanceKm} km • Comissão {fmtBrl(r.freightCommission)}
                      </span>
                    </div>
                    <div className="mt-2 space-y-1">
                      {r.stops.map((st, idx) => (
                        <div key={`${st.orderNumberSnapshot}-${idx}`} className="flex justify-between text-[11px] text-slate-600">
                          <span>
                            #{st.orderNumberSnapshot} — {st.customerNameSnapshot} ({st.destinationAddress})
                          </span>
                          <span className="font-mono">{fmtBrl(st.commission)}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}
            <div className="mt-8 hidden border-t border-slate-300 pt-4 text-xs text-slate-700 print:flex print:justify-between">
              <div>Assinatura Entregador: ________________________________________</div>
              <div>Conferência Caixa / Loja: _____________________________________</div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
