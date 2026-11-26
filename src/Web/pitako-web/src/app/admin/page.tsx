"use client";

import { ExternalLink, Pause, Play } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { AdminAcaiConfigDto, AdminAcaiTab, AdminTeamTab } from "@/components/admin/AdminAcaiAndTeamTabs";
import { AdminCategoryDto, AdminCatalogTab, AdminProductDto } from "@/components/admin/AdminCatalogTab";
import { AdminBrandingAndStoreTab, AdminComboDto, AdminCommercialTab, AdminCouponDto, AdminDeliveryPolicyDto, AdminMarginDto } from "@/components/admin/AdminCommercialAndStoreTab";
import { AdminInventoryTab, AdminSupplyDto, AdminTechnicalSheetDto } from "@/components/admin/AdminInventoryTab";
import { AdminModifierGroupDto, AdminModifiersTab } from "@/components/admin/AdminModifiersTab";
import { apiFetch, loadSession } from "@/lib/api";
import { StoreBannerSnapshot, StoreOperatingHoursEntry } from "@/types/menu";

const DEFAULT_HOURS: StoreOperatingHoursEntry[] = [
  { dayOfWeek: 0, isOpen: true, openTime: "13:00", closeTime: "23:00" },
  { dayOfWeek: 1, isOpen: true, openTime: "11:00", closeTime: "23:00" },
  { dayOfWeek: 2, isOpen: true, openTime: "11:00", closeTime: "23:00" },
  { dayOfWeek: 3, isOpen: true, openTime: "11:00", closeTime: "23:00" },
  { dayOfWeek: 4, isOpen: true, openTime: "11:00", closeTime: "23:00" },
  { dayOfWeek: 5, isOpen: true, openTime: "11:00", closeTime: "23:30" },
  { dayOfWeek: 6, isOpen: true, openTime: "11:00", closeTime: "23:30" }
];

type AdminTabKey = "catalog" | "modifiers" | "acai" | "inventory" | "commercial" | "branding" | "team";

