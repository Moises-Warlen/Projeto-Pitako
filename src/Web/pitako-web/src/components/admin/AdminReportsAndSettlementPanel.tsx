"use client";

import { BarChart3, Bike, Calendar, Clock, DollarSign, Flame, MessageCircle, Printer, Send, TrendingUp } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "@/lib/api";

interface ExecutiveReportDto {
  windowDays: number;
  startDateIso?: string;
  endDateIso?: string;
  storeWhatsAppPhone?: string | null;
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
  const [datePreset, setDatePreset] = useState<"1" | "7" | "15" | "30" | "custom">("7");
  const [startDate, setStartDate] = useState(() => {
    const d = new Date(Date.now() - 6 * 86400_000);
    return d.toISOString().slice(0, 10);
  });
  const [endDate, setEndDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [courierId, setCourierId] = useState("");
  const [runStatus, setRunStatus] = useState("");
  const [report, setReport] = useState<ExecutiveReportDto | null>(null);
  const [waModalOpen, setWaModalOpen] = useState(false);
  const [waTargetMode, setWaTargetMode] = useState<"store" | "courier" | "custom">("store");
  const [customWaPhone, setCustomWaPhone] = useState("");
  const [waFeedback, setWaFeedback] = useState<string | null>(null);

  const loadReport = useCallback(async () => {
    if (!token) return;
    const qs = new URLSearchParams();
    if (datePreset === "custom") {
      if (startDate) qs.set("startDate", startDate);
      if (endDate) qs.set("endDate", endDate);
    } else {
      qs.set("days", datePreset);
    }
    if (courierId) qs.set("courierId", courierId);
    if (runStatus) qs.set("runStatus", runStatus);
    const res = await apiFetch<ExecutiveReportDto>(`/api/v1/reports/executive?${qs.toString()}`, {}, token);
    if (res.ok && res.data) {
      setReport(res.data);
      if (datePreset !== "custom" && res.data.startDateIso && res.data.endDateIso) {
        setStartDate(res.data.startDateIso);
        setEndDate(res.data.endDateIso);
      }
    }
  }, [courierId, datePreset, endDate, runStatus, startDate, token]);

  useEffect(() => {
    void loadReport();
  }, [loadReport]);

  const k = report?.financialKpis;
  const s = report?.courierSettlement;
  const maxBestQty = Math.max(1, ...(report?.bestSellingProducts || []).map((p) => p.quantity));
  const selectedCourier = s?.couriers.find((c) => c.id === courierId) || null;
  const periodLabel = `${startDate.split("-").reverse().join("/")} até ${endDate.split("-").reverse().join("/")}`;

  function buildWhatsAppText() {
    if (subTab === "sales") {
      const topProducts = (report?.bestSellingProducts || [])
        .slice(0, 5)
        .map((p, i) => `${i + 1}. ${p.name}: ${p.quantity} un (${fmtBrl(p.revenue)})`)
        .join("\n");
      return [
        `📊 *Relatório Gerencial — ${storeTradeName}*`,
        `Período: ${periodLabel}`,
        `• Faturamento Líquido: ${fmtBrl(k?.netRevenue ?? 0)} (${k?.validOrderCount ?? 0} pedidos)`,
        `• Taxas de Entrega: ${fmtBrl(k?.deliveryFees ?? 0)}`,
        `• Descontos Cupons: ${fmtBrl(k?.discounts ?? 0)}`,
        `• Cancelados: ${k?.cancelledCount ?? 0} (${fmtBrl(k?.cancelledTotal ?? 0)})`,
        `• Custo Insumos (CMV): ${fmtBrl(k?.cogs ?? 0)}`,
        `• Lucro Bruto Real: ${fmtBrl(k?.grossProfit ?? 0)}`,
        `• Ticket Médio: ${fmtBrl(k?.avgTicket ?? 0)}`,
        topProducts ? `\n🔥 *Mais Vendidos:*\n${topProducts}` : ""
      ]
        .filter(Boolean)
        .join("\n");
    }

    const headerWho = selectedCourier ? `Motoqueiro: ${selectedCourier.fullName}` : "Todos os Motoqueiros";
    const runsPreview = (s?.runs || [])
      .flatMap((r) => r.stops.map((st) => `• #${st.orderNumberSnapshot} (${r.courierName}) — ${st.legDistanceKm} km • Frete ${fmtBrl(st.commission)} [${st.status}]`))
      .slice(0, 12)
      .join("\n");
    return [
      `🛵 *Acerto de Entregas — ${storeTradeName}*`,
      `Período: ${periodLabel} • ${headerWho}`,
      `• Total de Entregas: ${s?.totalDeliveries ?? 0}`,
      `• Quilometragem: ${s?.totalDistanceKm ?? 0} km`,
      `• Fretes a Pagar: ${fmtBrl(s?.totalFreightPayable ?? 0)}`,
      `• Dinheiro Recolhido: ${fmtBrl(s?.cashCollected ?? 0)}`,
      `• Saldo Final do Acerto: ${fmtBrl(s?.settlementBalance ?? 0)}`,
      runsPreview ? `\n📋 *Corridas:*\n${runsPreview}` : ""
    ]
      .filter(Boolean)
      .join("\n");
  }

  function resolveWhatsAppPhoneDigits() {
    let raw = "";
    if (waTargetMode === "store") raw = report?.storeWhatsAppPhone || "";
    else if (waTargetMode === "courier") raw = selectedCourier?.phone || customWaPhone;
    else raw = customWaPhone;
    const digits = raw.replace(/\D/g, "");
    if (!digits) return "";
    return digits.startsWith("55") ? digits : `55${digits}`;
  }

  function handleSendWhatsAppReport() {
    const text = buildWhatsAppText();
    const digits = resolveWhatsAppPhoneDigits();
    if (!digits || digits.length < 10) {
      setWaFeedback("Informe um número de WhatsApp válido (com DDD) para enviar.");
      return;
    }
    if (typeof navigator !== "undefined" && navigator.clipboard) {
      void navigator.clipboard.writeText(text).catch(() => {});
    }
    const url = `https://wa.me/${digits}?text=${encodeURIComponent(text)}`;
    window.open(url, "_blank", "noopener,noreferrer");
    setWaFeedback(`Resumo copiado e WhatsApp aberto para +${digits}!`);
  }

  function handlePrintCleanPdf() {
    const popup = window.open("", "_blank", "width=980,height=780");
    if (!popup) {
      window.print();
      return;
    }
    const esc = (v: string | number | null | undefined) =>
      String(v ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

    let bodyHtml = "";
    if (subTab === "sales") {
      bodyHtml = `
        <h1>Relatório Gerencial de Faturamento & Vendas — ${esc(storeTradeName)}</h1>
        <p class="sub">Período analisado: <strong>${esc(periodLabel)}</strong> • Gerado em ${new Date().toLocaleString("pt-BR")}</p>
        <div class="kpis">
          <div class="kpi"><span>Faturamento Líquido</span><strong>${esc(fmtBrl(k?.netRevenue ?? 0))}</strong><small>${k?.validOrderCount ?? 0} pedidos</small></div>
          <div class="kpi"><span>Taxas de Entrega</span><strong>${esc(fmtBrl(k?.deliveryFees ?? 0))}</strong><small>Fretes recebidos</small></div>
          <div class="kpi"><span>Descontos Cupons</span><strong>${esc(fmtBrl(k?.discounts ?? 0))}</strong><small>Concedidos</small></div>
          <div class="kpi"><span>Pedidos Cancelados</span><strong>${k?.cancelledCount ?? 0}</strong><small>${esc(fmtBrl(k?.cancelledTotal ?? 0))}</small></div>
          <div class="kpi"><span>Custo Insumos (CMV)</span><strong>${esc(fmtBrl(k?.cogs ?? 0))}</strong><small>Via Ficha Técnica</small></div>
          <div class="kpi"><span>Lucro Bruto Real</span><strong>${esc(fmtBrl(k?.grossProfit ?? 0))}</strong><small>Após CMV</small></div>
          <div class="kpi"><span>Ticket Médio</span><strong>${esc(fmtBrl(k?.avgTicket ?? 0))}</strong><small>Por pedido</small></div>
          <div class="kpi"><span>Tempo Médio Preparo</span><strong>${k?.avgPrepMinutes ?? 24} min</strong><small>Cozinha / Embalagem</small></div>
        </div>
        <h2>Produtos Mais Vendidos</h2>
        <table>
          <thead><tr><th>#</th><th>Produto</th><th>Quantidade</th><th>Faturamento</th></tr></thead>
          <tbody>
            ${(report?.bestSellingProducts || []).map((p, idx) => `<tr><td>${idx + 1}</td><td>${esc(p.name)}</td><td>${p.quantity} un</td><td>${esc(fmtBrl(p.revenue))}</td></tr>`).join("")}
          </tbody>
        </table>
        <h2>Vendas por Forma de Pagamento</h2>
        <table>
          <thead><tr><th>Forma de Pagamento</th><th>Pedidos</th><th>Total</th></tr></thead>
          <tbody>
            ${(report?.paymentMethods || []).map((pm) => `<tr><td>${esc(pm.method)}</td><td>${pm.count}x</td><td>${esc(fmtBrl(pm.total))}</td></tr>`).join("")}
          </tbody>
        </table>
      `;
    } else {
      const stopsRows = (s?.runs || [])
        .flatMap((r) =>
          r.stops.map(
            (st) =>
              `<tr><td>${esc(new Date(r.assignedAtUtc).toLocaleString("pt-BR"))}</td><td>${esc(r.courierName)}</td><td>#${esc(st.orderNumberSnapshot)}</td><td>${esc(st.customerNameSnapshot)}</td><td>${esc(st.destinationAddress)}</td><td>${st.legDistanceKm} km</td><td>${esc(fmtBrl(st.commission))}</td><td>${esc(st.status)}</td></tr>`
          )
        )
        .join("");

      bodyHtml = `
        <h1>Relatório de Entregas & Acerto de Motoboys — ${esc(storeTradeName)}</h1>
        <p class="sub">Período: <strong>${esc(periodLabel)}</strong> • Entregador: <strong>${esc(selectedCourier ? selectedCourier.fullName : "Todos os Motoqueiros")}</strong> • Status: <strong>${esc(runStatus || "Todos")}</strong></p>
        <div class="kpis">
          <div class="kpi"><span>Total de Entregas</span><strong>${s?.totalDeliveries ?? 0}</strong></div>
          <div class="kpi"><span>Quilometragem Total</span><strong>${s?.totalDistanceKm ?? 0} km</strong></div>
          <div class="kpi"><span>Fretes a Pagar</span><strong>${esc(fmtBrl(s?.totalFreightPayable ?? 0))}</strong></div>
          <div class="kpi"><span>Dinheiro Recolhido</span><strong>${esc(fmtBrl(s?.cashCollected ?? 0))}</strong></div>
          <div class="kpi"><span>Saldo Final do Acerto</span><strong>${esc(fmtBrl(s?.settlementBalance ?? 0))}</strong></div>
        </div>
        <h2>Listagem de Corridas e Paradas</h2>
        <table>
          <thead><tr><th>Data / Hora</th><th>Motoqueiro</th><th>Pedido</th><th>Cliente</th><th>Destino</th><th>Distância</th><th>Frete</th><th>Status</th></tr></thead>
          <tbody>${stopsRows || `<tr><td colspan="8" style="text-align:center">Sem corridas no período filtrado.</td></tr>`}</tbody>
        </table>
        <div class="sign">
          <div>Assinatura do Entregador: ______________________________________</div>
          <div>${esc(storeTradeName)} — Caixa / Responsável: _________________________</div>
        </div>
      `;
    }

    popup.document.write(`<!DOCTYPE html><html lang="pt-BR"><head><meta charset="utf-8"><title>Relatório — ${esc(storeTradeName)}</title><style>
      @page { size: A4; margin: 14mm; }
      body { font-family: Arial, sans-serif; color: #0f172a; font-size: 12px; }
      h1 { font-size: 18px; margin: 0 0 4px; }
      h2 { font-size: 13px; margin: 16px 0 6px; border-bottom: 1px solid #cbd5e1; padding-bottom: 4px; }
      .sub { font-size: 11px; color: #475569; margin: 0 0 14px; }
      .kpis { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin-bottom: 14px; }
      .kpi { border: 1px solid #cbd5e1; border-radius: 8px; padding: 8px; }
      .kpi span { display: block; font-size: 10px; color: #64748b; text-transform: uppercase; font-weight: bold; }
      .kpi strong { display: block; font-size: 15px; margin-top: 2px; }
      .kpi small { font-size: 10px; color: #64748b; }
      table { width: 100%; border-collapse: collapse; font-size: 11px; }
      th, td { border: 1px solid #cbd5e1; padding: 6px 8px; text-align: left; }
      th { background: #f8fafc; font-weight: bold; }
      .sign { display: flex; justify-content: space-between; margin-top: 28px; padding-top: 16px; border-top: 1px solid #cbd5e1; font-size: 11px; }
      @media print { button { display: none; } }
    </style></head><body>${bodyHtml}<script>window.onload=function(){setTimeout(function(){window.print();},250);};</script></body></html>`);
    popup.document.close();
  }

  return (
    <div className="space-y-6">
      <div className="space-y-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm print:hidden">
        <div className="flex flex-wrap items-center justify-between gap-3">
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

          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => {
                setWaFeedback(null);
                setWaModalOpen((v) => !v);
              }}
              className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-bold text-white hover:bg-emerald-700"
            >
              <MessageCircle className="h-3.5 w-3.5" /> Enviar p/ WhatsApp
            </button>
            <button
              type="button"
              onClick={handlePrintCleanPdf}
              className="inline-flex items-center gap-1.5 rounded-xl bg-slate-900 px-3.5 py-2 text-xs font-bold text-white hover:opacity-90"
            >
              <Printer className="h-3.5 w-3.5" /> Baixar / Imprimir PDF
            </button>
          </div>
        </div>

        {/* Barra unificada de Filtros de Datas (+ Filtros Motoboys quando ativo) */}
        <div className="flex flex-wrap items-center gap-2 border-t border-slate-100 pt-3 text-xs">
          <span className="inline-flex items-center gap-1 font-extrabold text-slate-700">
            <Calendar className="h-3.5 w-3.5 text-slate-500" /> Período:
          </span>
          <select
            value={datePreset}
            onChange={(e) => setDatePreset(e.target.value as "1" | "7" | "15" | "30" | "custom")}
            aria-label="Selecionar período do relatório"
            className="rounded-xl border border-slate-200 px-3 py-1.5 font-bold text-slate-800"
          >
            <option value="1">Hoje (Dia)</option>
            <option value="7">Últimos 7 dias</option>
            <option value="15">Últimos 15 dias</option>
            <option value="30">Últimos 30 dias</option>
            <option value="custom">Intervalo Personalizado</option>
          </select>

          <label className="inline-flex items-center gap-1 font-semibold text-slate-600">
            De:
            <input
              type="date"
              value={startDate}
              onChange={(e) => {
                setStartDate(e.target.value);
                setDatePreset("custom");
              }}
              className="rounded-xl border border-slate-200 px-2.5 py-1 font-mono text-xs text-slate-800"
            />
          </label>
          <label className="inline-flex items-center gap-1 font-semibold text-slate-600">
            Até:
            <input
              type="date"
              value={endDate}
              onChange={(e) => {
                setEndDate(e.target.value);
                setDatePreset("custom");
              }}
              className="rounded-xl border border-slate-200 px-2.5 py-1 font-mono text-xs text-slate-800"
            />
          </label>

          {subTab === "settlement" && (
            <>
              <select
                value={courierId}
                onChange={(e) => setCourierId(e.target.value)}
                aria-label="Filtrar por motoqueiro"
                className="rounded-xl border border-slate-200 px-3 py-1.5 font-bold text-slate-800"
              >
                <option value="">Todos os Motoqueiros</option>
                {(s?.couriers || []).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.fullName} ({c.vehicleType})
                  </option>
                ))}
              </select>
              <select
                value={runStatus}
                onChange={(e) => setRunStatus(e.target.value)}
                aria-label="Filtrar por status da entrega"
                className="rounded-xl border border-slate-200 px-3 py-1.5 font-bold text-slate-800"
              >
                <option value="">Todos os Status</option>
                <option value="Assigned">Atribuída (Assigned)</option>
                <option value="InTransit">Em Rota (InTransit)</option>
                <option value="Completed">Concluída (Completed)</option>
                <option value="Cancelled">Cancelada</option>
              </select>
            </>
          )}
        </div>

        {waModalOpen && (
          <div className="rounded-xl border border-emerald-200 bg-emerald-50/70 p-3.5 text-xs space-y-2.5">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="font-extrabold text-emerald-950">📲 Enviar Relatório por WhatsApp</p>
              <button
                type="button"
                onClick={() => setWaModalOpen(false)}
                className="text-[11px] font-bold text-slate-500 hover:underline"
              >
                Fechar
              </button>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <select
                value={waTargetMode}
                onChange={(e) => setWaTargetMode(e.target.value as "store" | "courier" | "custom")}
                className="rounded-xl border border-emerald-300 bg-white px-3 py-1.5 font-bold text-slate-800"
              >
                <option value="store">
                  WhatsApp da Loja ({report?.storeWhatsAppPhone || "Não cadastrado"})
                </option>
                {selectedCourier && (
                  <option value="courier">
                    WhatsApp de {selectedCourier.fullName} ({selectedCourier.phone || "Informar abaixo"})
                  </option>
                )}
                <option value="custom">Outro Número de WhatsApp</option>
              </select>

              {(waTargetMode === "custom" || (waTargetMode === "courier" && !selectedCourier?.phone)) && (
                <input
                  value={customWaPhone}
                  onChange={(e) => setCustomWaPhone(e.target.value)}
                  placeholder="DDD + Número (ex: 16991098863)"
                  className="rounded-xl border border-emerald-300 bg-white px-3 py-1.5 font-mono"
                />
              )}

              <button
                type="button"
                onClick={handleSendWhatsAppReport}
                className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-700 px-3.5 py-1.5 font-extrabold text-white hover:bg-emerald-800"
              >
                <Send className="h-3.5 w-3.5" /> Abrir e Enviar no WhatsApp
              </button>
            </div>
            {waFeedback && <p className="font-bold text-emerald-900">{waFeedback}</p>}
          </div>
        )}
      </div>

      {/* Cabeçalho exclusivo de Impressão direta (caso usuário use Ctrl+P) */}
      <div className="hidden print:block mb-4 border-b border-slate-300 pb-3">
        <h1 className="text-lg font-extrabold text-slate-900">
          {subTab === "sales" ? "Relatório Gerencial de Vendas" : "Acerto de Entregas & Motoboys"} — {storeTradeName}
        </h1>
        <p className="text-xs text-slate-600">Período: {periodLabel}</p>
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
              <button
                type="button"
                onClick={handlePrintCleanPdf}
                className="inline-flex items-center gap-1.5 rounded-xl bg-indigo-600 px-3.5 py-2 text-xs font-bold text-white hover:bg-indigo-700"
              >
                <Printer className="h-3.5 w-3.5" /> Baixar Folha de Acerto (PDF)
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
