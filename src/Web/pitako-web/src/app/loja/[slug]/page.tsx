"use client";

import {
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
  const [blockedReason, setBlockedReason] = useState<string | null>(null);
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

      if (storeRes.status === 403 || menuRes.status === 403) {
        setBlockedReason(
          storeRes.errorMessage ||
            menuRes.errorMessage ||
            "Esta loja está temporariamente suspensa pela administração."
        );
        return;
      }

      if (storeRes.ok && storeRes.data) {
        setStoreInfo(storeRes.data);
        if (typeof document !== "undefined") {
          document.title = `${storeRes.data.tradeName} | Cardápio Digital`;
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
  const isAcaiStore = (storeInfo?.segmentCode || "").toLowerCase() === "acaiteria";
  const catalogMonteProduct = useMemo(
    () =>
      menu?.products.find(
        (p) =>
          p.customTemplateKey === "acaiteria-monte" ||
          p.name.toLowerCase().includes("monte do seu jeito")
      ) ?? null,
    [menu]
  );

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
    const activeVars = product.variations.filter((v) => v.isInStock !== false);
    const lowestVarPrice =
      activeVars.length > 0
        ? Math.min(...activeVars.map((v) => v.price))
        : product.variations.length > 0
          ? Math.min(...product.variations.map((v) => v.price))
          : product.effectivePrice;
    const isMonte =
      product.customTemplateKey === "acaiteria-monte" || product.name.toLowerCase().includes("monte do seu jeito");
    const sizeLabels = product.variations
      .map((v) =>
        v.measureAmount && v.measureUnit
          ? `${v.variationName} (${v.measureAmount}${v.measureUnit})`
          : v.variationName
      )
      .filter(Boolean);
    const modifierNames = product.modifierGroupIds
      .map((gid) => menu?.modifierGroups.find((g) => g.id === gid)?.name)
      .filter((n): n is string => Boolean(n));
    const ingredientsList = (product.ingredients || []).filter((x) => Boolean(x && x.trim()));
    const outOfStock = product.isInStock === false || (product.variations.length > 0 && activeVars.length === 0);
    const outsideSchedule = product.isScheduledAvailable === false;
    const isUnavailable = outOfStock || outsideSchedule;
    const promoPct =
      product.discountPercent && product.discountPercent > 0
        ? product.discountPercent
        : product.discountAmount > 0 && product.price > 0
          ? Math.round((product.discountAmount / product.price) * 100)
          : 0;
    const dietaryLabelMap: Record<string, string> = {
      vegano: "🌱 Vegano",
      vegetariano: "🥗 Vegetariano",
      "sem-gluten": "🌾 Sem Glúten",
      "sem-lactose": "🥛 Sem Lactose",
      "alcoolico-18": "🔞 +18",
      artesanal: "✨ Artesanal"
    };

    return (
      <button
        key={product.id}
        type="button"
        disabled={isUnavailable}
        onClick={() => {
          if (isUnavailable) return;
          setEditingCartItem(null);
          setModalProduct(product);
        }}
        className={`group flex w-full items-stretch justify-between gap-3.5 rounded-2xl border border-slate-200 bg-white p-3.5 text-left shadow-sm transition ${
          isUnavailable ? "cursor-not-allowed opacity-60" : "hover:border-slate-300 hover:shadow-md"
        }`}
      >
        <div className="flex min-w-0 flex-1 flex-col justify-between">
          <div>
            <div className="flex flex-wrap items-center gap-1.5">
              {outOfStock && (
                <span className="rounded-full bg-rose-600 px-2 py-0.5 text-[10px] font-extrabold text-white">
                  Esgotado
                </span>
              )}
              {!outOfStock && outsideSchedule && (
                <span className="rounded-full bg-amber-600 px-2 py-0.5 text-[10px] font-extrabold text-white">
                  Disponível em outro horário
                </span>
              )}
              {!outOfStock && product.trackInventory && (product.stockQuantity ?? 0) > 0 && (product.stockQuantity ?? 0) <= 3 && (
                <span className="rounded-full bg-orange-100 px-2 py-0.5 text-[10px] font-extrabold text-orange-800">
                  Últimas {product.stockQuantity} un!
                </span>
              )}
              {product.isFeatured && (
                <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">
                  <Flame className="h-3 w-3" /> Mais Vendido
                </span>
              )}
              {product.isNew && (
                <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-bold text-emerald-800">
                  <Sparkles className="h-3 w-3" /> Lançamento
                </span>
              )}
              {product.discountAmount > 0 && (
                <span className="inline-flex items-center gap-1 rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-extrabold text-rose-800">
                  <Percent className="h-3 w-3" /> {promoPct > 0 ? `-${promoPct}% OFF` : "Promoção"}
                </span>
              )}
              {product.servesPeopleCount && (
                <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-bold text-slate-700">
                  Serve {product.servesPeopleCount} {product.servesPeopleCount === 1 ? "pessoa" : "pessoas"}
                </span>
              )}
              {(product.dietaryTags ?? []).map((dt) => (
                <span key={dt} className="rounded-full bg-lime-100 px-2 py-0.5 text-[10px] font-bold text-lime-800">
                  {dietaryLabelMap[dt] || dt}
                </span>
              ))}
              {sizeLabels.length > 0 && (
                <span className="inline-flex items-center rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold text-slate-700">
                  Opções: {sizeLabels.join(" · ")}
                </span>
              )}
              {product.yieldLabel && (
                <span className="inline-flex items-center rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold text-slate-700">
                  Porção: {product.yieldLabel}
                </span>
              )}
            </div>

            <h3 className="mt-1.5 line-clamp-2 text-sm font-extrabold text-slate-900 sm:text-base">{product.name}</h3>
            {product.description && (
              <p className="mt-1 line-clamp-2 text-xs leading-relaxed text-slate-600">{product.description}</p>
            )}
            {ingredientsList.length > 0 && (
              <p className="mt-1 line-clamp-2 text-[11px] text-slate-500">
                <strong className="font-semibold text-slate-700">Ingredientes:</strong> {ingredientsList.slice(0, 8).join(", ")}
                {ingredientsList.length > 8 ? ` (+${ingredientsList.length - 8})` : ""}
              </p>
            )}
            {modifierNames.length > 0 && (
              <p className="mt-0.5 line-clamp-1 text-[11px] font-semibold text-slate-600">
                Personalização: {modifierNames.join(" · ")}
              </p>
            )}
          </div>

          <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
            <div>
              {product.discountAmount > 0 && (
                <span className="mr-2 text-xs text-slate-400 line-through">{formatBrl(product.price)}</span>
              )}
              <span className="font-mono text-sm font-extrabold text-slate-900 sm:text-base">
                {product.variations.length > 1 ? `A partir de ${formatBrl(lowestVarPrice)}` : formatBrl(lowestVarPrice)}
              </span>
            </div>
            <span
              style={!isUnavailable ? { backgroundColor: "var(--store-primary)" } : undefined}
              className={`rounded-xl px-3.5 py-1.5 text-xs font-extrabold transition ${
                isUnavailable ? "bg-slate-200 text-slate-500" : "text-white group-hover:opacity-90"
              }`}
            >
              {outOfStock ? "Esgotado" : outsideSchedule ? "Indisponível" : isMonte ? "Montar" : hasCustomFlow ? "Pedir" : "+ Adicionar"}
            </span>
          </div>
        </div>

        <ProductMediaFrame
          src={product.primaryImageUrl}
          alt={product.name}
          fit="contain"
          className="h-28 w-28 shrink-0 rounded-2xl sm:h-32 sm:w-32 md:h-36 md:w-36"
          iconSize={32}
        />
      </button>
    );
  }

  if (blockedReason) {
    return (
      <StoreBrandShell theme={theme} hideOpsNav>
        <div className="mx-auto max-w-lg rounded-3xl border border-rose-200 bg-white p-8 text-center shadow-sm">
          <h1 className="text-xl font-extrabold text-slate-900">Cardápio Temporariamente Indisponível</h1>
          <p className="mt-2 text-sm text-slate-600">{blockedReason}</p>
        </div>
      </StoreBrandShell>
    );
  }

  return (
    <StoreBrandShell theme={theme} hideOpsNav hideTopHeader>
      {/* Toast flutuante */}
      {toastMsg && (
        <div className="fixed right-4 top-6 z-40 rounded-2xl bg-slate-900 px-4 py-3 text-xs font-bold text-white shadow-xl">
          {toastMsg}
        </div>
      )}

      {/* Cabeçalho Institucional Whitelabel da Loja */}
      <section className="mb-5 overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-sm">
        <div style={{ backgroundColor: "var(--store-primary)" }} className="h-2 w-full" />
        <div className="flex flex-col gap-5 p-5 lg:flex-row lg:items-center lg:justify-between lg:p-6">
          <div className="flex items-start gap-4 min-w-0">
            {storeInfo?.branding.logoUrl && (
              <ProductMediaFrame
                src={storeInfo.branding.logoUrl}
                alt={storeInfo.tradeName || theme.tradeName}
                className="h-16 w-16 shrink-0 rounded-2xl border border-slate-200 bg-white shadow-xs sm:h-20 sm:w-20"
                iconSize={28}
              />
            )}
            <div className="min-w-0 space-y-2">
              <div className="flex flex-wrap items-center gap-2">
                {opStatus && (
                  <button
                    type="button"
                    onClick={() => setHoursModalOpen(true)}
                    className={`inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full px-3 py-1 text-xs font-extrabold transition hover:opacity-90 ${
                      opStatus.isOpenNow ? "bg-emerald-100 text-emerald-800" : "bg-rose-100 text-rose-800"
                    }`}
                  >
                    <span className={`h-2 w-2 shrink-0 rounded-full ${opStatus.isOpenNow ? "bg-emerald-500" : "bg-rose-500"}`} />
                    <span>{opStatus.statusLabel}</span>
                    <span className="opacity-50">•</span>
                    <span className="underline">Ver horários</span>
                  </button>
                )}
                {storeInfo?.segmentCode && (
                  <span className="shrink-0 whitespace-nowrap rounded-full bg-slate-100 px-2.5 py-1 text-[11px] font-bold uppercase tracking-wide text-slate-600">
                    {storeInfo.segmentCode}
                  </span>
                )}
              </div>

              <div>
                <h1 className="text-xl font-extrabold tracking-tight text-slate-900 sm:text-2xl">
                  {storeInfo?.tradeName || theme.tradeName}
                </h1>
                <p className="mt-0.5 text-xs text-slate-600 sm:text-sm">
                  {storeInfo?.branding.slogan || "Peça online com preparo artesanal e entrega rápida."}
                </p>
              </div>

              <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs text-slate-500">
                {storeInfo?.address && (
                  <span className="inline-flex items-center gap-1.5">
                    <MapPin className="h-3.5 w-3.5 shrink-0 text-slate-400" />
                    <span>{storeInfo.address}</span>
                  </span>
                )}
                {storeInfo?.contactPhone && (
                  <span className="inline-flex items-center gap-1.5">
                    <Phone className="h-3.5 w-3.5 shrink-0 text-slate-400" />
                    <span className="font-mono">{storeInfo.contactPhone}</span>
                  </span>
                )}
                {menu?.delivery?.freeShippingThreshold && (
                  <span className="inline-flex items-center gap-1.5 font-semibold text-emerald-700">
                    <Tag className="h-3.5 w-3.5 shrink-0" />
                    <span>Frete grátis acima de {formatBrl(menu.delivery.freeShippingThreshold)}</span>
                  </span>
                )}
              </div>
            </div>
          </div>

          <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 lg:flex lg:shrink-0 lg:items-center">
            <button
              type="button"
              onClick={() => setTrackingOpen(true)}
              className="inline-flex h-11 w-full items-center justify-center gap-2 whitespace-nowrap rounded-xl border border-slate-300 bg-white px-4 text-xs font-extrabold text-slate-700 shadow-2xs transition hover:bg-slate-50 lg:w-auto"
            >
              <Clock className="h-4 w-4 shrink-0 text-slate-500" />
              <span>Acompanhar Pedido</span>
            </button>
            <button
              type="button"
              onClick={() => setDrawerOpen(true)}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="inline-flex h-11 w-full items-center justify-center gap-2 whitespace-nowrap rounded-xl px-5 text-xs font-extrabold text-white shadow-sm transition hover:opacity-95 lg:w-auto"
            >
              <ShoppingBag className="h-4 w-4 shrink-0" />
              <span>Sacola ({cartItemsCount})</span>
              <span className="font-mono font-bold tabular-nums opacity-95">· {formatBrl(cart?.totalAmount ?? 0)}</span>
            </button>
          </div>
        </div>
      </section>

      {/* Hero Vitrine & Atalhos Rápidos (Organização inspirada em vitrines líderes de açaí) */}
      {activeFilter === "all" && !query && (
        <section className="mb-6 space-y-4">
          <div
            style={{
              background: "linear-gradient(135deg, var(--store-primary) 0%, color-mix(in srgb, var(--store-primary) 55%, #0f172a) 100%)"
            }}
            className="overflow-hidden rounded-3xl p-6 text-white shadow-md sm:p-8"
          >
            <div className="flex flex-col justify-between gap-6 md:flex-row md:items-center">
              <div className="max-w-2xl space-y-3">
                <span className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3 py-1 text-[11px] font-bold uppercase tracking-wider text-white/95">
                  <Sparkles className="h-3.5 w-3.5" /> {isAcaiStore ? "Cardápio Digital & Delivery" : "Peça Online Direto com a Loja"}
                </span>
                <h2 className="text-2xl font-black leading-tight sm:text-3xl">
                  {isAcaiStore
                    ? "Uma explosão de sabor em cada colherada!"
                    : `Sabores artesanais em ${storeInfo?.tradeName || theme.tradeName}`}
                </h2>
                <p className="text-sm text-white/85">
                  {storeInfo?.branding.slogan ||
                    "Monte do seu jeito, escolha os acompanhamentos inclusos e receba super gelado no conforto da sua casa."}
                </p>
                <div className="flex flex-wrap gap-2 pt-1 text-xs font-bold">
                  <span className="rounded-full bg-white/15 px-3 py-1">{isAcaiStore ? "Açaí Cremoso" : "Preparo na Hora"}</span>
                  <span className="rounded-full bg-white/15 px-3 py-1">{isAcaiStore ? "Frutas Frescas Selecionadas" : "Ingredientes Selecionados"}</span>
                  <span className="rounded-full bg-white/15 px-3 py-1">Entrega Rápida</span>
                </div>
                {catalogMonteProduct && (
                  <div className="pt-2">
                    <button
                      type="button"
                      onClick={() => {
                        setEditingCartItem(null);
                        setModalProduct(catalogMonteProduct);
                      }}
                      style={{ backgroundColor: "var(--store-accent)" }}
                      className="inline-flex items-center gap-2 rounded-2xl px-5 py-3 text-sm font-extrabold text-white shadow-lg transition hover:opacity-95"
                    >
                      <Sparkles className="h-4 w-4" />
                      {catalogMonteProduct.name.toLowerCase().startsWith("monte")
                        ? catalogMonteProduct.name
                        : `Montar ${catalogMonteProduct.name}`}
                    </button>
                  </div>
                )}
              </div>

              {storeInfo?.branding.banners && storeInfo.branding.banners.length > 0 && (
                <div className="grid grid-cols-3 gap-2 md:w-72 shrink-0">
                  {storeInfo.branding.banners.slice(0, 3).map((b, idx) => (
                    <div key={idx} className="overflow-hidden rounded-2xl border border-white/20 bg-white/10 p-1.5 backdrop-blur-xs">
                      <ProductMediaFrame src={b.imageUrl} alt={b.title} className="h-20 w-full rounded-xl" iconSize={20} />
                      <p className="mt-1 truncate text-center text-[10px] font-bold text-white/95">{b.title}</p>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
            <button
              type="button"
              onClick={() => setActiveFilter("best")}
              className="flex items-center justify-between rounded-2xl border border-amber-200 bg-amber-50/70 p-3.5 text-left transition hover:bg-amber-50"
            >
              <div>
                <p className="text-xs font-extrabold text-amber-900">Mais Vendidos</p>
                <p className="text-[11px] text-amber-700">{bestSellers.length} destaques</p>
              </div>
              <Flame className="h-5 w-5 text-amber-500" />
            </button>
            <button
              type="button"
              onClick={() => setActiveFilter("new")}
              className="flex items-center justify-between rounded-2xl border border-emerald-200 bg-emerald-50/70 p-3.5 text-left transition hover:bg-emerald-50"
            >
              <div>
                <p className="text-xs font-extrabold text-emerald-900">Novidades e Lançamentos</p>
                <p className="text-[11px] text-emerald-700">{newLaunches.length} novidades</p>
              </div>
              <Sparkles className="h-5 w-5 text-emerald-600" />
            </button>
            <button
              type="button"
              onClick={() => setActiveFilter("promo")}
              className="flex items-center justify-between rounded-2xl border border-rose-200 bg-rose-50/70 p-3.5 text-left transition hover:bg-rose-50"
            >
              <div>
                <p className="text-xs font-extrabold text-rose-900">Promoções</p>
                <p className="text-[11px] text-rose-700">{promoProducts.length} ofertas</p>
              </div>
              <Percent className="h-5 w-5 text-rose-600" />
            </button>
            <button
              type="button"
              onClick={() => setActiveFilter("combos")}
              className="flex items-center justify-between rounded-2xl border border-slate-200 bg-white p-3.5 text-left transition hover:bg-slate-50"
            >
              <div>
                <p className="text-xs font-extrabold text-slate-900">Combos Especiais</p>
                <p className="text-[11px] text-slate-500">{menu?.combos.length ?? 0} combos</p>
              </div>
              <Layers className="h-5 w-5 text-slate-600" />
            </button>
          </div>
        </section>
      )}

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
              ...(menu?.categories
                .filter((c) => c.isScheduledAvailable !== false && menu.products.some((p) => p.categoryId === c.id))
                .sort((a, b) => a.displayOrder - b.displayOrder)
                .map((c) => ({ id: c.id, label: c.name })) ?? [])
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
                    <ProductMediaFrame src={combo.primaryImageUrl} alt={combo.name} fit="contain" className="h-20 w-20 shrink-0 rounded-2xl" iconSize={24} />
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

      {/* Montador Clássico de Açaí (fallback somente se não houver produto Monte do Seu Jeito no catálogo) */}
      {menu && activeCup && !catalogMonteProduct && activeFilter === "all" && !query && (
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
              .filter((c) => c.isScheduledAvailable !== false)
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

      {/* Barra Flutuante Inferior de Pedido (Mobile e Desktop) */}
      {cartItemsCount > 0 && (
        <div className="pointer-events-none fixed inset-x-0 bottom-4 z-30 flex justify-center px-4">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            style={{ backgroundColor: "var(--store-accent)" }}
            className="pointer-events-auto flex w-full max-w-md items-center justify-between rounded-2xl px-5 py-3.5 text-sm font-extrabold text-white shadow-xl transition hover:opacity-95"
          >
            <span className="flex items-center gap-2">
              <ShoppingBag className="h-4 w-4" />
              Ver meu pedido · {cartItemsCount} {cartItemsCount === 1 ? "item" : "itens"}
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
