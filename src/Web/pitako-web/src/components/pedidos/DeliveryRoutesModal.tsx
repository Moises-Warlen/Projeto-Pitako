"use client";

import { useMemo, useState } from "react";
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

interface DeliveryRoutesModalProps {
  open: boolean;
  onClose: () => void;
  orders: PedidoOrderItem[];
  couriers: CourierDto[];
  recentRuns?: DispatchRunDto[];
  storeName: string;
  storeAddress: string;
  originLat: number;
  originLng: number;
  token: string;
  onRoutesGenerated: () => Promise<void>;
}

const ROUTE_COLORS = ["#7c3aed", "#059669", "#ea580c", "#0284c7", "#db2777"];

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

export function DeliveryRoutesModal({
  open,
  onClose,
  orders,
  couriers,
  recentRuns = [],
  storeName,
  storeAddress,
  originLat,
  originLng,
  token,
  onRoutesGenerated,
}: DeliveryRoutesModalProps) {
  const [filterMode, setFilterMode] = useState<"ACTIVE" | "READY" | "DISPATCHED">("ACTIVE");
  const [motoboyCount, setMotoboyCount] = useState(1);
  const [zoom, setZoom] = useState(14);
  const [excludedOrderIds, setExcludedOrderIds] = useState<string[]>([]);
  const [generating, setGenerating] = useState(false);
  const [settlingId, setSettlingId] = useState<string | null>(null);
  const [statusMsg, setStatusMsg] = useState<string | null>(null);
  const [generatedRoutes, setGeneratedRoutes] = useState<GeneratedRoute[]>([]);

  const deliveryOrders = useMemo(() => {
    return orders.filter((o) => {
      const isDelivery =
        o.fulfillmentModeCode === 1 ||
        o.fulfillmentMode.toLowerCase() === "delivery";
      if (!isDelivery) return false;
      if (filterMode === "READY") return o.statusCode === 4;
      if (filterMode === "DISPATCHED") return o.statusCode === 5;
      // ACTIVE: Received(1), Accepted(2), InPreparation(3), Ready(4), Dispatched(5)
      return o.statusCode >= 1 && o.statusCode <= 5;
    });
  }, [orders, filterMode]);

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

  const allChecked = deliveryOrders.length > 0 && selectedOrders.length === deliveryOrders.length;

  function toggleAll() {
    if (allChecked) {
      setExcludedOrderIds(deliveryOrders.map((o) => o.id));
    } else {
      setExcludedOrderIds([]);
    }
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
    } else {
      setStatusMsg(res.errorMessage || "Erro ao finalizar toda a rota.");
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
      // Auto-register extra couriers if user selects more motoboys than currently registered
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
              fixedCommissionAmount: 6.5,
            }),
          },
          token
        );
        if (created.ok && created.data) {
          availableCouriers.push(created.data);
        } else {
          break;
        }
      }

      if (availableCouriers.length === 0) {
        setStatusMsg("Nenhum entregador disponível para vincular o romaneio.");
        setGenerating(false);
        return;
      }

      const buckets: typeof selectedOrders[] = Array.from({ length: Math.min(motoboyCount, availableCouriers.length) }, () => []);
      selectedOrders.forEach((order, idx) => {
        buckets[idx % buckets.length].push(order);
      });

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
            deliveryFee: o.netDeliveryFee || 6,
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
              notes: `Rota otimizada para ${courier.fullName}`,
            }),
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
            lng: s.destinationLongitude ?? stopsPayload[sIdx]?.destinationLongitude ?? originLng,
          }));
          results.push({
            courierName: courier.fullName,
            color: ROUTE_COLORS[b % ROUTE_COLORS.length],
            stops: plannedStops,
            estimatedKm: Number(res.data.totalPlannedDistanceKm ?? plannedStops.length * 2.1),
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

  if (!open) return null;

  const centerTile = latLngToTile(originLat, originLng, zoom);
  const spanDeg = 0.08 * Math.pow(2, 14 - zoom);

  function projectToPercent(lat: number, lng: number): { left: number; top: number } {
    const left = Math.min(92, Math.max(8, 50 + ((lng - originLng) / spanDeg) * 42));
    const top = Math.min(90, Math.max(10, 50 - ((lat - originLat) / spanDeg) * 42));
    return { left, top };
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/75 p-3 backdrop-blur-sm">
      <div className="flex max-h-[92vh] w-full max-w-6xl flex-col overflow-hidden rounded-2xl border border-violet-500/30 bg-[#140e24] text-white shadow-2xl">
        {/* Top bar */}
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-white/10 px-5 py-4">
          <div>
            <h2 className="text-base font-bold text-white">Mapa de Entregas & Rotas Otimizadas</h2>
            <p className="text-xs text-slate-300">
              Localização de pedidos ativos e geração de trajeto por proximidade para o motoboy.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <select
              value={filterMode}
              onChange={(e) => setFilterMode(e.target.value as typeof filterMode)}
              className="rounded-lg border border-white/15 bg-[#201836] px-3 py-1.5 text-xs font-semibold text-white"
            >
              <option value="ACTIVE">Todos os Pedidos Ativos ({deliveryOrders.length})</option>
              <option value="READY">Somente Prontos p/ Despacho</option>
              <option value="DISPATCHED">Já em Entrega</option>
            </select>
            <button
              onClick={onClose}
              className="rounded-lg border border-white/15 bg-white/5 px-3 py-1.5 text-xs font-semibold text-slate-200 hover:bg-white/15"
            >
              × Fechar
            </button>
          </div>
        </div>

        {/* Content Grid */}
        <div className="grid flex-1 overflow-y-auto lg:grid-cols-12">
          {/* Map Canvas */}
          <div className="relative min-h-[380px] overflow-hidden bg-slate-200 lg:col-span-7">
            {/* 3x3 OpenStreetMap Tile Backdrop + Clean fallback map grid */}
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
                backgroundSize: "40px 40px",
              }}
            />

            {/* Zoom Controls */}
            <div className="absolute left-3 top-3 z-20 flex flex-col overflow-hidden rounded-lg border border-slate-300 bg-white text-slate-900 shadow">
              <button
                type="button"
                onClick={() => setZoom((z) => Math.min(16, z + 1))}
                className="border-b border-slate-200 px-2.5 py-1 text-sm font-bold hover:bg-slate-100"
              >
                +
              </button>
              <button
                type="button"
                onClick={() => setZoom((z) => Math.max(12, z - 1))}
                className="px-2.5 py-1 text-sm font-bold hover:bg-slate-100"
              >
                -
              </button>
            </div>

            {/* Route lines SVG overlay */}
            <svg viewBox="0 0 100 100" preserveAspectRatio="none" className="pointer-events-none absolute inset-0 z-10 h-full w-full">
              {generatedRoutes.map((route, rIdx) => {
                const originPos = projectToPercent(originLat, originLng);
                const pts = [
                  `${originPos.left},${originPos.top}`,
                  ...route.stops.map((s) => {
                    const p = projectToPercent(s.lat, s.lng);
                    return `${p.left},${p.top}`;
                  }),
                ].join(" ");
                return (
                  <polyline
                    key={rIdx}
                    points={pts}
                    fill="none"
                    stroke={route.color}
                    strokeWidth="0.9"
                    strokeDasharray="2 1"
                  />
                );
              })}
            </svg>

            {/* Store Origin Marker */}
            {(() => {
              const p = projectToPercent(originLat, originLng);
              return (
                <div
                  style={{ left: `${p.left}%`, top: `${p.top}%` }}
                  className="absolute z-20 -translate-x-1/2 -translate-y-1/2"
                  title={`${storeName} (Ponto de Partida)`}
                >
                  <div className="flex h-10 w-10 items-center justify-center rounded-full border-2 border-white bg-violet-600 text-xs font-bold text-white shadow-lg">
                    LOJA
                  </div>
                </div>
              );
            })()}

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
                  title={`${order.orderNumber} · ${order.customerName} (${order.deliveryAddressSummary})`}
                >
                  #{idx + 1} · {order.orderNumber.slice(-4)}
                </button>
              );
            })}

            <div className="absolute bottom-2 right-2 z-20 rounded bg-white/85 px-2 py-0.5 text-[10px] font-medium text-slate-700">
              Mapa GPS · Zoom {zoom}x · {originLat.toFixed(4)}, {originLng.toFixed(4)}
            </div>
          </div>

          {/* Right Routing Sidebar */}
          <div className="flex flex-col gap-3 border-l border-white/10 p-5 lg:col-span-5">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-bold text-amber-300">Rota Sugerida de Entrega</h3>
              <span className="rounded-full bg-white/10 px-2.5 py-0.5 text-[11px] text-slate-200">
                {selectedOrders.length} de {deliveryOrders.length} selecionadas
              </span>
            </div>

            {/* Store departure point */}
            <div className="rounded-xl border border-violet-400/25 bg-violet-950/50 p-3">
              <div className="text-[10px] font-bold tracking-wider text-violet-300">PONTO DE PARTIDA (LOJA)</div>
              <div className="mt-0.5 text-sm font-bold text-white">{storeName}</div>
              <div className="text-xs text-slate-300">{storeAddress}</div>
            </div>

            {/* Motoboys working stepper + Generate button */}
            <div className="rounded-xl border border-white/10 bg-white/5 p-3">
              <div className="text-xs font-bold text-white">Quantos Motoboys estão trabalhando?</div>
              <div className="text-[11px] text-slate-300">Use 1, 2, 3 ou quantos entregadores tiver</div>
              <div className="mt-2.5 flex flex-wrap items-center gap-2">
                <div className="flex items-center overflow-hidden rounded-lg border border-white/15 bg-[#1b1330]">
                  <button
                    type="button"
                    onClick={() => setMotoboyCount((c) => Math.max(1, c - 1))}
                    className="px-3 py-1.5 text-sm font-bold hover:bg-white/10"
                  >
                    -
                  </button>
                  <span className="w-8 text-center font-mono text-sm font-bold">{motoboyCount}</span>
                  <button
                    type="button"
                    onClick={() => setMotoboyCount((c) => Math.min(8, c + 1))}
                    className="px-3 py-1.5 text-sm font-bold hover:bg-white/10"
                  >
                    +
                  </button>
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

            {statusMsg && (
              <div className="rounded-lg border border-emerald-400/30 bg-emerald-950/60 px-3 py-2 text-xs text-emerald-200">
                {statusMsg}
              </div>
            )}

            {/* Stops checklist */}
            <div className="flex items-center justify-between pt-1 text-xs">
              <span className="font-semibold text-slate-200">Selecione as paradas da viagem:</span>
              <label className="flex cursor-pointer items-center gap-1.5 text-slate-200">
                <input
                  type="checkbox"
                  checked={allChecked}
                  onChange={toggleAll}
                  className="h-3.5 w-3.5 rounded accent-violet-500"
                />
                Todos
              </label>
            </div>

            {deliveryOrders.length === 0 ? (
              <div className="rounded-xl border border-dashed border-white/15 p-5 text-center text-xs text-slate-400">
                Nenhum pedido de delivery pendente para o filtro selecionado.
              </div>
            ) : (
              <div className="max-h-48 space-y-1.5 overflow-y-auto pr-1">
                {deliveryOrders.map((order) => {
                  const checked = !excludedOrderIds.includes(order.id);
                  return (
                    <label
                      key={order.id}
                      className="flex cursor-pointer items-start justify-between gap-2 rounded-lg border border-white/10 bg-white/5 px-3 py-2 text-xs hover:bg-white/10"
                    >
                      <div className="flex items-start gap-2">
                        <input
                          type="checkbox"
                          checked={checked}
                          onChange={() => toggleOne(order.id)}
                          className="mt-0.5 h-3.5 w-3.5 rounded accent-violet-500"
                        />
                        <div>
                          <div className="font-bold text-white">
                            {order.orderNumber} · {order.customerName}
                          </div>
                          <div className="text-[11px] text-slate-300">
                            {order.deliveryAddressSummary || "Endereço de entrega"}
                          </div>
                        </div>
                      </div>
                      <span className="shrink-0 font-mono font-semibold text-emerald-300">
                        R$ {order.totalAmount.toFixed(2)}
                      </span>
                    </label>
                  );
                })}
              </div>
            )}

            {/* Generated Route Details */}
            {generatedRoutes.length > 0 && (
              <div className="space-y-2 border-t border-white/10 pt-3">
                <div className="text-xs font-bold text-white">Romaneios Gerados</div>
                {generatedRoutes.map((route, idx) => {
                  const waypoints = route.stops.map((s) => encodeURIComponent(s.address)).join("/");
                  const mapsUrl = `https://www.google.com/maps/dir/${encodeURIComponent(storeAddress)}/${waypoints}`;
                  return (
                    <div
                      key={idx}
                      className="rounded-xl border border-white/15 bg-[#1d1533] p-3 text-xs"
                    >
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-bold" style={{ color: route.color }}>
                          {route.courierName} · {route.stops.length} parada(s) · ~{route.estimatedKm.toFixed(1)} km
                        </span>
                        <a
                          href={mapsUrl}
                          target="_blank"
                          rel="noreferrer"
                          className="rounded bg-white/10 px-2 py-0.5 text-[10px] font-semibold text-white hover:bg-white/20"
                        >
                          Abrir GPS Maps
                        </a>
                      </div>
                      <ol className="mt-1.5 space-y-1 text-[11px] text-slate-300">
                        {route.stops.map((s) => (
                          <li key={s.sequence}>
                            {s.sequence}. {s.orderNumber} · {s.customerName} ({s.address})
                          </li>
                        ))}
                      </ol>
                    </div>
                  );
                })}
              </div>
            )}
            {/* Recent / Active Dispatch Runs with Baixa / Settlement Controls */}
            {recentRuns.length > 0 && (
              <div className="space-y-2 border-t border-white/10 pt-3">
                <div className="text-xs font-bold text-white">Romaneios & Baixa dos Entregadores ({recentRuns.length})</div>
                <div className="max-h-56 space-y-2 overflow-y-auto pr-1">
                  {recentRuns.map((run) => {
                    const courierName = couriers.find((c) => c.id === run.courierId)?.fullName || "Motoboy";
                    const isDone = run.status === "Completed" || run.stops.every((s) => s.status !== "Pending");
                    return (
                      <div key={run.id} className="rounded-xl border border-white/15 bg-[#1b1330] p-3 text-xs">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <span className="font-extrabold text-amber-300">{run.runCode}</span>
                            <span className="ml-2 font-bold text-white">{courierName}</span>
                            <span className="ml-2 text-[11px] text-slate-300">
                              · {run.totalEstimatedDistanceKm.toFixed(1)} km · Frete/Comissão: R$ {run.courierCommissionAmount.toFixed(2)}
                            </span>
                          </div>
                          <div className="flex flex-wrap items-center gap-1.5">
                            <span
                              className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${
                                isDone
                                  ? "bg-emerald-500/20 text-emerald-300"
                                  : run.status === "InTransit"
                                    ? "bg-violet-500/30 text-violet-200"
                                    : "bg-amber-500/20 text-amber-300"
                              }`}
                            >
                              {isDone ? "Baixada / Concluída" : run.status === "InTransit" ? "Em Trânsito" : "Aguardando Saída"}
                            </span>
                            {!isDone && run.status !== "InTransit" && (
                              <button
                                type="button"
                                disabled={settlingId === run.id}
                                onClick={() => void handleStartRun(run.id)}
                                className="rounded-lg bg-violet-600 px-2.5 py-1 text-[10px] font-bold text-white hover:bg-violet-500 disabled:opacity-50"
                              >
                                Iniciar Saída
                              </button>
                            )}
                            {!isDone && (
                              <button
                                type="button"
                                disabled={settlingId === run.id}
                                onClick={() => void handleCompleteAllRun(run.id)}
                                className="rounded-lg bg-emerald-600 px-2.5 py-1 text-[10px] font-extrabold text-white hover:bg-emerald-500 disabled:opacity-50"
                              >
                                Dar Baixa Total ✓
                              </button>
                            )}
                          </div>
                        </div>

                        <ul className="mt-2 space-y-1">
                          {run.stops.map((st) => {
                            const delivered = st.status === "Delivered";
                            return (
                              <li key={st.id} className="flex items-center justify-between gap-2 rounded bg-white/5 px-2 py-1 text-[11px]">
                                <span className={delivered ? "text-emerald-300 line-through" : "text-slate-200"}>
                                  {st.stopSequence}. {st.orderNumber} · {st.customerName} ({st.destinationAddress})
                                </span>
                                {delivered ? (
                                  <span className="shrink-0 font-bold text-emerald-400">Entregue ✓</span>
                                ) : (
                                  <button
                                    type="button"
                                    disabled={settlingId === st.id}
                                    onClick={() => void handleResolveStop(run.id, st.id)}
                                    className="shrink-0 rounded bg-emerald-600 px-2 py-0.5 text-[10px] font-bold text-white hover:bg-emerald-500"
                                  >
                                    Baixar Parada ✓
                                  </button>
                                )}
                              </li>
                            );
                          })}
                        </ul>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
