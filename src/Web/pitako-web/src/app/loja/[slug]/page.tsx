"use client";

import { useEffect, useState } from "react";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { apiFetch } from "@/lib/api";

interface PublicStoreInfo {
  tradeName: string;
  formattedCnpj: string;
  branding: {
    logoUrl?: string;
    slogan?: string;
    primaryColorHex: string;
    secondaryColorHex: string;
    accentColorHex: string;
  };
}

interface MenuResponse {
  categories: { id: string; name: string }[];
  products: { id: string; categoryId: string; name: string; description?: string; price: number }[];
  acai: {
    cupSizes: { id: string; name: string; volumeMl: number; basePrice: number; freeToppingsLimit: number }[];
    baseFlavors: { id: string; name: string; extraPrice: number }[];
    freeToppings: { id: string; name: string; maxPortionsPerCup: number }[];
    paidAddons: { id: string; name: string; price: number; maxQuantityPerCup: number }[];
  };
}

interface CartLine {
  key: string;
  kind: "product" | "acai";
  title: string;
  details?: string;
  unitPrice: number;
  productId?: string;
  acaiRequest?: {
    cupSizeId: string;
    baseFlavorIds: string[];
    freeToppings: { itemId: string; quantity: number }[];
    paidAddons: { itemId: string; quantity: number }[];
  };
}

