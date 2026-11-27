"use client";

import { BellRing, Printer, Radio, Volume2, VolumeX } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { StoreBrandShell } from "@/components/StoreBrandShell";
import {
  CourierDto,
  DeliveryRoutesModal,
  DispatchRunDto,
  PedidoOrderItem,
} from "@/components/pedidos/DeliveryRoutesModal";
import { ThermalComandaModal } from "@/components/pedidos/ThermalComandaModal";
import { apiFetch, loadSession } from "@/lib/api";
import {
  getOrderAlarmMuted,
  playContinuousOrderRingPulse,
  setOrderAlarmMuted,
  subscribeOrderAlarmMute
} from "@/lib/soundAlerts";
import { usePitakoRealtime } from "@/lib/usePitakoRealtime";

interface FullOrderDto extends PedidoOrderItem {
  customerOrderCount?: number;
  customerPreviousOrdersCount?: number;
  itemsSubtotal: number;
  itemsDiscount: number;
  completedAtUtc?: string | null;
  cancelledAtUtc?: string | null;
  items: Array<{
    id: string;
    itemTitle: string;
    customizationSummaryText: string;
    quantity: number;
    lineSubtotal: number;
    customerNotes?: string | null;
  }>;
}

interface DeliveryPolicyCoords {
  originLatitude: number;
  originLongitude: number;
}

interface StoreMetaDto {
  tradeName?: string;
  legalName?: string;
  address?: string;
}

const SEVEN_COLUMNS = [
  { code: 1, key: "received", label: "Novos Pedidos", accent: "border-t-amber-500" },
  { code: 2, key: "accepted", label: "Aceitos", accent: "border-t-blue-500" },
  { code: 3, key: "preparing", label: "Em Preparo", accent: "border-t-indigo-500" },
  { code: 4, key: "ready", label: "Prontos", accent: "border-t-emerald-500" },
  { code: 5, key: "dispatched", label: "Em Entrega", accent: "border-t-violet-500" },
  { code: 6, key: "completed", label: "Concluídos", accent: "border-t-slate-600" },
  { code: 7, key: "cancelled", label: "Cancelados", accent: "border-t-rose-500" },
];

