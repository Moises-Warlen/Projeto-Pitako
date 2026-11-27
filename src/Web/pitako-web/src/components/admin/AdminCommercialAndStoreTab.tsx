"use client";

import { BarChart3, Clock, Eye, Percent, Plus, Store, Tag, Trash2, Truck, Upload } from "lucide-react";
import { useState } from "react";
import { apiFetch, compressImageFileToDataUrl, resolveMediaUrl } from "@/lib/api";
import { StoreBannerSnapshot, StoreOperatingHoursEntry } from "@/types/menu";

const DAY_NAMES = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];

export interface AdminMarginDto {
  productId: string;
  name: string;
  sellingPrice: number;
  unitCost: number;
  grossProfit: number;
  grossMarginPercent: number;
  hasTechnicalSheet: boolean;
  salesCount: number;
}

export interface AdminComboDto {
  id: string;
  name: string;
  description?: string | null;
  comboPrice: number;
  isFeatured: boolean;
  isActive: boolean;
}

export interface AdminCouponDto {
  id: string;
  code: string;
  discountType: number;
  discountValue: number;
  minimumOrderAmount: number;
  validFromUtc?: string;
  validUntilUtc?: string;
  maxTotalUses?: number | null;
  maxUsesPerCustomer?: number | null;
  firstOrderOnly?: boolean;
  currentUses: number;
  isActive: boolean;
}

export interface AdminDeliveryPolicyDto {
  id: string;
  baseFee: number;
  freeShippingThreshold?: number | null;
  minimumOrderAmount: number;
  maxRadiusKm: number;
  baseEstimatedMinutes: number;
  isDeliveryEnabled: boolean;
  allowPickup: boolean;
  isTemporarilyPaused: boolean;
  zoneRules: Array<{ id: string; zoneName: string; zipCodePrefix?: string | null; fee: number; estimatedMinutes: number; isBlocked: boolean }>;
}

