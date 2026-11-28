"use client";

import { ExternalLink, Pause, Play } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { AdminAcaiConfigDto, AdminAcaiTab, AdminTeamTab } from "@/components/admin/AdminAcaiAndTeamTabs";
import { AdminCategoryDto, AdminCatalogTab, AdminProductDto } from "@/components/admin/AdminCatalogTab";
import { AdminBrandingAndStoreTab, AdminCouponDto } from "@/components/admin/AdminCommercialAndStoreTab";
import { AdminSupplyDto } from "@/components/admin/AdminInventoryTab";
import { AdminModifierGroupDto, AdminModifiersTab } from "@/components/admin/AdminModifiersTab";
import { AdminCustomerChatPanel, AdminWhatsAppBotPanel } from "@/components/admin/AdminChatAndBotPanel";
import {
  AdminCouponsPanel,
  AdminOperationalSettingsPanel,
  AdminPrintersPanel,
  AdminSecurityFirewallPanel
} from "@/components/admin/AdminOpsPrintersCouponsSecurityPanel";
import { AdminReportsAndSettlementPanel } from "@/components/admin/AdminReportsAndSettlementPanel";
import { AdminTechSheetPdfModal, TechnicalSheetSummaryItem } from "@/components/admin/AdminTechSheetPdfModal";
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

type AdminTabKey =
  | "catalog"
  | "chat"
  | "whatsapp"
  | "reports"
  | "modifiers"
  | "acai"
  | "coupons"
  | "printers"
  | "settings"
  | "branding"
  | "security"
  | "team";

const ADMIN_TOAST_KEY = "pitako_admin_toast";
const ADMIN_TAB_KEY = "pitako_admin_active_tab";
const ADMIN_TOAST_DURATION_MS = 3000;

