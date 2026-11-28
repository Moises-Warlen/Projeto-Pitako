"use client";

import {
  CheckCircle2,
  Clock,
  Copy,
  ExternalLink,
  Link2,
  MapPin,
  MessageSquare,
  Navigation,
  Printer,
  Send,
  ShieldCheck,
  TrendingUp,
  Truck,
  Users,
  Zap
} from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "@/lib/api";

export interface PedidoOrderItem {
  id: string;
  orderNumber: string;
  channel: string;
  fulfillmentMode: string;
  fulfillmentModeCode: number;
  status: string;
  statusCode: number;
  customerName: string;
  customerPhone: string;
  deliveryAddressSummary: string;
  paymentMethod: string;
  isPaymentConfirmed: boolean;
  netDeliveryFee: number;
  totalAmount: number;
  placedAtUtc: string;
}

export interface CourierDto {
  id: string;
  fullName: string;
  phone: string;
  vehicleType: string;
  status: string;
  licensePlate?: string | null;
}

interface GeneratedRoute {
  courierName: string;
  color: string;
  stops: Array<{
    sequence: number;
    orderNumber: string;
    customerName: string;
    address: string;
    lat: number;
    lng: number;
  }>;
  estimatedKm: number;
}

export interface DispatchRunDto {
  id: string;
  courierId: string;
  runCode: string;
  status?: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  totalEstimatedDistanceKm: number;
  courierCommissionAmount: number;
  assignedAtUtc: string;
  stops: Array<{
    id: string;
    stopSequence: number;
    orderId: string;
    orderNumber: string;
    customerName: string;
    destinationAddress: string;
    destinationLatitude: number;
    destinationLongitude: number;
    distanceFromPreviousKm: number;
    status: string;
  }>;
}

interface HubOpsPayload {
  storeSlug: string;
  storeName: string;
  settings: {
    autoDispatchEnabled: boolean;
    slaTargetMinutes: number;
    externalHubActive: boolean;
    externalHubPartner: string;
    autoSyncMarketplaces: boolean;
  };
  shifts: Array<{
    id: string;
    courierId?: string | null;
    courierName: string;
    shiftWindow: string;
    dailyRate: number;
    daysCsv: string;
    active: boolean;
  }>;
  chatMessages: Array<{
    id: string;
    courierId?: string | null;
    courierName: string;
    senderRole: string;
    orderNumber?: string | null;
    message: string;
    sentAtUtc: string;
  }>;
  externalRequests: Array<{
    protocolCode: string;
    provider: string;
    orderNumber: string;
    estimatedFee: number;
    status: string;
    requestedAtUtc: string;
  }>;
  confirmations: Array<{
    orderId: string;
    orderNumber: string;
    confirmationCode: string;
    notes: string;
    confirmedAtUtc: string;
  }>;
  gpsPositions: Array<{
    courierId: string;
    courierName: string;
    status: string;
    vehicleType: string;
    latitude: number;
    longitude: number;
    batteryPct: number;
    lastPingUtc: string;
  }>;
  policy?: {
    mode: string;
    fixedAmount: number;
    freeOverAmount?: number | null;
    estimatedMinutes: number;
    maxRadiusKm: number;
  } | null;
  integrations: Array<{
    id: string;
    provider: string;
    externalStoreName: string;
    status: string;
    autoAcceptOrders: boolean;
    lastSyncUtc?: string | null;
  }>;
}

interface DeliveryRoutesModalProps {
  open: boolean;
  onClose: () => void;
  orders: PedidoOrderItem[];
  couriers: CourierDto[];
  recentRuns?: DispatchRunDto[];
  storeSlug?: string;
  storeName: string;
  storeAddress: string;
  originLat: number;
  originLng: number;
  token: string;
  onRoutesGenerated: () => Promise<void>;
}

const ROUTE_COLORS = ["#7c3aed", "#059669", "#ea580c", "#0284c7", "#db2777"];

type HubTabKey = "routes" | "orders_sla" | "courier_hub" | "analytics";

function pseudoCoordsFromText(seedText: string, originLat: number, originLng: number, index: number): [number, number] {
  let hash = 0;
  for (let i = 0; i < seedText.length; i++) {
    hash = (hash * 31 + seedText.charCodeAt(i)) | 0;
  }
  const angle = ((Math.abs(hash) % 360) * Math.PI) / 180;
  const distKm = 0.6 + ((Math.abs(hash >> 4) + index * 7) % 35) * 0.12;
  const dLat = (distKm / 111.0) * Math.sin(angle);
  const dLng = (distKm / (111.0 * Math.cos((originLat * Math.PI) / 180))) * Math.cos(angle);
  return [Number((originLat + dLat).toFixed(6)), Number((originLng + dLng).toFixed(6))];
}

function latLngToTile(lat: number, lng: number, zoom: number): { x: number; y: number } {
  const n = Math.pow(2, zoom);
  const x = Math.floor(((lng + 180) / 360) * n);
  const latRad = (lat * Math.PI) / 180;
  const y = Math.floor(((1 - Math.log(Math.tan(latRad) + 1 / Math.cos(latRad)) / Math.PI) / 2) * n);
  return { x, y };
}

function formatElapsedMinutes(isoDate: string): number {
  const diffMs = Math.max(0, Date.now() - new Date(isoDate).getTime());
  return Math.floor(diffMs / 60000);
}

