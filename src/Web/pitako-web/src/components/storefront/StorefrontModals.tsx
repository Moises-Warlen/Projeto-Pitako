"use client";

import { Check, CheckCircle2, Clock, Copy, MessageCircle, PackageCheck, QrCode, Search, Truck, X } from "lucide-react";
import { useState } from "react";
import { QrCodeMatrix } from "@/components/QrCodeMatrix";
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

  const todayDow = new Date().getDay();

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
          {status.pauseReason ? <p className="mt-1 font-normal">{status.pauseReason}</p> : null}
        </div>

        <div className="mt-4 space-y-1.5 text-sm">
          {status.schedule.map((entry) => {
            const isToday = entry.dayOfWeek === todayDow;
            return (
              <div
                key={entry.dayOfWeek}
                className={`flex items-center justify-between rounded-xl px-3 py-2.5 ${
                  isToday ? "border border-emerald-200 bg-emerald-50/70" : "border border-transparent"
                }`}
              >
                <span className="flex items-center gap-2 font-semibold text-slate-800">
                  {DAY_LABELS[entry.dayOfWeek] ?? `Dia ${entry.dayOfWeek}`}
                  {isToday && (
                    <span className="rounded-full bg-emerald-600 px-2 py-0.5 text-[10px] font-bold text-white">
                      Hoje
                    </span>
                  )}
                </span>
                {entry.isOpen ? (
                  <span className="font-mono text-xs font-bold text-slate-700">
                    {entry.openTime} às {entry.closeTime}
                  </span>
                ) : (
                  <span className="text-xs font-semibold text-rose-600">Fechado</span>
                )}
              </div>
            );
          })}
        </div>

        <button
          type="button"
          onClick={onClose}
          className="mt-5 w-full rounded-2xl bg-slate-900 py-3 text-sm font-bold text-white hover:bg-slate-800"
        >
          Entendi
        </button>
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

function sanitizeBrazilPhone(phone?: string | null): string {
  const digits = (phone || "").replace(/\D/g, "");
  if (!digits) return "";
  return digits.startsWith("55") ? digits : `55${digits}`;
}