export default function AdminStorePage() {
  const [token, setToken] = useState("");
  const [slug, setSlug] = useState("acai-demo");
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: "Administração da Loja",
    primaryColorHex: "#1e293b",
    secondaryColorHex: "#f8fafc",
    accentColorHex: "#0f766e"
  });
  const [tab, setTab] = useState<AdminTabKey>("catalog");
  const [msg, setMsg] = useState<string | null>(null);
  const [isPaused, setIsPaused] = useState(false);

  const [categories, setCategories] = useState<AdminCategoryDto[]>([]);
  const [products, setProducts] = useState<AdminProductDto[]>([]);
  const [modifierGroups, setModifierGroups] = useState<AdminModifierGroupDto[]>([]);
  const [acai, setAcai] = useState<AdminAcaiConfigDto>({ cupSizes: [], baseFlavors: [], freeToppings: [], paidAddons: [] });
  const [supplies, setSupplies] = useState<AdminSupplyDto[]>([]);
  const [sheets, setSheets] = useState<AdminTechnicalSheetDto[]>([]);
  const [margins, setMargins] = useState<AdminMarginDto[]>([]);
  const [combos, setCombos] = useState<AdminComboDto[]>([]);
  const [coupons, setCoupons] = useState<AdminCouponDto[]>([]);
  const [deliveryPolicy, setDeliveryPolicy] = useState<AdminDeliveryPolicyDto | null>(null);
  const [team, setTeam] = useState<Array<{ id: string; fullName: string; normalizedEmail: string; role: string }>>([]);

  const [storeProfile, setStoreProfile] = useState({
    legalName: "",
    tradeName: "",
    address: "",
    contactPhone: "",
    contactEmail: ""
  });
  const [brandingData, setBrandingData] = useState({
    logoUrl: "",
    coverImageUrl: "",
    slogan: "",
    primaryColorHex: "#1e293b",
    secondaryColorHex: "#f8fafc",
    accentColorHex: "#0f766e",
    announcementText: "",
    whatsAppNumber: "",
    instagramHandle: "",
    banners: [] as StoreBannerSnapshot[],
    schedule: DEFAULT_HOURS
  });

  const refreshStoreData = useCallback(async () => {
    const session = loadSession();
    if (!session) return;
    setToken(session.accessToken);
    if (session.storeSlug) setSlug(session.storeSlug);

    const [brandRes, catRes, acaiRes, supRes, sheetRes, marginRes, comboRes, couponRes, delivRes, userRes] = await Promise.all([
      apiFetch<{
        legalName?: string;
        tradeName?: string;
        slug?: string;
        address?: string;
        contactPhone?: string;
        contactEmail?: string;
        draft: {
          logoUrl?: string;
          coverImageUrl?: string;
          slogan?: string;
          primaryColorHex: string;
          secondaryColorHex: string;
          accentColorHex: string;
          announcementText?: string;
          whatsAppNumber?: string;
          instagramHandle?: string;
          operatingHoursJson?: string;
          isTemporarilyPaused?: boolean;
          banners?: StoreBannerSnapshot[];
        };
      }>("/api/v1/store/branding", {}, session.accessToken),
      apiFetch<{ categories: AdminCategoryDto[]; products: AdminProductDto[]; modifierGroups: AdminModifierGroupDto[] }>(
        "/api/v1/catalog/products",
        {},
        session.accessToken
      ),
      apiFetch<AdminAcaiConfigDto>("/api/v1/acai/config", {}, session.accessToken),
      apiFetch<AdminSupplyDto[]>("/api/v1/inventory/supplies", {}, session.accessToken),
      apiFetch<AdminTechnicalSheetDto[]>("/api/v1/inventory/technical-sheets", {}, session.accessToken),
      apiFetch<AdminMarginDto[]>("/api/v1/catalog/analytics/margins", {}, session.accessToken),
      apiFetch<AdminComboDto[]>("/api/v1/catalog/combos", {}, session.accessToken),
      apiFetch<AdminCouponDto[]>("/api/v1/coupons", {}, session.accessToken),
      apiFetch<AdminDeliveryPolicyDto | null>("/api/v1/delivery/policy", {}, session.accessToken),
      apiFetch<Array<{ id: string; fullName: string; normalizedEmail: string; role: string }>>("/api/v1/store/users", {}, session.accessToken)
    ]);

    if (brandRes.ok && brandRes.data) {
      const d = brandRes.data.draft;
      setTheme({
        tradeName: brandRes.data.tradeName || session.email,
        logoUrl: d.logoUrl,
        primaryColorHex: d.primaryColorHex,
        secondaryColorHex: d.secondaryColorHex,
        accentColorHex: d.accentColorHex
      });
      if (brandRes.data.slug) setSlug(brandRes.data.slug);
      setIsPaused(Boolean(d.isTemporarilyPaused));
      setStoreProfile({
        legalName: brandRes.data.legalName || brandRes.data.tradeName || "",
        tradeName: brandRes.data.tradeName || "",
        address: brandRes.data.address || "",
        contactPhone: brandRes.data.contactPhone || "",
        contactEmail: brandRes.data.contactEmail || ""
      });

      let parsedSchedule = DEFAULT_HOURS;
      try {
        if (d.operatingHoursJson) {
          const arr = JSON.parse(d.operatingHoursJson) as StoreOperatingHoursEntry[];
          if (Array.isArray(arr) && arr.length > 0) parsedSchedule = arr;
        }
      } catch {
        // Mantém padrão semanal
      }

      setBrandingData({
        logoUrl: d.logoUrl || "",
        coverImageUrl: d.coverImageUrl || "",
        slogan: d.slogan || "",
        primaryColorHex: d.primaryColorHex,
        secondaryColorHex: d.secondaryColorHex,
        accentColorHex: d.accentColorHex,
        announcementText: d.announcementText || "",
        whatsAppNumber: d.whatsAppNumber || "",
        instagramHandle: d.instagramHandle || "",
        banners: d.banners || [],
        schedule: parsedSchedule
      });
    }

    if (catRes.ok && catRes.data) {
      setCategories(catRes.data.categories || []);
      setProducts(catRes.data.products || []);
      setModifierGroups(catRes.data.modifierGroups || []);
    }
    if (acaiRes.ok && acaiRes.data) setAcai(acaiRes.data);
    if (supRes.ok && supRes.data) setSupplies(supRes.data);
    if (sheetRes.ok && sheetRes.data) setSheets(sheetRes.data);
    if (marginRes.ok && marginRes.data) setMargins(marginRes.data);
    if (comboRes.ok && comboRes.data) setCombos(comboRes.data);
    if (couponRes.ok && couponRes.data) setCoupons(couponRes.data);
    if (delivRes.ok) setDeliveryPolicy(delivRes.data ?? null);
    if (userRes.ok && userRes.data) setTeam(userRes.data);
  }, []);

  useEffect(() => {
    void refreshStoreData();
  }, [refreshStoreData]);

  async function toggleStoreOpenPause() {
    if (!token) return;
    const nextPaused = !isPaused;
    const res = await apiFetch(
      "/api/v1/store/pause",
      { method: "POST", body: JSON.stringify({ isPaused: nextPaused, reason: nextPaused ? "Pausada temporariamente pelo gestor" : null }) },
      token
    );
    if (res.ok) {
      setIsPaused(nextPaused);
      setMsg(nextPaused ? "Loja pausada no cardápio digital." : "Loja reaberta para receber pedidos!");
      await refreshStoreData();
    }
  }

  return (
    <StoreBrandShell theme={theme} activeSection="/admin">
      {/* Cabeçalho Administrativo */}
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
        <div>
          <h1 className="text-lg font-extrabold text-slate-900">Painel do Estabelecimento — {theme.tradeName}</h1>
          <p className="text-xs text-slate-500">
            Gerencie cardápio multi-segmento, grupos de personalização, estoque com perdas, promoções, entrega e visual da loja.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={toggleStoreOpenPause}
            className={`inline-flex items-center gap-1.5 rounded-xl px-3.5 py-2 text-xs font-bold ${
              isPaused ? "bg-emerald-600 text-white" : "bg-rose-100 text-rose-800"
            }`}
          >
            {isPaused ? <Play className="h-3.5 w-3.5" /> : <Pause className="h-3.5 w-3.5" />}
            {isPaused ? "Reabrir Cardápio" : "Pausar Cardápio"}
          </button>

          <Link
            href={`/loja/${slug}`}
            target="_blank"
            style={{ backgroundColor: "var(--store-accent)" }}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-bold text-white shadow-sm"
          >
            Cardápio (/loja/{slug}) <ExternalLink className="h-3.5 w-3.5" />
          </Link>

          <Link
            href={`/totem?slug=${slug}`}
            target="_blank"
            className="inline-flex items-center gap-1.5 rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs font-bold text-slate-800 shadow-sm hover:bg-slate-50"
          >
            Totem desta Loja <ExternalLink className="h-3.5 w-3.5" />
          </Link>

          <Link
            href="/integracoes"
            className="inline-flex items-center gap-1.5 rounded-xl bg-slate-900 px-3.5 py-2 text-xs font-bold text-white shadow-sm hover:bg-slate-800"
          >
            Marketplaces (iFood / 99Food)
          </Link>
        </div>
      </div>

      {msg && (
        <div className="mb-5 flex items-center justify-between rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs font-bold text-emerald-900">
          <span>{msg}</span>
          <button type="button" onClick={() => setMsg(null)} className="underline">Fechar</button>
        </div>
      )}

      {/* Abas */}
      <div className="mb-6 flex flex-wrap gap-2">
        {(
          [
            { key: "catalog", label: "1. Cardápio & Segmentos" },
            { key: "modifiers", label: "2. Grupos & Opções" },
            { key: "acai", label: "3. Montador Açaí Clássico" },
            { key: "inventory", label: "4. Estoque & Ficha Técnica" },
            { key: "commercial", label: "5. Margem, Combos & Frete" },
            { key: "branding", label: "6. Loja, Horários & Visual" },
            { key: "team", label: "7. Equipe & Acessos" }
          ] as const
        ).map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key)}
            style={tab === t.key ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
            className={`rounded-xl border px-3.5 py-2 text-xs font-bold transition ${
              tab === t.key ? "border-transparent shadow-sm" : "border-slate-200 bg-white text-slate-700 hover:bg-slate-50"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "catalog" && (
        <AdminCatalogTab
          token={token}
          categories={categories}
          products={products}
          modifierGroups={modifierGroups}
          onNotify={setMsg}
          onReload={refreshStoreData}
        />
      )}

      {tab === "modifiers" && (
        <AdminModifiersTab
          token={token}
          groups={modifierGroups}
          supplies={supplies.map((s) => ({ id: s.id, name: s.name, contentUnitCode: s.contentUnitCode }))}
          onNotify={setMsg}
          onReload={refreshStoreData}
        />
      )}

      {tab === "acai" && (
        <AdminAcaiTab token={token} acai={acai} onNotify={setMsg} onReload={refreshStoreData} />
      )}

      {tab === "inventory" && (
        <AdminInventoryTab
          token={token}
          supplies={supplies}
          sheets={sheets}
          products={products.map((p) => ({ id: p.id, name: p.name }))}
          onNotify={setMsg}
          onReload={refreshStoreData}
        />
      )}

      {tab === "commercial" && (
        <AdminCommercialTab
          token={token}
          products={products.map((p) => ({ id: p.id, name: p.name }))}
          margins={margins}
          combos={combos}
          coupons={coupons}
          deliveryPolicy={deliveryPolicy}
          onNotify={setMsg}
          onReload={refreshStoreData}
        />
      )}

      {tab === "branding" && (
        <AdminBrandingAndStoreTab
          token={token}
          slug={slug}
          initialProfile={storeProfile}
          initialBranding={brandingData}
          onNotify={setMsg}
          onReload={refreshStoreData}
        />
      )}

      {tab === "team" && (
        <AdminTeamTab token={token} team={team} onNotify={setMsg} onReload={refreshStoreData} />
      )}
    </StoreBrandShell>
  );
}
