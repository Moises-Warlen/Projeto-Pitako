"use client";

import {
  Award,
  Clock,
  Flame,
  Layers,
  MapPin,
  Percent,
  Phone,
  Search,
  ShoppingBag,
  Sparkles,
  Tag
} from "lucide-react";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { CartAndCheckoutDrawer } from "@/components/storefront/CartAndCheckoutDrawer";
import {
  ModifierSelectionPayload,
  ProductCustomizationModal
} from "@/components/storefront/ProductCustomizationModal";
import { formatBrl, ProductMediaFrame } from "@/components/storefront/ProductMediaFrame";
import { CustomerChatDrawer } from "@/components/storefront/CustomerChatDrawer";
import { OperatingHoursModal, OrderTrackingModal } from "@/components/storefront/StorefrontModals";
import { apiFetch } from "@/lib/api";
import { playCustomerOrderUpdateSound, primeAlertAudio } from "@/lib/soundAlerts";
import {
  MenuCombo,
  MenuProduct,
  OrderTrackingResponse,
  PublicMenuResponse,
  SmartCartItem,
  SmartCartResponse,
  StoreBannerSnapshot,
  StoreOperatingStatus
} from "@/types/menu";

interface PublicStoreInfo {
  tradeName: string;
  segmentCode: string;
  address?: string | null;
  contactPhone?: string | null;
  contactEmail?: string | null;
  operatingStatus?: StoreOperatingStatus | null;
  branding: {
    logoUrl?: string | null;
    coverImageUrl?: string | null;
    slogan?: string | null;
    primaryColorHex: string;
    secondaryColorHex: string;
    accentColorHex: string;
    menuLayoutMode: string;
    announcementText?: string | null;
    whatsAppNumber?: string | null;
    instagramHandle?: string | null;
    banners?: StoreBannerSnapshot[];
  };
}