export default function AdminStorePage() {
  const router = useRouter();
  const [token, setToken] = useState("");
  const [slug, setSlug] = useState("acai-demo");
  const [segmentCode, setSegmentCode] = useState("acaiteria");
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: "Administração da Loja",
    primaryColorHex: "#1e293b",
    secondaryColorHex: "#f8fafc",
    accentColorHex: "#0f766e"
  });
  const [tab, setTab] = useState<AdminTabKey>("catalog");
  const [msg, setMsg] = useState<string | null>(null);
  const [toastNonce, setToastNonce] = useState(0);
  const [toastDurationMs, setToastDurationMs] = useState(ADMIN_TOAST_DURATION_MS);
  const [isPaused, setIsPaused] = useState(false);

  const notifyAdmin = useCallback((nextMessage: string) => {
    setMsg(nextMessage);
    setToastDurationMs(ADMIN_TOAST_DURATION_MS);
    setToastNonce((prev) => prev + 1);
    if (typeof window !== "undefined") {
      try {
        window.sessionStorage.setItem(
          ADMIN_TOAST_KEY,
          JSON.stringify({ message: nextMessage, expiresAt: Date.now() + ADMIN_TOAST_DURATION_MS })
        );
      } catch {
        // Ignora falha de storage
      }
    }
  }, []);

  function changeTab(next: AdminTabKey) {
    setTab(next);
    if (typeof window !== "undefined") {
      try {
        window.sessionStorage.setItem(ADMIN_TAB_KEY, next);
      } catch {
        // Ignora falha de storage
      }
    }
  }

  useEffect(() => {
    if (typeof window === "undefined") return;
    try {
      const searchParams = new URLSearchParams(window.location.search);
      const urlTab = searchParams.get("tab") as AdminTabKey | null;
      const urlConv = searchParams.get("conv");
      const savedTab = window.sessionStorage.getItem(ADMIN_TAB_KEY) as AdminTabKey | null;
      if (urlTab) {
        setTab(urlTab);
        window.sessionStorage.setItem(ADMIN_TAB_KEY, urlTab);
      } else if (savedTab) {
        setTab(savedTab);
      }
      if (urlConv) setChatConvId(urlConv);

      const rawToast = window.sessionStorage.getItem(ADMIN_TOAST_KEY);
      if (rawToast) {
        const parsed = JSON.parse(rawToast) as { message?: string; expiresAt?: number };
        const remaining = (parsed.expiresAt ?? 0) - Date.now();
        if (parsed.message && remaining > 50) {
          setMsg(parsed.message);
          setToastDurationMs(Math.min(ADMIN_TOAST_DURATION_MS, remaining));
          setToastNonce((prev) => prev + 1);
        } else {
          window.sessionStorage.removeItem(ADMIN_TOAST_KEY);
        }
      }
    } catch {
      // Ignora JSON inválido
    }
  }, []);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const handler = (ev: Event) => {
      const custom = ev as CustomEvent<{ conversationId?: string }>;
      changeTab("chat");
      if (custom.detail?.conversationId) setChatConvId(custom.detail.conversationId);
    };
    window.addEventListener("pitako:open-chat", handler);
    return () => window.removeEventListener("pitako:open-chat", handler);
  }, []);

  useEffect(() => {
    if (!msg) return;
    const t = setTimeout(() => {
      setMsg(null);
      if (typeof window !== "undefined") window.sessionStorage.removeItem(ADMIN_TOAST_KEY);
    }, toastDurationMs);
    return () => clearTimeout(t);
  }, [msg, toastNonce, toastDurationMs]);

  const [categories, setCategories] = useState<AdminCategoryDto[]>([]);
  const [products, setProducts] = useState<AdminProductDto[]>([]);
  const [modifierGroups, setModifierGroups] = useState<AdminModifierGroupDto[]>([]);
  const [acai, setAcai] = useState<AdminAcaiConfigDto>({ cupSizes: [], baseFlavors: [], freeToppings: [], paidAddons: [] });
  const [supplies, setSupplies] = useState<AdminSupplyDto[]>([]);
  const [techSheetSummaries, setTechSheetSummaries] = useState<TechnicalSheetSummaryItem[]>([]);
  const [pdfSheetModal, setPdfSheetModal] = useState<TechnicalSheetSummaryItem | null>(null);
  const [chatPrefill, setChatPrefill] = useState<string | null>(null);
  const [chatConvId, setChatConvId] = useState<string | null>(null);
  const [coupons, setCoupons] = useState<AdminCouponDto[]>([]);
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

    const [brandRes, catRes, acaiRes, supRes, sheetSummaryRes, couponRes, userRes] = await Promise.all([
      apiFetch<{
        legalName?: string;
        tradeName?: string;
        slug?: string;
        segmentCode?: string;
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
      apiFetch<TechnicalSheetSummaryItem[]>("/api/v1/inventory/technical-sheets/summary", {}, session.accessToken),
      apiFetch<AdminCouponDto[]>("/api/v1/coupons", {}, session.accessToken),
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
      if (brandRes.data.segmentCode) setSegmentCode(brandRes.data.segmentCode);
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
    if (sheetSummaryRes.ok && sheetSummaryRes.data) setTechSheetSummaries(sheetSummaryRes.data);
    if (couponRes.ok && couponRes.data) setCoupons(couponRes.data);
    if (userRes.ok && userRes.data) setTeam(userRes.data);
  }, []);

  useEffect(() => {
    void refreshStoreData();
  }, [refreshStoreData]);

  const handleReloadStoreData = useCallback(async () => {
    await refreshStoreData();
    router.refresh();
  }, [refreshStoreData, router]);

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
      notifyAdmin(nextPaused ? "Loja pausada no cardápio digital." : "Loja reaberta para receber pedidos!");
      await handleReloadStoreData();
    }
  }

  return (
    <StoreBrandShell
      theme={theme}
      activeSection="/admin"
      onStoreStatusChanged={(paused) => setIsPaused(paused)}
    >
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
      <div className="mb-6 flex flex-wrap gap-2 print:hidden">
        {(
          [
            { key: "catalog", label: "Cardápio" },
            { key: "chat", label: "Chat de Clientes" },
            { key: "whatsapp", label: "Bot WhatsApp & IA" },
            { key: "reports", label: "Relatórios & Motoboys" },
            { key: "modifiers", label: "Grupos & Adicionais" },
            { key: "acai", label: "Montador Açaí" },
            { key: "coupons", label: "Cupons" },
            { key: "printers", label: "Impressoras" },
            { key: "settings", label: "Configurações & PIX" },
            { key: "branding", label: "Visual & Vitrine" },
            { key: "security", label: "Segurança / Firewall" },
            { key: "team", label: "Equipe" }
          ] as const
        ).map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => changeTab(t.key)}
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
          slug={slug}
          segmentCode={segmentCode}
          storeTradeName={theme.tradeName}
          logoUrl={brandingData.logoUrl}
          slogan={brandingData.slogan}
          primaryColorHex={brandingData.primaryColorHex}
          accentColorHex={brandingData.accentColorHex}
          categories={categories}
          products={products}
          modifierGroups={modifierGroups}
          techSheetSummaries={techSheetSummaries}
          onOpenTechSheetPdf={(s) => setPdfSheetModal(s)}
          onNotify={notifyAdmin}
          onReload={handleReloadStoreData}
        />
      )}

      {tab === "chat" && (
        <AdminCustomerChatPanel
          token={token}
          storeTradeName={theme.tradeName}
          prefillMessage={chatPrefill}
          initialConversationId={chatConvId}
          onNotify={notifyAdmin}
        />
      )}

      {tab === "whatsapp" && (
        <AdminWhatsAppBotPanel token={token} storeTradeName={theme.tradeName} onNotify={notifyAdmin} />
      )}

      {tab === "reports" && (
        <AdminReportsAndSettlementPanel token={token} storeTradeName={theme.tradeName} />
      )}

      {tab === "coupons" && (
        <AdminCouponsPanel
          token={token}
          coupons={coupons}
          onSendInChat={(txt) => {
            setChatPrefill(txt);
            changeTab("chat");
          }}
          onNotify={notifyAdmin}
          onReload={handleReloadStoreData}
        />
      )}

      {tab === "printers" && <AdminPrintersPanel token={token} onNotify={notifyAdmin} />}

      {tab === "settings" && (
        <AdminOperationalSettingsPanel token={token} onNotify={notifyAdmin} onReload={handleReloadStoreData} />
      )}

      {tab === "security" && <AdminSecurityFirewallPanel token={token} />}

      {tab === "modifiers" && (
        <AdminModifiersTab
          token={token}
          groups={modifierGroups}
          supplies={supplies.map((s) => ({ id: s.id, name: s.name, contentUnitCode: s.contentUnitCode }))}
          onNotify={notifyAdmin}
          onReload={handleReloadStoreData}
        />
      )}

      {tab === "acai" && (
        <AdminAcaiTab token={token} acai={acai} onNotify={notifyAdmin} onReload={handleReloadStoreData} />
      )}

      {tab === "branding" && (
        <AdminBrandingAndStoreTab
          token={token}
          slug={slug}
          initialProfile={storeProfile}
          initialBranding={brandingData}
          onNotify={notifyAdmin}
          onReload={handleReloadStoreData}
        />
      )}

      {tab === "team" && (
        <AdminTeamTab token={token} team={team} onNotify={notifyAdmin} onReload={handleReloadStoreData} />
      )}

      <AdminTechSheetPdfModal
        token={token}
        sheet={pdfSheetModal}
        supplies={supplies.map((s) => ({
          id: s.id,
          name: s.name,
          contentUnitCode: s.contentUnitCode,
          costPerContentUnit: s.costPerContentUnit
        }))}
        storeTradeName={theme.tradeName}
        onSaved={handleReloadStoreData}
        onNotify={notifyAdmin}
        onClose={() => setPdfSheetModal(null)}
      />

      {msg && (
        <div
          role="status"
          aria-live="polite"
          className={`fixed bottom-5 right-5 z-50 flex max-w-md items-center gap-3 rounded-2xl border px-5 py-3.5 text-xs font-extrabold shadow-2xl print:hidden ${
            /erro|falh|inválid/i.test(msg)
              ? "border-rose-300 bg-rose-900 text-rose-50"
              : "border-emerald-300 bg-emerald-900 text-emerald-50"
          }`}
        >
          <span>{msg}</span>
          <button
            type="button"
            onClick={() => {
              setMsg(null);
              if (typeof window !== "undefined") window.sessionStorage.removeItem(ADMIN_TOAST_KEY);
            }}
            className="rounded px-1.5 py-0.5 text-xs opacity-75 hover:opacity-100"
            aria-label="Fechar notificação"
          >
            ✕
          </button>
        </div>
      )}
    </StoreBrandShell>
  );
}
