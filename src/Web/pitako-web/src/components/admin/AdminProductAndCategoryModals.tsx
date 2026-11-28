"use client";

import { CalendarClock, Check, Layers, PackageCheck, Plus, Sliders, Tag, Trash2, Upload, X } from "lucide-react";
import { useState } from "react";
import { apiFetch, compressImageFileToDataUrl, resolveMediaUrl } from "@/lib/api";
import { AdminCategoryDto, AdminModifierGroupSummary, AdminProductDto } from "./AdminCatalogTab";

export interface ModifierGroupDetailedDto extends AdminModifierGroupSummary {
  groupType?: number;
  isRequired?: boolean;
  countingMode?: number;
  options?: Array<{ id: string; name: string; additionalPrice: number }>;
}

interface ScheduleDayEntry {
  dayOfWeek: number;
  isAvailable: boolean;
  startTime: string;
  endTime: string;
}

const WEEK_DAYS = [
  { day: 0, label: "Dom" },
  { day: 1, label: "Seg" },
  { day: 2, label: "Ter" },
  { day: 3, label: "Qua" },
  { day: 4, label: "Qui" },
  { day: 5, label: "Sex" },
  { day: 6, label: "Sáb" }
];

const DEFAULT_SCHEDULE_DAYS: ScheduleDayEntry[] = WEEK_DAYS.map((d) => ({
  dayOfWeek: d.day,
  isAvailable: true,
  startTime: "10:00",
  endTime: "23:00"
}));

function parseScheduleJson(json?: string | null): { enabled: boolean; days: ScheduleDayEntry[] } {
  if (!json) return { enabled: false, days: DEFAULT_SCHEDULE_DAYS.map((d) => ({ ...d })) };
  try {
    const parsed = JSON.parse(json) as ScheduleDayEntry[];
    if (!Array.isArray(parsed) || parsed.length === 0) {
      return { enabled: false, days: DEFAULT_SCHEDULE_DAYS.map((d) => ({ ...d })) };
    }
    const merged = WEEK_DAYS.map((wd) => {
      const found = parsed.find((x) => Number(x.dayOfWeek) === wd.day);
      return found
        ? {
            dayOfWeek: wd.day,
            isAvailable: Boolean(found.isAvailable),
            startTime: found.startTime || "10:00",
            endTime: found.endTime || "23:00"
          }
        : { dayOfWeek: wd.day, isAvailable: false, startTime: "10:00", endTime: "23:00" };
    });
    return { enabled: true, days: merged };
  } catch {
    return { enabled: false, days: DEFAULT_SCHEDULE_DAYS.map((d) => ({ ...d })) };
  }
}