function brl(val: number): string {
  return val.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

export default function OrdersKanbanPage() {
  const [token, setToken] = useState<string>("");
  const [orders, setOrders] = useState<FullOrderDto[]>([]);
  const [couriers, setCouriers] = useState<CourierDto[]>([]);
  const [recentRuns, setRecentRuns] = useState<DispatchRunDto[]>([]);
  const [originCoords, setOriginCoords] = useState<DeliveryPolicyCoords>({
    originLatitude: -20.5386,
    originLongitude: -47.4008,
  });
  const [storeMeta, setStoreMeta] = useState<StoreMetaDto>({
    tradeName: "Loja PITAKO",
    address: "Centro — Ponto de Partida",
  });

  // View mode & filters (aligned with reference screenshots)
  const [viewMode, setViewMode] = useState<"LIVE4" | "ALL7">("LIVE4");
  const [statusFilter, setStatusFilter] = useState<string>("ALL");
  const [dateFilter, setDateFilter] = useState<"ALL" | "TODAY" | "7D" | "30D">("ALL");
  const [paymentFilter, setPaymentFilter] = useState<
    "ALL" | "PIX" | "CASH" | "CARD" | "CONFIRMED" | "COD" | "PENDING"
  >("ALL");

  const [routesOpen, setRoutesOpen] = useState(false);
  const [printState, setPrintState] = useState<{ orderId: string; width: 58 | 80 } | null>(null);
  const [alarmMuted, setAlarmMuted] = useState(false);
  const [msg, setMsg] = useState<{ type: "ok" | "err" | "info"; text: string } | null>(null);
  const [loading, setLoading] = useState(true);

  const loadBoardData = useCallback(async () => {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") {
      setToken("");
      setLoading(false);
      setMsg({
        type: "info",
        text: "Faça login em /login com usuário da loja para acompanhar pedidos, confirmar pagamentos e gerar rotas.",
      });
      return;
    }

    setToken(session.accessToken);
    setLoading(true);
    const [ordersRes, couriersRes, policyRes, brandRes] = await Promise.all([
      apiFetch<FullOrderDto[]>("/api/v1/orders/all", {}, session.accessToken),
      apiFetch<{ couriers: CourierDto[]; recentRuns?: DispatchRunDto[] }>("/api/v1/delivery/couriers", {}, session.accessToken),
      apiFetch<DeliveryPolicyCoords | null>("/api/v1/delivery/policy", {}, session.accessToken),
      apiFetch<StoreMetaDto>("/api/v1/store/branding", {}, session.accessToken),
    ]);

    if (ordersRes.ok && ordersRes.data) {
      setOrders(ordersRes.data);
      setMsg(null);
    } else {
      setMsg({ type: "err", text: ordersRes.errorMessage || "Erro ao carregar pedidos." });
    }

    if (couriersRes.ok && couriersRes.data?.couriers) {
      setCouriers(couriersRes.data.couriers);
      setRecentRuns(couriersRes.data.recentRuns ?? []);
    }
    if (policyRes.ok && policyRes.data) {
      setOriginCoords({
        originLatitude: policyRes.data.originLatitude,
        originLongitude: policyRes.data.originLongitude,
      });
    }
    if (brandRes.ok && brandRes.data) {
      setStoreMeta({
        tradeName: brandRes.data.tradeName || brandRes.data.legalName || "Loja",
        address: brandRes.data.address || "Endereço da Loja",
      });
    }
    setLoading(false);
  }, []);

  const realtimeState = usePitakoRealtime(token || null, {
    onOrderUpdated: () => {
      void loadBoardData();
    }
  });

  useEffect(() => {
    setAlarmMuted(getOrderAlarmMuted());
    return subscribeOrderAlarmMute(setAlarmMuted);
  }, []);

  useEffect(() => {
    void loadBoardData();
    const pollMs = realtimeState === "connected" ? 15000 : 4000;
    const id = setInterval(() => void loadBoardData(), pollMs);
    return () => clearInterval(id);
  }, [loadBoardData, realtimeState]);

  useEffect(() => {
    if (!msg || !token) return;
    const t = setTimeout(() => setMsg(null), 2500);
    return () => clearTimeout(t);
  }, [msg, token]);

  const filteredOrders = useMemo(() => {
    const now = Date.now();
    const todayStr = new Date().toISOString().slice(0, 10);

    return orders.filter((o) => {
      if (statusFilter !== "ALL" && o.statusCode !== Number(statusFilter)) {
        return false;
      }

      if (dateFilter !== "ALL") {
        const placedMs = new Date(o.placedAtUtc).getTime();
        if (dateFilter === "TODAY" && o.placedAtUtc.slice(0, 10) !== todayStr) return false;
        if (dateFilter === "7D" && now - placedMs > 7 * 86400_000) return false;
        if (dateFilter === "30D" && now - placedMs > 30 * 86400_000) return false;
      }

      const pm = (o.paymentMethod || "").toLowerCase();
      if (paymentFilter === "PIX" && !pm.includes("pix")) return false;
      if (paymentFilter === "CASH" && !pm.includes("dinheiro") && !pm.includes("troco") && !pm.includes("cash")) {
        return false;
      }
      if (
        paymentFilter === "CARD" &&
        !pm.includes("cart") &&
        !pm.includes("crédito") &&
        !pm.includes("débito") &&
        !pm.includes("card")
      ) {
        return false;
      }
      if (paymentFilter === "CONFIRMED" && !o.isPaymentConfirmed) return false;
      if (paymentFilter === "COD" && (o.isPaymentConfirmed || pm.includes("online"))) return false;
      if (paymentFilter === "PENDING" && o.isPaymentConfirmed) return false;

      return true;
    });
  }, [orders, statusFilter, dateFilter, paymentFilter]);

  const visibleColumns = useMemo(() => {
    if (viewMode === "LIVE4") {
      return SEVEN_COLUMNS.filter((c) => c.code >= 1 && c.code <= 4);
    }
    return SEVEN_COLUMNS;
  }, [viewMode]);

  async function handleTransitionStatus(orderId: string, nextStatus: number, reason?: string) {
    if (!token) return;
    const res = await apiFetch(
      `/api/v1/orders/${orderId}/status`,
      {
        method: "POST",
        body: JSON.stringify({ nextStatus, reason: reason ?? null }),
      },
      token
    );
    if (res.ok) {
      setMsg({ type: "ok", text: "Status do pedido atualizado no Kanban." });
      await loadBoardData();
    } else {
      setMsg({ type: "err", text: res.errorMessage || "Não foi possível alterar o status." });
    }
  }

  async function handleConfirmPayment(order: FullOrderDto) {
    if (!token) return;
    const res = await apiFetch(
      `/api/v1/orders/${order.id}/confirm-payment`,
      {
        method: "POST",
        body: JSON.stringify({ notes: "Pagamento conferido pelo operador no Kanban" }),
      },
      token
    );
    if (res.ok) {
      setMsg({ type: "ok", text: `Pagamento confirmado para o pedido ${order.orderNumber}.` });
      await loadBoardData();
    } else {
      setMsg({ type: "err", text: res.errorMessage || "Falha ao confirmar pagamento." });
    }
  }

  function nextStepFor(statusCode: number, fulfillmentMode: string): { next: number; label: string } | null {
    switch (statusCode) {
      case 1:
        return { next: 2, label: "Aceitar Pedido" };
      case 2:
        return { next: 3, label: "Enviar p/ Preparo" };
      case 3:
        return { next: 4, label: "Marcar Pronto" };
      case 4:
        return fulfillmentMode === "Delivery"
          ? { next: 5, label: "Despachar Entrega" }
          : { next: 6, label: "Entregar / Concluir" };
      case 5:
        return { next: 6, label: "Concluir" };
      default:
        return null;
    }
  }

  const pendingAcceptanceOrders = useMemo(
    () => orders.filter((o) => o.statusCode === 1),
    [orders]
  );

  async function acceptAllPendingOrders() {
    if (!token || pendingAcceptanceOrders.length === 0) return;
    await Promise.all(
      pendingAcceptanceOrders.map((o) =>
        apiFetch(`/api/v1/orders/${o.id}/status`, { method: "POST", body: JSON.stringify({ nextStatus: 2, reason: "Aceite em lote no Gestor de Pedidos" }) }, token)
      )
    );
    setMsg({ type: "ok", text: `${pendingAcceptanceOrders.length} pedido(s) aceito(s)!` });
    await loadBoardData();
  }

  return (
    <StoreBrandShell activeSection="/pedidos">
      {/* Banner Urgente de Campainha Contínua iFood para Novos Pedidos */}
      {token && pendingAcceptanceOrders.length > 0 && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-2xl border-2 border-amber-400 bg-amber-50 px-4 py-3 text-xs text-amber-950 shadow-md">
          <div className="flex items-center gap-2.5">
            <span className="flex h-8 w-8 items-center justify-center rounded-xl bg-amber-500 text-white">
              <BellRing className="h-4 w-4 animate-bounce" />
            </span>
            <div>
              <p className="text-sm font-extrabold">
                {pendingAcceptanceOrders.length} pedido(s) novo(s) aguardando aceite!
              </p>
              <p className="text-[11px] text-amber-900">
                {alarmMuted
                  ? "Alarme sonoro silenciado manualmente neste terminal."
                  : "Alerta sonoro contínuo ativo até você aceitar ou cancelar os novos pedidos."}
              </p>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => void acceptAllPendingOrders()}
              className="rounded-xl bg-emerald-600 px-3.5 py-2 font-extrabold text-white shadow-sm hover:bg-emerald-700"
            >
              Aceitar Todos Agora ({pendingAcceptanceOrders.length})
            </button>
            <button
              type="button"
              onClick={() => setOrderAlarmMuted(!alarmMuted)}
              className={`inline-flex items-center gap-1 rounded-xl border px-3 py-2 font-bold ${
                alarmMuted ? "border-emerald-300 bg-white text-emerald-800" : "border-rose-300 bg-rose-100 text-rose-900"
              }`}
            >
              {alarmMuted ? <Volume2 className="h-3.5 w-3.5" /> : <VolumeX className="h-3.5 w-3.5" />}
              {alarmMuted ? "Reativar Som" : "Silenciar Alarme"}
            </button>
          </div>
        </div>
      )}

      {/* Header & Top Action Filters */}
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-xl font-bold text-slate-900">Gestor de Pedidos (Tempo Real SignalR & KDS)</h1>
            {token && (
              <span
                className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[10px] font-extrabold ${
                  realtimeState === "connected"
                    ? "bg-emerald-100 text-emerald-800"
                    : "bg-amber-100 text-amber-800"
                }`}
              >
                <Radio className="h-3 w-3" />
                {realtimeState === "connected" ? "SignalR Ao Vivo" : "Sincronizando..."}
              </span>
            )}
          </div>
          <p className="text-xs text-slate-500">
            Recebimento em tempo real, campainha contínua até o aceite, comandas 58mm/80mm e rotas GPS.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() => playContinuousOrderRingPulse()}
            className="inline-flex items-center gap-1 rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-bold text-slate-700 hover:bg-slate-50"
          >
            <Volume2 className="h-3.5 w-3.5" /> Testar Campainha
          </button>
          <select
            aria-label="Status Pedidos"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-semibold text-slate-800"
          >
            <option value="ALL">Status Pedidos (Todos)</option>
            {SEVEN_COLUMNS.map((c) => (
              <option key={c.code} value={String(c.code)}>
                {c.label}
              </option>
            ))}
          </select>

          <select
            aria-label="Período"
            value={dateFilter}
            onChange={(e) => setDateFilter(e.target.value as typeof dateFilter)}
            className="rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-semibold text-slate-800"
          >
            <option value="ALL">Todos os Dias (Geral)</option>
            <option value="TODAY">Hoje</option>
            <option value="7D">Últimos 7 Dias</option>
            <option value="30D">Últimos 30 Dias</option>
          </select>

          <select
            aria-label="Pagamento"
            value={paymentFilter}
            onChange={(e) => setPaymentFilter(e.target.value as typeof paymentFilter)}
            className="rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-semibold text-slate-800"
          >
            <option value="ALL">Pagamento (Todos)</option>
            <option value="PIX">Pix (Online / Entrega)</option>
            <option value="CASH">Dinheiro (com Troco)</option>
            <option value="CARD">Cartão (Máquina / Online)</option>
            <option value="CONFIRMED">Pagamento Confirmado</option>
            <option value="COD">Cobrar na Entrega</option>
            <option value="PENDING">Aguardando Pagamento</option>
          </select>

          <button
            onClick={() => setRoutesOpen(true)}
            className="rounded-xl border border-amber-400 bg-amber-50 px-3.5 py-2 text-xs font-bold text-amber-950 shadow-sm hover:bg-amber-100"
          >
            Ver Rotas & GPS
          </button>

          {token && (
            <button
              onClick={() => void loadBoardData()}
              className="rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50"
            >
              Atualizar
            </button>
          )}
        </div>
      </div>

      {/* Mode Switch + Filter Summary */}
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="inline-flex rounded-xl border border-slate-200 bg-white p-1 shadow-sm">
          <button
            onClick={() => setViewMode("LIVE4")}
            className={`rounded-lg px-3.5 py-1.5 text-xs font-bold transition ${
              viewMode === "LIVE4" ? "bg-violet-600 text-white shadow" : "text-slate-700 hover:bg-slate-100"
            }`}
          >
            Operação Ao Vivo (4 Colunas)
          </button>
          <button
            onClick={() => setViewMode("ALL7")}
            className={`rounded-lg px-3.5 py-1.5 text-xs font-bold transition ${
              viewMode === "ALL7" ? "bg-violet-600 text-white shadow" : "text-slate-700 hover:bg-slate-100"
            }`}
          >
            Todas as Colunas (7 Colunas)
          </button>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white px-3.5 py-1.5 text-xs text-slate-700">
          <strong className="text-slate-900">{filteredOrders.length} pedido(s)</strong> encontrado(s) com os filtros ativos
          {" "}(de {orders.length} no total)
        </div>
      </div>

      {!token && (
        <div className="mb-5 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-300 bg-amber-50 p-4 text-xs text-amber-950">
          <span>{msg?.text}</span>
          <Link href="/login" className="rounded-lg bg-slate-900 px-3.5 py-2 font-bold text-white">
            Entrar em /login
          </Link>
        </div>
      )}

      {token && msg && (
        <div
          className={`mb-4 rounded-xl border p-3 text-xs font-medium ${
            msg.type === "ok"
              ? "border-emerald-200 bg-emerald-50 text-emerald-900"
              : "border-rose-200 bg-rose-50 text-rose-900"
          }`}
        >
          {msg.text}
        </div>
      )}

      {/* Kanban Grid */}
      <div
        className={`grid gap-3 ${
          viewMode === "LIVE4" ? "grid-cols-1 md:grid-cols-2 xl:grid-cols-4" : "grid-cols-1 md:grid-cols-3 xl:grid-cols-7"
        }`}
      >
        {visibleColumns.map((col) => {
          const cards = filteredOrders.filter((o) => o.statusCode === col.code);
          return (
            <section
              key={col.key}
              className={`flex min-h-[260px] flex-col rounded-xl border border-slate-200 border-t-4 ${col.accent} bg-slate-50/70 p-3 shadow-sm`}
            >
              <div className="mb-3 flex items-center justify-between border-b border-slate-200 pb-2">
                <h2 className="text-xs font-bold text-slate-900">{col.label}</h2>
                <span className="rounded-full bg-slate-200 px-2 py-0.5 text-[11px] font-bold text-slate-800">
                  {cards.length}
                </span>
              </div>

              {loading ? (
                <p className="text-[11px] text-slate-400">Carregando...</p>
              ) : cards.length === 0 ? (
                <p className="py-8 text-center text-[11px] text-slate-400">Nenhum pedido nesta coluna.</p>
              ) : (
                <div className="space-y-2.5">
                  {cards.map((o) => {
                    const action = nextStepFor(o.statusCode, o.fulfillmentMode);
                    return (
                      <article
                        key={o.id}
                        className="rounded-xl border border-slate-200 bg-white p-3 text-xs shadow-sm"
                      >
                        <div className="flex items-center justify-between gap-1">
                          <span className="font-mono text-xs font-bold text-slate-900">{o.orderNumber}</span>
                          <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-semibold text-slate-700">
                            {o.fulfillmentMode}
                          </span>
                        </div>

                        <div className="mt-1 flex flex-wrap items-center justify-between gap-1">
                          <span className="font-semibold text-slate-900">{o.customerName}</span>
                          {typeof o.customerOrderCount === "number" && (
                            <span
                              className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${
                                o.customerOrderCount <= 1
                                  ? "bg-indigo-100 text-indigo-800"
                                  : "bg-emerald-100 text-emerald-800"
                              }`}
                            >
                              {o.customerOrderCount <= 1
                                ? "1º Pedido"
                                : `${o.customerOrderCount} pedidos na loja`}
                            </span>
                          )}
                        </div>
                        {o.customerPhone && (
                          <div className="text-[11px] text-slate-500">{o.customerPhone}</div>
                        )}
                        {o.deliveryAddressSummary && (
                          <div className="mt-1 rounded bg-slate-50 p-1.5 text-[11px] text-slate-600">
                            {o.deliveryAddressSummary}
                          </div>
                        )}

                        {o.items?.length > 0 && (
                          <ul className="mt-2 space-y-1 border-t border-slate-100 pt-1.5 text-[11px]">
                            {o.items.map((it) => (
                              <li key={it.id} className="text-slate-700">
                                <span className="font-semibold">
                                  {it.quantity}x {it.itemTitle}
                                </span>
                                {it.customizationSummaryText && (
                                  <div className="text-[10px] text-slate-500">{it.customizationSummaryText}</div>
                                )}
                                {it.customerNotes && (
                                  <div className="text-[10px] italic text-amber-700">Obs: {it.customerNotes}</div>
                                )}
                              </li>
                            ))}
                          </ul>
                        )}

                        <div className="mt-2 flex flex-wrap items-center justify-between gap-1 border-t border-slate-100 pt-2">
                          <span className="text-[11px] font-medium text-slate-600">{o.paymentMethod}</span>
                          <span className="font-mono text-xs font-bold text-slate-900">{brl(o.totalAmount)}</span>
                        </div>

                        <div className="mt-2 flex flex-wrap items-center gap-1.5">
                          {o.isPaymentConfirmed ? (
                            <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-bold text-emerald-800">
                              Pagamento Confirmado
                            </span>
                          ) : (
                            <button
                              onClick={() => void handleConfirmPayment(o)}
                              className="rounded-lg border border-amber-400 bg-amber-50 px-2 py-1 text-[10px] font-bold text-amber-900 hover:bg-amber-100"
                            >
                              Confirmar Pagamento
                            </button>
                          )}
                        </div>

                        <div className="mt-2.5 flex flex-wrap items-center gap-1.5">
                          {action && (
                            <button
                              onClick={() => void handleTransitionStatus(o.id, action.next)}
                              className="flex-1 rounded-lg bg-slate-900 px-2.5 py-1.5 text-[11px] font-bold text-white hover:bg-slate-800"
                            >
                              {action.label}
                            </button>
                          )}
                          <button
                            type="button"
                            title="Imprimir comanda térmica 80mm"
                            onClick={() => setPrintState({ orderId: o.id, width: 80 })}
                            className="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-2 py-1.5 text-[10px] font-bold text-slate-700 hover:bg-slate-100"
                          >
                            <Printer className="h-3 w-3" /> 80mm
                          </button>
                          <button
                            type="button"
                            title="Imprimir comanda térmica 58mm"
                            onClick={() => setPrintState({ orderId: o.id, width: 58 })}
                            className="rounded-lg border border-slate-300 bg-white px-1.5 py-1.5 text-[10px] font-bold text-slate-700 hover:bg-slate-100"
                          >
                            58mm
                          </button>
                          {o.statusCode < 6 && (
                            <button
                              onClick={() =>
                                void handleTransitionStatus(o.id, 7, "Cancelado pelo painel operacional")
                              }
                              className="rounded-lg border border-rose-200 px-2 py-1.5 text-[10px] font-semibold text-rose-700 hover:bg-rose-50"
                            >
                              Cancelar
                            </button>
                          )}
                        </div>
                      </article>
                    );
                  })}
                </div>
              )}
            </section>
          );
        })}
      </div>

      <DeliveryRoutesModal
        open={routesOpen}
        onClose={() => setRoutesOpen(false)}
        orders={orders}
        couriers={couriers}
        recentRuns={recentRuns}
        storeName={storeMeta.tradeName || "Loja"}
        storeAddress={storeMeta.address || "Ponto de Partida"}
        originLat={originCoords.originLatitude}
        originLng={originCoords.originLongitude}
        token={token}
        onRoutesGenerated={loadBoardData}
      />

      <ThermalComandaModal
        open={Boolean(printState)}
        orderId={printState?.orderId ?? null}
        initialWidthMm={printState?.width ?? 80}
        token={token}
        onClose={() => setPrintState(null)}
      />
    </StoreBrandShell>
  );
}