export default function StorefrontPage({ params }: { params: { slug: string } }) {
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: `Loja ${params.slug}`,
    primaryColorHex: "#4C1D95",
    secondaryColorHex: "#FAF5FF",
    accentColorHex: "#F59E0B"
  });
  const [menu, setMenu] = useState<MenuResponse | null>(null);
  const [cart, setCart] = useState<CartLine[]>([]);
  const [selectedCupId, setSelectedCupId] = useState<string>("");
  const [selectedFlavorId, setSelectedFlavorId] = useState<string>("");
  const [selectedFreeIds, setSelectedFreeIds] = useState<string[]>([]);
  const [selectedPaidIds, setSelectedPaidIds] = useState<string[]>([]);
  const [customerName, setCustomerName] = useState("");
  const [address, setAddress] = useState("");
  const [orderNotice, setOrderNotice] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      apiFetch<PublicStoreInfo>(`/api/v1/public/store/${params.slug}`),
      apiFetch<MenuResponse>(`/api/v1/public/store/${params.slug}/menu`)
    ]).then(([infoRes, menuRes]) => {
      if (infoRes.ok && infoRes.data) {
        setTheme({
          tradeName: infoRes.data.tradeName,
          formattedCnpj: infoRes.data.formattedCnpj,
          logoUrl: infoRes.data.branding.logoUrl,
          primaryColorHex: infoRes.data.branding.primaryColorHex,
          secondaryColorHex: infoRes.data.branding.secondaryColorHex,
          accentColorHex: infoRes.data.branding.accentColorHex
        });
      }
      if (menuRes.ok && menuRes.data) {
        setMenu(menuRes.data);
        if (menuRes.data.acai.cupSizes[0]) setSelectedCupId(menuRes.data.acai.cupSizes[0].id);
        if (menuRes.data.acai.baseFlavors[0]) setSelectedFlavorId(menuRes.data.acai.baseFlavors[0].id);
      }
    });
  }, [params.slug]);

  const activeCup = menu?.acai.cupSizes.find((c) => c.id === selectedCupId);

  function toggleFreeTopping(id: string) {
    if (!activeCup) return;
    setSelectedFreeIds((prev) => {
      if (prev.includes(id)) return prev.filter((x) => x !== id);
      if (prev.length >= activeCup.freeToppingsLimit) return prev;
      return [...prev, id];
    });
  }

  function togglePaidAddon(id: string) {
    setSelectedPaidIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function addCustomAcaiToCart() {
    if (!menu || !activeCup || !selectedFlavorId) return;
    const flavor = menu.acai.baseFlavors.find((f) => f.id === selectedFlavorId);
    const paidSum = selectedPaidIds.reduce((acc, id) => {
      const a = menu.acai.paidAddons.find((x) => x.id === id);
      return acc + (a ? a.price : 0);
    }, 0);
    const total = activeCup.basePrice + (flavor?.extraPrice || 0) + paidSum;

    setCart((prev) => [
      ...prev,
      {
        key: `${Date.now()}-${Math.random()}`,
        kind: "acai",
        title: `Açaí Personalizado — ${activeCup.name}`,
        details: `${flavor?.name || ""} | ${selectedFreeIds.length} grátis, ${selectedPaidIds.length} adicionais`,
        unitPrice: total,
        acaiRequest: {
          cupSizeId: activeCup.id,
          baseFlavorIds: [selectedFlavorId],
          freeToppings: selectedFreeIds.map((id) => ({ itemId: id, quantity: 1 })),
          paidAddons: selectedPaidIds.map((id) => ({ itemId: id, quantity: 1 }))
        }
      }
    ]);
    setSelectedFreeIds([]);
    setSelectedPaidIds([]);
  }

  async function submitCheckout(e: React.FormEvent) {
    e.preventDefault();
    if (cart.length === 0) return;

    const res = await apiFetch<{ orderNumber: string; totalAmount: number }>(
      `/api/v1/public/store/${params.slug}/checkout`,
      {
        method: "POST",
        body: JSON.stringify({
          customerName,
          fulfillmentMode: 1,
          deliveryAddress: address,
          items: cart
            .filter((i) => i.kind === "product" && i.productId)
            .map((i) => ({ productId: i.productId, quantity: 1 })),
          acaiCups: cart.filter((i) => i.kind === "acai" && i.acaiRequest).map((i) => i.acaiRequest)
        })
      }
    );

    if (res.ok && res.data) {
      setOrderNotice(`Pedido ${res.data.orderNumber} confirmado (R$ ${res.data.totalAmount.toFixed(2)})!`);
      setCart([]);
    } else {
      setOrderNotice(res.errorMessage || "Erro ao finalizar pedido.");
    }
  }

  return (
    <StoreBrandShell theme={theme} hideOpsNav>
      {orderNotice && (
        <div className="mb-6 rounded-xl border border-emerald-400 bg-emerald-50 p-4 text-sm font-medium text-emerald-900">
          {orderNotice}
        </div>
      )}

      <div className="grid gap-8 lg:grid-cols-3">
        <div className="space-y-8 lg:col-span-2">
          <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
            <h1 className="text-xl font-bold text-slate-900">Monte seu Açaí Personalizado</h1>
            {menu && activeCup ? (
              <div className="mt-4 space-y-5">
                <div>
                  <p className="mb-2 text-xs font-semibold uppercase text-slate-500">1. Escolha o Copo</p>
                  <div className="flex flex-wrap gap-2">
                    {menu.acai.cupSizes.map((c) => (
                      <button
                        key={c.id}
                        type="button"
                        onClick={() => {
                          setSelectedCupId(c.id);
                          setSelectedFreeIds([]);
                        }}
                        className={`rounded-lg border px-3 py-2 text-sm font-medium ${
                          selectedCupId === c.id ? "border-purple-700 bg-purple-50 text-purple-900" : "border-slate-200"
                        }`}
                      >
                        {c.name} — R$ {c.basePrice.toFixed(2)} (até {c.freeToppingsLimit} grátis)
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <p className="mb-2 text-xs font-semibold uppercase text-slate-500">
                    2. Acompanhamentos Gratuitos ({selectedFreeIds.length}/{activeCup.freeToppingsLimit})
                  </p>
                  <div className="flex flex-wrap gap-2">
                    {menu.acai.freeToppings.map((t) => (
                      <button
                        key={t.id}
                        type="button"
                        onClick={() => toggleFreeTopping(t.id)}
                        className={`rounded-lg border px-3 py-1.5 text-xs font-medium ${
                          selectedFreeIds.includes(t.id)
                            ? "border-emerald-600 bg-emerald-50 text-emerald-900"
                            : "border-slate-200 text-slate-700"
                        }`}
                      >
                        {t.name} (Grátis)
                      </button>
                    ))}
                  </div>
                </div>

                <div className="border-t pt-4">
                  <p className="mb-2 text-xs font-semibold uppercase text-amber-700">
                    3. Adicionais Pagos (Área Separada)
                  </p>
                  <div className="flex flex-wrap gap-2">
                    {menu.acai.paidAddons.map((a) => (
                      <button
                        key={a.id}
                        type="button"
                        onClick={() => togglePaidAddon(a.id)}
                        className={`rounded-lg border px-3 py-1.5 text-xs font-medium ${
                          selectedPaidIds.includes(a.id)
                            ? "border-amber-600 bg-amber-50 text-amber-900"
                            : "border-slate-200 text-slate-700"
                        }`}
                      >
                        {a.name} (+ R$ {a.price.toFixed(2)})
                      </button>
                    ))}
                  </div>
                </div>

                <button
                  type="button"
                  onClick={addCustomAcaiToCart}
                  style={{ backgroundColor: "var(--store-primary)" }}
                  className="rounded-lg px-4 py-2 text-sm font-semibold text-white"
                >
                  Adicionar Copo Individual ao Carrinho
                </button>
              </div>
            ) : (
              <p className="mt-2 text-sm text-slate-500">
                Conecte a API `Pitako.Api` com semente demo para exibir o cardápio ao vivo de `{params.slug}`.
              </p>
            )}
          </section>

          {menu && (
            <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
              <h2 className="mb-4 text-lg font-bold text-slate-900">Cardápio da Loja</h2>
              <div className="grid gap-3 sm:grid-cols-2">
                {menu.products.map((p) => (
                  <div key={p.id} className="flex items-center justify-between rounded-xl border border-slate-200 p-3">
                    <div>
                      <p className="font-semibold text-slate-900">{p.name}</p>
                      <p className="text-xs text-slate-500">R$ {p.price.toFixed(2)}</p>
                    </div>
                    <button
                      type="button"
                      onClick={() =>
                        setCart((prev) => [
                          ...prev,
                          { key: `${p.id}-${Date.now()}`, kind: "product", productId: p.id, title: p.name, unitPrice: p.price }
                        ])
                      }
                      style={{ backgroundColor: "var(--store-accent)" }}
                      className="rounded-lg px-3 py-1.5 text-xs font-bold text-white"
                    >
                      + Carrinho
                    </button>
                  </div>
                ))}
              </div>
            </section>
          )}
        </div>

        <aside className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <h2 className="text-lg font-bold text-slate-900">Seu Pedido ({cart.length})</h2>
          <div className="mt-4 space-y-2.5">
            {cart.map((line) => (
              <div key={line.key} className="rounded-lg border border-slate-100 bg-slate-50 p-2.5 text-xs">
                <p className="font-semibold text-slate-900">{line.title}</p>
                {line.details && <p className="text-slate-500">{line.details}</p>}
                <p className="mt-1 font-mono font-bold text-slate-800">R$ {line.unitPrice.toFixed(2)}</p>
              </div>
            ))}
          </div>

          <form onSubmit={submitCheckout} className="mt-5 space-y-3 border-t pt-4">
            <input
              required
              value={customerName}
              onChange={(e) => setCustomerName(e.target.value)}
              placeholder="Seu nome"
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
            />
            <input
              required
              value={address}
              onChange={(e) => setAddress(e.target.value)}
              placeholder="Endereço de entrega"
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
            />
            <button
              type="submit"
              disabled={cart.length === 0}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="w-full rounded-lg py-2.5 text-sm font-semibold text-white disabled:opacity-40"
            >
              Finalizar Pedido
            </button>
          </form>
        </aside>
      </div>
    </StoreBrandShell>
  );
}