function ScheduleWindowEditor({
  enabled,
  days,
  onToggleEnabled,
  onChangeDays
}: {
  enabled: boolean;
  days: ScheduleDayEntry[];
  onToggleEnabled: (next: boolean) => void;
  onChangeDays: (next: ScheduleDayEntry[]) => void;
}) {
  return (
    <div className="space-y-3 rounded-2xl border border-slate-200 bg-slate-50/80 p-3.5 text-xs">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <p className="flex items-center gap-1.5 font-extrabold text-slate-900">
            <CalendarClock className="h-4 w-4 text-indigo-600" /> Disponibilidade Programada
          </p>
          <p className="text-[11px] text-slate-500">Defina dias e horários específicos de exibição na vitrine.</p>
        </div>
        <label className="inline-flex cursor-pointer items-center gap-2 rounded-xl border border-slate-300 bg-white px-3 py-1.5 font-bold text-slate-800">
          <input type="checkbox" checked={enabled} onChange={(e) => onToggleEnabled(e.target.checked)} />
          {enabled ? "Usar agenda personalizada" : "Sempre disponível (horário da loja)"}
        </label>
      </div>

      {enabled && (
        <div className="space-y-2">
          {WEEK_DAYS.map((wd) => {
            const entry = days.find((d) => d.dayOfWeek === wd.day) || {
              dayOfWeek: wd.day,
              isAvailable: true,
              startTime: "10:00",
              endTime: "23:00"
            };
            return (
              <div key={wd.day} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2">
                <label className="flex min-w-[85px] cursor-pointer items-center gap-2 font-bold text-slate-800">
                  <input
                    type="checkbox"
                    checked={entry.isAvailable}
                    onChange={(e) =>
                      onChangeDays(
                        days.map((d) => (d.dayOfWeek === wd.day ? { ...d, isAvailable: e.target.checked } : d))
                      )
                    }
                  />
                  {wd.label}
                </label>
                {entry.isAvailable ? (
                  <div className="flex items-center gap-2">
                    <input
                      type="time"
                      value={entry.startTime}
                      onChange={(e) =>
                        onChangeDays(
                          days.map((d) => (d.dayOfWeek === wd.day ? { ...d, startTime: e.target.value } : d))
                        )
                      }
                      className="rounded-lg border border-slate-300 px-2 py-1 font-mono text-xs"
                    />
                    <span className="text-slate-400">até</span>
                    <input
                      type="time"
                      value={entry.endTime}
                      onChange={(e) =>
                        onChangeDays(
                          days.map((d) => (d.dayOfWeek === wd.day ? { ...d, endTime: e.target.value } : d))
                        )
                      }
                      className="rounded-lg border border-slate-300 px-2 py-1 font-mono text-xs"
                    />
                  </div>
                ) : (
                  <span className="text-[11px] font-semibold text-slate-400">Pausado neste dia</span>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

interface SegmentLabels {
  freeTitle: string;
  extrasTitle: string;
  sizesTitle: string;
  sizesHint: string;
  allowanceLabel: string;
  addSizeText: string;
  units: string[];
  defaultUnit: string;
}

export function getSegmentLabels(segmentCode?: string): SegmentLabels {
  const seg = (segmentCode || "acaiteria").toLowerCase();
  if (seg === "pizzaria") {
    return {
      freeTitle: "Sabores & Massas Disponíveis para esta Pizza:",
      extrasTitle: "Habilitar Bordas Recheadas & Adicionais neste Produto",
      sizesTitle: "Tamanhos, Fatias, Sabores Inclusos e Custos de Insumo",
      sizesHint: "Defina tamanhos (ex: Broto 4 fatias, Grande 8 fatias). Controle preço, custo estimado, limite de sabores e adicionais por tamanho.",
      allowanceLabel: "Sabores Inclusos",
      addSizeText: "+ Adicionar tamanho de pizza",
      units: ["fatias", "cm", "un"],
      defaultUnit: "fatias"
    };
  }
  if (seg === "hamburgueria") {
    return {
      freeTitle: "Pães, Pontos, Molhos & Saladas Disponíveis:",
      extrasTitle: "Habilitar Carnes Extras, Queijos & Adicionais Pagos",
      sizesTitle: "Versões, Combos, Inclusos e Custos de Insumo",
      sizesHint: "Defina versões/tamanhos (ex: Tradicional, Duplo, Combo c/ Fritas + Refri), itens inclusos, preço de venda e custo CMV.",
      allowanceLabel: "Itens Inclusos",
      addSizeText: "+ Adicionar variação / combo",
      units: ["g", "un", "ml"],
      defaultUnit: "g"
    };
  }
  if (seg === "restaurante" || seg === "lanchonete" || seg === "pastelaria" || seg === "padaria" || seg === "doceria" || seg === "adega") {
    return {
      freeTitle: "Acompanhamentos, Sabores & Opções Inclusas:",
      extrasTitle: "Habilitar Adicionais Extras Pagos neste Produto",
      sizesTitle: "Tamanhos, Porções, Inclusos e Custos de Insumo",
      sizesHint: "Configure porções/tamanhos (ex: P, M, G, Individual, Família), quantidade inclusa grátis, preço e custo real de preparo.",
      allowanceLabel: "Inclusos Grátis",
      addSizeText: "+ Adicionar tamanho / porção",
      units: ["un", "g", "ml", "kg", "L"],
      defaultUnit: "un"
    };
  }
  return {
    freeTitle: "Acompanhamentos Disponíveis para este Produto:",
    extrasTitle: "Habilitar Adicionais Extras neste Produto",
    sizesTitle: "Tamanhos, Preços, Acompanhamentos Inclusos e Custos de Insumo",
    sizesHint: "Defina os tamanhos em ml ou Litro(s) (Ex: 300 ml, 500 ml, 700 ml, 1 Litro). Controle preço, custo, limite grátis e extras por tamanho.",
    allowanceLabel: "Acomp. Inclusos",
    addSizeText: "+ Adicionar tamanho",
    units: ["ml", "L", "g", "un"],
    defaultUnit: "ml"
  };
}

const CATEGORY_TEMPLATES = [
  { id: 1, title: "Padrão (Itens Gerais)", desc: "Pratos, lanches, bebidas, sobremesas, produtos de varejo." },
  { id: 2, title: "Montável / Pizza", desc: "Tamanhos + escolha de múltiplos sabores, bordas ou acompanhamentos." },
  { id: 3, title: "Combos / Promoções", desc: "Agrupamento promocional e kits em destaque." }
];

export function AdminCategoryEditModal({
  token,
  category,
  nextDisplayOrder,
  onClose,
  onNotify,
  onSaved
}: {
  token: string;
  category: AdminCategoryDto | null;
  nextDisplayOrder: number;
  onClose: () => void;
  onNotify: (msg: string) => void;
  onSaved: () => Promise<void>;
}) {
  const initialSched = parseScheduleJson(category?.availabilityScheduleJson);
  const [name, setName] = useState(category?.name || "");
  const [description, setDescription] = useState(category?.description || "");
  const [displayOrder, setDisplayOrder] = useState(String(category?.displayOrder ?? nextDisplayOrder));
  const [templateKind, setTemplateKind] = useState<number>(category?.templateKind ?? 1);
  const [schedEnabled, setSchedEnabled] = useState<boolean>(initialSched.enabled);
  const [schedDays, setSchedDays] = useState<ScheduleDayEntry[]>(initialSched.days);
  const [saving, setSaving] = useState(false);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    const payload = {
      name: name.trim(),
      description: description.trim() || null,
      imageUrl: category?.imageUrl || null,
      displayOrder: Math.max(0, Number(displayOrder) || 1),
      isActive: true,
      segmentTemplateCode: category?.segmentTemplateCode || null,
      ruleNotes: category?.ruleNotes || null,
      templateKind,
      availabilityScheduleJson: schedEnabled ? JSON.stringify(schedDays) : ""
    };

    const res = category
      ? await apiFetch(`/api/v1/catalog/categories/${category.id}`, { method: "PUT", body: JSON.stringify(payload) }, token)
      : await apiFetch("/api/v1/catalog/categories", { method: "POST", body: JSON.stringify(payload) }, token);

    setSaving(false);
    if (res.ok) {
      onNotify(category ? `Categoria "${name}" atualizada!` : "Nova categoria criada!");
      await onSaved();
      onClose();
    } else {
      onNotify(res.errorMessage || "Erro ao salvar categoria.");
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/70 p-4 backdrop-blur-sm">
      <form onSubmit={handleSave} className="max-h-[92vh] w-full max-w-lg overflow-y-auto rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl space-y-4">
        <div className="flex items-center justify-between">
          <h3 className="text-base font-extrabold text-slate-900">
            {category ? `Editar Categoria: ${category.name}` : "Nova Categoria do Cardápio"}
          </h3>
          <button type="button" onClick={onClose} className="rounded-lg p-1 text-slate-400 hover:bg-slate-100">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="space-y-3 text-xs">
          <div>
            <label className="font-bold text-slate-700">Formato da Categoria</label>
            <div className="mt-1.5 grid gap-2 sm:grid-cols-3">
              {CATEGORY_TEMPLATES.map((tpl) => {
                const selected = templateKind === tpl.id;
                return (
                  <button
                    key={tpl.id}
                    type="button"
                    onClick={() => setTemplateKind(tpl.id)}
                    className={`rounded-xl border p-2.5 text-left transition ${
                      selected
                        ? "border-indigo-600 bg-indigo-50 ring-2 ring-indigo-200"
                        : "border-slate-200 bg-white hover:bg-slate-50"
                    }`}
                  >
                    <p className="font-extrabold text-slate-900">{tpl.title}</p>
                    <p className="mt-0.5 text-[10px] leading-snug text-slate-500">{tpl.desc}</p>
                  </button>
                );
              })}
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <div className="sm:col-span-2">
              <label className="font-bold text-slate-700">Nome da Categoria</label>
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                placeholder="Ex: Pizzas Tradicionais ou Combos"
                className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 text-sm font-semibold"
              />
            </div>
            <div>
              <label className="font-bold text-slate-700">Ordem de Exibição</label>
              <input
                type="number"
                min="1"
                value={displayOrder}
                onChange={(e) => setDisplayOrder(e.target.value)}
                className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 font-mono text-sm font-bold"
              />
            </div>
          </div>

          <div>
            <label className="font-bold text-slate-700">Subtítulo / Frase de Destaque</label>
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Ex: Mais sabor e muito mais economia para o seu momento"
              className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 text-xs"
            />
          </div>

          <ScheduleWindowEditor
            enabled={schedEnabled}
            days={schedDays}
            onToggleEnabled={setSchedEnabled}
            onChangeDays={setSchedDays}
          />
        </div>

        <div className="flex gap-2 pt-2">
          <button
            type="submit"
            disabled={saving}
            className="flex-1 rounded-xl bg-emerald-600 py-2.5 text-xs font-extrabold text-white hover:bg-emerald-700 disabled:opacity-50"
          >
            {saving ? "Salvando..." : "Salvar Categoria"}
          </button>
          <button type="button" onClick={onClose} className="rounded-xl border border-slate-300 px-4 py-2.5 text-xs font-bold text-slate-700">
            Cancelar
          </button>
        </div>
      </form>
    </div>
  );
}

type CupAddonMode = "both" | "free_only" | "paid_only" | "none";

interface SizeFormRow {
  id?: string | null;
  variationName: string;
  description: string;
  sku: string;
  measureAmount: string;
  measureUnit: string;
  price: string;
  costAmount: string;
  freeAllowance: string;
  addonMode: CupAddonMode;
  allowPaidExtras: boolean;
  trackInventory: boolean;
  stockQuantity: string;
}

const DIETARY_SEALS = [
  { id: "vegano", label: "🌱 Vegano" },
  { id: "vegetariano", label: "🥗 Vegetariano" },
  { id: "sem-gluten", label: "🌾 Sem Glúten" },
  { id: "sem-lactose", label: "🥛 Sem Lactose" },
  { id: "alcoolico-18", label: "🔞 Bebida Alcoólica +18" },
  { id: "artesanal", label: "✨ Artesanal" }
];

type ProductTabId = "basic" | "pricing" | "variations" | "modifiers" | "schedule";

export function AdminProductFullModal({
  token,
  segmentCode,
  categories,
  modifierGroups,
  product,
  estimatedSheetCost,
  onClose,
  onNotify,
  onSaved
}: {
  token: string;
  segmentCode?: string;
  categories: AdminCategoryDto[];
  modifierGroups: ModifierGroupDetailedDto[];
  product: AdminProductDto | null;
  estimatedSheetCost?: number;
  onClose: () => void;
  onNotify: (msg: string) => void;
  onSaved: () => Promise<void>;
}) {
  const labels = getSegmentLabels(segmentCode);
  const [activeTab, setActiveTab] = useState<ProductTabId>("basic");

  const freeGroups = modifierGroups.filter((g) => g.selectionKind !== 3 && g.groupType !== 2);
  const paidGroups = modifierGroups.filter((g) => g.selectionKind === 3 || g.groupType === 2);

  // Aba 1: Dados Básicos
  const [name, setName] = useState(product?.name || "");
  const [categoryId, setCategoryId] = useState(product?.categoryId || categories[0]?.id || "");
  const [displayOrder, setDisplayOrder] = useState(String(product?.displayOrder ?? 1));
  const [sku, setSku] = useState(product?.sku || "");
  const [preparationMinutes, setPreparationMinutes] = useState(product?.preparationMinutes ? String(product.preparationMinutes) : "15");
  const [isActive, setIsActive] = useState(product ? product.status === "Active" : true);
  const [isFeatured, setIsFeatured] = useState(Boolean(product?.isFeatured));
  const [isNew, setIsNew] = useState(Boolean(product?.isNew));
  const [allowsNotes, setAllowsNotes] = useState(product?.allowsNotes ?? true);
  const [description, setDescription] = useState(product?.description || "");
  const [imageUrl, setImageUrl] = useState(product?.primaryImageUrl || "");
  const [uploadingImg, setUploadingImg] = useState(false);

  // Aba 2: Preço & Estoque
  const [basePrice, setBasePrice] = useState(product ? String(product.basePrice) : "19.90");
  const [promotionalPrice, setPromotionalPrice] = useState(product?.promotionalPrice ? String(product.promotionalPrice) : "");
  const [isPromotion, setIsPromotion] = useState(Boolean(product?.tagsCsv?.toLowerCase().includes("promo") || product?.promotionalPrice));
  const [trackInventory, setTrackInventory] = useState(Boolean(product?.trackInventory));
  const [stockQuantity, setStockQuantity] = useState(String(product?.stockQuantity ?? 10));
  const [autoPauseOnZeroStock, setAutoPauseOnZeroStock] = useState(Boolean(product?.autoPauseOnZeroStock));

  // Aba 3: Variações & Estratégia
  const [pricingStrategy, setPricingStrategy] = useState<number>(product?.pricingStrategy ?? (segmentCode?.toLowerCase() === "pizzaria" ? 2 : 1));
  const [sizes, setSizes] = useState<SizeFormRow[]>(() => {
    if (product && product.variations.length > 0) {
      return product.variations.map((v) => {
        const freeRule = product.variationRules.find((r) => r.variationId === v.id && freeGroups.some((fg) => fg.id === r.modifierGroupId));
        const paidRule = product.variationRules.find((r) => r.variationId === v.id && paidGroups.some((pg) => pg.id === r.modifierGroupId));
        const costMatch = v.description?.match(/Custo\s*(?:estimado)?\s*R\$\s*([\d.,]+)/i);
        const allowFree = freeRule ? !freeRule.isHidden : true;
        const allowPaid = paidRule ? !paidRule.isHidden : true;
        const resolvedMode: CupAddonMode =
          allowFree && allowPaid ? "both" : allowFree ? "free_only" : allowPaid ? "paid_only" : "none";
        const resolvedCost =
          v.costPrice !== undefined && v.costPrice > 0
            ? String(v.costPrice)
            : costMatch
              ? costMatch[1].replace(",", ".")
              : estimatedSheetCost
                ? String(estimatedSheetCost)
                : "0";
        const cleanDesc = v.description && costMatch && v.description.trim().startsWith("Custo") ? "" : (v.description || "");
        return {
          id: v.id,
          variationName: v.variationName,
          description: cleanDesc,
          sku: v.sku || "",
          measureAmount: v.measureAmount ? String(v.measureAmount) : "",
          measureUnit: v.measureUnit || labels.defaultUnit,
          price: String(v.price),
          costAmount: resolvedCost,
          freeAllowance: String(freeRule?.freeAllowance ?? Math.max(0, v.maxFlavors || 3)),
          addonMode: resolvedMode,
          allowPaidExtras: allowPaid,
          trackInventory: Boolean(v.trackInventory),
          stockQuantity: String(v.stockQuantity ?? 10)
        };
      });
    }
    if (segmentCode?.toLowerCase() === "pizzaria") {
      return [
        { variationName: "Broto (4 fatias)", description: "4 fatias individuais", sku: "", measureAmount: "4", measureUnit: "fatias", price: basePrice || "32.90", costAmount: "11.50", freeAllowance: "2", addonMode: "both", allowPaidExtras: true, trackInventory: false, stockQuantity: "10" },
        { variationName: "Grande (8 fatias)", description: "8 fatias tradicionais", sku: "", measureAmount: "8", measureUnit: "fatias", price: "49.90", costAmount: "16.80", freeAllowance: "2", addonMode: "both", allowPaidExtras: true, trackInventory: false, stockQuantity: "10" }
      ];
    }
    if (segmentCode?.toLowerCase() === "acaiteria" || !segmentCode) {
      return [
        { variationName: "300 ml", description: "Copo 300 ml cremoso", sku: "", measureAmount: "300", measureUnit: "ml", price: "15.90", costAmount: "6.80", freeAllowance: "3", addonMode: "both", allowPaidExtras: true, trackInventory: false, stockQuantity: "10" },
        { variationName: "500 ml", description: "Copo 500 ml caprichado", sku: "", measureAmount: "500", measureUnit: "ml", price: "19.90", costAmount: "9.40", freeAllowance: "4", addonMode: "both", allowPaidExtras: true, trackInventory: false, stockQuantity: "10" },
        { variationName: "700 ml", description: "Copo 700 ml tamanho família", sku: "", measureAmount: "700", measureUnit: "ml", price: "26.90", costAmount: "12.50", freeAllowance: "5", addonMode: "both", allowPaidExtras: true, trackInventory: false, stockQuantity: "10" }
      ];
    }
    return [];
  });

  // Aba 4: Complementos & Selos
  const [selectedFreeGroupIds, setSelectedFreeGroupIds] = useState<string[]>(() => {
    if (!product) return freeGroups.map((g) => g.id);
    const attached = new Set(product.modifierGroupIds || []);
    return freeGroups.filter((g) => attached.has(g.id)).map((g) => g.id);
  });

  const [enablePaidExtras, setEnablePaidExtras] = useState<boolean>(() => {
    if (!product) return paidGroups.length > 0;
    const attached = new Set(product.modifierGroupIds || []);
    return paidGroups.some((g) => attached.has(g.id));
  });

  const [selectedPaidGroupIds, setSelectedPaidGroupIds] = useState<string[]>(() => {
    if (!product) return paidGroups.map((g) => g.id);
    const attached = new Set(product.modifierGroupIds || []);
    return paidGroups.filter((g) => attached.has(g.id)).map((g) => g.id);
  });

  const [servesPeopleCount, setServesPeopleCount] = useState<string>(product?.servesPeopleCount ? String(product.servesPeopleCount) : "");
  const [dietaryTags, setDietaryTags] = useState<string[]>(() =>
    product?.dietaryTagsCsv
      ? product.dietaryTagsCsv.split(",").map((x) => x.trim().toLowerCase()).filter(Boolean)
      : []
  );

  // Aba 5: Agenda
  const initialProdSched = parseScheduleJson(product?.availabilityScheduleJson);
  const [schedEnabled, setSchedEnabled] = useState<boolean>(initialProdSched.enabled);
  const [schedDays, setSchedDays] = useState<ScheduleDayEntry[]>(initialProdSched.days);

  const [saving, setSaving] = useState(false);

  async function handleFileUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploadingImg(true);
    try {
      const compressedDataUrl = await compressImageFileToDataUrl(file, 480, 0.75);
      const upRes = await apiFetch<{ url: string }>(
        "/api/v1/catalog/upload-image",
        { method: "POST", body: JSON.stringify({ base64DataUrl: compressedDataUrl, fileName: file.name }) },
        token
      );
      if (upRes.ok && upRes.data?.url) {
        setImageUrl(upRes.data.url);
        onNotify("Foto carregada do computador com sucesso!");
      } else {
        onNotify(upRes.errorMessage || "Erro ao enviar foto.");
      }
    } catch {
      onNotify("Não foi possível processar o arquivo de imagem.");
    }
    setUploadingImg(false);
  }

  function addSizeRow() {
    setSizes([
      ...sizes,
      {
        variationName: segmentCode?.toLowerCase() === "pizzaria" ? "Família (12 fatias)" : "1 Litro",
        description: segmentCode?.toLowerCase() === "pizzaria" ? "12 fatias família" : "Pote / Copo 1 Litro",
        sku: "",
        measureAmount: segmentCode?.toLowerCase() === "pizzaria" ? "12" : "1000",
        measureUnit: labels.defaultUnit,
        price: "34.90",
        costAmount: "14.00",
        freeAllowance: "4",
        addonMode: "both",
        allowPaidExtras: true,
        trackInventory: false,
        stockQuantity: "10"
      }
    ]);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim() || !categoryId) {
      setActiveTab("basic");
      return;
    }
    setSaving(true);

    let targetId = product?.id;
    const effectiveBase = sizes.length > 0 ? Math.min(...sizes.map((s) => Number(s.price) || 1)) : Number(basePrice) || 1;
    const parsedPromo = promotionalPrice && Number(promotionalPrice) > 0 && Number(promotionalPrice) < effectiveBase ? Number(promotionalPrice) : null;
    const parsedOrder = Math.max(0, Number(displayOrder) || 1);
    const parsedPrep = preparationMinutes ? Math.max(1, Number(preparationMinutes) || 15) : null;
    const parsedServes = servesPeopleCount ? Math.max(1, Number(servesPeopleCount)) : null;

    if (!targetId) {
      const createRes = await apiFetch<{ id: string }>(
        "/api/v1/catalog/products",
        {
          method: "POST",
          body: JSON.stringify({
            categoryId,
            name: name.trim(),
            basePrice: Math.max(0.01, effectiveBase),
            description: description.trim() || null,
            sku: sku.trim() || null,
            isFeatured,
            displayOrder: parsedOrder,
            isNew,
            isRecommended: false,
            pricingStrategy,
            preparationMinutes: parsedPrep,
            allowsNotes,
            promotionalPrice: parsedPromo,
            trackInventory,
            stockQuantity: Math.max(0, Number(stockQuantity) || 0),
            autoPauseOnZeroStock,
            servesPeopleCount: parsedServes,
            dietaryTagsCsv: dietaryTags.join(",") || null,
            availabilityScheduleJson: schedEnabled ? JSON.stringify(schedDays) : ""
          })
        },
        token
      );
      if (!createRes.ok || !createRes.data?.id) {
        setSaving(false);
        onNotify(createRes.errorMessage || "Erro ao criar produto.");
        return;
      }
      targetId = createRes.data.id;
    }

    const attachedGroupIds = Array.from(
      new Set([...selectedFreeGroupIds, ...(enablePaidExtras ? selectedPaidGroupIds : [])])
    );

    const res = await apiFetch(
      `/api/v1/catalog/products/${targetId}/full-config`,
      {
        method: "PUT",
        body: JSON.stringify({
          categoryId,
          name: name.trim(),
          description: description.trim() || null,
          basePrice: Math.max(0.01, effectiveBase),
          sku: sku.trim() || null,
          preparationMinutes: parsedPrep,
          allowsNotes,
          isActive,
          isNew,
          isPromotion: isPromotion || parsedPromo !== null,
          isFeatured,
          displayOrder: parsedOrder,
          pricingStrategy,
          imageUrl: imageUrl || null,
          promotionalPrice: parsedPromo,
          trackInventory,
          stockQuantity: Math.max(0, Number(stockQuantity) || 0),
          autoPauseOnZeroStock,
          servesPeopleCount: parsedServes,
          dietaryTagsCsv: dietaryTags.join(",") || "",
          availabilityScheduleJson: schedEnabled ? JSON.stringify(schedDays) : "",
          attachedGroupIds,
          sizes: sizes.map((s) => ({
            id: s.id || null,
            variationName: s.variationName.trim() || `${s.measureAmount} ${s.measureUnit}`.trim(),
            description: s.description.trim() || null,
            sku: s.sku.trim() || null,
            price: Math.max(0.01, Number(s.price) || effectiveBase),
            measureAmount: s.measureAmount ? Number(s.measureAmount) : null,
            measureUnit: s.measureUnit || null,
            maxFlavors: segmentCode?.toLowerCase() === "pizzaria" || pricingStrategy !== 1 ? Math.max(1, Number(s.freeAllowance) || 2) : 1,
            freeAllowance: s.addonMode === "paid_only" || s.addonMode === "none" ? 0 : Math.max(0, Number(s.freeAllowance) || 0),
            costAmount: s.costAmount ? Number(s.costAmount) : 0,
            allowFree: s.addonMode === "both" || s.addonMode === "free_only",
            allowPaidExtras: s.addonMode === "both" || s.addonMode === "paid_only",
            addonMode: s.addonMode,
            isActive: true,
            trackInventory: s.trackInventory,
            stockQuantity: Math.max(0, Number(s.stockQuantity) || 0)
          }))
        })
      },
      token
    );

    setSaving(false);
    if (res.ok) {
      onNotify(`Produto "${name}" salvo com sucesso!`);
      await onSaved();
      onClose();
    } else {
      onNotify(res.errorMessage || "Erro ao salvar configuração do produto.");
    }
  }

  const effectivePreviewBase = sizes.length > 0 ? Math.min(...sizes.map((s) => Number(s.price) || 1)) : Number(basePrice) || 1;
  const promoNum = Number(promotionalPrice) || 0;
  const promoDiscountPct =
    promoNum > 0 && promoNum < effectivePreviewBase
      ? Math.round(((effectivePreviewBase - promoNum) / effectivePreviewBase) * 100)
      : 0;

  const tabs: Array<{ id: ProductTabId; label: string; icon: React.ReactNode }> = [
    { id: "basic", label: "1. Dados Básicos", icon: <Layers className="h-3.5 w-3.5" /> },
    { id: "pricing", label: "2. Preço & Estoque", icon: <PackageCheck className="h-3.5 w-3.5" /> },
    { id: "variations", label: `3. Tamanhos (${sizes.length})`, icon: <Sliders className="h-3.5 w-3.5" /> },
    { id: "modifiers", label: "4. Complementos & Selos", icon: <Tag className="h-3.5 w-3.5" /> },
    { id: "schedule", label: "5. Disponibilidade", icon: <CalendarClock className="h-3.5 w-3.5" /> }
  ];

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/75 p-3 backdrop-blur-sm sm:p-6">
      <form
        onSubmit={handleSubmit}
        className="flex max-h-[92vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl"
      >
        <div className="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <div>
            <h2 className="text-base font-extrabold text-slate-900">
              {product ? `Editar Produto: ${product.name}` : "Novo Produto Completo (Padrão iFood)"}
            </h2>
            <p className="text-[11px] text-slate-500">Organizado em 5 etapas: Dados, Preço/Estoque, Tamanhos, Complementos/Selos e Agenda.</p>
          </div>
          <button type="button" onClick={onClose} className="rounded-lg p-1 text-slate-400 hover:bg-slate-100">
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Barra de Abas iFood */}
        <div className="flex gap-1 overflow-x-auto border-b border-slate-200 bg-slate-50 px-6 py-2">
          {tabs.map((tab) => {
            const active = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => setActiveTab(tab.id)}
                className={`inline-flex shrink-0 items-center gap-1.5 rounded-xl px-3 py-2 text-xs font-bold transition ${
                  active ? "bg-slate-900 text-white shadow-sm" : "bg-white text-slate-600 hover:bg-slate-100"
                }`}
              >
                {tab.icon}
                {tab.label}
              </button>
            );
          })}
        </div>

        <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5 text-xs">
          {/* ABA 1: DADOS BÁSICOS */}
          {activeTab === "basic" && (
            <div className="space-y-4">
              <div className="grid gap-3 sm:grid-cols-4">
                <div className="sm:col-span-2">
                  <label className="font-bold text-slate-700">Nome do Produto *</label>
                  <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    required
                    placeholder="Ex: Monte do seu jeito ou X-Burger Artesanal"
                    className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 text-sm font-semibold"
                  />
                </div>
                <div>
                  <label className="font-bold text-slate-700">Categoria *</label>
                  <select
                    value={categoryId}
                    onChange={(e) => setCategoryId(e.target.value)}
                    className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 text-sm font-semibold"
                  >
                    {[...categories].sort((a, b) => a.displayOrder - b.displayOrder).map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="font-bold text-slate-700">Ordem na Cat.</label>
                  <input
                    type="number"
                    min="1"
                    value={displayOrder}
                    onChange={(e) => setDisplayOrder(e.target.value)}
                    className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 font-mono text-sm font-bold"
                  />
                </div>
              </div>

              <div className="grid gap-3 sm:grid-cols-3">
                <div>
                  <label className="font-bold text-slate-700">Código SKU / PDV (Opcional)</label>
                  <input
                    value={sku}
                    onChange={(e) => setSku(e.target.value)}
                    placeholder="Ex: ACAI-500"
                    className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 font-mono text-xs uppercase"
                  />
                </div>
                <div>
                  <label className="font-bold text-slate-700">Tempo Médio Preparo (min)</label>
                  <input
                    type="number"
                    min="1"
                    value={preparationMinutes}
                    onChange={(e) => setPreparationMinutes(e.target.value)}
                    className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 font-mono text-xs"
                  />
                </div>
                <div>
                  <label className="font-bold text-slate-700">Status de Exibição</label>
                  <select
                    value={isActive ? "active" : "paused"}
                    onChange={(e) => setIsActive(e.target.value === "active")}
                    className={`mt-1 w-full rounded-xl border px-3 py-2 text-xs font-extrabold ${
                      isActive ? "border-emerald-300 bg-emerald-50 text-emerald-800" : "border-amber-300 bg-amber-50 text-amber-800"
                    }`}
                  >
                    <option value="active">● Ativo no Cardápio</option>
                    <option value="paused">⏸ Pausado Temporariamente</option>
                  </select>
                </div>
              </div>

              <div className="grid gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3 sm:grid-cols-3">
                <label className="flex cursor-pointer items-center gap-2 font-bold text-slate-800">
                  <input type="checkbox" checked={isFeatured} onChange={(e) => setIsFeatured(e.target.checked)} />
                  Destacar em Mais Vendidos
                </label>
                <label className="flex cursor-pointer items-center gap-2 font-bold text-slate-800">
                  <input type="checkbox" checked={isNew} onChange={(e) => setIsNew(e.target.checked)} />
                  Badge Lançamento / Novidade
                </label>
                <label className="flex cursor-pointer items-center gap-2 font-bold text-slate-800">
                  <input type="checkbox" checked={allowsNotes} onChange={(e) => setAllowsNotes(e.target.checked)} />
                  Permitir campo de observações
                </label>
              </div>

              <div>
                <label className="font-bold text-slate-700">Descrição / Ingredientes</label>
                <textarea
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  rows={2}
                  placeholder="Use sua criatividade e monte do seu jeito!"
                  className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 text-xs"
                />
              </div>

              <div>
                <label className="font-bold text-slate-700">Foto do Produto (Upload do PC ou URL)</label>
                <div className="mt-1.5 flex flex-wrap items-center gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3">
                  {resolveMediaUrl(imageUrl) ? (
                    <img
                      src={resolveMediaUrl(imageUrl)!}
                      alt={name || "Preview"}
                      className="h-14 w-14 rounded-xl border border-slate-200 bg-white object-cover"
                    />
                  ) : (
                    <div className="flex h-14 w-14 items-center justify-center rounded-xl border border-dashed border-slate-300 bg-white text-[10px] text-slate-400">
                      Sem foto
                    </div>
                  )}
                  <label className="inline-flex cursor-pointer items-center gap-2 rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs font-bold text-slate-800 shadow-sm hover:bg-slate-100">
                    <Upload className="h-3.5 w-3.5" />
                    {uploadingImg ? "Enviando foto..." : "Escolher arquivo do PC"}
                    <input type="file" accept="image/*" onChange={handleFileUpload} className="hidden" />
                  </label>
                  <input
                    value={imageUrl}
                    onChange={(e) => setImageUrl(e.target.value)}
                    placeholder="Ou cole URL opcional..."
                    className="min-w-0 flex-1 rounded-xl border border-slate-200 bg-white px-2.5 py-2 text-[11px]"
                  />
                </div>
              </div>
            </div>
          )}

          {/* ABA 2: PREÇO & ESTOQUE */}
          {activeTab === "pricing" && (
            <div className="space-y-4">
              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4 space-y-3">
                <p className="font-extrabold text-slate-900">Precificação & Promoção Direta (&quot;De / Por&quot;)</p>
                <div className="grid gap-3 sm:grid-cols-2">
                  <div>
                    <label className="font-bold text-slate-700">
                      {sizes.length > 0 ? "Preço Base Inicial (sincronizado c/ menor tamanho)" : "Preço Base de Venda (R$)"}
                    </label>
                    <input
                      type="number"
                      step="0.01"
                      disabled={sizes.length > 0}
                      value={sizes.length > 0 ? String(effectivePreviewBase) : basePrice}
                      onChange={(e) => setBasePrice(e.target.value)}
                      className="mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 font-mono text-sm font-bold disabled:bg-slate-100"
                    />
                  </div>
                  <div>
                    <label className="flex items-center justify-between font-bold text-slate-700">
                      <span>Preço Promocional &quot;Por&quot; (R$)</span>
                      {promoDiscountPct > 0 && (
                        <span className="rounded-full bg-rose-600 px-2 py-0.5 text-[10px] font-extrabold text-white">
                          -{promoDiscountPct}% OFF
                        </span>
                      )}
                    </label>
                    <input
                      type="number"
                      step="0.01"
                      value={promotionalPrice}
                      onChange={(e) => setPromotionalPrice(e.target.value)}
                      placeholder="Ex: menor que o preço base (opcional)"
                      className="mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 font-mono text-sm font-bold text-rose-700"
                    />
                  </div>
                </div>
                <label className="flex cursor-pointer items-center gap-2 font-bold text-slate-800">
                  <input type="checkbox" checked={isPromotion} onChange={(e) => setIsPromotion(e.target.checked)} />
                  Exibir badge de &quot;Promoção&quot; na vitrine
                </label>
              </div>

              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4 space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <p className="font-extrabold text-slate-900">Controle de Estoque do Produto</p>
                    <p className="text-[11px] text-slate-500">Baixa automática a cada venda confirmada no cardápio digital.</p>
                  </div>
                  <label className="flex cursor-pointer items-center gap-2 rounded-xl border border-slate-300 bg-white px-3 py-1.5 font-bold text-slate-900">
                    <input type="checkbox" checked={trackInventory} onChange={(e) => setTrackInventory(e.target.checked)} />
                    Ativar controle de estoque
                  </label>
                </div>

                {trackInventory && (
                  <div className="grid gap-3 pt-1 sm:grid-cols-2">
                    <div>
                      <label className="font-bold text-slate-700">Quantidade Disponível (Unidades)</label>
                      <input
                        type="number"
                        min="0"
                        value={stockQuantity}
                        onChange={(e) => setStockQuantity(e.target.value)}
                        className="mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 font-mono text-sm font-bold"
                      />
                    </div>
                    <div className="flex items-end">
                      <label className="flex w-full cursor-pointer items-center gap-2 rounded-xl border border-amber-300 bg-amber-50 px-3 py-2.5 font-bold text-amber-900">
                        <input
                          type="checkbox"
                          checked={autoPauseOnZeroStock}
                          onChange={(e) => setAutoPauseOnZeroStock(e.target.checked)}
                        />
                        Pausar produto automaticamente ao zerar estoque
                      </label>
                    </div>
                  </div>
                )}
              </div>
            </div>
          )}

          {/* ABA 3: TAMANHOS & VARIAÇÕES */}
          {activeTab === "variations" && (
            <div className="space-y-4">
              <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
                <label className="font-bold text-slate-700">Regra de Cálculo para Sabores / Montagem</label>
                <select
                  value={pricingStrategy}
                  onChange={(e) => setPricingStrategy(Number(e.target.value))}
                  className="mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-bold"
                >
                  <option value={1}>Preço Fixo do Produto/Tamanho + Adicionais Pagos</option>
                  <option value={2}>Cobrar pelo Maior Valor dos Sabores (Estilo Pizzaria Meia-a-Meia)</option>
                  <option value={3}>Cobrar pela Média / Proporcional dos Sabores</option>
                </select>
              </div>

              <div>
                <p className="font-extrabold text-slate-900">{labels.sizesTitle}</p>
                <p className="mt-0.5 text-[11px] text-slate-500">{labels.sizesHint}</p>

                <div className="mt-3 grid gap-3 sm:grid-cols-2">
                  {sizes.map((sz, idx) => {
                    const saleNum = Number(sz.price) || 0;
                    const costNum = Number(sz.costAmount) || 0;
                    const profitNum = saleNum - costNum;
                    const marginPct = saleNum > 0 ? Math.round((profitNum / saleNum) * 100) : 0;
                    return (
                      <div key={idx} className="rounded-2xl border border-slate-200 bg-slate-50 p-3.5 space-y-2.5">
                        <div className="flex items-center justify-between gap-2">
                          <input
                            value={sz.variationName}
                            onChange={(e) => {
                              const copy = [...sizes];
                              copy[idx] = { ...sz, variationName: e.target.value };
                              setSizes(copy);
                            }}
                            placeholder="Nome do copo/tamanho"
                            className="min-w-0 flex-1 rounded-lg border border-slate-200 bg-white px-2.5 py-1 text-xs font-extrabold text-amber-700"
                          />
                          <button
                            type="button"
                            onClick={() => setSizes(sizes.filter((_, i) => i !== idx))}
                            className="inline-flex items-center gap-1 rounded-lg bg-rose-600 px-2 py-1 text-[10px] font-bold text-white"
                          >
                            <Trash2 className="h-3 w-3" /> Remover
                          </button>
                        </div>

                        <div className="grid grid-cols-3 gap-2">
                          <div className="col-span-2">
                            <label className="text-[10px] font-bold text-slate-600">Descrição</label>
                            <input
                              value={sz.description}
                              onChange={(e) => {
                                const copy = [...sizes];
                                copy[idx] = { ...sz, description: e.target.value };
                                setSizes(copy);
                              }}
                              placeholder="Ex: 315g cremoso + inclusos"
                              className="mt-0.5 w-full rounded-lg border border-slate-200 bg-white px-2 py-1 text-[11px]"
                            />
                          </div>
                          <div>
                            <label className="text-[10px] font-bold text-slate-600">SKU Variação</label>
                            <input
                              value={sz.sku}
                              onChange={(e) => {
                                const copy = [...sizes];
                                copy[idx] = { ...sz, sku: e.target.value };
                                setSizes(copy);
                              }}
                              placeholder="SKU"
                              className="mt-0.5 w-full rounded-lg border border-slate-200 bg-white px-2 py-1 font-mono text-[11px] uppercase"
                            />
                          </div>
                        </div>

                        <div className="grid grid-cols-2 gap-2">
                          <div>
                            <label className="text-[10px] font-bold text-slate-600">Volume / Medida</label>
                            <div className="mt-0.5 flex gap-1">
                              <input
                                value={sz.measureAmount}
                                onChange={(e) => {
                                  const copy = [...sizes];
                                  copy[idx] = { ...sz, measureAmount: e.target.value };
                                  setSizes(copy);
                                }}
                                className="w-14 rounded-lg border border-slate-200 bg-white px-2 py-1 font-mono"
                              />
                              <select
                                value={sz.measureUnit}
                                onChange={(e) => {
                                  const copy = [...sizes];
                                  copy[idx] = { ...sz, measureUnit: e.target.value };
                                  setSizes(copy);
                                }}
                                className="flex-1 rounded-lg border border-slate-200 bg-white px-1.5 py-1"
                              >
                                {labels.units.map((u) => (
                                  <option key={u} value={u}>
                                    {u}
                                  </option>
                                ))}
                              </select>
                            </div>
                          </div>

                          <div>
                            <label className="text-[10px] font-bold text-slate-600">Adicionais</label>
                            <select
                              value={sz.addonMode}
                              onChange={(e) => {
                                const mode = e.target.value as CupAddonMode;
                                const copy = [...sizes];
                                copy[idx] = {
                                  ...sz,
                                  addonMode: mode,
                                  allowPaidExtras: mode === "both" || mode === "paid_only",
                                  freeAllowance: mode === "paid_only" || mode === "none" ? "0" : sz.freeAllowance
                                };
                                setSizes(copy);
                              }}
                              className="mt-0.5 w-full rounded-lg border border-slate-200 bg-white px-2 py-1 text-[11px] font-bold"
                            >
                              <option value="both">Gratuitos + Pagos</option>
                              <option value="free_only">Somente Gratuitos</option>
                              <option value="paid_only">Somente Pagos</option>
                              <option value="none">Sem Adicionais</option>
                            </select>
                          </div>

                          <div>
                            <label className="text-[10px] font-bold text-slate-600">Preço Venda (R$)</label>
                            <input
                              type="number"
                              step="0.01"
                              value={sz.price}
                              onChange={(e) => {
                                const copy = [...sizes];
                                copy[idx] = { ...sz, price: e.target.value };
                                setSizes(copy);
                              }}
                              className="mt-0.5 w-full rounded-lg border border-slate-200 bg-white px-2 py-1 font-mono font-bold text-emerald-700"
                            />
                          </div>

                          <div>
                            <label className="text-[10px] font-bold text-slate-600">Preço Custo (R$)</label>
                            <input
                              type="number"
                              step="0.01"
                              value={sz.costAmount}
                              onChange={(e) => {
                                const copy = [...sizes];
                                copy[idx] = { ...sz, costAmount: e.target.value };
                                setSizes(copy);
                              }}
                              className="mt-0.5 w-full rounded-lg border border-slate-200 bg-white px-2 py-1 font-mono text-rose-700"
                            />
                          </div>
                        </div>

                        <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-200 pt-2">
                          {(sz.addonMode === "both" || sz.addonMode === "free_only") ? (
                            <label className="flex items-center gap-1.5 text-[11px] font-semibold text-slate-700">
                              <span>{labels.allowanceLabel}:</span>
                              <input
                                type="number"
                                min="0"
                                value={sz.freeAllowance}
                                onChange={(e) => {
                                  const copy = [...sizes];
                                  copy[idx] = { ...sz, freeAllowance: e.target.value };
                                  setSizes(copy);
                                }}
                                className="w-14 rounded-lg border border-slate-200 bg-white px-2 py-0.5 font-mono font-bold"
                              />
                            </label>
                          ) : (
                            <span className="text-[10px] font-semibold text-slate-500">Sem itens grátis</span>
                          )}

                          <label className="flex items-center gap-1 text-[10px] font-bold text-slate-700">
                            <input
                              type="checkbox"
                              checked={sz.trackInventory}
                              onChange={(e) => {
                                const copy = [...sizes];
                                copy[idx] = { ...sz, trackInventory: e.target.checked };
                                setSizes(copy);
                              }}
                            />
                            Estq:
                            {sz.trackInventory && (
                              <input
                                type="number"
                                min="0"
                                value={sz.stockQuantity}
                                onChange={(e) => {
                                  const copy = [...sizes];
                                  copy[idx] = { ...sz, stockQuantity: e.target.value };
                                  setSizes(copy);
                                }}
                                className="w-12 rounded border border-slate-300 bg-white px-1 py-0.5 font-mono"
                              />
                            )}
                          </label>

                          <span className={`rounded-full px-2 py-0.5 text-[10px] font-extrabold ${profitNum >= 0 ? "bg-emerald-100 text-emerald-800" : "bg-rose-100 text-rose-800"}`}>
                            Lucro: R$ {profitNum.toFixed(2)} ({marginPct}%)
                          </span>
                        </div>
                      </div>
                    );
                  })}
                </div>

                <button
                  type="button"
                  onClick={addSizeRow}
                  className="mt-3 inline-flex items-center gap-1.5 rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs font-bold text-slate-800 hover:bg-slate-100"
                >
                  <Plus className="h-3.5 w-3.5" /> {labels.addSizeText}
                </button>
              </div>
            </div>
          )}

          {/* ABA 4: COMPLEMENTOS & SELOS */}
          {activeTab === "modifiers" && (
            <div className="space-y-4">
              {/* Classificação Serve X Pessoas + Selos Dietéticos */}
              <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4 space-y-3">
                <p className="font-extrabold text-slate-900">Classificação & Selos Informativos (iFood)</p>
                <div>
                  <label className="font-bold text-slate-700">Serve quantas pessoas?</label>
                  <div className="mt-1.5 flex flex-wrap gap-1.5">
                    {["", "1", "2", "3", "4"].map((v) => (
                      <button
                        key={v || "none"}
                        type="button"
                        onClick={() => setServesPeopleCount(v)}
                        className={`rounded-xl border px-3 py-1.5 text-xs font-bold ${
                          servesPeopleCount === v
                            ? "border-slate-900 bg-slate-900 text-white"
                            : "border-slate-300 bg-white text-slate-700"
                        }`}
                      >
                        {v === "" ? "Não informar" : v === "4" ? "4+ pessoas" : `Serve ${v} pessoa${v === "1" ? "" : "s"}`}
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <label className="font-bold text-slate-700">Selos Dietéticos e Características</label>
                  <div className="mt-1.5 flex flex-wrap gap-2">
                    {DIETARY_SEALS.map((seal) => {
                      const active = dietaryTags.includes(seal.id);
                      return (
                        <button
                          key={seal.id}
                          type="button"
                          onClick={() =>
                            setDietaryTags(
                              active ? dietaryTags.filter((t) => t !== seal.id) : [...dietaryTags, seal.id]
                            )
                          }
                          className={`inline-flex items-center gap-1 rounded-full border px-3 py-1 text-[11px] font-bold ${
                            active
                              ? "border-emerald-600 bg-emerald-100 text-emerald-900"
                              : "border-slate-300 bg-white text-slate-700"
                          }`}
                        >
                          {active && <Check className="h-3 w-3" />}
                          {seal.label}
                        </button>
                      );
                    })}
                  </div>
                </div>
              </div>

              {/* Checklist de Grupos Inclusos / Obrigatórios */}
              {freeGroups.length > 0 && (
                <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                  <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                    <p className="font-extrabold text-slate-900">{labels.freeTitle}</p>
                    <div className="flex gap-1.5">
                      <button
                        type="button"
                        onClick={() => setSelectedFreeGroupIds(freeGroups.map((g) => g.id))}
                        className="rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-[11px] font-bold hover:bg-slate-100"
                      >
                        Marcar Todos
                      </button>
                      <button
                        type="button"
                        onClick={() => setSelectedFreeGroupIds([])}
                        className="rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-[11px] font-bold hover:bg-slate-100"
                      >
                        Desmarcar Todos
                      </button>
                    </div>
                  </div>
                  <div className="grid max-h-40 gap-2 overflow-y-auto sm:grid-cols-2">
                    {freeGroups.map((grp) => {
                      const checked = selectedFreeGroupIds.includes(grp.id);
                      const isReq = Boolean(grp.isRequired || grp.minSelections >= 1);
                      return (
                        <label
                          key={grp.id}
                          className="flex cursor-pointer items-start gap-2 rounded-xl border border-slate-200 bg-white p-2.5"
                        >
                          <input
                            type="checkbox"
                            checked={checked}
                            onChange={(e) =>
                              setSelectedFreeGroupIds(
                                e.target.checked
                                  ? [...selectedFreeGroupIds, grp.id]
                                  : selectedFreeGroupIds.filter((id) => id !== grp.id)
                              )
                            }
                          />
                          <div className="min-w-0 flex-1">
                            <div className="flex flex-wrap items-center gap-1.5">
                              <span className="font-bold text-slate-900">{grp.name}</span>
                              <span className={`rounded px-1.5 py-0.5 text-[9px] font-extrabold uppercase ${isReq ? "bg-rose-100 text-rose-800" : "bg-slate-100 text-slate-600"}`}>
                                {isReq ? "Obrigatório" : "Opcional"}
                              </span>
                            </div>
                            {grp.options && grp.options.length > 0 && (
                              <p className="text-[10px] text-slate-500">
                                {grp.options.slice(0, 4).map((o) => o.name).join(", ")}
                                {grp.options.length > 4 ? ` +${grp.options.length - 4}` : ""}
                              </p>
                            )}
                          </div>
                        </label>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Adicionais Extras Pagos */}
              {paidGroups.length > 0 && (
                <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                  <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                    <label className="flex cursor-pointer items-center gap-2 font-extrabold text-slate-900">
                      <input
                        type="checkbox"
                        checked={enablePaidExtras}
                        onChange={(e) => setEnablePaidExtras(e.target.checked)}
                      />
                      {labels.extrasTitle}
                    </label>
                    {enablePaidExtras && (
                      <div className="flex gap-1.5">
                        <button
                          type="button"
                          onClick={() => setSelectedPaidGroupIds(paidGroups.map((g) => g.id))}
                          className="rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-[11px] font-bold hover:bg-slate-100"
                        >
                          Marcar Todos
                        </button>
                        <button
                          type="button"
                          onClick={() => setSelectedPaidGroupIds([])}
                          className="rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-[11px] font-bold hover:bg-slate-100"
                        >
                          Desmarcar Todos
                        </button>
                      </div>
                    )}
                  </div>

                  {enablePaidExtras && (
                    <div className="grid max-h-36 gap-2 overflow-y-auto sm:grid-cols-2">
                      {paidGroups.map((grp) => {
                        const checked = selectedPaidGroupIds.includes(grp.id);
                        const isReq = Boolean(grp.isRequired || grp.minSelections >= 1);
                        return (
                          <label
                            key={grp.id}
                            className="flex cursor-pointer items-start gap-2 rounded-xl border border-slate-200 bg-white p-2.5"
                          >
                            <input
                              type="checkbox"
                              checked={checked}
                              onChange={(e) =>
                                setSelectedPaidGroupIds(
                                  e.target.checked
                                    ? [...selectedPaidGroupIds, grp.id]
                                    : selectedPaidGroupIds.filter((id) => id !== grp.id)
                                )
                              }
                            />
                            <div>
                              <div className="flex flex-wrap items-center gap-1.5">
                                <span className="font-bold text-slate-900">{grp.name}</span>
                                <span className={`rounded px-1.5 py-0.5 text-[9px] font-extrabold uppercase ${isReq ? "bg-rose-100 text-rose-800" : "bg-slate-100 text-slate-600"}`}>
                                  {isReq ? "Obrigatório" : "Opcional"}
                                </span>
                              </div>
                              {grp.options && grp.options.length > 0 && (
                                <p className="text-[10px] text-slate-500">
                                  {grp.options
                                    .slice(0, 3)
                                    .map((o) => `${o.name} (+R$ ${o.additionalPrice.toFixed(2)})`)
                                    .join(", ")}
                                </p>
                              )}
                            </div>
                          </label>
                        );
                      })}
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {/* ABA 5: DISPONIBILIDADE PROGRAMADA */}
          {activeTab === "schedule" && (
            <ScheduleWindowEditor
              enabled={schedEnabled}
              days={schedDays}
              onToggleEnabled={setSchedEnabled}
              onChangeDays={setSchedDays}
            />
          )}
        </div>

        <div className="flex gap-3 border-t border-slate-200 bg-slate-50 px-6 py-4">
          <button
            type="submit"
            disabled={saving}
            className="flex-1 rounded-xl bg-emerald-600 py-2.5 text-xs font-extrabold text-white shadow-sm hover:bg-emerald-700 disabled:opacity-50"
          >
            {saving ? "Salvando Produto..." : "Salvar Produto"}
          </button>
          <button type="button" onClick={onClose} className="rounded-xl border border-slate-300 bg-white px-5 py-2.5 text-xs font-bold text-slate-700">
            Cancelar
          </button>
        </div>
      </form>
    </div>
  );
}
