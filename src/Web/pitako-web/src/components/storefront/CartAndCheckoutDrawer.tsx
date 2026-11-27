"use client";

import {
  CheckCircle2,
  Clock,
  CreditCard,
  Edit3,
  MapPin,
  Minus,
  Plus,
  ShieldCheck,
  ShoppingBag,
  Tag,
  Trash2,
  Truck,
  X
} from "lucide-react";
import { useState } from "react";
import { SmartCartItem, SmartCartResponse } from "@/types/menu";
import { formatBrl } from "./ProductMediaFrame";

interface CartAndCheckoutDrawerProps {
  open: boolean;
  onClose: () => void;
  cart: SmartCartResponse | null;
  storeTradeName: string;
  allowDelivery: boolean;
  allowPickup: boolean;
  onEditCartItem: (item: SmartCartItem) => void;
  onUpdateQty: (itemId: string, nextQty: number) => Promise<void>;
  onRemoveItem: (itemId: string) => Promise<void>;
  onClearCart: () => Promise<void>;
  onApplyCoupon: (code: string, customerPhone?: string, customerName?: string) => Promise<string | null>;
  onRemoveCoupon: () => Promise<void>;
  onQuoteFreight: (payload: {
    fulfillmentMode: 1 | 2;
    neighborhood?: string;
    zipCode?: string;
  }) => Promise<{ fee: number; summary: string; estimatedMinutes: number; error?: string }>;
  onSubmitCheckout: (payload: {
    customerName: string;
    customerPhone: string;
    fulfillmentMode: 1 | 2;
    deliveryAddress?: string;
    neighborhood?: string;
    zipCode?: string;
    paymentMethod: string;
    lgpdConsentAccepted: boolean;
  }) => Promise<string | null>;
}

