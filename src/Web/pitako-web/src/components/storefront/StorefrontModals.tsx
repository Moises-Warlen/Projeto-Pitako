"use client";

import { CheckCircle2, Clock, PackageCheck, Search, Truck, X } from "lucide-react";
import { useState } from "react";
import { OrderTrackingResponse, StoreOperatingStatus } from "@/types/menu";
import { formatBrl } from "./ProductMediaFrame";

const DAY_LABELS = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];

export function OperatingHoursModal({
  open,
  onClose,
  storeTradeName,
  status
}: {
  open: boolean;
  onClose: () => void;
  storeTradeName: string;
  status?: StoreOperatingStatus | null;
}) {
  if (!open || !status) return null;

  return (
    <div role="dialog" aria-modal="true" aria-label="Horários de Funcionamento" className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4 backdrop-blur-sm">
      <div className="w-full max-w-md rounded-3xl bg-white p-6 shadow-2xl">
        <div className="flex items-start justify-between">
          <div>
            <p className="text-xs font-bold uppercase tracking-wide text-slate-500">{storeTradeName}</p>
            <h3 className="mt-1 flex items-center gap-2 text-lg font-extrabold text-slate-900">
              <Clock className="h-5 w-5" /> Horários de Funcionamento
            </h3>
          </div>
          <button type="button" onClick={onClose} aria-label="Fechar horários" className="rounded-full bg-slate-100 p-2 text-slate-600">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div
          className={`mt-4 rounded-2xl px-4 py-3 text-xs font-bold ${
            status.isOpenNow ? "bg-emerald-50 text-emerald-800" : "bg-rose-50 text-rose-800"
          }`}
        >
          {status.statusLabel}
          {status.nextChangeText ? ` · ${status.nextChangeText}` : ""}
        </div>

        <div className="mt-4 divide-y divide-slate-100 text-sm">
          {status.schedule.map((entry) => (
            <div key={entry.dayOfWeek} className="flex items-center justify-between py-2.5">
              <span className="font-semibold text-slate-800">{DAY_LABELS[entry.dayOfWeek] ?? `Dia ${entry.dayOfWeek}`}</span>
              {entry.isOpen ? (
                <span className="font-mono text-xs font-bold text-slate-700">
                  {entry.openTime} às {entry.closeTime}
                </span>
              ) : (
                <span className="text-xs font-semibold text-rose-600">Fechado</span>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

const ORDER_STEPS = [
  { code: 1, label: "Recebido", icon: CheckCircle2 },
  { code: 3, label: "Em Preparo", icon: PackageCheck },
  { code: 5, label: "Saiu / Pronto", icon: Truck },
  { code: 6, label: "Concluído", icon: CheckCircle2 }
];

export function OrderTrackingModal({
  open,
  onClose,
  initialOrder,
  onLookupOrder
}: {
  open: boolean;
  onClose: () => void;
  initialOrder: OrderTrackingResponse | null;
  onLookupOrder: (orderNumber: string, phone: string) => Promise<string | null>;
}) {
  const [searchCode, setSearchCode] = useState(initialOrder?.orderNumber ?? "");
  const [searchPhone, setSearchPhone] = useState("");
  const [lookupError, setLookupError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (!open) return null;

  async function handleSearch(e: React.FormEvent) {
    e.preventDefault();
    if (!searchCode.trim()) return;
    setBusy(true);
    setLookupError(null);
    const err = await onLookupOrder(searchCode.trim(), searchPhone.trim());
    setLookupError(err);
    setBusy(false);
  }

  return (
    <div role="dialog" aria-modal="true" aria-label="Acompanhamento do Pedido" className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4 backdrop-blur-sm">
      <div className="max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-3xl bg-white p-6 shadow-2xl">
        <div className="flex items-start justify-between">
          <div>
            <h3 className="text-lg font-extrabold text-slate-900">Acompanhamento do Pedido</h3>
            <p className="text-xs text-slate-500">Consulte o status em tempo real pelo código do pedido.</p>
          </div>
          <button type="button" onClick={onClose} aria-label="Fechar acompanhamento" className="rounded-full bg-slate-100 p-2 text-slate-600">
            <X className="h-4 w-4" />
          </button>
        </div>

        <form onSubmit={handleSearch} className="mt-4 flex flex-wrap gap-2">
          <input
            value={searchCode}
            onChange={(e) => setSearchCode(e.target.value)}
            placeholder="Código (ex: #0001)"
            className="flex-1 rounded-xl border border-slate-300 px-3 py-2 text-xs font-mono"
          />
          <input
            value={searchPhone}
            onChange={(e) => setSearchPhone(e.target.value)}
            placeholder="4 últimos dígitos do tel."
            className="w-40 rounded-xl border border-slate-300 px-3 py-2 text-xs"
          />
          <button type="submit" disabled={busy} className="inline-flex items-center gap-1 rounded-xl bg-slate-900 px-3.5 py-2 text-xs font-bold text-white">
            <Search className="h-3.5 w-3.5" /> Buscar
          </button>
        </form>

        {lookupError && <p className="mt-2 text-xs font-semibold text-rose-700">{lookupError}</p>}

        {initialOrder && (
          <div className="mt-5 space-y-4">
            <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
              <div className="flex items-center justify-between">
                <span className="font-mono text-base font-extrabold text-slate-900">Pedido {initialOrder.orderNumber}</span>
                <span className="rounded-full bg-emerald-100 px-3 py-0.5 text-xs font-bold text-emerald-800">
                  {initialOrder.status}
                </span>
              </div>
              <p className="mt-1 text-xs text-slate-600">
                Cliente: {initialOrder.customerName} · {initialOrder.paymentMethod || "Pagamento combinado"}
              </p>
              {initialOrder.deliveryAddressSummary && (
                <p className="mt-1 text-xs text-slate-600">Entrega: {initialOrder.deliveryAddressSummary}</p>
              )}

              <div className="mt-4 grid grid-cols-4 gap-2">
                {ORDER_STEPS.map((st) => {
                  const Icon = st.icon;
                  const active = initialOrder.statusCode !== 7 && initialOrder.statusCode >= st.code;
                  return (
                    <div
                      key={st.code}
                      className={`flex flex-col items-center rounded-xl p-2 text-center ${
                        active ? "bg-emerald-600 text-white" : "bg-white text-slate-400"
                      }`}
                    >
                      <Icon className="h-4 w-4" />
                      <span className="mt-1 text-[10px] font-bold leading-tight">{st.label}</span>
                    </div>
                  );
                })}
              </div>
            </div>

            <div className="space-y-2 rounded-2xl border border-slate-200 p-4 text-xs">
              <p className="font-bold text-slate-900">Itens do Pedido</p>
              {initialOrder.items.map((it) => (
                <div key={it.id} className="flex justify-between gap-2 border-b border-slate-100 pb-2">
                  <div>
                    <p className="font-semibold text-slate-800">
                      {it.quantity}x {it.itemTitle}
                    </p>
                    {it.customizationSummaryText && <p className="text-slate-500">{it.customizationSummaryText}</p>}
                  </div>
                  <span className="font-mono font-bold text-slate-900">{formatBrl(it.lineSubtotal)}</span>
                </div>
              ))}
              <div className="flex justify-between pt-1 text-sm font-extrabold text-slate-900">
                <span>Total Pago/Previsto</span>
                <span className="font-mono">{formatBrl(initialOrder.totalAmount)}</span>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