export function DeliveryRoutesModal({
  open,
  onClose,
  orders,
  couriers,
  recentRuns = [],
  storeSlug = "acai-demo",
  storeName,
  storeAddress,
  originLat,
  originLng,
  token,
  onRoutesGenerated
}: DeliveryRoutesModalProps) {
  const [activeHubTab, setActiveHubTab] = useState<HubTabKey>("routes");
  const [filterMode, setFilterMode] = useState<"ACTIVE" | "READY" | "DISPATCHED">("ACTIVE");
  const [motoboyCount, setMotoboyCount] = useState(1);
  const [zoom, setZoom] = useState(14);
  const [excludedOrderIds, setExcludedOrderIds] = useState<string[]>([]);
  const [generating, setGenerating] = useState(false);
  const [settlingId, setSettlingId] = useState<string | null>(null);
  const [statusMsg, setStatusMsg] = useState<string | null>(null);
  const [generatedRoutes, setGeneratedRoutes] = useState<GeneratedRoute[]>([]);
  const [hubOps, setHubOps] = useState<HubOpsPayload | null>(null);

  // Shifts form
  const [shiftCourier, setShiftCourier] = useState("");
  const [shiftWindow, setShiftWindow] = useState("18:00 - 23:30");
  const [shiftDailyRate, setShiftDailyRate] = useState("90.00");
  const [shiftDays, setShiftDays] = useState("Sex, Sáb, Dom");

  // Chat form
  const [chatCourierName, setChatCourierName] = useState("");
  const [chatText, setChatText] = useState("");
  const [chatOrderRef, setChatOrderRef] = useState("");

  // Pin confirmations
  const [confirmCodes, setConfirmCodes] = useState<Record<string, string>>({});

  // External Hub
  const [extProvider, setExtProvider] = useState("iFood Entrega Sob Demanda");
  const [extOrderNumber, setExtOrderNumber] = useState("");

  const loadHubOps = useCallback(async () => {
    if (!token || !open) return;
    const res = await apiFetch<HubOpsPayload>("/api/v1/delivery/hub-ops", {}, token);
    if (res.ok && res.data) {
      setHubOps(res.data);
      if (!shiftCourier && couriers.length > 0) setShiftCourier(couriers[0].fullName);
      if (!chatCourierName && couriers.length > 0) setChatCourierName(couriers[0].fullName);
    }
  }, [token, open, couriers, shiftCourier, chatCourierName]);

  useEffect(() => {
    if (open) void loadHubOps();
  }, [open, loadHubOps]);

  const deliveryOrders = useMemo(() => {
    return orders.filter((o) => {
      const isDelivery = o.fulfillmentModeCode === 1 || o.fulfillmentMode.toLowerCase() === "delivery";
      if (!isDelivery) return false;
      if (filterMode === "READY") return o.statusCode === 4;
      if (filterMode === "DISPATCHED") return o.statusCode === 5;
      return o.statusCode >= 1 && o.statusCode <= 5;
    });
  }, [orders, filterMode]);

  const allDeliveryHistory = useMemo(
    () => orders.filter((o) => o.fulfillmentModeCode === 1 || o.fulfillmentMode.toLowerCase() === "delivery"),
    [orders]
  );

  const selectedOrders = useMemo(
    () => deliveryOrders.filter((o) => !excludedOrderIds.includes(o.id)),
    [deliveryOrders, excludedOrderIds]
  );

  const stopsWithCoords = useMemo(
    () =>
      deliveryOrders.map((o, idx) => {
        const [lat, lng] = pseudoCoordsFromText(o.deliveryAddressSummary || o.orderNumber, originLat, originLng, idx);
        return { order: o, lat, lng };
      }),
    [deliveryOrders, originLat, originLng]
  );

  const slaTarget = hubOps?.settings.slaTargetMinutes ?? 35;
  const allChecked = deliveryOrders.length > 0 && selectedOrders.length === deliveryOrders.length;

  function toggleAll() {
    if (allChecked) setExcludedOrderIds(deliveryOrders.map((o) => o.id));
    else setExcludedOrderIds([]);
  }

  function toggleOne(id: string) {
    setExcludedOrderIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  async function handleStartRun(runId: string) {
    setSettlingId(runId);
    const res = await apiFetch(`/api/v1/delivery/dispatch-runs/${runId}/start`, { method: "POST" }, token);
    setSettlingId(null);
    if (res.ok) {
      setStatusMsg("Saída do motoboy iniciada! Pedidos vinculados atualizados para Em Entrega.");
      await onRoutesGenerated();
    } else {
      setStatusMsg(res.errorMessage || "Não foi possível iniciar a rota.");
    }
  }

  async function handleResolveStop(runId: string, stopId: string) {
    setSettlingId(stopId);
    const res = await apiFetch(
      `/api/v1/delivery/dispatch-runs/${runId}/stops/${stopId}/resolve`,
      { method: "POST", body: JSON.stringify({ delivered: true, notes: "Entrega confirmada" }) },
      token
    );
    setSettlingId(null);
    if (res.ok) {
      setStatusMsg("Parada baixada e pedido concluído com sucesso!");
      await onRoutesGenerated();
      await loadHubOps();
    } else {
      setStatusMsg(res.errorMessage || "Não foi possível baixar a parada.");
    }
  }

  async function handleCompleteAllRun(runId: string) {
    setSettlingId(runId);
    const res = await apiFetch(`/api/v1/delivery/dispatch-runs/${runId}/complete-all`, { method: "POST" }, token);
    setSettlingId(null);
    if (res.ok) {
      setStatusMsg("Baixa completa da rota realizada! Entregas concluídas e comissão liberada.");
      await onRoutesGenerated();
      await loadHubOps();
    } else {
      setStatusMsg(res.errorMessage || "Erro ao finalizar toda a rota.");
    }
  }

  async function handleAutoDispatch() {
    setGenerating(true);
    const res = await apiFetch<{ runCode: string; courierName: string; dispatchedOrders: number; totalEstimatedDistanceKm: number }>(
      "/api/v1/delivery/hub-ops/auto-dispatch",
      { method: "POST" },
      token
    );
    setGenerating(false);
    if (res.ok && res.data) {
      setStatusMsg(
        `Despacho Automático concluído (${res.data.runCode}): ${res.data.dispatchedOrders} pedido(s) atribuídos a ${res.data.courierName} (${res.data.totalEstimatedDistanceKm.toFixed(1)} km).`
      );
      await onRoutesGenerated();
      await loadHubOps();
    } else {
      setStatusMsg(res.errorMessage || "Nenhum pedido elegível para despacho automático.");
    }
  }

  async function handleSaveHubSettings(patch: Partial<HubOpsPayload["settings"]>) {
    if (!hubOps) return;
    const next = { ...hubOps.settings, ...patch };
    const res = await apiFetch<HubOpsPayload["settings"]>(
      "/api/v1/delivery/hub-ops/settings",
      { method: "PUT", body: JSON.stringify(next) },
      token
    );
    if (res.ok && res.data) {
      setHubOps({ ...hubOps, settings: res.data });
      setStatusMsg("Configurações do Hub Logístico salvas.");
    }
  }

  async function handleAddShift(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch<HubOpsPayload["shifts"]>(
      "/api/v1/delivery/hub-ops/shifts",
      {
        method: "POST",
        body: JSON.stringify({
          courierName: shiftCourier || couriers[0]?.fullName || "Motoboy Turno",
          shiftWindow,
          dailyRate: Math.max(0, Number(shiftDailyRate) || 90),
          daysCsv: shiftDays
        })
      },
      token
    );
    if (res.ok && res.data && hubOps) {
      setHubOps({ ...hubOps, shifts: res.data });
      setStatusMsg("Escala & diária registrada!");
    }
  }

  async function handleToggleOrDeleteShift(payload: { toggleId?: string; deleteId?: string }) {
    const res = await apiFetch<HubOpsPayload["shifts"]>(
      "/api/v1/delivery/hub-ops/shifts",
      { method: "POST", body: JSON.stringify(payload) },
      token
    );
    if (res.ok && res.data && hubOps) {
      setHubOps({ ...hubOps, shifts: res.data });
    }
  }

  async function handleSendChat(e: React.FormEvent) {
    e.preventDefault();
    if (!chatText.trim()) return;
    const res = await apiFetch<HubOpsPayload["chatMessages"][number]>(
      "/api/v1/delivery/hub-ops/chat",
      {
        method: "POST",
        body: JSON.stringify({
          courierName: chatCourierName || couriers[0]?.fullName || "Motoboy",
          senderRole: "Loja / Despacho",
          orderNumber: chatOrderRef || null,
          message: chatText.trim()
        })
      },
      token
    );
    if (res.ok && res.data && hubOps) {
      setHubOps({ ...hubOps, chatMessages: [...hubOps.chatMessages, res.data] });
      setChatText("");
    }
  }

  async function handleRequestExternalFleet() {
    const targetOrd = extOrderNumber.trim() || deliveryOrders[0]?.orderNumber || "FILA-GERAL";
    const res = await apiFetch<HubOpsPayload["externalRequests"][number]>(
      "/api/v1/delivery/hub-ops/request-external-fleet",
      {
        method: "POST",
        body: JSON.stringify({
          provider: extProvider,
          orderNumber: targetOrd,
          estimatedFee: 9.9
        })
      },
      token
    );
    if (res.ok && res.data && hubOps) {
      setHubOps({ ...hubOps, externalRequests: [...hubOps.externalRequests, res.data] });
      setStatusMsg(`Hub Logístico acionado (${res.data.protocolCode}): ${res.data.provider} para ${res.data.orderNumber}.`);
    }
  }

  async function handleConfirmOrderDelivery(order: PedidoOrderItem) {
    setSettlingId(order.id);
    const pin = (confirmCodes[order.id] || "").trim();
    const res = await apiFetch(
      "/api/v1/delivery/hub-ops/confirm-delivery",
      {
        method: "POST",
        body: JSON.stringify({
          orderId: order.id,
          confirmationCode: pin || "CONFIRMACAO-HUB",
          notes: pin ? `Entrega confirmada com código ${pin}` : "Entrega confirmada no painel logístico"
        })
      },
      token
    );
    setSettlingId(null);
    if (res.ok) {
      setStatusMsg(`Pedido ${order.orderNumber} confirmado como entregue!`);
      await onRoutesGenerated();
      await loadHubOps();
    } else {
      setStatusMsg(res.errorMessage || "Erro ao confirmar entrega.");
    }
  }

  async function handleGenerateRoutes() {
    if (selectedOrders.length === 0) {
      setStatusMsg("Selecione ao menos 1 pedido de entrega para gerar rotas.");
      return;
    }

    setGenerating(true);
    setStatusMsg(null);

    try {
      const availableCouriers = [...couriers];
      while (availableCouriers.length < motoboyCount) {
        const idx = availableCouriers.length + 1;
        const created = await apiFetch<CourierDto>(
          "/api/v1/delivery/couriers",
          {
            method: "POST",
            body: JSON.stringify({
              fullName: `Motoboy ${String(idx).padStart(2, "0")}`,
              phone: `169999900${String(idx).padStart(2, "0")}`,
              vehicleType: 1,
              commissionMode: 1,
              fixedCommissionAmount: 6.5
            })
          },
          token
        );
        if (created.ok && created.data) availableCouriers.push(created.data);
        else break;
      }

      if (availableCouriers.length === 0) {
        setStatusMsg("Nenhum entregador disponível para vincular o romaneio.");
        setGenerating(false);
        return;
      }

      const buckets: (typeof selectedOrders)[] = Array.from(
        { length: Math.min(motoboyCount, availableCouriers.length) },
        () => []
      );
      selectedOrders.forEach((order, idx) => buckets[idx % buckets.length].push(order));

      const results: GeneratedRoute[] = [];
      for (let b = 0; b < buckets.length; b++) {
        const group = buckets[b];
        if (group.length === 0) continue;
        const courier = availableCouriers[b];
        const stopsPayload = group.map((o, idx) => {
          const [lat, lng] = pseudoCoordsFromText(o.deliveryAddressSummary || o.orderNumber, originLat, originLng, idx + b * 3);
          return {
            orderId: o.id,
            orderNumber: o.orderNumber,
            customerName: o.customerName,
            destinationAddress: o.deliveryAddressSummary || "Endereço informado no pedido",
            destinationLatitude: lat,
            destinationLongitude: lng,
            deliveryFee: o.netDeliveryFee || 6
          };
        });

        const res = await apiFetch<{
          totalPlannedDistanceKm?: number;
          stops?: Array<{
            sequenceNumber: number;
            orderNumber: string;
            customerName: string;
            destinationAddress: string;
            destinationLatitude: number;
            destinationLongitude: number;
          }>;
        }>(
          "/api/v1/delivery/dispatch-runs/optimize",
          {
            method: "POST",
            body: JSON.stringify({
              courierId: courier.id,
              originLatitude: originLat,
              originLongitude: originLng,
              stops: stopsPayload,
              notes: `Rota otimizada para ${courier.fullName}`
            })
          },
          token
        );

        if (res.ok && res.data) {
          const plannedStops = (res.data.stops ?? []).map((s, sIdx) => ({
            sequence: s.sequenceNumber ?? sIdx + 1,
            orderNumber: s.orderNumber,
            customerName: s.customerName,
            address: s.destinationAddress,
            lat: s.destinationLatitude ?? stopsPayload[sIdx]?.destinationLatitude ?? originLat,
            lng: s.destinationLongitude ?? stopsPayload[sIdx]?.destinationLongitude ?? originLng
          }));
          results.push({
            courierName: courier.fullName,
            color: ROUTE_COLORS[b % ROUTE_COLORS.length],
            stops: plannedStops,
            estimatedKm: Number(res.data.totalPlannedDistanceKm ?? plannedStops.length * 2.1)
          });
        }
      }

      setGeneratedRoutes(results);
      setStatusMsg(`Rotas otimizadas geradas para ${results.length} motoboy(s) e despachadas no sistema.`);
      await onRoutesGenerated();
    } catch {
      setStatusMsg("Não foi possível gerar o romaneio otimizado.");
    } finally {
      setGenerating(false);
    }
  }

  function buildClientTrackingUrl(order: PedidoOrderItem): string {
    const origin = typeof window !== "undefined" ? window.location.origin : "http://localhost:3000";
    const last4 = (order.customerPhone || "").replace(/\D/g, "").slice(-4);
    return `${origin}/loja/${hubOps?.storeSlug || storeSlug}?pedido=${encodeURIComponent(order.orderNumber)}${last4 ? `&tel=${last4}` : ""}`;
  }

  async function copyTrackingLink(order: PedidoOrderItem) {
    const url = buildClientTrackingUrl(order);
    try {
      if (typeof navigator !== "undefined" && navigator.clipboard) {
        await navigator.clipboard.writeText(url);
      }
      setStatusMsg(`Link de rastreamento copiado para ${order.orderNumber}! Envie ao cliente pelo WhatsApp.`);
    } catch {
      setStatusMsg(`Link gerado: ${url}`);
    }
  }

  if (!open) return null;

  const centerTile = latLngToTile(originLat, originLng, zoom);
  const spanDeg = 0.08 * Math.pow(2, 14 - zoom);

  function projectToPercent(lat: number, lng: number): { left: number; top: number } {
    const left = Math.min(92, Math.max(8, 50 + ((lng - originLng) / spanDeg) * 42));
    const top = Math.min(90, Math.max(10, 50 - ((lat - originLat) / spanDeg) * 42));
    return { left, top };
  }

  const overdueOrders = deliveryOrders.filter((o) => formatElapsedMinutes(o.placedAtUtc) > slaTarget);
  const totalCommissionDue = recentRuns.reduce((sum, r) => sum + r.courierCommissionAmount, 0);
  const totalShiftRates = (hubOps?.shifts || []).filter((s) => s.active).reduce((sum, s) => sum + s.dailyRate, 0);
  const completedDeliveryCount = allDeliveryHistory.filter((o) => o.statusCode === 6).length;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/80 p-2 backdrop-blur-sm sm:p-4">
      <div className="flex max-h-[94vh] w-full max-w-6xl flex-col overflow-hidden rounded-2xl border border-rose-500/30 bg-[#140e24] text-white shadow-2xl">
        {/* Top bar */}
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-white/10 px-5 py-3.5">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="text-base font-extrabold text-white">Central Logística, Rotas & Delivery</h2>
              <span className="rounded-full bg-rose-600/25 px-2.5 py-0.5 text-[10px] font-extrabold text-rose-300">
                12 Módulos Ativos
              </span>
              {overdueOrders.length > 0 && (
                <span className="inline-flex items-center gap-1 rounded-full bg-amber-500/20 px-2.5 py-0.5 text-[10px] font-extrabold text-amber-300">
                  <Clock className="h-3 w-3" /> {overdueOrders.length} acima do SLA ({slaTarget}m)
                </span>
              )}
            </div>
            <p className="text-xs text-slate-300">
              Roteirizador GPS, Despacho Automático, Hub Logístico, App Motoboy + Chat, Rastreio Cliente, Escalas/Diárias, Taxas e Confirmações.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={generating}
              onClick={() => void handleAutoDispatch()}
              className="inline-flex items-center gap-1.5 rounded-lg bg-rose-600 px-3 py-1.5 text-xs font-extrabold text-white shadow hover:bg-rose-500 disabled:opacity-50"
            >
              <Zap className="h-3.5 w-3.5" /> Despacho Automático
            </button>
            <button
              onClick={onClose}
              className="rounded-lg border border-white/15 bg-white/5 px-3 py-1.5 text-xs font-semibold text-slate-200 hover:bg-white/15"
            >
              × Fechar
            </button>
          </div>
        </div>

        {/* Navigation sub-tabs */}
        <div className="flex flex-wrap items-center gap-1.5 border-b border-white/10 bg-[#1b1330] px-5 py-2.5 text-xs">
          {[
            { id: "routes" as const, label: "Roteirizador & GPS Ao Vivo", icon: Navigation },
            { id: "orders_sla" as const, label: "SLA, Rastreio Cliente & Confirmações", icon: ShieldCheck },
            { id: "courier_hub" as const, label: "App Motoboy + Chat, Escalas & Hub", icon: Truck },
            { id: "analytics" as const, label: "Taxas, Financeiro, Integrações & BI", icon: TrendingUp }
          ].map((t) => {
            const Icon = t.icon;
            return (
              <button
                key={t.id}
                type="button"
                onClick={() => setActiveHubTab(t.id)}
                className={`inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 font-bold transition ${
                  activeHubTab === t.id
                    ? "bg-rose-600 text-white shadow"
                    : "bg-white/5 text-slate-300 hover:bg-white/10"
                }`}
              >
                <Icon className="h-3.5 w-3.5" />
                {t.label}
              </button>
            );
          })}
        </div>

        {statusMsg && (
          <div className="border-b border-emerald-400/20 bg-emerald-950/70 px-5 py-2 text-xs font-semibold text-emerald-200">
            {statusMsg}
          </div>
        )}

        {/* TAB 1: ROTEIRIZADOR & GPS */}
        {activeHubTab === "routes" && (
          <div className="grid flex-1 overflow-y-auto lg:grid-cols-12">
            {/* Map Canvas */}
            <div className="relative min-h-[380px] overflow-hidden bg-slate-200 lg:col-span-7">
              <div className="absolute inset-0 grid grid-cols-3 grid-rows-3 opacity-90">
                {[-1, 0, 1].flatMap((dy) =>
                  [-1, 0, 1].map((dx) => (
                    <img
                      key={`${zoom}-${dx}-${dy}`}
                      src={`https://tile.openstreetmap.org/${zoom}/${centerTile.x + dx}/${centerTile.y + dy}.png`}
                      alt=""
                      className="h-full w-full object-cover"
                      onError={(e) => {
                        (e.currentTarget as HTMLImageElement).style.opacity = "0";
                      }}
                    />
                  ))
                )}
              </div>
              <div
                className="pointer-events-none absolute inset-0"
                style={{
                  backgroundImage:
                    "linear-gradient(to right, rgba(15,23,42,0.08) 1px, transparent 1px), linear-gradient(to bottom, rgba(15,23,42,0.08) 1px, transparent 1px)",
                  backgroundSize: "40px 40px"
                }}
              />

              {/* Zoom + Filter overlay */}
              <div className="absolute left-3 top-3 z-20 flex flex-col gap-2">
                <div className="flex flex-col overflow-hidden rounded-lg border border-slate-300 bg-white text-slate-900 shadow w-fit">
                  <button type="button" onClick={() => setZoom((z) => Math.min(16, z + 1))} className="border-b border-slate-200 px-2.5 py-1 text-sm font-bold hover:bg-slate-100">+</button>
                  <button type="button" onClick={() => setZoom((z) => Math.max(12, z - 1))} className="px-2.5 py-1 text-sm font-bold hover:bg-slate-100">-</button>
                </div>
                <select
                  value={filterMode}
                  onChange={(e) => setFilterMode(e.target.value as typeof filterMode)}
                  className="rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-[11px] font-bold text-slate-800 shadow"
                >
                  <option value="ACTIVE">Todos Ativos ({deliveryOrders.length})</option>
                  <option value="READY">Prontos p/ Despacho</option>
                  <option value="DISPATCHED">Em Entrega</option>
                </select>
              </div>

              <svg viewBox="0 0 100 100" preserveAspectRatio="none" className="pointer-events-none absolute inset-0 z-10 h-full w-full">
                {generatedRoutes.map((route, rIdx) => {
                  const originPos = projectToPercent(originLat, originLng);
                  const pts = [
                    `${originPos.left},${originPos.top}`,
                    ...route.stops.map((s) => {
                      const p = projectToPercent(s.lat, s.lng);
                      return `${p.left},${p.top}`;
                    })
                  ].join(" ");
                  return (
                    <polyline key={rIdx} points={pts} fill="none" stroke={route.color} strokeWidth="0.9" strokeDasharray="2 1" />
                  );
                })}
              </svg>

              {/* Store Origin */}
              {(() => {
                const p = projectToPercent(originLat, originLng);
                return (
                  <div style={{ left: `${p.left}%`, top: `${p.top}%` }} className="absolute z-20 -translate-x-1/2 -translate-y-1/2" title={`${storeName} (Ponto de Partida)`}>
                    <div className="flex h-10 w-10 items-center justify-center rounded-full border-2 border-white bg-violet-700 text-[10px] font-extrabold text-white shadow-lg">LOJA</div>
                  </div>
                );
              })()}

              {/* Live Courier GPS Markers */}
              {(hubOps?.gpsPositions || []).map((g) => {
                const p = projectToPercent(g.latitude, g.longitude);
                return (
                  <div
                    key={g.courierId}
                    style={{ left: `${p.left}%`, top: `${p.top}%` }}
                    className="absolute z-20 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-white bg-emerald-600 px-2 py-0.5 text-[10px] font-extrabold text-white shadow-md"
                    title={`${g.courierName} · GPS ativo (${g.batteryPct}% bateria)`}
                  >
                    🏍 {g.courierName.split(" ")[0]}
                  </div>
                );
              })}

              {/* Order Stop Pins */}
              {stopsWithCoords.map(({ order, lat, lng }, idx) => {
                const p = projectToPercent(lat, lng);
                const isSelected = !excludedOrderIds.includes(order.id);
                return (
                  <button
                    key={order.id}
                    type="button"
                    onClick={() => toggleOne(order.id)}
                    style={{ left: `${p.left}%`, top: `${p.top}%` }}
                    className={`absolute z-20 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-white px-2 py-1 text-[10px] font-bold shadow-md transition ${
                      isSelected ? "bg-amber-500 text-slate-950" : "bg-slate-600 text-white opacity-60"
                    }`}
                    title={`${order.orderNumber} · ${order.customerName}`}
                  >
                    #{idx + 1} · {order.orderNumber.slice(-4)}
                  </button>
                );
              })}

              <div className="absolute bottom-2 right-2 z-20 rounded bg-white/90 px-2 py-0.5 text-[10px] font-bold text-slate-800 shadow">
                Rastreamento GPS Ao Vivo · {couriers.length} entregador(es) · Zoom {zoom}x
              </div>
            </div>

            {/* Right Routing Sidebar */}
            <div className="flex flex-col gap-3 border-l border-white/10 p-5 lg:col-span-5">
              <div className="flex items-center justify-between">
                <h3 className="text-sm font-bold text-amber-300">Roteirizador Inteligente</h3>
                <span className="rounded-full bg-white/10 px-2.5 py-0.5 text-[11px] text-slate-200">
                  {selectedOrders.length} de {deliveryOrders.length} selecionadas
                </span>
              </div>

              <div className="rounded-xl border border-violet-400/25 bg-violet-950/50 p-3">
                <div className="text-[10px] font-bold tracking-wider text-violet-300">PONTO DE PARTIDA (LOJA)</div>
                <div className="mt-0.5 text-sm font-bold text-white">{storeName}</div>
                <div className="text-xs text-slate-300">{storeAddress}</div>
              </div>

              <div className="rounded-xl border border-white/10 bg-white/5 p-3">
                <div className="flex items-center justify-between">
                  <div>
                    <div className="text-xs font-bold text-white">Frota na Rota</div>
                    <div className="text-[11px] text-slate-300">Dividir paradas entre quantos motoboys?</div>
                  </div>
                  <label className="flex items-center gap-1.5 text-[11px] font-semibold text-emerald-300">
                    <input
                      type="checkbox"
                      checked={Boolean(hubOps?.settings.autoDispatchEnabled)}
                      onChange={(e) => void handleSaveHubSettings({ autoDispatchEnabled: e.target.checked })}
                    />
                    Auto-despacho
                  </label>
                </div>
                <div className="mt-2.5 flex flex-wrap items-center gap-2">
                  <div className="flex items-center overflow-hidden rounded-lg border border-white/15 bg-[#1b1330]">
                    <button type="button" onClick={() => setMotoboyCount((c) => Math.max(1, c - 1))} className="px-3 py-1.5 text-sm font-bold hover:bg-white/10">-</button>
                    <span className="w-8 text-center font-mono text-sm font-bold">{motoboyCount}</span>
                    <button type="button" onClick={() => setMotoboyCount((c) => Math.min(8, c + 1))} className="px-3 py-1.5 text-sm font-bold hover:bg-white/10">+</button>
                  </div>
                  <button
                    type="button"
                    disabled={generating}
                    onClick={() => void handleGenerateRoutes()}
                    className="flex-1 rounded-lg bg-violet-600 px-4 py-2 text-xs font-bold text-white hover:bg-violet-500 disabled:opacity-50"
                  >
                    {generating ? "Otimizando..." : "Gerar Rotas"}
                  </button>
                </div>
              </div>

              <div className="flex items-center justify-between pt-1 text-xs">
                <span className="font-semibold text-slate-200">Selecione as paradas:</span>
                <label className="flex cursor-pointer items-center gap-1.5 text-slate-200">
                  <input type="checkbox" checked={allChecked} onChange={toggleAll} className="h-3.5 w-3.5 rounded accent-violet-500" />
                  Todos
                </label>
              </div>

              {deliveryOrders.length === 0 ? (
                <div className="rounded-xl border border-dashed border-white/15 p-4 text-center text-xs text-slate-400">
                  Nenhum pedido de delivery ativo para o filtro selecionado.
                </div>
              ) : (
                <div className="max-h-40 space-y-1.5 overflow-y-auto pr-1">
                  {deliveryOrders.map((order) => {
                    const checked = !excludedOrderIds.includes(order.id);
                    const elapsed = formatElapsedMinutes(order.placedAtUtc);
                    const late = elapsed > slaTarget;
                    return (
                      <label key={order.id} className="flex cursor-pointer items-start justify-between gap-2 rounded-lg border border-white/10 bg-white/5 px-3 py-2 text-xs hover:bg-white/10">
                        <div className="flex items-start gap-2">
                          <input type="checkbox" checked={checked} onChange={() => toggleOne(order.id)} className="mt-0.5 h-3.5 w-3.5 rounded accent-violet-500" />
                          <div>
                            <div className="flex items-center gap-1.5 font-bold text-white">
                              <span>{order.orderNumber} · {order.customerName}</span>
                              <span className={`rounded px-1.5 py-0.5 text-[9px] font-extrabold ${late ? "bg-rose-600 text-white" : "bg-emerald-500/20 text-emerald-300"}`}>
                                {elapsed}m
                              </span>
                            </div>
                            <div className="text-[11px] text-slate-300">{order.deliveryAddressSummary || "Endereço de entrega"}</div>
                          </div>
                        </div>
                        <span className="shrink-0 font-mono font-semibold text-emerald-300">R$ {order.totalAmount.toFixed(2)}</span>
                      </label>
                    );
                  })}
                </div>
              )}

              {generatedRoutes.length > 0 && (
                <div className="space-y-2 border-t border-white/10 pt-3">
                  <div className="text-xs font-bold text-white">Romaneios Gerados</div>
                  {generatedRoutes.map((route, idx) => {
                    const waypoints = route.stops.map((s) => encodeURIComponent(s.address)).join("/");
                    const mapsUrl = `https://www.google.com/maps/dir/${encodeURIComponent(storeAddress)}/${waypoints}`;
                    return (
                      <div key={idx} className="rounded-xl border border-white/15 bg-[#1d1533] p-3 text-xs">
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-bold" style={{ color: route.color }}>
                            {route.courierName} · {route.stops.length} parada(s) · ~{route.estimatedKm.toFixed(1)} km
                          </span>
                          <a href={mapsUrl} target="_blank" rel="noreferrer" className="rounded bg-white/10 px-2 py-0.5 text-[10px] font-semibold text-white hover:bg-white/20">
                            Abrir GPS Maps
                          </a>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}

              {recentRuns.length > 0 && (
                <div className="space-y-2 border-t border-white/10 pt-3">
                  <div className="text-xs font-bold text-white">Romaneios & Baixa dos Entregadores ({recentRuns.length})</div>
                  <div className="max-h-48 space-y-2 overflow-y-auto pr-1">
                    {recentRuns.map((run) => {
                      const courierName = couriers.find((c) => c.id === run.courierId)?.fullName || "Motoboy";
                      const isDone = run.status === "Completed" || run.stops.every((s) => s.status !== "Pending");
                      return (
                        <div key={run.id} className="rounded-xl border border-white/15 bg-[#1b1330] p-2.5 text-xs">
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <div>
                              <span className="font-extrabold text-amber-300">{run.runCode}</span>
                              <span className="ml-2 font-bold text-white">{courierName}</span>
                              <span className="ml-2 text-[11px] text-slate-300">· {run.totalEstimatedDistanceKm.toFixed(1)} km · R$ {run.courierCommissionAmount.toFixed(2)}</span>
                            </div>
                            <div className="flex flex-wrap items-center gap-1.5">
                              {!isDone && run.status !== "InTransit" && (
                                <button type="button" disabled={settlingId === run.id} onClick={() => void handleStartRun(run.id)} className="rounded-lg bg-violet-600 px-2.5 py-1 text-[10px] font-bold text-white hover:bg-violet-500">Iniciar Saída</button>
                              )}
                              {!isDone && (
                                <button type="button" disabled={settlingId === run.id} onClick={() => void handleCompleteAllRun(run.id)} className="rounded-lg bg-emerald-600 px-2.5 py-1 text-[10px] font-extrabold text-white hover:bg-emerald-500">Baixa Total ✓</button>
                              )}
                              {isDone && <span className="rounded-full bg-emerald-500/20 px-2 py-0.5 text-[10px] font-bold text-emerald-300">Concluída ✓</span>}
                            </div>
                          </div>
                          <ul className="mt-2 space-y-1">
                            {run.stops.map((st) => (
                              <li key={st.id} className="flex items-center justify-between gap-2 rounded bg-white/5 px-2 py-1 text-[11px]">
                                <span className={st.status === "Delivered" ? "text-emerald-300 line-through" : "text-slate-200"}>
                                  {st.stopSequence}. {st.orderNumber} · {st.customerName}
                                </span>
                                {st.status === "Delivered" ? (
                                  <span className="font-bold text-emerald-400">Entregue ✓</span>
                                ) : (
                                  <button type="button" disabled={settlingId === st.id} onClick={() => void handleResolveStop(run.id, st.id)} className="rounded bg-emerald-600 px-2 py-0.5 text-[10px] font-bold text-white hover:bg-emerald-500">Baixar</button>
                                )}
                              </li>
                            ))}
                          </ul>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </div>
          </div>
        )}

        {/* TAB 2: SLA, RASTREAMENTO PARA CLIENTE & CONFIRMAÇÕES */}
        {activeHubTab === "orders_sla" && (
          <div className="flex-1 space-y-5 overflow-y-auto p-5 text-xs">
            <div className="grid gap-3 sm:grid-cols-4">
              <div className="rounded-xl border border-white/10 bg-white/5 p-3.5">
                <p className="text-[10px] font-bold uppercase text-slate-400">Meta Cronômetro SLA</p>
                <div className="mt-1 flex items-center gap-2">
                  <input
                    type="number"
                    min="10"
                    max="180"
                    value={slaTarget}
                    onChange={(e) => void handleSaveHubSettings({ slaTargetMinutes: Math.max(10, Number(e.target.value) || 35) })}
                    className="w-16 rounded-lg border border-white/20 bg-[#1b1330] px-2 py-1 font-mono font-bold text-white"
                  />
                  <span className="text-slate-300">minutos</span>
                </div>
              </div>
              <div className="rounded-xl border border-white/10 bg-white/5 p-3.5">
                <p className="text-[10px] font-bold uppercase text-slate-400">Pedidos em Atraso SLA</p>
                <p className="mt-1 text-xl font-extrabold text-rose-400">{overdueOrders.length}</p>
              </div>
              <div className="rounded-xl border border-white/10 bg-white/5 p-3.5">
                <p className="text-[10px] font-bold uppercase text-slate-400">Entregas Confirmadas</p>
                <p className="mt-1 text-xl font-extrabold text-emerald-400">{hubOps?.confirmations.length ?? completedDeliveryCount}</p>
              </div>
              <div className="rounded-xl border border-white/10 bg-white/5 p-3.5">
                <p className="text-[10px] font-bold uppercase text-slate-400">Links Rastreio Cliente</p>
                <p className="mt-1 text-[11px] font-semibold text-violet-300">Link direto `/loja/{hubOps?.storeSlug || storeSlug}?pedido=...` ativo</p>
              </div>
            </div>

            <div className="space-y-2.5">
              <h3 className="text-sm font-extrabold text-white">Cronômetro de Prazo, Link do Cliente & Confirmação de Entrega</h3>
              {deliveryOrders.length === 0 ? (
                <div className="rounded-xl border border-dashed border-white/15 p-6 text-center text-slate-400">
                  Nenhum pedido de delivery ativo no momento. Crie um pedido pelo cardápio `/loja/{hubOps?.storeSlug || storeSlug}` ou simule em `/integracoes`.
                </div>
              ) : (
                <div className="grid gap-3 md:grid-cols-2">
                  {deliveryOrders.map((ord) => {
                    const elapsed = formatElapsedMinutes(ord.placedAtUtc);
                    const isLate = elapsed > slaTarget;
                    const trackUrl = buildClientTrackingUrl(ord);
                    return (
                      <div key={ord.id} className={`rounded-xl border p-4 ${isLate ? "border-rose-500/60 bg-rose-950/30" : "border-white/15 bg-white/5"}`}>
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <div>
                            <div className="flex items-center gap-2">
                              <span className="font-extrabold text-white">{ord.orderNumber}</span>
                              <span className="rounded-full bg-white/10 px-2 py-0.5 text-[10px] font-bold text-slate-200">{ord.channel}</span>
                              <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-extrabold ${isLate ? "bg-rose-600 text-white" : "bg-emerald-500/20 text-emerald-300"}`}>
                                <Clock className="h-3 w-3" /> {elapsed} min {isLate ? "· ATRASADO" : "· NO PRAZO"}
                              </span>
                            </div>
                            <p className="mt-1 font-semibold text-slate-200">{ord.customerName} · {ord.customerPhone}</p>
                            <p className="text-[11px] text-slate-400">{ord.deliveryAddressSummary}</p>
                          </div>
                          <span className="font-mono text-sm font-extrabold text-emerald-300">R$ {ord.totalAmount.toFixed(2)}</span>
                        </div>

                        <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-white/10 pt-3">
                          <button
                            type="button"
                            onClick={() => void copyTrackingLink(ord)}
                            className="inline-flex items-center gap-1 rounded-lg bg-violet-600 px-2.5 py-1.5 text-[11px] font-bold text-white hover:bg-violet-500"
                          >
                            <Copy className="h-3.5 w-3.5" /> Copiar Link Cliente
                          </button>
                          <a
                            href={trackUrl}
                            target="_blank"
                            rel="noreferrer"
                            className="inline-flex items-center gap-1 rounded-lg border border-white/20 bg-white/5 px-2.5 py-1.5 text-[11px] font-bold text-slate-200 hover:bg-white/10"
                          >
                            <ExternalLink className="h-3.5 w-3.5" /> Abrir Rastreio
                          </a>
                          <input
                            value={confirmCodes[ord.id] || ""}
                            onChange={(e) => setConfirmCodes({ ...confirmCodes, [ord.id]: e.target.value })}
                            placeholder="PIN / Cód. Confirmação (opc.)"
                            className="w-36 rounded-lg border border-white/20 bg-[#1b1330] px-2 py-1 text-[11px] text-white"
                          />
                          <button
                            type="button"
                            disabled={settlingId === ord.id}
                            onClick={() => void handleConfirmOrderDelivery(ord)}
                            className="inline-flex items-center gap-1 rounded-lg bg-emerald-600 px-2.5 py-1.5 text-[11px] font-extrabold text-white hover:bg-emerald-500"
                          >
                            <CheckCircle2 className="h-3.5 w-3.5" /> Confirmar Entrega
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          </div>
        )}

        {/* TAB 3: APP MOTOBOY + CHAT, ESCALAS/DIÁRIAS & HUB LOGÍSTICO EXTERNO */}
        {activeHubTab === "courier_hub" && (
          <div className="grid flex-1 gap-5 overflow-y-auto p-5 text-xs lg:grid-cols-12">
            {/* App do Motoboy + Chat */}
            <div className="space-y-4 rounded-xl border border-white/10 bg-white/5 p-4 lg:col-span-5">
              <div className="flex items-center justify-between">
                <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-white">
                  <MessageSquare className="h-4 w-4 text-rose-400" /> App do Motoboy + Chat Direto
                </h3>
                <span className="rounded-full bg-emerald-500/20 px-2 py-0.5 text-[10px] font-bold text-emerald-300">
                  {(hubOps?.gpsPositions || []).length} online
                </span>
              </div>

              <div className="space-y-1.5">
                {(hubOps?.gpsPositions || []).map((g) => (
                  <div key={g.courierId} className="flex items-center justify-between rounded-lg border border-white/10 bg-[#1b1330] px-3 py-2">
                    <div>
                      <p className="font-bold text-white">{g.courierName}</p>
                      <p className="text-[10px] text-slate-300">
                        GPS {g.latitude}, {g.longitude} · Bateria {g.batteryPct}%
                      </p>
                    </div>
                    <span className="rounded-full bg-emerald-500/20 px-2 py-0.5 text-[10px] font-bold text-emerald-300">{g.status}</span>
                  </div>
                ))}
              </div>

              <div className="max-h-48 space-y-2 overflow-y-auto rounded-xl border border-white/10 bg-[#120b21] p-3">
                {(hubOps?.chatMessages || []).length === 0 ? (
                  <p className="text-center text-slate-400">Nenhuma mensagem enviada ao motoboy ainda.</p>
                ) : (
                  (hubOps?.chatMessages || []).map((m) => (
                    <div key={m.id} className="rounded-lg bg-white/10 p-2">
                      <div className="flex items-center justify-between text-[10px] text-slate-300">
                        <span className="font-bold text-rose-300">{m.senderRole} → {m.courierName}</span>
                        {m.orderNumber && <span className="rounded bg-white/10 px-1.5 py-0.5">{m.orderNumber}</span>}
                      </div>
                      <p className="mt-1 text-white">{m.message}</p>
                    </div>
                  ))
                )}
              </div>

              <form onSubmit={handleSendChat} className="space-y-2">
                <div className="grid grid-cols-2 gap-2">
                  <select
                    value={chatCourierName}
                    onChange={(e) => setChatCourierName(e.target.value)}
                    className="rounded-lg border border-white/20 bg-[#1b1330] px-2.5 py-1.5 text-white"
                  >
                    {couriers.map((c) => <option key={c.id} value={c.fullName}>{c.fullName}</option>)}
                    {couriers.length === 0 && <option value="Motoboy 01">Motoboy 01</option>}
                  </select>
                  <input
                    value={chatOrderRef}
                    onChange={(e) => setChatOrderRef(e.target.value)}
                    placeholder="Pedido ref. (opc.)"
                    className="rounded-lg border border-white/20 bg-[#1b1330] px-2.5 py-1.5 text-white"
                  />
                </div>
                <div className="flex gap-2">
                  <input
                    value={chatText}
                    onChange={(e) => setChatText(e.target.value)}
                    placeholder="Ex: Cliente aguardando na portaria com troco"
                    className="flex-1 rounded-lg border border-white/20 bg-[#1b1330] px-3 py-1.5 text-white"
                  />
                  <button type="submit" className="inline-flex items-center gap-1 rounded-lg bg-rose-600 px-3 py-1.5 font-bold text-white hover:bg-rose-500">
                    <Send className="h-3.5 w-3.5" /> Enviar
                  </button>
                </div>
              </form>
            </div>

            {/* Escalas e Diárias + Hub Logístico Sob Demanda */}
            <div className="space-y-5 lg:col-span-7">
              <section className="rounded-xl border border-white/10 bg-white/5 p-4 space-y-3">
                <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-white">
                  <Users className="h-4 w-4 text-amber-400" /> Escalas e Diárias da Equipe
                </h3>
                <form onSubmit={handleAddShift} className="grid gap-2 sm:grid-cols-4">
                  <input value={shiftCourier} onChange={(e) => setShiftCourier(e.target.value)} placeholder="Motoboy / Turno" className="rounded-lg border border-white/20 bg-[#1b1330] px-2.5 py-1.5 text-white" />
                  <input value={shiftWindow} onChange={(e) => setShiftWindow(e.target.value)} placeholder="18:00 - 23:30" className="rounded-lg border border-white/20 bg-[#1b1330] px-2.5 py-1.5 text-white" />
                  <input type="number" step="0.01" value={shiftDailyRate} onChange={(e) => setShiftDailyRate(e.target.value)} placeholder="Diária R$" className="rounded-lg border border-white/20 bg-[#1b1330] px-2.5 py-1.5 text-white" />
                  <div className="flex gap-1.5">
                    <input value={shiftDays} onChange={(e) => setShiftDays(e.target.value)} placeholder="Dias" className="w-full rounded-lg border border-white/20 bg-[#1b1330] px-2 py-1.5 text-white" />
                    <button type="submit" className="shrink-0 rounded-lg bg-emerald-600 px-3 py-1.5 font-bold text-white hover:bg-emerald-500">Salvar</button>
                  </div>
                </form>

                <div className="space-y-1.5">
                  {(hubOps?.shifts || []).map((sh) => (
                    <div key={sh.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-white/10 bg-[#1b1330] px-3 py-2">
                      <div>
                        <span className="font-bold text-white">{sh.courierName}</span>
                        <span className="ml-2 text-slate-300">{sh.shiftWindow} · {sh.daysCsv}</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="font-mono font-bold text-emerald-300">R$ {sh.dailyRate.toFixed(2)}/dia</span>
                        <button type="button" onClick={() => void handleToggleOrDeleteShift({ toggleId: sh.id })} className={`rounded px-2 py-0.5 text-[10px] font-bold ${sh.active ? "bg-emerald-500/20 text-emerald-300" : "bg-slate-600 text-slate-200"}`}>
                          {sh.active ? "Ativa" : "Pausada"}
                        </button>
                        <button type="button" onClick={() => void handleToggleOrDeleteShift({ deleteId: sh.id })} className="rounded bg-rose-700 px-2 py-0.5 text-[10px] font-bold text-white">Excluir</button>
                      </div>
                    </div>
                  ))}
                </div>
              </section>

              <section className="rounded-xl border border-white/10 bg-white/5 p-4 space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-white">
                      <Truck className="h-4 w-4 text-rose-400" /> Hub Logístico (Frota Externa Sob Demanda)
                    </h3>
                    <p className="text-[11px] text-slate-300">Acione entregadores parceiros (iFood Entrega Sob Demanda, 99Food, Frota Parceira) quando precisar de reforço.</p>
                  </div>
                  <label className="flex items-center gap-1.5 text-xs font-bold text-emerald-300">
                    <input
                      type="checkbox"
                      checked={Boolean(hubOps?.settings.externalHubActive)}
                      onChange={(e) => void handleSaveHubSettings({ externalHubActive: e.target.checked })}
                    />
                    Hub Ativo
                  </label>
                </div>

                <div className="flex flex-wrap gap-2">
                  <select value={extProvider} onChange={(e) => setExtProvider(e.target.value)} className="rounded-lg border border-white/20 bg-[#1b1330] px-3 py-1.5 text-white">
                    <option value="iFood Entrega Sob Demanda">iFood Entrega Sob Demanda</option>
                    <option value="99Entrega Parceiro">99Entrega Parceiro</option>
                    <option value="Lalamove / Frota Local">Lalamove / Frota Local</option>
                  </select>
                  <input value={extOrderNumber} onChange={(e) => setExtOrderNumber(e.target.value)} placeholder="Nº Pedido (ou automático)" className="flex-1 rounded-lg border border-white/20 bg-[#1b1330] px-3 py-1.5 text-white" />
                  <button type="button" onClick={() => void handleRequestExternalFleet()} className="rounded-lg bg-rose-600 px-4 py-1.5 font-extrabold text-white hover:bg-rose-500">
                    Chamar Entregador Parceiro
                  </button>
                </div>

                {(hubOps?.externalRequests || []).length > 0 && (
                  <div className="space-y-1">
                    {(hubOps?.externalRequests || []).slice(-5).map((req) => (
                      <div key={req.protocolCode} className="flex items-center justify-between rounded bg-[#1b1330] px-3 py-1.5 text-[11px]">
                        <span><strong className="text-amber-300">{req.protocolCode}</strong> · {req.provider} · {req.orderNumber}</span>
                        <span className="font-bold text-emerald-300">{req.status}</span>
                      </div>
                    ))}
                  </div>
                )}
              </section>
            </div>
          </div>
        )}

        {/* TAB 4: TAXAS, FINANCEIRO, INTEGRAÇÕES & DASHBOARDS */}
        {activeHubTab === "analytics" && (
          <div className="flex-1 space-y-5 overflow-y-auto p-5 text-xs">
            <div className="grid gap-4 md:grid-cols-2">
              {/* Controle de Taxas */}
              <div className="rounded-xl border border-white/10 bg-white/5 p-4 space-y-2">
                <div className="flex items-center justify-between">
                  <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-white">
                    <MapPin className="h-4 w-4 text-rose-400" /> Controle de Taxas por Distância
                  </h3>
                  <Link href="/admin?tab=settings" className="rounded-lg bg-white/10 px-2.5 py-1 text-[11px] font-bold text-white hover:bg-white/20">
                    Configurar Frete
                  </Link>
                </div>
                {hubOps?.policy ? (
                  <div className="grid grid-cols-2 gap-2 text-[11px] text-slate-200">
                    <div className="rounded-lg bg-[#1b1330] p-2.5">Modo: <strong>{hubOps.policy.mode}</strong></div>
                    <div className="rounded-lg bg-[#1b1330] p-2.5">Valor Base: <strong>R$ {hubOps.policy.fixedAmount.toFixed(2)}</strong></div>
                    <div className="rounded-lg bg-[#1b1330] p-2.5">Raio Máximo: <strong>{hubOps.policy.maxRadiusKm} km</strong></div>
                    <div className="rounded-lg bg-[#1b1330] p-2.5">Frete Grátis Acima: <strong>{hubOps.policy.freeOverAmount ? `R$ ${hubOps.policy.freeOverAmount.toFixed(2)}` : "Desativado"}</strong></div>
                  </div>
                ) : (
                  <p className="text-slate-300">Política padrão ativa.</p>
                )}
              </div>

              {/* Integrações */}
              <div className="rounded-xl border border-white/10 bg-white/5 p-4 space-y-2">
                <div className="flex items-center justify-between">
                  <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-white">
                    <Link2 className="h-4 w-4 text-emerald-400" /> Integrações PDV & Marketplaces
                  </h3>
                  <Link href="/integracoes" className="rounded-lg bg-rose-600 px-2.5 py-1 text-[11px] font-bold text-white hover:bg-rose-500">
                    Abrir Central Marketplaces
                  </Link>
                </div>
                <div className="space-y-1.5">
                  {(hubOps?.integrations || []).map((intg) => (
                    <div key={intg.id} className="flex items-center justify-between rounded-lg bg-[#1b1330] px-3 py-2">
                      <div>
                        <span className="font-bold text-white">{intg.provider}</span>
                        <span className="ml-2 text-slate-300">{intg.externalStoreName}</span>
                      </div>
                      <span className="rounded-full bg-emerald-500/20 px-2 py-0.5 text-[10px] font-bold text-emerald-300">
                        {intg.status} {intg.autoAcceptOrders ? "· Auto-Aceite" : ""}
                      </span>
                    </div>
                  ))}
                  {(!hubOps?.integrations || hubOps.integrations.length === 0) && (
                    <p className="text-slate-300">Vincule iFood, 99Food e PDVs em /integracoes.</p>
                  )}
                </div>
              </div>
            </div>

            {/* Controle Financeiro + Dashboards e Relatórios */}
            <div className="grid gap-4 md:grid-cols-2">
              <div className="rounded-xl border border-white/10 bg-white/5 p-4 space-y-2">
                <div className="flex items-center justify-between">
                  <h3 className="text-sm font-extrabold text-white">Controle Financeiro dos Entregadores</h3>
                  <Link href="/admin?tab=reports" className="inline-flex items-center gap-1 rounded-lg bg-white/10 px-2.5 py-1 text-[11px] font-bold text-white hover:bg-white/20">
                    <Printer className="h-3.5 w-3.5" /> PDF / Relatório Completo
                  </Link>
                </div>
                <div className="grid grid-cols-3 gap-2 text-center">
                  <div className="rounded-lg bg-[#1b1330] p-2.5">
                    <p className="text-[10px] text-slate-400">Rotas / Corridas</p>
                    <p className="text-lg font-extrabold text-white">{recentRuns.length}</p>
                  </div>
                  <div className="rounded-lg bg-[#1b1330] p-2.5">
                    <p className="text-[10px] text-slate-400">Comissões</p>
                    <p className="text-lg font-extrabold text-emerald-300">R$ {totalCommissionDue.toFixed(2)}</p>
                  </div>
                  <div className="rounded-lg bg-[#1b1330] p-2.5">
                    <p className="text-[10px] text-slate-400">Diárias Escala</p>
                    <p className="text-lg font-extrabold text-amber-300">R$ {totalShiftRates.toFixed(2)}</p>
                  </div>
                </div>
              </div>

              <div className="rounded-xl border border-white/10 bg-white/5 p-4 space-y-2">
                <h3 className="text-sm font-extrabold text-white">Dashboards & Desempenho da Frota</h3>
                <div className="grid grid-cols-3 gap-2 text-center">
                  <div className="rounded-lg bg-[#1b1330] p-2.5">
                    <p className="text-[10px] text-slate-400">Total Entregas</p>
                    <p className="text-lg font-extrabold text-white">{allDeliveryHistory.length}</p>
                  </div>
                  <div className="rounded-lg bg-[#1b1330] p-2.5">
                    <p className="text-[10px] text-slate-400">Concluídas</p>
                    <p className="text-lg font-extrabold text-emerald-300">{completedDeliveryCount}</p>
                  </div>
                  <div className="rounded-lg bg-[#1b1330] p-2.5">
                    <p className="text-[10px] text-slate-400">Dentro do SLA</p>
                    <p className="text-lg font-extrabold text-violet-300">
                      {deliveryOrders.length > 0
                        ? `${Math.round(((deliveryOrders.length - overdueOrders.length) / deliveryOrders.length) * 100)}%`
                        : "100%"}
                    </p>
                  </div>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