export function CartAndCheckoutDrawer({
  open,
  onClose,
  cart,
  storeTradeName,
  allowDelivery,
  allowPickup,
  onEditCartItem,
  onUpdateQty,
  onRemoveItem,
  onClearCart,
  onApplyCoupon,
  onRemoveCoupon,
  onQuoteFreight,
  onSubmitCheckout
}: CartAndCheckoutDrawerProps) {
  const [step, setStep] = useState<"cart" | "checkout">("cart");
  const [confirmClear, setConfirmClear] = useState(false);
  const [couponInput, setCouponInput] = useState("");
  const [couponFeedback, setCouponFeedback] = useState<string | null>(null);

  const [customerName, setCustomerName] = useState("");
  const [customerPhone, setCustomerPhone] = useState("");
  const [fulfillmentMode, setFulfillmentMode] = useState<1 | 2>(allowDelivery ? 1 : 2);
  const [deliveryAddress, setDeliveryAddress] = useState("");
  const [neighborhood, setNeighborhood] = useState("");
  const [zipCode, setZipCode] = useState("");
  const [paymentMethod, setPaymentMethod] = useState("PIX");
  const [lgpdAccepted, setLgpdAccepted] = useState(true);
  const [freightInfo, setFreightInfo] = useState<{ fee: number; summary: string; estimatedMinutes: number; error?: string } | null>(null);
  const [checkoutError, setCheckoutError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (!open) return null;

  const items = cart?.items ?? [];
  const itemsSubtotal = cart?.itemsSubtotal ?? 0;
  const itemsDiscount = cart?.itemsDiscount ?? 0;
  const quotedDeliveryFee = fulfillmentMode === 2 ? 0 : (freightInfo?.fee ?? cart?.deliveryFee ?? 0);
  const grandTotal = Math.max(0, itemsSubtotal - itemsDiscount + quotedDeliveryFee);

  async function handleApplyCoupon(e: React.FormEvent) {
    e.preventDefault();
    if (!couponInput.trim()) return;
    setBusy(true);
    setCouponFeedback(null);
    const err = await onApplyCoupon(couponInput.trim(), customerPhone.trim() || undefined, customerName.trim() || undefined);
    setCouponFeedback(err ?? "Cupom aplicado com sucesso!");
    setTimeout(() => setCouponFeedback(null), 2800);
    setBusy(false);
  }

  async function handleFreightCheck(nextMode: 1 | 2 = fulfillmentMode, nextNeighborhood = neighborhood, nextZip = zipCode) {
    const info = await onQuoteFreight({
      fulfillmentMode: nextMode,
      neighborhood: nextNeighborhood.trim() || undefined,
      zipCode: nextZip.trim() || undefined
    });
    setFreightInfo(info);
  }

  async function handleCheckoutSubmit(e: React.FormEvent) {
    e.preventDefault();
    setCheckoutError(null);
    if (!lgpdAccepted) {
      setCheckoutError("É necessário concordar com os termos de privacidade (LGPD) para concluir o pedido.");
      return;
    }
    setBusy(true);
    const err = await onSubmitCheckout({
      customerName: customerName.trim(),
      customerPhone: customerPhone.trim(),
      fulfillmentMode,
      deliveryAddress: fulfillmentMode === 1 ? deliveryAddress.trim() : undefined,
      neighborhood: fulfillmentMode === 1 ? neighborhood.trim() || undefined : undefined,
      zipCode: fulfillmentMode === 1 ? zipCode.trim() || undefined : undefined,
      paymentMethod,
      lgpdConsentAccepted: lgpdAccepted
    });
    setBusy(false);
    if (err) {
      setCheckoutError(err);
    } else {
      setStep("cart");
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-950/60 backdrop-blur-sm">
      <div className="flex h-full w-full max-w-md flex-col bg-white shadow-2xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <div className="flex items-center gap-2.5">
            <ShoppingBag className="h-5 w-5 text-slate-900" />
            <div>
              <h2 className="text-base font-extrabold text-slate-900">
                {step === "cart" ? `Sua Sacola (${items.reduce((a, b) => a + b.quantity, 0)})` : "Finalizar Pedido"}
              </h2>
              <p className="text-[11px] text-slate-500">{storeTradeName}</p>
            </div>
          </div>
          <button type="button" onClick={onClose} aria-label="Fechar sacola" className="rounded-full bg-slate-100 p-2 text-slate-600 hover:bg-slate-200">
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-5">
          {items.length === 0 ? (
            <div className="flex h-full flex-col items-center justify-center text-center text-slate-500">
              <ShoppingBag className="mb-3 h-12 w-12 text-slate-300" />
              <p className="text-sm font-bold text-slate-700">Sua sacola está vazia</p>
              <p className="mt-1 text-xs">Escolha um item do cardápio e personalize do seu jeito.</p>
            </div>
          ) : step === "cart" ? (
            <div className="space-y-4">
              {items.map((item) => (
                <div key={item.id} className="rounded-2xl border border-slate-200 bg-slate-50/70 p-3.5">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-extrabold text-slate-900">{item.itemTitle}</p>
                      {item.customizationSummaryText && (
                        <p className="mt-1 whitespace-pre-line text-xs leading-relaxed text-slate-600">
                          {item.customizationSummaryText}
                        </p>
                      )}
                      {item.customerNotes && (
                        <p className="mt-1 rounded bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-900">
                          Obs: {item.customerNotes}
                        </p>
                      )}
                    </div>
                    <p className="shrink-0 font-mono text-sm font-extrabold text-slate-900">{formatBrl(item.lineSubtotal)}</p>
                  </div>

                  <div className="mt-3 flex items-center justify-between border-t border-slate-200/80 pt-2.5">
                    <div className="flex items-center gap-1.5">
                      {item.lineKind === "CatalogProduct" && item.productId && (
                        <button
                          type="button"
                          onClick={() => onEditCartItem(item)}
                          className="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700 hover:bg-slate-100"
                        >
                          <Edit3 className="h-3 w-3" /> Editar
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={() => onRemoveItem(item.id)}
                        className="inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-semibold text-rose-600 hover:bg-rose-50"
                      >
                        <Trash2 className="h-3 w-3" /> Remover
                      </button>
                    </div>

                    <div className="flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-1 py-0.5">
                      <button
                        type="button"
                        onClick={() => onUpdateQty(item.id, item.quantity - 1)}
                        className="p-1 text-slate-700"
                        aria-label="Diminuir quantidade"
                      >
                        <Minus className="h-3.5 w-3.5" />
                      </button>
                      <span className="w-5 text-center text-xs font-bold text-slate-900">{item.quantity}</span>
                      <button
                        type="button"
                        onClick={() => onUpdateQty(item.id, item.quantity + 1)}
                        className="p-1 text-slate-700"
                        aria-label="Aumentar quantidade"
                      >
                        <Plus className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </div>
                </div>
              ))}

              {/* Limpar sacola */}
              <div className="flex justify-end">
                {!confirmClear ? (
                  <button type="button" onClick={() => setConfirmClear(true)} className="text-xs font-semibold text-slate-500 underline">
                    Limpar sacola
                  </button>
                ) : (
                  <div className="flex items-center gap-2 rounded-lg bg-rose-50 px-3 py-1.5 text-xs">
                    <span className="font-semibold text-rose-800">Esvaziar tudo?</span>
                    <button
                      type="button"
                      onClick={async () => {
                        await onClearCart();
                        setConfirmClear(false);
                      }}
                      className="font-bold text-rose-700 underline"
                    >
                      Sim
                    </button>
                    <button type="button" onClick={() => setConfirmClear(false)} className="text-slate-600">
                      Não
                    </button>
                  </div>
                )}
              </div>

              {/* Cupom */}
              <form onSubmit={handleApplyCoupon} className="rounded-2xl border border-slate-200 bg-white p-3.5">
                <label className="mb-1.5 flex items-center gap-1.5 text-xs font-bold text-slate-700">
                  <Tag className="h-3.5 w-3.5" /> Cupom de Desconto
                </label>
                {cart?.appliedCouponCode ? (
                  <div className="flex items-center justify-between rounded-lg bg-emerald-50 px-3 py-2 text-xs font-bold text-emerald-800">
                    <span>
                      Cupom {cart.appliedCouponCode} (-{formatBrl(itemsDiscount)})
                    </span>
                    <button type="button" onClick={onRemoveCoupon} className="underline">
                      Remover
                    </button>
                  </div>
                ) : (
                  <div className="flex gap-2">
                    <input
                      value={couponInput}
                      onChange={(e) => setCouponInput(e.target.value.toUpperCase())}
                      placeholder="Ex: PIZZA10"
                      className="flex-1 rounded-lg border border-slate-300 px-3 py-1.5 text-xs font-mono uppercase"
                    />
                    <button type="submit" disabled={busy} className="rounded-lg bg-slate-900 px-3 py-1.5 text-xs font-bold text-white">
                      Aplicar
                    </button>
                  </div>
                )}
                {couponFeedback && <p className="mt-1.5 text-[11px] font-medium text-slate-600">{couponFeedback}</p>}
              </form>
            </div>
          ) : (
            <form id="pitako-checkout-form" onSubmit={handleCheckoutSubmit} className="space-y-4">
              <div className="grid grid-cols-2 gap-2">
                {allowDelivery && (
                  <button
                    type="button"
                    onClick={() => {
                      setFulfillmentMode(1);
                      void handleFreightCheck(1);
                    }}
                    className={`flex items-center justify-center gap-1.5 rounded-xl border py-2.5 text-xs font-bold ${
                      fulfillmentMode === 1 ? "border-slate-900 bg-slate-900 text-white" : "border-slate-200 text-slate-700"
                    }`}
                  >
                    <Truck className="h-4 w-4" /> Delivery
                  </button>
                )}
                {allowPickup && (
                  <button
                    type="button"
                    onClick={() => {
                      setFulfillmentMode(2);
                      void handleFreightCheck(2);
                    }}
                    className={`flex items-center justify-center gap-1.5 rounded-xl border py-2.5 text-xs font-bold ${
                      fulfillmentMode === 2 ? "border-slate-900 bg-slate-900 text-white" : "border-slate-200 text-slate-700"
                    }`}
                  >
                    <MapPin className="h-4 w-4" /> Retirar na Loja
                  </button>
                )}
              </div>

              <div className="space-y-2.5">
                <input
                  required
                  value={customerName}
                  onChange={(e) => setCustomerName(e.target.value)}
                  placeholder="Seu nome completo"
                  className="w-full rounded-xl border border-slate-300 px-3.5 py-2.5 text-sm"
                />
                <input
                  required
                  value={customerPhone}
                  onChange={(e) => setCustomerPhone(e.target.value)}
                  placeholder="WhatsApp / Telefone (ex: 16999887766)"
                  className="w-full rounded-xl border border-slate-300 px-3.5 py-2.5 text-sm"
                />
              </div>

              {fulfillmentMode === 1 && (
                <div className="space-y-2.5 rounded-2xl border border-slate-200 bg-slate-50 p-3.5">
                  <p className="text-xs font-bold text-slate-800">Endereço de Entrega</p>
                  <input
                    required
                    value={deliveryAddress}
                    onChange={(e) => setDeliveryAddress(e.target.value)}
                    placeholder="Rua, número e complemento"
                    className="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm"
                  />
                  <div className="grid grid-cols-2 gap-2">
                    <input
                      value={neighborhood}
                      onChange={(e) => setNeighborhood(e.target.value)}
                      onBlur={() => void handleFreightCheck(1, neighborhood, zipCode)}
                      placeholder="Bairro"
                      className="rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs"
                    />
                    <input
                      value={zipCode}
                      onChange={(e) => setZipCode(e.target.value)}
                      onBlur={() => void handleFreightCheck(1, neighborhood, zipCode)}
                      placeholder="CEP"
                      className="rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs"
                    />
                  </div>
                  <button
                    type="button"
                    onClick={() => void handleFreightCheck(1, neighborhood, zipCode)}
                    className="text-xs font-bold text-slate-700 underline"
                  >
                    Calcular taxa e prazo de entrega
                  </button>
                  {freightInfo && (
                    <p className={`text-xs font-semibold ${freightInfo.error ? "text-rose-700" : "text-emerald-700"}`}>
                      {freightInfo.error || `${freightInfo.summary} · ${freightInfo.estimatedMinutes} min · Frete ${formatBrl(freightInfo.fee)}`}
                    </p>
                  )}
                </div>
              )}

              <div>
                <p className="mb-2 flex items-center gap-1.5 text-xs font-bold text-slate-800">
                  <CreditCard className="h-3.5 w-3.5" /> Forma de Pagamento
                </p>
                <div className="grid grid-cols-2 gap-2">
                  {["PIX", "Cartão de Crédito", "Cartão de Débito", "Dinheiro"].map((pm) => (
                    <button
                      key={pm}
                      type="button"
                      onClick={() => setPaymentMethod(pm)}
                      className={`rounded-xl border px-3 py-2 text-xs font-semibold ${
                        paymentMethod === pm ? "border-slate-900 bg-slate-900 text-white" : "border-slate-200 text-slate-700"
                      }`}
                    >
                      {pm}
                    </button>
                  ))}
                </div>
              </div>

              <label className="flex items-start gap-2.5 rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600">
                <input
                  type="checkbox"
                  checked={lgpdAccepted}
                  onChange={(e) => setLgpdAccepted(e.target.checked)}
                  className="mt-0.5 h-4 w-4 accent-slate-900"
                />
                <span>
                  <strong className="inline-flex items-center gap-1 text-slate-800">
                    <ShieldCheck className="h-3.5 w-3.5 text-emerald-600" /> Privacidade (LGPD):
                  </strong>{" "}
                  Concordo em compartilhar nome, telefone e endereço exclusivamente com {storeTradeName} para processamento e entrega do pedido.
                </span>
              </label>

              {checkoutError && (
                <div className="rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-800">
                  {checkoutError}
                </div>
              )}
            </form>
          )}
        </div>

        {/* Footer Totals */}
        {items.length > 0 && (
          <div className="border-t border-slate-200 bg-slate-50 p-5">
            <div className="space-y-1 text-xs text-slate-600">
              <div className="flex justify-between">
                <span>Subtotal dos itens</span>
                <span className="font-mono font-semibold text-slate-900">{formatBrl(itemsSubtotal)}</span>
              </div>
              {itemsDiscount > 0 && (
                <div className="flex justify-between text-emerald-700">
                  <span>Desconto (Cupom)</span>
                  <span className="font-mono font-semibold">- {formatBrl(itemsDiscount)}</span>
                </div>
              )}
              {step === "checkout" && (
                <div className="flex justify-between">
                  <span>Taxa de entrega</span>
                  <span className="font-mono font-semibold text-slate-900">
                    {quotedDeliveryFee === 0 ? "Grátis" : formatBrl(quotedDeliveryFee)}
                  </span>
                </div>
              )}
              <div className="flex justify-between border-t border-slate-200 pt-2 text-base font-extrabold text-slate-900">
                <span>Total</span>
                <span className="font-mono">{formatBrl(grandTotal)}</span>
              </div>
            </div>

            <div className="mt-4 flex gap-2">
              {step === "checkout" && (
                <button
                  type="button"
                  onClick={() => setStep("cart")}
                  className="rounded-xl border border-slate-300 bg-white px-4 py-3 text-xs font-bold text-slate-700"
                >
                  Voltar
                </button>
              )}

              {step === "cart" ? (
                <button
                  key="btn-to-checkout"
                  type="button"
                  onClick={() => {
                    setStep("checkout");
                    void handleFreightCheck(fulfillmentMode);
                  }}
                  style={{ backgroundColor: "var(--store-primary)" }}
                  className="flex flex-1 items-center justify-center gap-2 rounded-xl py-3 text-sm font-bold text-white shadow"
                >
                  <CheckCircle2 className="h-4 w-4" /> Continuar para Entrega
                </button>
              ) : (
                <button
                  key="btn-submit-checkout"
                  type="submit"
                  form="pitako-checkout-form"
                  disabled={busy}
                  style={{ backgroundColor: "var(--store-primary)" }}
                  className="flex flex-1 items-center justify-center gap-2 rounded-xl py-3 text-sm font-bold text-white shadow disabled:opacity-50"
                >
                  <Clock className="h-4 w-4" /> {busy ? "Enviando..." : "Enviar Pedido Agora"}
                </button>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