export default function PublicStorefrontPage() {
  const params = useParams<{ slug: string }>();
  const slug = String(params.slug || "");

  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: "Cardápio Digital",
    primaryColorHex: "#1e293b",
    secondaryColorHex: "#f8fafc",
    accentColorHex: "#0f766e"
  });
  const [storeInfo, setStoreInfo] = useState<PublicStoreInfo | null>(null);
  const [menu, setMenu] = useState<PublicMenuResponse | null>(null);
  const [cart, setCart] = useState<SmartCartResponse | null>(null);
  const [sessionKey, setSessionKey] = useState<string>("");

  const [searchTerm, setSearchTerm] = useState("");
  const [activeFilter, setActiveFilter] = useState<string>("all");
  const [modalProduct, setModalProduct] = useState<MenuProduct | null>(null);
  const [editingCartItem, setEditingCartItem] = useState<SmartCartItem | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [hoursModalOpen, setHoursModalOpen] = useState(false);
  const [trackingOpen, setTrackingOpen] = useState(false);
  const [trackedOrder, setTrackedOrder] = useState<OrderTrackingResponse | null>(null);
  const [toastMsg, setToastMsg] = useState<string | null>(null);

  // Estado para montador clássico de açaí (compatibilidade garantida)
  const [selectedCupId, setSelectedCupId] = useState<string>("");
  const [selectedFlavorId, setSelectedFlavorId] = useState<string>("");
  const [selectedFreeIds, setSelectedFreeIds] = useState<string[]>([]);
  const [selectedPaidIds, setSelectedPaidIds] = useState<string[]>([]);

  useEffect(() => {
    const storageKey = `pitako_storefront_sess_${slug}`;
    let sKey = typeof window !== "undefined" ? window.localStorage.getItem(storageKey) || "" : "";
    if (!sKey) {
      sKey = `web-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
      if (typeof window !== "undefined") window.localStorage.setItem(storageKey, sKey);
    }
    setSessionKey(sKey);

    async function load() {
      const [storeRes, menuRes, cartRes] = await Promise.all([
        apiFetch<PublicStoreInfo>(`/api/v1/public/store/${slug}/`),
        apiFetch<PublicMenuResponse>(`/api/v1/public/store/${slug}/menu`),
        apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sKey}`)
      ]);

      if (storeRes.ok && storeRes.data) {
        setStoreInfo(storeRes.data);
        if (typeof document !== "undefined") {
          document.title = `${storeRes.data.tradeName} — Cardápio Digital`;
        }
        setTheme({
          tradeName: storeRes.data.tradeName,
          logoUrl: storeRes.data.branding.logoUrl,
          primaryColorHex: storeRes.data.branding.primaryColorHex,
          secondaryColorHex: storeRes.data.branding.secondaryColorHex,
          accentColorHex: storeRes.data.branding.accentColorHex
        });
      }

      if (menuRes.ok && menuRes.data) {
        setMenu(menuRes.data);
        if (menuRes.data.acai.cupSizes[0]) setSelectedCupId(menuRes.data.acai.cupSizes[0].id);
        if (menuRes.data.acai.baseFlavors[0]) setSelectedFlavorId(menuRes.data.acai.baseFlavors[0].id);
      }

      if (cartRes.ok && cartRes.data) {
        setCart(cartRes.data);
      }
    }

    if (slug) void load();
  }, [slug]);

  const showToast = useCallback((msg: string) => {
    setToastMsg(msg);
    setTimeout(() => setToastMsg((prev) => (prev === msg ? null : prev)), 2500);
  }, []);

  useEffect(() => {
    primeAlertAudio();
  }, []);

  useEffect(() => {
    if (!slug || typeof window === "undefined") return;
    if (!trackedOrder) {
      const savedOrderNum = window.localStorage.getItem(`pitako_last_order_${slug}`);
      const savedPhone = window.localStorage.getItem(`pitako_last_order_phone_${slug}`) || "";
      if (savedOrderNum) {
        void apiFetch<OrderTrackingResponse>(
          `/api/v1/public/store/${slug}/orders/${encodeURIComponent(savedOrderNum)}?phone=${encodeURIComponent(savedPhone)}`
        ).then((res) => {
          if (res.ok && res.data && res.data.statusCode < 6) {
            setTrackedOrder(res.data);
          }
        });
      }
      return;
    }

    if (trackedOrder.statusCode >= 6) return;

    const timer = setInterval(async () => {
      const savedPhone = window.localStorage.getItem(`pitako_last_order_phone_${slug}`) || "";
      const res = await apiFetch<OrderTrackingResponse>(
        `/api/v1/public/store/${slug}/orders/${encodeURIComponent(trackedOrder.orderNumber)}?phone=${encodeURIComponent(savedPhone)}`
      );
      if (!res.ok || !res.data) return;
      const latest = res.data;
      if (
        latest.statusCode !== trackedOrder.statusCode ||
        latest.isPaymentConfirmed !== trackedOrder.isPaymentConfirmed
      ) {
        playCustomerOrderUpdateSound();
        const ptStatus =
          latest.statusCode === 2
            ? "Pedido Aceito"
            : latest.statusCode === 3
              ? "Em Preparo"
              : latest.statusCode === 4
                ? "Pronto"
                : latest.statusCode === 5
                  ? "Saiu para Entrega"
                  : latest.statusCode === 6
                    ? "Concluído"
                    : latest.statusCode === 7
                      ? "Cancelado"
                      : latest.status;
        const alertText =
          latest.statusCode !== trackedOrder.statusCode
            ? `Atualização do pedido ${latest.orderNumber}: ${ptStatus}!`
            : `Pagamento do pedido ${latest.orderNumber} confirmado!`;
        showToast(alertText);
        if (typeof Notification !== "undefined" && Notification.permission === "granted") {
          new Notification(storeInfo?.tradeName || "Atualização do Pedido", { body: alertText });
        }
      }
      setTrackedOrder(latest);
    }, 4000);

    return () => clearInterval(timer);
  }, [slug, trackedOrder, storeInfo?.tradeName, showToast]);

  const query = searchTerm.trim().toLowerCase();

  const filteredProducts = useMemo(() => {
    if (!menu) return [];
    return menu.products.filter((p) => {
      if (query) {
        const match =
          p.name.toLowerCase().includes(query) ||
          (p.description || "").toLowerCase().includes(query) ||
          p.tags.some((t) => t.toLowerCase().includes(query));
        if (!match) return false;
      }
      if (activeFilter === "best") return menu.bestSellerIds.includes(p.id) || p.isFeatured;
      if (activeFilter === "new") return p.isNew;
      if (activeFilter === "promo") return p.discountAmount > 0;
      if (activeFilter !== "all" && activeFilter !== "combos") return p.categoryId === activeFilter;
      return true;
    });
  }, [menu, query, activeFilter]);

  const bestSellers = useMemo(() => {
    if (!menu) return [];
    const set = new Set(menu.bestSellerIds);
    return menu.products.filter((p) => set.has(p.id) || p.isFeatured).slice(0, 6);
  }, [menu]);

  const newLaunches = useMemo(() => (menu ? menu.products.filter((p) => p.isNew) : []), [menu]);
  const promoProducts = useMemo(() => (menu ? menu.products.filter((p) => p.discountAmount > 0) : []), [menu]);

  const opStatus = storeInfo?.operatingStatus ?? menu?.operatingStatus;
  const activeCup = menu?.acai.cupSizes.find((c) => c.id === selectedCupId);
  const cartItemsCount = cart?.items.reduce((acc, i) => acc + i.quantity, 0) ?? 0;

  async function handleConfirmCustomization(payload: {
    productId: string;
    variationId?: string;
    modifiers: ModifierSelectionPayload[];
    quantity: number;
    notes?: string;
    editingCartItemId?: string;
  }) {
    if (!sessionKey) return;
    if (payload.editingCartItemId) {
      const res = await apiFetch<SmartCartResponse>(
        `/api/v1/public/store/${slug}/cart/${sessionKey}/items/${payload.editingCartItemId}`,
        {
          method: "PUT",
          body: JSON.stringify({
            quantity: payload.quantity,
            variationId: payload.variationId || null,
            modifiers: payload.modifiers,
            customerNotes: payload.notes ?? ""
          })
        }
      );
      if (!res.ok || !res.data) throw new Error(res.errorMessage || "Falha ao atualizar item.");
      setCart(res.data);
      setModalProduct(null);
      setEditingCartItem(null);
      setDrawerOpen(true);
      showToast("Item atualizado na sacola.");
      return;
    }

    const res = await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}/items`, {
      method: "POST",
      body: JSON.stringify({
        kind: "catalog",
        productId: payload.productId,
        variationId: payload.variationId || null,
        modifiers: payload.modifiers,
        quantity: payload.quantity,
        notes: payload.notes
      })
    });
    if (!res.ok || !res.data) throw new Error(res.errorMessage || "Não foi possível adicionar este item.");
    setCart(res.data);
    setModalProduct(null);
    showToast("Produto adicionado à sacola!");
  }

  async function handleQuickAddCombo(combo: MenuCombo) {
    if (!sessionKey) return;
    const res = await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}/items`, {
      method: "POST",
      body: JSON.stringify({ kind: "combo", comboId: combo.id, quantity: 1 })
    });
    if (res.ok && res.data) {
      setCart(res.data);
      showToast(`${combo.name} adicionado à sacola!`);
    } else {
      showToast(res.errorMessage || "Erro ao adicionar combo.");
    }
  }

  async function handleAddClassicAcai() {
    if (!sessionKey || !activeCup || !selectedFlavorId) return;
    const allowFree = activeCup.allowFreeToppings !== false && activeCup.freeToppingsLimit > 0;
    const allowPaid = activeCup.allowPaidAddons !== false;
    const res = await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}/items`, {
      method: "POST",
      body: JSON.stringify({
        kind: "acai",
        acaiCup: {
          cupSizeId: activeCup.id,
          baseFlavorIds: [selectedFlavorId],
          freeToppings: allowFree ? selectedFreeIds.map((id) => ({ itemId: id, quantity: 1 })) : [],
          paidAddons: allowPaid ? selectedPaidIds.map((id) => ({ itemId: id, quantity: 1 })) : []
        },
        quantity: 1
      })
    });
    if (res.ok && res.data) {
      setCart(res.data);
      setSelectedFreeIds([]);
      setSelectedPaidIds([]);
      showToast(`Copo ${activeCup.name} adicionado à sacola!`);
    } else {
      showToast(res.errorMessage || "Erro ao montar açaí.");
    }
  }

  function handleOpenEditCartItem(item: SmartCartItem) {
    if (!menu || !item.productId) return;
    const prod = menu.products.find((p) => p.id === item.productId);
    if (!prod) return;
    setEditingCartItem(item);
    setModalProduct(prod);
    setDrawerOpen(false);
  }

  async function handleUpdateCartQty(itemId: string, nextQty: number) {
    if (!sessionKey) return;
    const res =
      nextQty <= 0
        ? await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}/items/${itemId}`, { method: "DELETE" })
        : await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}/items/${itemId}`, {
            method: "PUT",
            body: JSON.stringify({ quantity: nextQty })
          });
    if (res.ok && res.data) setCart(res.data);
  }

  async function handleRemoveCartItem(itemId: string) {
    if (!sessionKey) return;
    const res = await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}/items/${itemId}`, { method: "DELETE" });
    if (res.ok && res.data) setCart(res.data);
  }

  async function handleClearCart() {
    if (!sessionKey) return;
    const res = await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}`, { method: "DELETE" });
    if (res.ok && res.data) setCart(res.data);
  }

  async function handleApplyCoupon(code: string, customerPhone?: string, customerName?: string) {
    if (!sessionKey) return "Sessão indisponível.";
    const savedPhone = typeof window !== "undefined" ? window.localStorage.getItem(`pitako_last_order_phone_${slug}`) || undefined : undefined;
    const res = await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}/coupon`, {
      method: "POST",
      body: JSON.stringify({
        couponCode: code,
        customerPhone: customerPhone || savedPhone,
        customerName
      })
    });
    if (res.ok && res.data) {
      setCart(res.data);
      return null;
    }
    return res.errorMessage || "Cupom inválido ou não elegível.";
  }

  async function handleRemoveCoupon() {
    if (!sessionKey) return;
    const res = await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}/coupon`, { method: "DELETE" });
    if (res.ok && res.data) setCart(res.data);
  }

  async function handleQuoteFreight(payload: { fulfillmentMode: 1 | 2; neighborhood?: string; zipCode?: string }) {
    const res = await apiFetch<{ finalDeliveryFee: number; estimatedMinutes: number; appliedRuleSummary: string }>(
      `/api/v1/public/store/${slug}/delivery/quote`,
      {
        method: "POST",
        body: JSON.stringify({
          fulfillmentMode: payload.fulfillmentMode,
          orderSubtotal: Math.max(0, (cart?.itemsSubtotal ?? 0) - (cart?.itemsDiscount ?? 0)),
          neighborhood: payload.neighborhood,
          zipCode: payload.zipCode
        })
      }
    );
    if (res.ok && res.data) {
      return {
        fee: res.data.finalDeliveryFee,
        summary: res.data.appliedRuleSummary,
        estimatedMinutes: res.data.estimatedMinutes
      };
    }
    return { fee: 0, summary: "Indisponível", estimatedMinutes: 35, error: res.errorMessage || "Região fora da área de entrega." };
  }

  async function handleSubmitCheckout(payload: {
    customerName: string;
    customerPhone: string;
    fulfillmentMode: 1 | 2;
    deliveryAddress?: string;
    neighborhood?: string;
    zipCode?: string;
    paymentMethod: string;
    lgpdConsentAccepted: boolean;
  }) {
    if (!sessionKey) return "Sessão inválida.";
    const idempotencyKey = `web-${sessionKey}-${Date.now()}`;
    const res = await apiFetch<{ orderNumber: string }>(
      `/api/v1/public/store/${slug}/checkout`,
      {
        method: "POST",
        body: JSON.stringify({
          customerName: payload.customerName,
          customerPhone: payload.customerPhone,
          fulfillmentMode: payload.fulfillmentMode,
          deliveryAddress: payload.deliveryAddress,
          neighborhood: payload.neighborhood,
          zipCode: payload.zipCode,
          cartSessionKey: sessionKey,
          idempotencyKey,
          paymentMethod: payload.paymentMethod,
          lgpdConsentAccepted: payload.lgpdConsentAccepted,
          clearCartAfterCheckout: true
        })
      }
    );

    if (!res.ok || !res.data) return res.errorMessage || "Não foi possível enviar o pedido.";

    const freshCart = await apiFetch<SmartCartResponse>(`/api/v1/public/store/${slug}/cart/${sessionKey}`);
    if (freshCart.ok && freshCart.data) setCart(freshCart.data);

    const track = await apiFetch<OrderTrackingResponse>(
      `/api/v1/public/store/${slug}/orders/${encodeURIComponent(res.data.orderNumber)}?phone=${encodeURIComponent(payload.customerPhone)}`
    );
    if (track.ok && track.data) setTrackedOrder(track.data);
    if (typeof window !== "undefined") {
      window.localStorage.setItem(`pitako_last_order_${slug}`, res.data.orderNumber);
      window.localStorage.setItem(`pitako_last_order_phone_${slug}`, payload.customerPhone);
      if (typeof Notification !== "undefined" && Notification.permission === "default") {
        void Notification.requestPermission().catch(() => {});
      }
    }
    setDrawerOpen(false);
    setTrackingOpen(true);
    return null;
  }

  async function handleLookupOrder(orderNumber: string, phone: string) {
    const res = await apiFetch<OrderTrackingResponse>(
      `/api/v1/public/store/${slug}/orders/${encodeURIComponent(orderNumber)}?phone=${encodeURIComponent(phone)}`
    );
    if (res.ok && res.data) {
      setTrackedOrder(res.data);
      return null;
    }
    return res.errorMessage || "Pedido não encontrado.";
  }

  async function handleVerifyPix(orderNumber: string): Promise<string | null> {
    const verifyRes = await apiFetch<{ isPaymentConfirmed?: boolean; message?: string }>(
      `/api/v1/public/store/${slug}/orders/${encodeURIComponent(orderNumber)}/verify-pix`,
      { method: "POST" }
    );
    if (!verifyRes.ok) {
      return verifyRes.errorMessage || "Não foi possível consultar o PIX no momento.";
    }
    const refreshRes = await apiFetch<OrderTrackingResponse>(
      `/api/v1/public/store/${slug}/orders/${encodeURIComponent(orderNumber)}`
    );
    if (refreshRes.ok && refreshRes.data) {
      setTrackedOrder(refreshRes.data);
    }
    return verifyRes.data?.message || (verifyRes.data?.isPaymentConfirmed ? "Pagamento PIX confirmado!" : "Aguardando compensação bancária.");
  }

  function renderProductCard(product: MenuProduct) {
    const hasCustomFlow = product.variations.length > 0 || product.modifierGroupIds.length > 0;
    const lowestVarPrice = product.variations.length > 0 ? Math.min(...product.variations.map((v) => v.price)) : product.effectivePrice;

    return (
      <button
        key={product.id}
        type="button"
        onClick={() => {
          setEditingCartItem(null);
          setModalProduct(product);
        }}
        className="group flex w-full items-stretch justify-between gap-3.5 rounded-2xl border border-slate-200 bg-white p-3.5 text-left shadow-sm transition hover:border-slate-300 hover:shadow-md"
      >
        <div className="flex min-w-0 flex-1 flex-col justify-between">
          <div>
            <div className="flex flex-wrap items-center gap-1.5">
              {product.isFeatured && (
                <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">
                  <Flame className="h-3 w-3" /> Popular
                </span>
              )}
              {product.isNew && (
                <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-bold text-emerald-800">
                  <Sparkles className="h-3 w-3" /> Novo
                </span>
              )}
              {product.discountAmount > 0 && (
                <span className="inline-flex items-center gap-1 rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-bold text-rose-800">
                  <Percent className="h-3 w-3" /> Promoção
                </span>
              )}
              {product.isRecommended && !product.isFeatured && (
                <span className="inline-flex items-center gap-1 rounded-full bg-blue-100 px-2 py-0.5 text-[10px] font-bold text-blue-800">
                  <Award className="h-3 w-3" /> Sugestão
                </span>
              )}
            </div>

            <h3 className="mt-1.5 line-clamp-1 text-sm font-extrabold text-slate-900 sm:text-base">{product.name}</h3>
            {product.description && <p className="mt-1 line-clamp-2 text-xs leading-relaxed text-slate-500">{product.description}</p>}
          </div>

          <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
            <div>
              {product.discountAmount > 0 && (
                <span className="mr-2 text-xs text-slate-400 line-through">{formatBrl(product.price)}</span>
              )}
              <span className="font-mono text-sm font-extrabold text-slate-900 sm:text-base">
                {hasCustomFlow ? `A partir de ${formatBrl(lowestVarPrice)}` : formatBrl(product.effectivePrice)}
              </span>
            </div>
            <span
              style={{ backgroundColor: "var(--store-primary)" }}
              className="rounded-xl px-3 py-1.5 text-xs font-bold text-white transition group-hover:opacity-90"
            >
              {hasCustomFlow ? "Personalizar" : "+ Adicionar"}
            </span>
          </div>
        </div>

        <ProductMediaFrame src={product.primaryImageUrl} alt={product.name} className="h-28 w-28 shrink-0 rounded-xl sm:h-32 sm:w-32" iconSize={30} />
      </button>
    );
  }

  return (
    <StoreBrandShell theme={theme} hideOpsNav>
      {/* Faixa de aviso / anúncio da loja */}
      {storeInfo?.branding.announcementText && (
        <div
          style={{ backgroundColor: "var(--store-accent)" }}
          className="mb-4 rounded-2xl px-4 py-2.5 text-center text-xs font-bold text-white shadow-sm"
        >
          {storeInfo.branding.announcementText}
        </div>
      )}

      {/* Toast flutuante */}
      {toastMsg && (
        <div className="fixed right-4 top-20 z-40 rounded-2xl bg-slate-900 px-4 py-3 text-xs font-bold text-white shadow-xl">
          {toastMsg}
        </div>
      )}

      {/* Cabeçalho Institucional Whitelabel da Loja */}
      <section className="mb-6 overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-sm">
        <div className="flex flex-col justify-between gap-4 p-5 sm:flex-row sm:items-center sm:p-6">
          <div className="space-y-1.5">
            <div className="flex flex-wrap items-center gap-2">
              {opStatus && (
                <button
                  type="button"
                  onClick={() => setHoursModalOpen(true)}
                  className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-extrabold ${
                    opStatus.isOpenNow ? "bg-emerald-100 text-emerald-800" : "bg-rose-100 text-rose-800"
                  }`}
                >
                  <span className={`h-2 w-2 rounded-full ${opStatus.isOpenNow ? "bg-emerald-500" : "bg-rose-500"}`} />
                  {opStatus.statusLabel}
                  <span className="underline">Ver horários</span>
                </button>
              )}
              {storeInfo?.segmentCode && (
                <span className="rounded-full bg-slate-100 px-2.5 py-1 text-[11px] font-bold uppercase tracking-wide text-slate-600">
                  {storeInfo.segmentCode}
                </span>
              )}
            </div>

            <h1 className="text-xl font-extrabold text-slate-900 sm:text-2xl">{storeInfo?.tradeName || theme.tradeName}</h1>
            <p className="text-xs text-slate-600 sm:text-sm">{storeInfo?.branding.slogan || "Peça online com preparo artesanal e entrega rápida."}</p>

            <div className="flex flex-wrap items-center gap-3 pt-1 text-xs text-slate-500">
              {storeInfo?.address && (
                <span className="inline-flex items-center gap-1">
                  <MapPin className="h-3.5 w-3.5" /> {storeInfo.address}
                </span>
              )}
              {storeInfo?.contactPhone && (
                <span className="inline-flex items-center gap-1">
                  <Phone className="h-3.5 w-3.5" /> {storeInfo.contactPhone}
                </span>
              )}
              {menu?.delivery?.freeShippingThreshold && (
                <span className="inline-flex items-center gap-1 font-semibold text-emerald-700">
                  <Tag className="h-3.5 w-3.5" /> Frete grátis acima de {formatBrl(menu.delivery.freeShippingThreshold)}
                </span>
              )}
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2.5">
            <button
              type="button"
              onClick={() => setTrackingOpen(true)}
              className="inline-flex items-center gap-1.5 rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-bold text-slate-700 hover:bg-slate-50"
            >
              <Clock className="h-4 w-4" /> Acompanhar Pedido
            </button>
            <button
              type="button"
              onClick={() => setDrawerOpen(true)}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-xs font-bold text-white shadow-sm"
            >
              <ShoppingBag className="h-4 w-4" />
              <span>Sacola ({cartItemsCount})</span>
              <span className="font-mono">· {formatBrl(cart?.totalAmount ?? 0)}</span>
            </button>
          </div>
        </div>

        {/* Banners Promocionais */}
        {storeInfo?.branding.banners && storeInfo.branding.banners.length > 0 && (
          <div className="grid gap-3 border-t border-slate-100 bg-slate-50/60 p-4 sm:grid-cols-2 lg:grid-cols-3">
            {storeInfo.branding.banners.map((b, idx) => (
              <div key={idx} className="flex items-center gap-3 rounded-2xl border border-slate-200 bg-white p-3">
                <ProductMediaFrame src={b.imageUrl} alt={b.title} className="h-16 w-16 shrink-0 rounded-xl" iconSize={20} />
                <div className="min-w-0">
                  <p className="truncate text-sm font-extrabold text-slate-900">{b.title}</p>
                  {b.subtitle && <p className="line-clamp-2 text-xs text-slate-500">{b.subtitle}</p>}
                </div>
              </div>
            ))}
          </div>
        )}
      </section>

      {/* Busca e Navegação por Categorias (Chips) */}
      <div className="sticky top-0 z-20 -mx-2 mb-6 rounded-2xl border border-slate-200/80 bg-white/95 p-3 shadow-sm backdrop-blur">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="relative flex-1">
            <Search className="absolute left-3.5 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              placeholder="Buscar item, sabor, ingrediente..."
              className="w-full rounded-xl border border-slate-200 bg-slate-50 py-2 pl-9 pr-3 text-sm focus:border-slate-400 focus:bg-white focus:outline-none"
            />
          </div>

          <div className="flex gap-1.5 overflow-x-auto pb-1 sm:pb-0">
            {[
              { id: "all", label: "Todos" },
              { id: "best", label: "Mais Vendidos" },
              ...(newLaunches.length > 0 ? [{ id: "new", label: "Novidades" }] : []),
              ...(promoProducts.length > 0 ? [{ id: "promo", label: "Promoções" }] : []),
              ...(menu && menu.combos.length > 0 ? [{ id: "combos", label: "Combos" }] : []),
              ...(menu?.categories.map((c) => ({ id: c.id, label: c.name })) ?? [])
            ].map((chip) => {
              const active = activeFilter === chip.id;
              return (
                <button
                  key={chip.id}
                  type="button"
                  onClick={() => setActiveFilter(chip.id)}
                  style={active ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
                  className={`shrink-0 rounded-full border px-3.5 py-1.5 text-xs font-bold transition ${
                    active ? "border-transparent shadow-sm" : "border-slate-200 bg-white text-slate-700 hover:bg-slate-50"
                  }`}
                >
                  {chip.label}
                </button>
              );
            })}
          </div>
        </div>
      </div>

      {/* Seção Combos Especiais */}
      {menu && menu.combos.length > 0 && (activeFilter === "all" || activeFilter === "combos") && !query && (
        <section className="mb-8">
          <div className="mb-3 flex items-center gap-2">
            <Layers className="h-5 w-5 text-amber-600" />
            <h2 className="text-lg font-extrabold text-slate-900">Combos Especiais</h2>
          </div>
          <div className="grid gap-3.5 sm:grid-cols-2 lg:grid-cols-3">
            {menu.combos.map((combo) => (
              <div key={combo.id} className="flex flex-col justify-between rounded-2xl border border-amber-200 bg-gradient-to-br from-amber-50/60 to-white p-4 shadow-sm">
                <div>
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold uppercase text-amber-800">Combo</span>
                      <h3 className="mt-1 text-base font-extrabold text-slate-900">{combo.name}</h3>
                    </div>
                    <ProductMediaFrame src={combo.primaryImageUrl} alt={combo.name} className="h-14 w-14 shrink-0 rounded-xl" iconSize={20} />
                  </div>
                  {combo.description && <p className="mt-1 text-xs text-slate-600">{combo.description}</p>}
                  <ul className="mt-2 space-y-0.5 text-xs text-slate-500">
                    {combo.items.map((it, idx) => (
                      <li key={idx}>
                        • {it.quantity}x {it.productName}
                      </li>
                    ))}
                  </ul>
                </div>

                <div className="mt-4 flex items-center justify-between border-t border-amber-100 pt-3">
                  <div>
                    {combo.originalPrice > combo.comboPrice && (
                      <span className="mr-2 text-xs text-slate-400 line-through">{formatBrl(combo.originalPrice)}</span>
                    )}
                    <span className="font-mono text-base font-extrabold text-slate-900">{formatBrl(combo.comboPrice)}</span>
                  </div>
                  <button
                    type="button"
                    onClick={() => handleQuickAddCombo(combo)}
                    style={{ backgroundColor: "var(--store-primary)" }}
                    className="rounded-xl px-3.5 py-2 text-xs font-bold text-white"
                  >
                    + Pedir Combo
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>
      )}

      {/* Seção Destaques (Mais Vendidos) */}
      {bestSellers.length > 0 && activeFilter === "all" && !query && (
        <section className="mb-8">
          <div className="mb-3 flex items-center gap-2">
            <Flame className="h-5 w-5 text-amber-500" />
            <h2 className="text-lg font-extrabold text-slate-900">Mais Vendidos</h2>
          </div>
          <div className="grid gap-3.5 md:grid-cols-2">{bestSellers.map(renderProductCard)}</div>
        </section>
      )}

      {/* Montador Clássico de Açaí (quando configurado no módulo Açaí) */}
      {menu && activeCup && activeFilter === "all" && !query && (
        <section className="mb-8 rounded-3xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h2 className="text-lg font-extrabold text-slate-900">Monte seu Açaí do Zero</h2>
              <p className="text-xs text-slate-500">
                {activeCup.description || "Escolha o tamanho do copo, seu creme base e monte com seus acompanhamentos favoritos."}
              </p>
            </div>
            {activeCup.allowFreeToppings !== false && activeCup.freeToppingsLimit > 0 ? (
              <span className="rounded-full bg-emerald-100 px-3 py-1 text-xs font-bold text-emerald-800">
                {selectedFreeIds.length}/{activeCup.freeToppingsLimit} acompanhamentos grátis usados
              </span>
            ) : (
              <span className="rounded-full bg-slate-100 px-3 py-1 text-xs font-bold text-slate-700">
                {activeCup.allowPaidAddons !== false ? "Adicionais pagos opcionais" : "Copo puro sem adicionais"}
              </span>
            )}
          </div>

          <div className="mt-4 space-y-5">
            <div>
              <p className="mb-2 text-xs font-bold uppercase text-slate-500">1. Escolha o Tamanho</p>
              <div className="flex flex-wrap gap-2">
                {menu.acai.cupSizes.map((c) => {
                  const hasFree = c.allowFreeToppings !== false && c.freeToppingsLimit > 0;
                  const hasPaid = c.allowPaidAddons !== false;
                  return (
                    <button
                      key={c.id}
                      type="button"
                      onClick={() => {
                        setSelectedCupId(c.id);
                        setSelectedFreeIds([]);
                        if (c.allowPaidAddons === false) setSelectedPaidIds([]);
                      }}
                      style={selectedCupId === c.id ? { borderColor: "var(--store-primary)", backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
                      className={`rounded-xl border px-3.5 py-2 text-left text-xs font-bold transition ${
                        selectedCupId === c.id ? "border-transparent" : "border-slate-200 text-slate-700 hover:bg-slate-50"
                      }`}
                    >
                      <span>
                        {c.name} · {formatBrl(c.basePrice)}
                        {hasFree ? ` (até ${c.freeToppingsLimit} grátis)` : hasPaid ? " (só pagos)" : " (puro)"}
                      </span>
                      {c.description && (
                        <span className={`block text-[10px] font-normal ${selectedCupId === c.id ? "text-white/85" : "text-slate-500"}`}>
                          {c.description}
                        </span>
                      )}
                    </button>
                  );
                })}
              </div>
            </div>

            {menu.acai.baseFlavors.length > 0 && (
              <div>
                <p className="mb-2 text-xs font-bold uppercase text-slate-500">2. Creme / Sabor Base</p>
                <div className="flex flex-wrap gap-2">
                  {menu.acai.baseFlavors.map((f) => (
                    <button
                      key={f.id}
                      type="button"
                      onClick={() => setSelectedFlavorId(f.id)}
                      className={`rounded-xl border px-3 py-1.5 text-xs font-semibold ${
                        selectedFlavorId === f.id ? "border-slate-900 bg-slate-900 text-white" : "border-slate-200 text-slate-700"
                      }`}
                    >
                      {f.name} {f.extraPrice > 0 ? `(+${formatBrl(f.extraPrice)})` : "(Incluso)"}
                    </button>
                  ))}
                </div>
              </div>
            )}

            {activeCup.allowFreeToppings !== false && activeCup.freeToppingsLimit > 0 && menu.acai.freeToppings.length > 0 && (
              <div className="space-y-2.5">
                <p className="text-xs font-bold uppercase text-slate-500">
                  3. Acompanhamentos Gratuitos ({selectedFreeIds.length}/{activeCup.freeToppingsLimit})
                </p>
                {Array.from(
                  menu.acai.freeToppings.reduce((map, item) => {
                    const cat = item.categoryName?.trim() || "Acompanhamentos";
                    const arr = map.get(cat) ?? [];
                    arr.push(item);
                    map.set(cat, arr);
                    return map;
                  }, new Map<string, typeof menu.acai.freeToppings>())
                ).map(([catLabel, items]) => (
                  <div key={catLabel} className="space-y-1.5">
                    <p className="text-[11px] font-bold text-slate-600">{catLabel}</p>
                    <div className="flex flex-wrap gap-2">
                      {items.map((t) => {
                        const chosen = selectedFreeIds.includes(t.id);
                        return (
                          <button
                            key={t.id}
                            type="button"
                            onClick={() =>
                              setSelectedFreeIds((prev) => {
                                if (prev.includes(t.id)) return prev.filter((x) => x !== t.id);
                                if (prev.length >= activeCup.freeToppingsLimit) return prev;
                                return [...prev, t.id];
                              })
                            }
                            className={`rounded-xl border px-3 py-1.5 text-xs font-semibold ${
                              chosen ? "border-emerald-600 bg-emerald-50 text-emerald-900" : "border-slate-200 text-slate-700"
                            }`}
                          >
                            {t.name} (Grátis)
                          </button>
                        );
                      })}
                    </div>
                  </div>
                ))}
              </div>
            )}

            {activeCup.allowPaidAddons !== false && menu.acai.paidAddons.length > 0 && (
              <div className="space-y-2.5">
                <p className="text-xs font-bold uppercase text-amber-700">4. Adicionais Pagos</p>
                {Array.from(
                  menu.acai.paidAddons.reduce((map, item) => {
                    const cat = item.categoryName?.trim() || "Adicionais";
                    const arr = map.get(cat) ?? [];
                    arr.push(item);
                    map.set(cat, arr);
                    return map;
                  }, new Map<string, typeof menu.acai.paidAddons>())
                ).map(([catLabel, items]) => (
                  <div key={catLabel} className="space-y-1.5">
                    <p className="text-[11px] font-bold text-amber-800">{catLabel}</p>
                    <div className="flex flex-wrap gap-2">
                      {items.map((a) => {
                        const chosen = selectedPaidIds.includes(a.id);
                        return (
                          <button
                            key={a.id}
                            type="button"
                            onClick={() => setSelectedPaidIds((prev) => (prev.includes(a.id) ? prev.filter((x) => x !== a.id) : [...prev, a.id]))}
                            className={`rounded-xl border px-3 py-1.5 text-xs font-semibold ${
                              chosen ? "border-amber-600 bg-amber-50 text-amber-900" : "border-slate-200 text-slate-700"
                            }`}
                          >
                            {a.name} (+{formatBrl(a.price)})
                          </button>
                        );
                      })}
                    </div>
                  </div>
                ))}
              </div>
            )}

            <button
              type="button"
              onClick={handleAddClassicAcai}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="rounded-xl px-5 py-2.5 text-xs font-bold text-white shadow"
            >
              Adicionar Copo à Sacola
            </button>
          </div>
        </section>
      )}

      {/* Cardápio por Categorias */}
      {menu && activeFilter !== "combos" && (
        <div className="space-y-8 pb-24">
          {filteredProducts.length === 0 ? (
            <div className="rounded-3xl border border-slate-200 bg-white p-8 text-center text-sm text-slate-500">
              Nenhum produto encontrado para este filtro ou busca.
            </div>
          ) : activeFilter === "all" && !query ? (
            [...menu.categories]
              .sort((a, b) => a.displayOrder - b.displayOrder)
              .map((cat) => {
              const catProds = [...filteredProducts]
                .filter((p) => p.categoryId === cat.id)
                .sort((a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name));
              if (catProds.length === 0) return null;
              return (
                <section key={cat.id}>
                  <div className="mb-3">
                    <h2 className="text-lg font-extrabold text-slate-900">{cat.name}</h2>
                    {cat.description && <p className="text-xs text-slate-500">{cat.description}</p>}
                  </div>
                  <div className="grid gap-3.5 md:grid-cols-2">{catProds.map(renderProductCard)}</div>
                </section>
              );
            })
          ) : (
            <section>
              <h2 className="mb-3 text-lg font-extrabold text-slate-900">Resultados ({filteredProducts.length})</h2>
              <div className="grid gap-3.5 md:grid-cols-2">{filteredProducts.map(renderProductCard)}</div>
            </section>
          )}
        </div>
      )}

      {/* Barra Flutuante Inferior Mobile */}
      {cartItemsCount > 0 && (
        <div className="fixed inset-x-0 bottom-0 z-30 border-t border-slate-200 bg-white/95 p-3 backdrop-blur sm:hidden">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            style={{ backgroundColor: "var(--store-primary)" }}
            className="flex w-full items-center justify-between rounded-2xl px-5 py-3.5 text-sm font-bold text-white shadow-lg"
          >
            <span className="flex items-center gap-2">
              <ShoppingBag className="h-4 w-4" /> Ver Sacola ({cartItemsCount})
            </span>
            <span className="font-mono">{formatBrl(cart?.totalAmount ?? 0)}</span>
          </button>
        </div>
      )}

      {/* Modais Universais */}
      <ProductCustomizationModal
        product={modalProduct}
        allModifierGroups={menu?.modifierGroups ?? []}
        editingItem={editingCartItem}
        onClose={() => {
          setModalProduct(null);
          setEditingCartItem(null);
        }}
        onConfirm={handleConfirmCustomization}
      />

      <CartAndCheckoutDrawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        cart={cart}
        storeTradeName={storeInfo?.tradeName || theme.tradeName}
        allowDelivery={menu?.delivery ? menu.delivery.isDeliveryEnabled : true}
        allowPickup={menu?.delivery ? menu.delivery.allowPickup : true}
        onEditCartItem={handleOpenEditCartItem}
        onUpdateQty={handleUpdateCartQty}
        onRemoveItem={handleRemoveCartItem}
        onClearCart={handleClearCart}
        onApplyCoupon={handleApplyCoupon}
        onRemoveCoupon={handleRemoveCoupon}
        onQuoteFreight={handleQuoteFreight}
        onSubmitCheckout={handleSubmitCheckout}
      />

      <OperatingHoursModal
        open={hoursModalOpen}
        onClose={() => setHoursModalOpen(false)}
        storeTradeName={storeInfo?.tradeName || theme.tradeName}
        status={opStatus}
      />

      <OrderTrackingModal
        open={trackingOpen}
        onClose={() => setTrackingOpen(false)}
        initialOrder={trackedOrder}
        onLookupOrder={handleLookupOrder}
        onVerifyPix={handleVerifyPix}
        storeTradeName={storeInfo?.tradeName || theme.tradeName}
        storePhone={storeInfo?.branding.whatsAppNumber || storeInfo?.contactPhone}
      />

      <CustomerChatDrawer slug={slug} storeTradeName={storeInfo?.tradeName || theme.tradeName} />
    </StoreBrandShell>
  );
}
