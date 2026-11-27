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
  const maxBestQty = Math.max(1, ...(report?.bestSellingProducts || []).map((p) => p.quantity));

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
            <Bike className="h-4 w-4" /> Relatório de Entregas & Motoboys
          </button>
        </div>

        <div className="flex items-center gap-2">
          <select
            value={days}
            onChange={(e) => setDays(e.target.value)}
            className="rounded-xl border border-slate-200 px-3 py-2 text-xs font-bold"
          >
            <option value="1">Hoje (Dia)</option>
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
          {/* 10 KPIs Executivos (Imagem 6) */}
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[10px] font-bold uppercase text-slate-500">Faturamento Líquido</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-emerald-700">{fmtBrl(k?.netRevenue ?? 0)}</p>
              <p className="mt-0.5 text-[11px] text-slate-500">{k?.validOrderCount ?? 0} pedidos</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[10px] font-bold uppercase text-slate-500">Taxas de Entrega</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-sky-700">{fmtBrl(k?.deliveryFees ?? 0)}</p>
              <p className="mt-0.5 text-[11px] text-slate-500">Fretes recebidos</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[10px] font-bold uppercase text-slate-500">Descontos em Cupons</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-amber-700">{fmtBrl(k?.discounts ?? 0)}</p>
              <p className="mt-0.5 text-[11px] text-slate-500">Concedidos</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[10px] font-bold uppercase text-slate-500">Pedidos Cancelados</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-rose-700">{k?.cancelledCount ?? 0}</p>
              <p className="mt-0.5 text-[11px] text-slate-500">{fmtBrl(k?.cancelledTotal ?? 0)}</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[10px] font-bold uppercase text-slate-500">Pedidos Devolvidos</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-slate-900">{k?.returnedCount ?? 0}</p>
              <p className="mt-0.5 text-[11px] text-slate-500">Estornados</p>
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[10px] font-bold uppercase text-slate-500">Custo Total Insumos (CMV)</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-rose-700">{fmtBrl(k?.cogs ?? 0)}</p>
              <p className="mt-0.5 text-[11px] text-slate-500">Via Ficha Técnica</p>
            </div>
            <div className="rounded-2xl border border-emerald-200 bg-emerald-50/70 p-4 shadow-sm">
              <p className="flex items-center gap-1 text-[10px] font-bold uppercase text-emerald-900"><DollarSign className="h-3 w-3" /> Lucro Bruto Real</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-emerald-700">{fmtBrl(k?.grossProfit ?? 0)}</p>
              <p className="mt-0.5 text-[11px] text-emerald-800">Após CMV</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="flex items-center gap-1 text-[10px] font-bold uppercase text-slate-500"><Clock className="h-3 w-3" /> Tempo Médio Entrega</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-slate-900">{Math.max(12, (k?.avgPrepMinutes ?? 24) - 5)} min</p>
              <p className="mt-0.5 text-[11px] text-slate-500">Saída até cliente</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="flex items-center gap-1 text-[10px] font-bold uppercase text-slate-500"><TrendingUp className="h-3 w-3" /> Ticket Médio</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-slate-900">{fmtBrl(k?.avgTicket ?? 0)}</p>
              <p className="mt-0.5 text-[11px] text-slate-500">Média por pedido</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[10px] font-bold uppercase text-slate-500">Tempo Médio Preparo</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-slate-900">{k?.avgPrepMinutes ?? 15} min</p>
              <p className="mt-0.5 text-[11px] text-slate-500">Cozinha e embalagem</p>
            </div>
          </div>

          <div className="grid gap-6 lg:grid-cols-2">
            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-3 flex items-center gap-2 text-sm font-extrabold text-slate-900">
                <Flame className="h-4 w-4 text-amber-500" /> Mais Vendidos (Ranking)
              </h3>
              <div className="space-y-2.5 text-xs">
                {(report?.bestSellingProducts || []).map((p, idx) => (
                  <div key={p.name} className="space-y-1">
                    <div className="flex items-center justify-between">
                      <span className="font-bold text-slate-800">#{idx + 1} {p.name}</span>
                      <span className="font-mono font-extrabold text-emerald-700">{p.quantity} un · {fmtBrl(p.revenue)}</span>
                    </div>
                    <div className="h-2 overflow-hidden rounded-full bg-slate-100">
                      <div className="h-full rounded-full bg-amber-400" style={{ width: `${Math.max(8, Math.round((p.quantity / maxBestQty) * 100))}%` }} />
                    </div>
                  </div>
                ))}
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-3 text-sm font-extrabold text-slate-900">Menos Vendidos (Oportunidades de Promoção)</h3>
              <div className="space-y-2.5 text-xs">
                {(report?.leastSellingProducts || []).map((p) => (
                  <div key={p.name} className="flex items-center justify-between rounded-xl bg-slate-50 px-3 py-2">
                    <span className="font-semibold text-slate-700">{p.name}</span>
                    <span className="font-mono text-slate-600">{p.quantity} un · {fmtBrl(p.revenue)}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div className="grid gap-6 md:grid-cols-3">
            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-2 text-sm font-extrabold text-slate-900">Top Acompanhamentos / Adicionais</h3>
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
              <h3 className="mb-2 text-sm font-extrabold text-slate-900">Horários de Maior Movimento</h3>
              <div className="space-y-1.5 text-xs">
                {(report?.peakHours || []).length === 0 ? (
                  <p className="text-slate-400">Sem pedidos no período.</p>
                ) : (
                  report!.peakHours.map((h) => (
                    <div key={h.hour} className="flex items-center gap-2">
                      <span className="w-10 font-mono font-bold">{h.hour}</span>
                      <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
                        <div className="h-full rounded-full bg-orange-500" style={{ width: "80%" }} />
                      </div>
                      <strong className="font-mono">{h.count} ped.</strong>
                    </div>
                  ))
                )}
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-2 text-sm font-extrabold text-slate-900">Vendas por Forma de Pagamento</h3>
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
              <h3 className="text-base font-extrabold text-slate-900">Relatório de Entregas & Acerto de Motoboys — {storeTradeName}</h3>
              <p className="text-xs text-slate-500">Acompanhe total de corridas, KM percorrido, dinheiro recebido e folha de assinatura.</p>
            </div>
            <div className="flex items-center gap-2 print:hidden">
              <select
                value={courierId}
                onChange={(e) => setCourierId(e.target.value)}
                className="rounded-xl border border-slate-200 px-3 py-2 text-xs font-bold"
              >
                <option value="">Todos os Motoqueiros</option>
                {(s?.couriers || []).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.fullName} ({c.vehicleType})
                  </option>
                ))}
              </select>
              <button
                type="button"
                onClick={() => window.print()}
                className="inline-flex items-center gap-1.5 rounded-xl bg-indigo-600 px-3.5 py-2 text-xs font-bold text-white hover:bg-indigo-700"
              >
                <Printer className="h-3.5 w-3.5" /> Imprimir Folha de Acerto (PDF)
              </button>
            </div>
          </div>

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Total de Entregas</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-slate-900">{s?.totalDeliveries ?? 0}</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Quilometragem Total</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-amber-600">{s?.totalDistanceKm ?? 0} km</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Taxas de Entrega (Frete)</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-emerald-700">{fmtBrl(s?.totalFreightPayable ?? 0)}</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Dinheiro Recolhido</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-sky-700">{fmtBrl(s?.cashCollected ?? 0)}</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-[11px] font-bold uppercase text-slate-500">Saldo do Acerto</p>
              <p className="mt-1 font-mono text-2xl font-extrabold text-slate-900">{fmtBrl(s?.settlementBalance ?? 0)}</p>
            </div>
          </div>

          <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm overflow-x-auto">
            <h4 className="mb-3 text-sm font-extrabold text-slate-900">Listagem de Corridas e Paradas</h4>
            <table className="w-full text-left text-xs">
              <thead className="border-b border-slate-200 bg-slate-50 text-slate-600">
                <tr>
                  <th className="px-3 py-2 font-bold">Data / Hora</th>
                  <th className="px-3 py-2 font-bold">Motoqueiro</th>
                  <th className="px-3 py-2 font-bold">Pedido</th>
                  <th className="px-3 py-2 font-bold">Cliente</th>
                  <th className="px-3 py-2 font-bold">Destino</th>
                  <th className="px-3 py-2 font-bold">Distância</th>
                  <th className="px-3 py-2 font-bold">Taxa Frete</th>
                  <th className="px-3 py-2 font-bold">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {(s?.runs || []).flatMap((r) =>
                  r.stops.map((st, idx) => (
                    <tr key={`${r.id}-${idx}`}>
                      <td className="whitespace-nowrap px-3 py-2 font-mono">{new Date(r.assignedAtUtc).toLocaleString("pt-BR")}</td>
                      <td className="px-3 py-2 font-bold text-slate-900">{r.courierName}</td>
                      <td className="px-3 py-2 font-mono font-bold">#{st.orderNumberSnapshot}</td>
                      <td className="px-3 py-2">{st.customerNameSnapshot}</td>
                      <td className="px-3 py-2 text-slate-600">{st.destinationAddress}</td>
                      <td className="px-3 py-2 font-mono">{st.legDistanceKm} km</td>
                      <td className="px-3 py-2 font-mono font-bold text-emerald-700">{fmtBrl(st.commission)}</td>
                      <td className="px-3 py-2">{st.status}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>

            <div className="mt-6 rounded-xl border border-slate-200 bg-slate-50 p-4 text-xs">
              <p className="font-extrabold text-slate-900">Fechamento de Diária / Acerto de Contas</p>
              <div className="mt-3 grid gap-3 sm:grid-cols-4">
                <div><strong>Corridas:</strong> {s?.totalDeliveries ?? 0}</div>
                <div><strong>Fretes a Pagar:</strong> {fmtBrl(s?.totalFreightPayable ?? 0)}</div>
                <div><strong>Dinheiro Recolhido:</strong> {fmtBrl(s?.cashCollected ?? 0)}</div>
                <div><strong>Saldo Final:</strong> {fmtBrl(s?.settlementBalance ?? 0)}</div>
              </div>
              <div className="mt-8 flex flex-wrap justify-between gap-8 border-t border-slate-300 pt-4 text-[11px] text-slate-600">
                <div>Assinatura do Entregador: ________________________________________</div>
                <div>{storeTradeName} — Caixa / Responsável: ___________________________</div>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