export function OrderTrackingModal({
  open,
  onClose,
  initialOrder,
  onLookupOrder,
  onVerifyPix,
  storeTradeName,
  storePhone
}: {
  open: boolean;
  onClose: () => void;
  initialOrder: OrderTrackingResponse | null;
  onLookupOrder: (orderNumber: string, phone: string) => Promise<string | null>;
  onVerifyPix?: (orderNumber: string) => Promise<string | null>;
  storeTradeName?: string;
  storePhone?: string | null;
}) {
  const [searchCode, setSearchCode] = useState(initialOrder?.orderNumber ?? "");
  const [searchPhone, setSearchPhone] = useState("");
  const [lookupError, setLookupError] = useState<string | null>(null);
  const [pixFeedback, setPixFeedback] = useState<string | null>(null);
  const [copiedPix, setCopiedPix] = useState(false);
  const [busy, setBusy] = useState(false);
  const [verifyingPix, setVerifyingPix] = useState(false);

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

  async function handleCopyPix() {
    if (!initialOrder?.pixBrCode) return;
    try {
      await navigator.clipboard.writeText(initialOrder.pixBrCode);
      setCopiedPix(true);
      setTimeout(() => setCopiedPix(false), 2500);
    } catch {
      setPixFeedback("Copie o código PIX manualmente no campo abaixo.");
    }
  }

  async function handleCheckPixConfirmation() {
    if (!initialOrder || !onVerifyPix) return;
    setVerifyingPix(true);
    setPixFeedback(null);
    const feedback = await onVerifyPix(initialOrder.orderNumber);
    if (feedback) setPixFeedback(feedback);
    setVerifyingPix(false);
  }

  const isPixOrder =
    Boolean(initialOrder?.pixBrCode) ||
    Boolean(initialOrder?.paymentMethod?.toLowerCase().includes("pix"));

  const cleanWaPhone = sanitizeBrazilPhone(storePhone);
  const itemsText = initialOrder
    ? initialOrder.items
        .map((it) => `• ${it.quantity}x ${it.itemTitle}${it.customizationSummaryText ? ` (${it.customizationSummaryText})` : ""} - ${formatBrl(it.lineSubtotal)}`)
        .join("\n")
    : "";
  const copyMessage = initialOrder
    ? `Olá${storeTradeName ? ` ${storeTradeName}` : ""}! Resumo do meu pedido *${initialOrder.orderNumber}*:\nCliente: ${initialOrder.customerName}\n${itemsText}\n*Total:* ${formatBrl(initialOrder.totalAmount)}\nForma: ${initialOrder.paymentMethod || "Combinado"}`
    : "";
  const waCopyUrl = cleanWaPhone
    ? `https://wa.me/${cleanWaPhone}?text=${encodeURIComponent(copyMessage)}`
    : `https://wa.me/?text=${encodeURIComponent(copyMessage)}`;
  const waChatUrl = cleanWaPhone
    ? `https://wa.me/${cleanWaPhone}?text=${encodeURIComponent(`Olá! Preciso de ajuda com o pedido ${initialOrder?.orderNumber ?? ""}.`)}`
    : `https://wa.me/?text=${encodeURIComponent(`Olá! Sobre o pedido ${initialOrder?.orderNumber ?? ""}`)}`;

  return (
    <div role="dialog" aria-modal="true" aria-label="Acompanhamento do Pedido" className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4 backdrop-blur-sm">
      <div className="max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-3xl bg-white p-6 shadow-2xl">
        <div className="flex items-start justify-between">
          <div>
            <h3 className="text-lg font-extrabold text-slate-900">Acompanhamento do Pedido</h3>
            <p className="text-xs text-slate-500">Consulte o status em tempo real, pague via PIX ou envie no WhatsApp.</p>
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
              <div className="flex items-center justify-between gap-2">
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

            {/* PIX Payment Card */}
            {isPixOrder && (
              <div className="rounded-2xl border border-emerald-200 bg-emerald-50/70 p-4 text-center">
                {initialOrder.isPaymentConfirmed ? (
                  <div className="flex flex-col items-center gap-1 py-2 text-emerald-800">
                    <CheckCircle2 className="h-7 w-7 text-emerald-600" />
                    <p className="text-sm font-extrabold">Pagamento PIX Confirmado!</p>
                    <p className="text-xs text-emerald-700">Seu pedido já está liberado para preparo.</p>
                  </div>
                ) : (
                  <>
                    <div className="mb-2 flex items-center justify-center gap-1.5 text-sm font-extrabold text-emerald-950">
                      <QrCode className="h-4 w-4 text-emerald-700" /> Pagar com PIX Agora
                    </div>
                    <p className="mb-3 text-xs text-emerald-800">
                      Escaneie o QR Code no aplicativo do seu banco ou copie o código PIX abaixo:
                    </p>
                    {initialOrder.pixBrCode && (
                      <>
                        <div className="mx-auto mb-3 inline-block rounded-2xl border border-emerald-200 bg-white p-3 shadow-sm">
                          <QrCodeMatrix value={initialOrder.pixBrCode} size={172} />
                        </div>
                        <div className="flex items-center gap-2">
                          <input
                            readOnly
                            aria-label="Código PIX Copia e Cola"
                            value={initialOrder.pixBrCode}
                            className="flex-1 rounded-xl border border-emerald-200 bg-white px-3 py-2 font-mono text-[11px] text-slate-700"
                          />
                          <button
                            type="button"
                            onClick={() => void handleCopyPix()}
                            className="inline-flex items-center gap-1.5 rounded-xl bg-slate-900 px-3 py-2 text-xs font-bold text-white hover:bg-slate-800"
                          >
                            {copiedPix ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
                            {copiedPix ? "Copiado" : "Copiar Código"}
                          </button>
                        </div>
                      </>
                    )}

                    {onVerifyPix && (
                      <button
                        type="button"
                        disabled={verifyingPix}
                        onClick={() => void handleCheckPixConfirmation()}
                        className="mt-3 w-full rounded-xl bg-emerald-600 py-2.5 text-xs font-bold text-white hover:bg-emerald-700 disabled:opacity-50"
                      >
                        {verifyingPix ? "Verificando no banco..." : "Verificar Confirmação Bancária"}
                      </button>
                    )}

                    {pixFeedback && (
                      <p className="mt-2 text-xs font-semibold text-emerald-900">{pixFeedback}</p>
                    )}
                  </>
                )}
              </div>
            )}

            {/* Items Summary */}
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

            {/* WhatsApp Actions */}
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              <a
                href={waCopyUrl}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center justify-center gap-2 rounded-2xl bg-emerald-600 px-4 py-3 text-xs font-bold text-white hover:bg-emerald-700"
              >
                <MessageCircle className="h-4 w-4" /> Receber Cópia no WhatsApp
              </a>
              <a
                href={waChatUrl}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-300 bg-white px-4 py-3 text-xs font-bold text-slate-800 hover:bg-slate-50"
              >
                <MessageCircle className="h-4 w-4 text-emerald-600" /> Abrir Chat com a Loja
              </a>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
