"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { StoreBrandShell } from "@/components/StoreBrandShell";
import {
  CourierDto,
  DeliveryRoutesModal,
  PedidoOrderItem,
} from "@/components/pedidos/DeliveryRoutesModal";
import { apiFetch, loadSession } from "@/lib/api";

interface FullOrderDto extends PedidoOrderItem {
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
      apiFetch<{ couriers: CourierDto[] }>("/api/v1/delivery/couriers", {}, session.accessToken),
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

  useEffect(() => {
    void loadBoardData();
  }, [loadBoardData]);

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

  function nextStepFor(statusCode: number): { next: number; label: string } | null {
    switch (statusCode) {
      case 1:
        return { next: 2, label: "Aceitar Pedido" };
      case 2:
        return { next: 3, label: "Enviar p/ Preparo" };
      case 3:
        return { next: 4, label: "Marcar Pronto" };
      case 4:
        return { next: 5, label: "Despachar Entrega" };
      case 5:
        return { next: 6, label: "Concluir" };
      default:
        return null;
    }
  }

  return (
    <StoreBrandShell activeSection="/pedidos">
      {/* Header & Top Action Filters */}
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Painel de Pedidos (Kanban KDS & Despacho)</h1>
          <p className="text-xs text-slate-500">
            Operação ao vivo, visão completa em 7 colunas, confirmação de pagamento PIX/Cartão/Dinheiro e otimização de rotas GPS.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
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
                    const action = nextStepFor(o.statusCode);
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

                        <div className="mt-1 font-semibold text-slate-900">{o.customerName}</div>
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

                        <div className="mt-2.5 flex items-center gap-1.5">
                          {action && (
                            <button
                              onClick={() => void handleTransitionStatus(o.id, action.next)}
                              className="flex-1 rounded-lg bg-slate-900 px-2.5 py-1.5 text-[11px] font-bold text-white hover:bg-slate-800"
                            >
                              {action.label}
                            </button>
                          )}
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
        storeName={storeMeta.tradeName || "Loja"}
        storeAddress={storeMeta.address || "Ponto de Partida"}
        originLat={originCoords.originLatitude}
        originLng={originCoords.originLongitude}
        token={token}
        onRoutesGenerated={loadBoardData}
      />
    </StoreBrandShell>
  );
}