export function AdminCommercialTab({
  token,
  products,
  margins,
  combos,
  coupons,
  deliveryPolicy,
  onNotify,
  onReload
}: {
  token: string;
  products: Array<{ id: string; name: string }>;
  margins: AdminMarginDto[];
  combos: AdminComboDto[];
  coupons: AdminCouponDto[];
  deliveryPolicy: AdminDeliveryPolicyDto | null;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [comboName, setComboName] = useState("");
  const [comboPrice, setComboPrice] = useState("39.90");
  const [comboDesc, setComboDesc] = useState("");
  const [comboProdId, setComboProdId] = useState("");

  const [couponCode, setCouponCode] = useState("");
  const [couponValue, setCouponValue] = useState("10");
  const [couponMin, setCouponMin] = useState("25");

  const [baseFee, setBaseFee] = useState(String(deliveryPolicy?.baseFee ?? 6));
  const [freeThreshold, setFreeThreshold] = useState(String(deliveryPolicy?.freeShippingThreshold ?? 60));
  const [minOrder, setMinOrder] = useState(String(deliveryPolicy?.minimumOrderAmount ?? 15));
  const [zoneName, setZoneName] = useState("");
  const [zoneFee, setZoneFee] = useState("5.00");

  async function createCombo(e: React.FormEvent) {
    e.preventDefault();
    const pid = comboProdId || products[0]?.id;
    if (!pid) return onNotify("Cadastre produtos antes de criar um combo.");
    const res = await apiFetch(
      "/api/v1/catalog/combos",
      {
        method: "POST",
        body: JSON.stringify({
          name: comboName,
          comboPrice: Number(comboPrice),
          description: comboDesc || null,
          isFeatured: true,
          items: [{ productId: pid, quantity: 1 }]
        })
      },
      token
    );
    if (res.ok) {
      setComboName("");
      setComboDesc("");
      onNotify("Combo criado!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao criar combo.");
  }

  async function deleteCombo(id: string) {
    await apiFetch(`/api/v1/catalog/combos/${id}`, { method: "DELETE" }, token);
    await onReload();
  }

  async function createCoupon(e: React.FormEvent) {
    e.preventDefault();
    const now = new Date();
    const later = new Date(now.getTime() + 60 * 86400000);
    const res = await apiFetch(
      "/api/v1/coupons",
      {
        method: "POST",
        body: JSON.stringify({
          code: couponCode.toUpperCase(),
          discountType: 1,
          discountValue: Number(couponValue),
          validFromUtc: now.toISOString(),
          validUntilUtc: later.toISOString(),
          minimumOrderAmount: Number(couponMin),
          canCombineWithPromotions: true
        })
      },
      token
    );
    if (res.ok) {
      setCouponCode("");
      onNotify("Cupom criado!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao criar cupom.");
  }

  async function deleteCoupon(id: string) {
    await apiFetch(`/api/v1/coupons/${id}`, { method: "DELETE" }, token);
    await onReload();
  }

  async function saveDeliveryPolicy(e: React.FormEvent) {
    e.preventDefault();
    const payload = {
      originLatitude: -20.5386,
      originLongitude: -47.4008,
      maxRadiusKm: 12,
      calculationMode: 1,
      baseFeeAmount: Number(baseFee),
      includedKm: 3,
      feePerKmAmount: 2,
      baseEstimatedMinutes: 35,
      minimumOrderAmount: Number(minOrder),
      freeShippingThresholdAmount: freeThreshold ? Number(freeThreshold) : null,
      isDeliveryEnabled: true,
      allowPickup: true
    };
    const res = deliveryPolicy
      ? await apiFetch(`/api/v1/delivery/policy/${deliveryPolicy.id}`, { method: "PUT", body: JSON.stringify(payload) }, token)
      : await apiFetch("/api/v1/delivery/policy", { method: "POST", body: JSON.stringify(payload) }, token);
    if (res.ok) {
      onNotify("Política de frete e delivery salva!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar frete.");
  }

  async function addZone(e: React.FormEvent) {
    e.preventDefault();
    if (!deliveryPolicy) return onNotify("Salve a política de frete primeiro.");
    const res = await apiFetch(
      `/api/v1/delivery/policy/${deliveryPolicy.id}/zones`,
      { method: "POST", body: JSON.stringify({ zoneName, feeAmount: Number(zoneFee), estimatedMinutes: 35, isBlocked: false }) },
      token
    );
    if (res.ok) {
      setZoneName("");
      onNotify("Zona de entrega adicionada!");
      await onReload();
    }
  }

  return (
    <div className="space-y-6">
      {/* Inteligência Comercial: Margem & Mais Vendidos */}
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h3 className="mb-3 flex items-center gap-2 text-base font-extrabold text-slate-900">
          <BarChart3 className="h-4 w-4" /> Inteligência Comercial — Lucratividade & Giro
        </h3>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-slate-200 text-slate-500">
                <th className="py-2">Produto</th>
                <th className="py-2">Venda</th>
                <th className="py-2">Custo Ficha</th>
                <th className="py-2">Lucro Bruto</th>
                <th className="py-2">Margem</th>
                <th className="py-2">Vendas</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {margins.map((m) => (
                <tr key={m.productId}>
                  <td className="py-2 font-bold text-slate-900">{m.name}</td>
                  <td className="py-2 font-mono">R$ {m.sellingPrice.toFixed(2)}</td>
                  <td className="py-2 font-mono text-slate-600">{m.hasTechnicalSheet ? `R$ ${m.unitCost.toFixed(2)}` : "Sem ficha"}</td>
                  <td className="py-2 font-mono font-bold text-emerald-700">R$ {m.grossProfit.toFixed(2)}</td>
                  <td className="py-2 font-bold">{m.grossMarginPercent.toFixed(1)}%</td>
                  <td className="py-2 font-mono">{m.salesCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-3">
        {/* Combos */}
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-xs">
          <h4 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
            <Tag className="h-4 w-4" /> Combos Especiais ({combos.length})
          </h4>
          <div className="space-y-1.5">
            {combos.map((c) => (
              <div key={c.id} className="flex items-center justify-between rounded-xl bg-slate-50 px-3 py-2">
                <span><strong>{c.name}</strong> · R$ {c.comboPrice.toFixed(2)}</span>
                <button type="button" onClick={() => deleteCombo(c.id)} className="text-rose-600"><Trash2 className="h-3.5 w-3.5" /></button>
              </div>
            ))}
          </div>
          <form onSubmit={createCombo} className="space-y-2 border-t pt-3">
            <input value={comboName} onChange={(e) => setComboName(e.target.value)} required placeholder="Nome do combo" className="w-full rounded-xl border px-3 py-2" />
            <input value={comboDesc} onChange={(e) => setComboDesc(e.target.value)} placeholder="Descrição" className="w-full rounded-xl border px-3 py-2" />
            <div className="grid grid-cols-2 gap-2">
              <input type="number" step="0.01" value={comboPrice} onChange={(e) => setComboPrice(e.target.value)} placeholder="Preço" className="rounded-xl border px-3 py-2" />
              <select value={comboProdId || products[0]?.id || ""} onChange={(e) => setComboProdId(e.target.value)} className="rounded-xl border px-2 py-2">
                {products.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </div>
            <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2 font-bold text-white">
              <Plus className="inline h-3.5 w-3.5" /> Criar Combo
            </button>
          </form>
        </section>

        {/* Cupons */}
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-xs">
          <h4 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
            <Percent className="h-4 w-4" /> Cupons Promocionais ({coupons.length})
          </h4>
          <div className="space-y-1.5">
            {coupons.map((cp) => (
              <div key={cp.id} className="flex items-center justify-between rounded-xl bg-slate-50 px-3 py-2">
                <span><strong>{cp.code}</strong> · {cp.discountValue}% OFF · mín R$ {cp.minimumOrderAmount.toFixed(0)}</span>
                <button type="button" onClick={() => deleteCoupon(cp.id)} className="text-rose-600"><Trash2 className="h-3.5 w-3.5" /></button>
              </div>
            ))}
          </div>
          <form onSubmit={createCoupon} className="space-y-2 border-t pt-3">
            <input value={couponCode} onChange={(e) => setCouponCode(e.target.value.toUpperCase())} required placeholder="Código (ex: BEMVINDO10)" className="w-full rounded-xl border px-3 py-2 font-mono uppercase" />
            <div className="grid grid-cols-2 gap-2">
              <label>% Desconto<input type="number" value={couponValue} onChange={(e) => setCouponValue(e.target.value)} className="mt-1 w-full rounded-lg border px-2.5 py-1.5" /></label>
              <label>Pedido Mín.<input type="number" value={couponMin} onChange={(e) => setCouponMin(e.target.value)} className="mt-1 w-full rounded-lg border px-2.5 py-1.5" /></label>
            </div>
            <button type="submit" className="w-full rounded-xl bg-emerald-600 py-2 font-bold text-white">Criar Cupom</button>
          </form>
        </section>

        {/* Frete e Delivery */}
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-xs">
          <h4 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
            <Truck className="h-4 w-4" /> Frete & Áreas de Entrega
          </h4>
          <form onSubmit={saveDeliveryPolicy} className="space-y-2">
            <div className="grid grid-cols-3 gap-2">
              <label>Taxa Fixa<input type="number" step="0.01" value={baseFee} onChange={(e) => setBaseFee(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
              <label>Pedido Mín<input type="number" step="0.01" value={minOrder} onChange={(e) => setMinOrder(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
              <label>Grátis A Partir<input type="number" step="0.01" value={freeThreshold} onChange={(e) => setFreeThreshold(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
            </div>
            <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2 font-bold text-white">
              Salvar Regras de Delivery
            </button>
          </form>

          {deliveryPolicy && (
            <form onSubmit={addZone} className="space-y-2 border-t pt-3">
              <p className="font-bold text-slate-700">Bairros / Zonas ({deliveryPolicy.zoneRules.length})</p>
              <div className="flex gap-2">
                <input value={zoneName} onChange={(e) => setZoneName(e.target.value)} required placeholder="Bairro" className="flex-1 rounded-lg border px-2.5 py-1.5" />
                <input type="number" step="0.01" value={zoneFee} onChange={(e) => setZoneFee(e.target.value)} className="w-20 rounded-lg border px-2 py-1.5" />
                <button type="submit" className="rounded-lg bg-slate-900 px-3 py-1.5 font-bold text-white">+</button>
              </div>
            </form>
          )}
        </section>
      </div>
    </div>
  );
}

export function AdminBrandingAndStoreTab({
  token,
  slug,
  initialProfile,
  initialBranding,
  onNotify,
  onReload
}: {
  token: string;
  slug: string;
  initialProfile: { legalName: string; tradeName: string; address: string; contactPhone: string; contactEmail: string };
  initialBranding: {
    logoUrl: string;
    coverImageUrl: string;
    slogan: string;
    primaryColorHex: string;
    secondaryColorHex: string;
    accentColorHex: string;
    announcementText: string;
    whatsAppNumber: string;
    instagramHandle: string;
    banners: StoreBannerSnapshot[];
    schedule: StoreOperatingHoursEntry[];
  };
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [legalName, setLegalName] = useState(initialProfile.legalName);
  const [tradeName, setTradeName] = useState(initialProfile.tradeName);
  const [address, setAddress] = useState(initialProfile.address);
  const [phone, setPhone] = useState(initialProfile.contactPhone);
  const [email, setEmail] = useState(initialProfile.contactEmail);

  const [logoUrl, setLogoUrl] = useState(initialBranding.logoUrl);
  const [coverUrl, setCoverUrl] = useState(initialBranding.coverImageUrl);
  const [slogan, setSlogan] = useState(initialBranding.slogan);
  const [primaryHex, setPrimaryHex] = useState(initialBranding.primaryColorHex);
  const [secondaryHex, setSecondaryHex] = useState(initialBranding.secondaryColorHex);
  const [accentHex, setAccentHex] = useState(initialBranding.accentColorHex);
  const [announcement, setAnnouncement] = useState(initialBranding.announcementText);
  const [whatsApp, setWhatsApp] = useState(initialBranding.whatsAppNumber);
  const [instagram, setInstagram] = useState(initialBranding.instagramHandle);
  const [bannerTitle, setBannerTitle] = useState(initialBranding.banners[0]?.title || "");
  const [bannerSubtitle, setBannerSubtitle] = useState(initialBranding.banners[0]?.subtitle || "");
  const [schedule, setSchedule] = useState<StoreOperatingHoursEntry[]>(initialBranding.schedule);
  const [previewOpen, setPreviewOpen] = useState(false);

  async function saveProfile(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch(
      "/api/v1/store/profile",
      { method: "PUT", body: JSON.stringify({ legalName, tradeName, address, contactPhone: phone, contactEmail: email }) },
      token
    );
    if (res.ok) {
      onNotify("Dados da loja salvos!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar dados.");
  }

  async function saveDraftAndPublish(publishNow: boolean) {
    const banners = bannerTitle.trim()
      ? [{ title: bannerTitle.trim(), subtitle: bannerSubtitle.trim() || null, imageUrl: coverUrl || null, targetUrl: null, displayOrder: 1 }]
      : [];

    const draftRes = await apiFetch(
      "/api/v1/store/branding/draft",
      {
        method: "PUT",
        body: JSON.stringify({
          logoUrl: logoUrl || null,
          coverImageUrl: coverUrl || null,
          slogan: slogan || null,
          primaryColorHex: primaryHex,
          secondaryColorHex: secondaryHex,
          accentColorHex: accentHex,
          menuLayoutMode: "grid",
          draftBanners: banners,
          operatingHoursJson: JSON.stringify(schedule),
          announcementText: announcement || null,
          whatsAppNumber: whatsApp || null,
          instagramHandle: instagram || null
        })
      },
      token
    );
    if (!draftRes.ok) return onNotify(draftRes.errorMessage || "Erro ao salvar rascunho visual.");

    if (publishNow) {
      await apiFetch("/api/v1/store/branding/publish", { method: "POST" }, token);
      onNotify("Identidade visual e horários publicados no cardápio!");
    } else {
      onNotify("Rascunho salvo! Veja a pré-visualização antes de publicar.");
    }
    await onReload();
  }

  return (
    <div className="space-y-6">
      <div className="grid gap-6 lg:grid-cols-2">
        {/* Dados da Loja */}
        <form onSubmit={saveProfile} className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-3 text-xs">
          <h3 className="flex items-center gap-2 text-base font-extrabold text-slate-900">
            <Store className="h-4 w-4" /> Perfil do Estabelecimento
          </h3>
          <input value={tradeName} onChange={(e) => setTradeName(e.target.value)} required placeholder="Nome Fantasia (aparece no cardápio)" className="w-full rounded-xl border px-3 py-2 text-sm" />
          <input value={legalName} onChange={(e) => setLegalName(e.target.value)} required placeholder="Razão Social" className="w-full rounded-xl border px-3 py-2" />
          <input value={address} onChange={(e) => setAddress(e.target.value)} placeholder="Endereço completo" className="w-full rounded-xl border px-3 py-2" />
          <div className="grid grid-cols-2 gap-2">
            <input value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="Telefone / WhatsApp" className="rounded-xl border px-3 py-2" />
            <input value={email} onChange={(e) => setEmail(e.target.value)} placeholder="E-mail de contato" className="rounded-xl border px-3 py-2" />
          </div>
          <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="rounded-xl px-4 py-2.5 text-sm font-bold text-white">
            Salvar Dados da Loja
          </button>
        </form>

        {/* Cores, Logo, Banner e Anúncio */}
        <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-3 text-xs">
          <h3 className="text-base font-extrabold text-slate-900">Identidade Visual & Banners Whitelabel</h3>
          <input value={slogan} onChange={(e) => setSlogan(e.target.value)} placeholder="Slogan da loja" className="w-full rounded-xl border px-3 py-2 text-sm" />
          <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 space-y-2">
            <div className="flex flex-wrap items-center gap-3">
              {resolveMediaUrl(logoUrl) && (
                <img src={resolveMediaUrl(logoUrl)!} alt="Logo" className="h-12 w-12 rounded-xl border bg-white object-contain p-1" />
              )}
              <label className="inline-flex cursor-pointer items-center gap-1.5 rounded-xl border border-slate-300 bg-white px-3 py-1.5 font-bold text-slate-800 hover:bg-slate-100">
                <Upload className="h-3.5 w-3.5" /> Enviar Logo do Computador
                <input
                  type="file"
                  accept="image/*"
                  className="hidden"
                  onChange={async (e) => {
                    const file = e.target.files?.[0];
                    if (!file) return;
                    const compressed = await compressImageFileToDataUrl(file, 360, 0.8);
                    const up = await apiFetch<{ url: string }>("/api/v1/store/branding/upload-logo", { method: "POST", body: JSON.stringify({ base64DataUrl: compressed, fileName: file.name }) }, token);
                    if (up.ok && up.data?.url) {
                      setLogoUrl(up.data.url);
                      onNotify("Logotipo enviado e publicado na loja!");
                      await onReload();
                    }
                  }}
                />
              </label>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <input value={logoUrl} onChange={(e) => setLogoUrl(e.target.value)} placeholder="URL do Logotipo" className="rounded-xl border bg-white px-3 py-2" />
              <input value={coverUrl} onChange={(e) => setCoverUrl(e.target.value)} placeholder="URL da Capa / Banner" className="rounded-xl border bg-white px-3 py-2" />
            </div>
          </div>
          <div className="grid grid-cols-3 gap-2">
            <label>Cor Primária<input type="color" value={primaryHex} onChange={(e) => setPrimaryHex(e.target.value)} className="mt-1 h-9 w-full rounded border" /></label>
            <label>Cor Secundária<input type="color" value={secondaryHex} onChange={(e) => setSecondaryHex(e.target.value)} className="mt-1 h-9 w-full rounded border" /></label>
            <label>Cor Destaque<input type="color" value={accentHex} onChange={(e) => setAccentHex(e.target.value)} className="mt-1 h-9 w-full rounded border" /></label>
          </div>
          <input value={announcement} onChange={(e) => setAnnouncement(e.target.value)} placeholder="Faixa de aviso no topo (ex: Entrega grátis acima de R$ 60!)" className="w-full rounded-xl border px-3 py-2" />
          <div className="grid grid-cols-2 gap-2">
            <input value={bannerTitle} onChange={(e) => setBannerTitle(e.target.value)} placeholder="Título do Banner Promocional" className="rounded-xl border px-3 py-2" />
            <input value={bannerSubtitle} onChange={(e) => setBannerSubtitle(e.target.value)} placeholder="Subtítulo do Banner" className="rounded-xl border px-3 py-2" />
          </div>
          <div className="grid grid-cols-2 gap-2">
            <input value={whatsApp} onChange={(e) => setWhatsApp(e.target.value)} placeholder="WhatsApp" className="rounded-xl border px-3 py-2" />
            <input value={instagram} onChange={(e) => setInstagram(e.target.value)} placeholder="@instagram" className="rounded-xl border px-3 py-2" />
          </div>
        </div>
      </div>

      {/* Horários de Funcionamento */}
      <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        <h3 className="mb-3 flex items-center gap-2 text-base font-extrabold text-slate-900">
          <Clock className="h-4 w-4" /> Horários de Funcionamento da Semana
        </h3>
        <div className="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4 text-xs">
          {schedule.map((item, idx) => (
            <div key={item.dayOfWeek} className="rounded-xl border border-slate-200 bg-slate-50 p-3 space-y-2">
              <div className="flex items-center justify-between font-bold text-slate-800">
                <span>{DAY_NAMES[item.dayOfWeek]}</span>
                <label className="flex items-center gap-1 font-medium">
                  <input
                    type="checkbox"
                    checked={item.isOpen}
                    onChange={(e) => {
                      const next = [...schedule];
                      next[idx] = { ...item, isOpen: e.target.checked };
                      setSchedule(next);
                    }}
                  />
                  Abre
                </label>
              </div>
              {item.isOpen && (
                <div className="flex items-center gap-1.5">
                  <input
                    type="time"
                    value={item.openTime}
                    onChange={(e) => {
                      const next = [...schedule];
                      next[idx] = { ...item, openTime: e.target.value };
                      setSchedule(next);
                    }}
                    className="flex-1 rounded border bg-white px-1.5 py-1"
                  />
                  <span>às</span>
                  <input
                    type="time"
                    value={item.closeTime}
                    onChange={(e) => {
                      const next = [...schedule];
                      next[idx] = { ...item, closeTime: e.target.value };
                      setSchedule(next);
                    }}
                    className="flex-1 rounded border bg-white px-1.5 py-1"
                  />
                </div>
              )}
            </div>
          ))}
        </div>

        <div className="mt-5 flex flex-wrap gap-3">
          <button type="button" onClick={() => saveDraftAndPublish(false)} className="rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-xs font-bold text-slate-700">
            Salvar Rascunho
          </button>
          <button type="button" onClick={() => setPreviewOpen((v) => !v)} className="inline-flex items-center gap-1.5 rounded-xl border border-slate-300 bg-white px-4 py-2.5 text-xs font-bold text-slate-800">
            <Eye className="h-4 w-4" /> {previewOpen ? "Ocultar Prévia" : "Pré-visualizar Cardápio"}
          </button>
          <button type="button" onClick={() => saveDraftAndPublish(true)} style={{ backgroundColor: "var(--store-primary)" }} className="rounded-xl px-5 py-2.5 text-xs font-bold text-white shadow">
            Publicar Visual & Horários
          </button>
        </div>

        {previewOpen && (
          <div className="mt-5 overflow-hidden rounded-2xl border border-slate-300">
            <div className="bg-slate-900 px-4 py-2 text-xs font-bold text-white">Pré-visualização do Cardápio Público (/loja/{slug})</div>
            <iframe title="Pré-visualização Cardápio" src={`/loja/${slug}`} className="h-[520px] w-full bg-white" />
          </div>
        )}
      </section>
    </div>
  );
}
