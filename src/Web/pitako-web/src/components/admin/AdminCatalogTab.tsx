"use client";

import { AlertTriangle, ArrowDown, ArrowUp, Clock, Edit3, FileSpreadsheet, FileText, Flame, GripVertical, Image as ImageIcon, Layers, LayoutGrid, List, Pause, Play, Plus, Sliders, Star, Trash2, Upload } from "lucide-react";
import { useState } from "react";
import { apiFetch, compressImageFileToDataUrl, resolveMediaUrl } from "@/lib/api";
import { ProductMediaFrame } from "../storefront/ProductMediaFrame";
import { AdminCatalogAndSheetsExportModal, AdminComboExportDto } from "./AdminCatalogAndSheetsExportModal";
import { AdminFlyerModal } from "./AdminFlyerModal";
import { AdminCategoryEditModal, AdminProductFullModal, ModifierGroupDetailedDto } from "./AdminProductAndCategoryModals";
import { TechnicalSheetSummaryItem } from "./AdminTechSheetPdfModal";

export interface AdminCategoryDto {
  id: string;
  name: string;
  description?: string | null;
  imageUrl?: string | null;
  displayOrder: number;
  segmentTemplateCode?: string | null;
  ruleNotes?: string | null;
  templateKind?: number;
  availabilityScheduleJson?: string | null;
  status: string;
}

export interface AdminVariationDto {
  id: string;
  variationName: string;
  description?: string | null;
  price: number;
  costPrice?: number;
  measureAmount?: number | null;
  measureUnit?: string | null;
  maxFlavors: number;
  sku?: string | null;
  displayOrder: number;
  isActive: boolean;
  trackInventory?: boolean;
  stockQuantity?: number;
}

export interface AdminVariationRuleDto {
  id: string;
  variationId?: string | null;
  modifierGroupId: string;
  minSelections: number;
  maxSelections: number;
  freeAllowance: number;
  isRequired: boolean;
  isHidden: boolean;
}

export interface AdminProductDto {
  id: string;
  categoryId: string;
  name: string;
  description?: string | null;
  sku?: string | null;
  basePrice: number;
  promotionalPrice?: number | null;
  trackInventory?: boolean;
  stockQuantity?: number;
  autoPauseOnZeroStock?: boolean;
  servesPeopleCount?: number | null;
  dietaryTagsCsv?: string | null;
  availabilityScheduleJson?: string | null;
  status: string;
  saleMode: string;
  pricingStrategy: number;
  isFeatured: boolean;
  isNew: boolean;
  isRecommended: boolean;
  allowsNotes: boolean;
  preparationMinutes?: number | null;
  tagsCsv?: string | null;
  displayOrder?: number;
  primaryImageUrl?: string | null;
  variations: AdminVariationDto[];
  modifierGroupIds: string[];
  variationRules: AdminVariationRuleDto[];
}

export interface AdminModifierGroupSummary {
  id: string;
  name: string;
  selectionKind: number;
  minSelections: number;
  maxSelections: number;
  freeAllowance: number;
}

export function AdminCatalogTab({
  token,
  slug = "acai-demo",
  segmentCode = "acaiteria",
  storeTradeName = "Estabelecimento",
  logoUrl,
  slogan,
  primaryColorHex = "#4C1D95",
  accentColorHex = "#F59E0B",
  formattedCnpj,
  storeAddress,
  whatsAppNumber,
  instagramHandle,
  categories,
  products,
  modifierGroups,
  techSheetSummaries = [],
  combos = [],
  onOpenTechSheetPdf,
  onNotify,
  onReload
}: {
  token: string;
  slug?: string;
  segmentCode?: string;
  storeTradeName?: string;
  logoUrl?: string | null;
  slogan?: string | null;
  primaryColorHex?: string;
  accentColorHex?: string;
  formattedCnpj?: string;
  storeAddress?: string;
  whatsAppNumber?: string;
  instagramHandle?: string;
  categories: AdminCategoryDto[];
  products: AdminProductDto[];
  modifierGroups: ModifierGroupDetailedDto[];
  techSheetSummaries?: TechnicalSheetSummaryItem[];
  combos?: AdminComboExportDto[];
  onOpenTechSheetPdf?: (sheet: TechnicalSheetSummaryItem) => void;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [viewMode, setViewMode] = useState<"table" | "cards">("cards");
  const [catModalOpen, setCatModalOpen] = useState(false);
  const [modalCategory, setModalCategory] = useState<AdminCategoryDto | null>(null);
  const [prodModalOpen, setProdModalOpen] = useState(false);
  const [modalProduct, setModalProduct] = useState<AdminProductDto | null>(null);
  const [flyerOpen, setFlyerOpen] = useState(false);
  const [exportCenterOpen, setExportCenterOpen] = useState(false);
  const [bestSellerPickId, setBestSellerPickId] = useState("");
  const [sidebarUploadingImg, setSidebarUploadingImg] = useState(false);
  const [selectedCatFilter, setSelectedCatFilter] = useState<string>("all");
  const [catToDelete, setCatToDelete] = useState<AdminCategoryDto | null>(null);
  const [moveTargetCatId, setMoveTargetCatId] = useState<string>("");
  const [draggingCatId, setDraggingCatId] = useState<string | null>(null);

  // Categoria form
  const [editingCatId, setEditingCatId] = useState<string | null>(null);
  const [catName, setCatName] = useState("");
  const [catDesc, setCatDesc] = useState("");
  const [catNotes, setCatNotes] = useState("");

  // Produto form
  const [editingProdId, setEditingProdId] = useState<string | null>(null);
  const [prodCatId, setProdCatId] = useState(categories[0]?.id || "");
  const [prodName, setProdName] = useState("");
  const [prodPrice, setProdPrice] = useState("24.90");
  const [prodDesc, setProdDesc] = useState("");
  const [prodSku, setProdSku] = useState("");
  const [prodImgUrl, setProdImgUrl] = useState("");
  const [prodPricingStrategy, setProdPricingStrategy] = useState("1");
  const [prodPrepMin, setProdPrepMin] = useState("15");
  const [prodFeatured, setProdFeatured] = useState(false);
  const [prodNew, setProdNew] = useState(false);
  const [prodRecommended, setProdRecommended] = useState(false);
  const [prodAllowsNotes, setProdAllowsNotes] = useState(true);

  // Painel de variação & regras por produto selecionado
  const [expandedProdId, setExpandedProdId] = useState<string | null>(null);
  const [varName, setVarName] = useState("");
  const [varPrice, setVarPrice] = useState("24.90");
  const [varDesc, setVarDesc] = useState("");
  const [varMeasureAmt, setVarMeasureAmt] = useState("500");
  const [varMeasureUnit, setVarMeasureUnit] = useState("ml");
  const [varMaxFlavors, setVarMaxFlavors] = useState("1");

  const [ruleVarId, setRuleVarId] = useState("");
  const [ruleGroupId, setRuleGroupId] = useState("");
  const [ruleMin, setRuleMin] = useState("0");
  const [ruleMax, setRuleMax] = useState("5");
  const [ruleFree, setRuleFree] = useState("3");
  const [ruleRequired, setRuleRequired] = useState(false);

  async function handleSidebarPhotoUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setSidebarUploadingImg(true);
    try {
      const compressed = await compressImageFileToDataUrl(file, 480, 0.75);
      const up = await apiFetch<{ url: string }>(
        "/api/v1/catalog/upload-image",
        { method: "POST", body: JSON.stringify({ base64DataUrl: compressed, fileName: file.name }) },
        token
      );
      if (up.ok && up.data?.url) {
        setProdImgUrl(up.data.url);
        onNotify("Foto carregada do computador!");
      } else onNotify(up.errorMessage || "Erro ao enviar foto.");
    } catch {
      onNotify("Erro ao ler imagem do computador.");
    }
    setSidebarUploadingImg(false);
  }

  async function toggleBestSeller(p: AdminProductDto, forceState?: boolean) {
    const nextFeatured = forceState !== undefined ? forceState : !p.isFeatured;
    const res = await apiFetch(
      `/api/v1/catalog/products/${p.id}`,
      {
        method: "PUT",
        body: JSON.stringify({
          categoryId: p.categoryId,
          name: p.name,
          basePrice: p.basePrice,
          description: p.description ?? null,
          sku: p.sku ?? null,
          isFeatured: nextFeatured,
          displayOrder: 1,
          saleMode: 1,
          isNew: p.isNew,
          isRecommended: p.isRecommended,
          pricingStrategy: p.pricingStrategy || 1,
          tagsCsv: p.tagsCsv ?? null,
          preparationMinutes: p.preparationMinutes ?? null,
          customTemplateKey: null,
          allowsNotes: p.allowsNotes
        })
      },
      token
    );
    if (res.ok) {
      onNotify(nextFeatured ? `"${p.name}" destacado em Mais Vendidos!` : `"${p.name}" removido de Mais Vendidos.`);
      await onReload();
    }
  }

  async function saveCategory(e: React.FormEvent) {
    e.preventDefault();
    const res = editingCatId
      ? await apiFetch(
          `/api/v1/catalog/categories/${editingCatId}`,
          {
            method: "PUT",
            body: JSON.stringify({ name: catName, description: catDesc || null, displayOrder: categories.length, isActive: true, ruleNotes: catNotes || null })
          },
          token
        )
      : await apiFetch(
          "/api/v1/catalog/categories",
          { method: "POST", body: JSON.stringify({ name: catName, description: catDesc || null, displayOrder: categories.length + 1, ruleNotes: catNotes || null }) },
          token
        );

    if (res.ok) {
      setEditingCatId(null);
      setCatName("");
      setCatDesc("");
      setCatNotes("");
      onNotify(editingCatId ? "Categoria atualizada!" : "Categoria criada!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar categoria.");
  }

  async function dropCategoryOn(targetCatId: string) {
    if (!draggingCatId || draggingCatId === targetCatId) {
      setDraggingCatId(null);
      return;
    }
    const orderedCats = [...categories].sort((a, b) => a.displayOrder - b.displayOrder);
    const fromIdx = orderedCats.findIndex((c) => c.id === draggingCatId);
    const toIdx = orderedCats.findIndex((c) => c.id === targetCatId);
    setDraggingCatId(null);
    if (fromIdx < 0 || toIdx < 0) return;

    const copy = [...orderedCats];
    const [moved] = copy.splice(fromIdx, 1);
    copy.splice(toIdx, 0, moved);
    const payload: Record<string, number> = {};
    copy.forEach((c, i) => {
      payload[c.id] = i + 1;
    });
    const res = await apiFetch("/api/v1/catalog/categories/reorder", { method: "POST", body: JSON.stringify(payload) }, token);
    if (res.ok) {
      onNotify("Categorias reordenadas por arraste!");
      await onReload();
    }
  }

  async function moveCategory(idx: number, dir: -1 | 1) {
    const orderedCats = [...categories].sort((a, b) => a.displayOrder - b.displayOrder);
    const targetIdx = idx + dir;
    if (targetIdx < 0 || targetIdx >= orderedCats.length) return;
    const copy = [...orderedCats];
    const [moved] = copy.splice(idx, 1);
    copy.splice(targetIdx, 0, moved);
    const payload: Record<string, number> = {};
    copy.forEach((c, i) => {
      payload[c.id] = i + 1;
    });
    await apiFetch("/api/v1/catalog/categories/reorder", { method: "POST", body: JSON.stringify(payload) }, token);
    onNotify("Ordem das categorias atualizada!");
    await onReload();
  }

  async function deleteCategory(cat: AdminCategoryDto) {
    const catProds = products.filter((p) => p.categoryId === cat.id);
    if (catProds.length > 0) {
      const otherCat = categories.find((c) => c.id !== cat.id);
      if (!otherCat) {
        onNotify("Crie outra categoria antes de excluir a única categoria com produtos.");
        return;
      }
      setCatToDelete(cat);
      setMoveTargetCatId(otherCat.id);
      return;
    }
    const res = await apiFetch(`/api/v1/catalog/categories/${cat.id}`, { method: "DELETE" }, token);
    if (res.ok) {
      onNotify("Categoria excluída!");
      await onReload();
    } else onNotify(res.errorMessage || "Não foi possível excluir categoria.");
  }

  async function confirmDeleteWithMove() {
    if (!catToDelete || !moveTargetCatId) return;
    const res = await apiFetch(
      `/api/v1/catalog/categories/${catToDelete.id}?moveToCategoryId=${encodeURIComponent(moveTargetCatId)}`,
      { method: "DELETE" },
      token
    );
    if (res.ok) {
      onNotify(`Categoria "${catToDelete.name}" removida e produtos transferidos!`);
      setCatToDelete(null);
      await onReload();
    } else {
      onNotify(res.errorMessage || "Erro ao transferir e excluir categoria.");
    }
  }

  async function moveProductCategory(productId: string, targetCategoryId: string) {
    const res = await apiFetch(
      `/api/v1/catalog/products/${productId}/move`,
      { method: "POST", body: JSON.stringify({ categoryId: targetCategoryId }) },
      token
    );
    if (res.ok) {
      const targetName = categories.find((c) => c.id === targetCategoryId)?.name || "nova categoria";
      onNotify(`Item movido para "${targetName}"!`);
      await onReload();
    } else {
      onNotify(res.errorMessage || "Erro ao mover item.");
    }
  }

  async function reorderProductInCategory(catId: string, productId: string, dir: -1 | 1) {
    const inCat = [...products]
      .filter((p) => p.categoryId === catId)
      .sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.name.localeCompare(b.name));
    const idx = inCat.findIndex((p) => p.id === productId);
    const targetIdx = idx + dir;
    if (idx < 0 || targetIdx < 0 || targetIdx >= inCat.length) return;
    const [moved] = inCat.splice(idx, 1);
    inCat.splice(targetIdx, 0, moved);
    const payload: Record<string, number> = {};
    inCat.forEach((p, i) => {
      payload[p.id] = i + 1;
    });
    const res = await apiFetch("/api/v1/catalog/products/reorder", { method: "POST", body: JSON.stringify(payload) }, token);
    if (res.ok) {
      onNotify("Ordem dos produtos atualizada!");
      await onReload();
    }
  }

  function startEditProduct(p: AdminProductDto) {
    setEditingProdId(p.id);
    setProdCatId(p.categoryId);
    setProdName(p.name);
    setProdPrice(String(p.basePrice));
    setProdDesc(p.description || "");
    setProdSku(p.sku || "");
    setProdImgUrl(p.primaryImageUrl || "");
    setProdPricingStrategy(String(p.pricingStrategy || 1));
    setProdPrepMin(p.preparationMinutes ? String(p.preparationMinutes) : "15");
    setProdFeatured(p.isFeatured);
    setProdNew(p.isNew);
    setProdRecommended(p.isRecommended);
    setProdAllowsNotes(p.allowsNotes);
  }

  async function saveProduct(e: React.FormEvent) {
    e.preventDefault();
    const catId = prodCatId || categories[0]?.id;
    if (!catId) {
      onNotify("Crie uma categoria primeiro.");
      return;
    }

    const payload = {
      categoryId: catId,
      name: prodName,
      basePrice: Number(prodPrice),
      description: prodDesc || null,
      sku: prodSku || null,
      saleMode: 1,
      isFeatured: prodFeatured,
      displayOrder: products.length + 1,
      isNew: prodNew,
      isRecommended: prodRecommended,
      pricingStrategy: Number(prodPricingStrategy),
      preparationMinutes: prodPrepMin ? Number(prodPrepMin) : null,
      allowsNotes: prodAllowsNotes
    };

    const res = editingProdId
      ? await apiFetch<{ id: string }>(`/api/v1/catalog/products/${editingProdId}`, { method: "PUT", body: JSON.stringify(payload) }, token)
      : await apiFetch<{ id: string }>("/api/v1/catalog/products", { method: "POST", body: JSON.stringify(payload) }, token);

    if (res.ok && res.data) {
      const targetProdId = editingProdId || res.data.id;
      if (prodImgUrl.trim()) {
        await apiFetch(
          `/api/v1/catalog/products/${targetProdId}/images`,
          { method: "POST", body: JSON.stringify({ imageUrl: prodImgUrl.trim(), displayOrder: 0, isPrimary: true }) },
          token
        );
      }
      setEditingProdId(null);
      setProdName("");
      setProdDesc("");
      setProdSku("");
      setProdImgUrl("");
      onNotify(editingProdId ? "Produto atualizado!" : "Produto adicionado ao cardápio!");
      await onReload();
    } else {
      onNotify(res.errorMessage || "Erro ao salvar produto.");
    }
  }

  async function toggleProduct(p: AdminProductDto) {
    await apiFetch(`/api/v1/catalog/products/${p.id}/${p.status === "Active" ? "pause" : "reactivate"}`, { method: "POST" }, token);
    await onReload();
  }

  async function deleteProduct(id: string) {
    const res = await apiFetch(`/api/v1/catalog/products/${id}`, { method: "DELETE" }, token);
    if (res.ok) {
      onNotify("Produto excluído/arquivado com segurança.");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao excluir produto.");
  }

  async function addVariation(e: React.FormEvent, productId: string) {
    e.preventDefault();
    const res = await apiFetch(
      `/api/v1/catalog/products/${productId}/variations`,
      {
        method: "POST",
        body: JSON.stringify({
          variationName: varName,
          price: Number(varPrice),
          description: varDesc || null,
          measureAmount: varMeasureAmt ? Number(varMeasureAmt) : null,
          measureUnit: varMeasureUnit || null,
          maxFlavors: Number(varMaxFlavors) || 1
        })
      },
      token
    );
    if (res.ok) {
      setVarName("");
      setVarDesc("");
      onNotify("Tamanho / variação cadastrado!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao adicionar variação.");
  }

  async function removeVariation(productId: string, varId: string) {
    await apiFetch(`/api/v1/catalog/products/${productId}/variations/${varId}`, { method: "DELETE" }, token);
    await onReload();
  }

  async function toggleGroupLink(product: AdminProductDto, groupId: string) {
    const linked = product.modifierGroupIds.includes(groupId);
    await apiFetch(
      `/api/v1/catalog/products/${product.id}/modifier-groups/${groupId}`,
      { method: linked ? "DELETE" : "POST" },
      token
    );
    await onReload();
  }

  async function saveVariationRule(e: React.FormEvent, productId: string) {
    e.preventDefault();
    if (!ruleGroupId) return;
    const res = await apiFetch(
      `/api/v1/catalog/products/${productId}/variation-rules`,
      {
        method: "POST",
        body: JSON.stringify({
          variationId: ruleVarId || null,
          modifierGroupId: ruleGroupId,
          minSelections: Number(ruleMin),
          maxSelections: Number(ruleMax),
          freeAllowance: Number(ruleFree),
          isRequired: ruleRequired,
          isHidden: false
        })
      },
      token
    );
    if (res.ok) {
      onNotify("Regra por tamanho salva!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao definir regra.");
  }

  return (
    <div className="space-y-6">
      {/* Gestor Visual de Mais Vendidos (Imagem 4 & 10) */}
      <section className="rounded-2xl border border-amber-300 bg-amber-50/70 p-4 shadow-sm space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="flex items-center gap-1.5 text-xs font-extrabold uppercase tracking-wide text-amber-950">
            <Flame className="h-4 w-4 text-amber-600" /> Gestão da Seção &quot;Mais Vendidos&quot; no Cardápio ({products.filter((x) => x.isFeatured).length} ativos)
          </h3>
          <div className="flex flex-wrap items-center gap-2">
            <select
              value={bestSellerPickId}
              onChange={(e) => setBestSellerPickId(e.target.value)}
              className="rounded-xl border border-amber-300 bg-white px-3 py-1.5 text-xs font-semibold text-slate-800"
            >
              <option value="">-- Selecione para adicionar --</option>
              {products.filter((p) => !p.isFeatured).map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
            <button
              type="button"
              disabled={!bestSellerPickId}
              onClick={() => {
                const target = products.find((p) => p.id === bestSellerPickId);
                if (target) {
                  setBestSellerPickId("");
                  void toggleBestSeller(target, true);
                }
              }}
              className="rounded-xl bg-emerald-600 px-3 py-1.5 text-xs font-extrabold text-white hover:bg-emerald-700 disabled:opacity-40"
            >
              + Adicionar
            </button>
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          {products
            .filter((p) => p.isFeatured)
            .map((p, idx) => (
              <div
                key={p.id}
                className="flex items-center gap-2 rounded-xl border border-amber-300 bg-white px-2.5 py-1.5 text-xs shadow-sm"
              >
                <ProductMediaFrame src={p.primaryImageUrl} alt={p.name} className="h-7 w-7 rounded-lg" iconSize={14} />
                <div>
                  <p className="font-bold text-slate-900">{p.name}</p>
                  <p className="text-[10px] font-extrabold text-amber-700">★ #{idx + 1} em Mais Vendidos</p>
                </div>
                <button
                  type="button"
                  onClick={() => void toggleBestSeller(p, false)}
                  className="rounded-lg bg-rose-600 px-2 py-0.5 text-[10px] font-bold text-white hover:bg-rose-700"
                >
                  Remover ×
                </button>
              </div>
            ))}
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-3">
        {/* Lista de Categorias e Produtos */}
        <div className="space-y-6 lg:col-span-2">
          <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="mb-3 flex items-center justify-between gap-2">
              <h3 className="text-base font-extrabold text-slate-900">Categorias & Ordem ({categories.length})</h3>
              <button
                type="button"
                onClick={() => {
                  setModalCategory(null);
                  setCatModalOpen(true);
                }}
                className="inline-flex items-center gap-1 rounded-xl bg-emerald-600 px-3 py-1.5 text-xs font-bold text-white hover:bg-emerald-700"
              >
                <Plus className="h-3.5 w-3.5" /> Nova Categoria
              </button>
            </div>
            <div className="space-y-2">
              {[...categories]
                .sort((a, b) => a.displayOrder - b.displayOrder)
                .map((c, idx) => {
                const countInCat = products.filter((p) => p.categoryId === c.id).length;
                const tplLabel = c.templateKind === 2 ? "Montável / Pizza" : c.templateKind === 3 ? "Combo / Promo" : "Padrão";
                const tplBadgeClass =
                  c.templateKind === 2
                    ? "bg-indigo-100 text-indigo-800"
                    : c.templateKind === 3
                      ? "bg-rose-100 text-rose-800"
                      : "bg-slate-200 text-slate-700";
                return (
                <div
                  key={c.id}
                  draggable
                  onDragStart={() => setDraggingCatId(c.id)}
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={() => void dropCategoryOn(c.id)}
                  onDragEnd={() => setDraggingCatId(null)}
                  className={`flex items-center justify-between gap-2 rounded-xl border px-3.5 py-2.5 text-xs transition ${
                    draggingCatId === c.id ? "border-indigo-400 bg-indigo-50 opacity-70" : "border-slate-100 bg-slate-50 hover:border-slate-200"
                  }`}
                >
                  <div className="flex items-start gap-2">
                    <span title="Arraste para reordenar" className="mt-0.5 cursor-grab text-slate-400 hover:text-slate-700">
                      <GripVertical className="h-4 w-4" />
                    </span>
                    <div>
                      <p className="flex flex-wrap items-center gap-1.5 font-bold text-slate-900">
                        <span className="rounded bg-slate-200 px-1.5 py-0.5 font-mono text-[10px]">Ordem {c.displayOrder}</span>
                        <span>{c.name}</span>
                        <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${tplBadgeClass}`}>{tplLabel}</span>
                        <span className="rounded-full bg-purple-100 px-2 py-0.5 text-[10px] font-bold text-purple-800">{countInCat} itens</span>
                        {c.availabilityScheduleJson && (
                          <span className="inline-flex items-center gap-0.5 rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">
                            <Clock className="h-2.5 w-2.5" /> Programada
                          </span>
                        )}
                      </p>
                      {c.description && <p className="text-slate-500">{c.description}</p>}
                    </div>
                  </div>
                  <div className="flex items-center gap-1">
                    <button type="button" title="Subir categoria" onClick={() => moveCategory(idx, -1)} className="rounded p-1 hover:bg-slate-200">
                      <ArrowUp className="h-3.5 w-3.5" />
                    </button>
                    <button type="button" title="Descer categoria" onClick={() => moveCategory(idx, 1)} className="rounded p-1 hover:bg-slate-200">
                      <ArrowDown className="h-3.5 w-3.5" />
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setModalCategory(c);
                        setCatModalOpen(true);
                      }}
                      className="rounded px-2 py-1 text-[11px] font-bold text-slate-700 hover:bg-slate-200"
                    >
                      <Edit3 className="inline h-3.5 w-3.5" /> Editar
                    </button>
                    <button type="button" onClick={() => deleteCategory(c)} className="rounded p-1 text-rose-600 hover:bg-rose-50">
                      <Trash2 className="inline h-3.5 w-3.5" /> Excluir
                    </button>
                  </div>
                </div>
                );
              })}
            </div>
          </section>

          <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
              <div>
                <h3 className="text-base font-extrabold text-slate-900">Gestão de Produtos, Ficha Técnica & Alerta de Prejuízo ({products.length})</h3>
                <p className="text-[11px] text-slate-500">Fotos do computador, limites por tamanho e lucro automático por ficha técnica.</p>
              </div>
              <div className="flex flex-wrap items-center gap-1.5">
                <button
                  type="button"
                  onClick={() => setViewMode("table")}
                  className={`inline-flex items-center gap-1 rounded-xl border px-2.5 py-1.5 text-xs font-bold ${viewMode === "table" ? "bg-slate-900 text-white" : "border-slate-200 bg-white text-slate-700"}`}
                >
                  <List className="h-3.5 w-3.5" /> Tabela
                </button>
                <button
                  type="button"
                  onClick={() => setViewMode("cards")}
                  className={`inline-flex items-center gap-1 rounded-xl border px-2.5 py-1.5 text-xs font-bold ${viewMode === "cards" ? "bg-slate-900 text-white" : "border-slate-200 bg-white text-slate-700"}`}
                >
                  <LayoutGrid className="h-3.5 w-3.5" /> Cards
                </button>
                <button
                  type="button"
                  onClick={() => setExportCenterOpen(true)}
                  className="inline-flex items-center gap-1 rounded-xl bg-indigo-600 px-3 py-1.5 text-xs font-extrabold text-white hover:bg-indigo-700"
                >
                  <FileSpreadsheet className="h-3.5 w-3.5" /> Fichas &amp; Catálogo PDF/Excel
                </button>
                <button
                  type="button"
                  onClick={() => setFlyerOpen(true)}
                  className="inline-flex items-center gap-1 rounded-xl bg-amber-400 px-3 py-1.5 text-xs font-extrabold text-slate-950 hover:bg-amber-300"
                >
                  📄 Cardápio Panfleto
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setModalProduct(null);
                    setProdModalOpen(true);
                  }}
                  className="inline-flex items-center gap-1 rounded-xl bg-emerald-600 px-3 py-1.5 text-xs font-extrabold text-white hover:bg-emerald-700"
                >
                  + Novo Produto
                </button>
              </div>
            </div>
            <div className="mb-3 flex flex-wrap gap-1.5">
              <button
                type="button"
                onClick={() => setSelectedCatFilter("all")}
                className={`rounded-full border px-3 py-1 text-[11px] font-bold ${selectedCatFilter === "all" ? "border-transparent bg-slate-900 text-white" : "border-slate-200 bg-slate-50 text-slate-700"}`}
              >
                Todas ({products.length})
              </button>
              {[...categories]
                .sort((a, b) => a.displayOrder - b.displayOrder)
                .map((c) => (
                  <button
                    key={c.id}
                    type="button"
                    onClick={() => setSelectedCatFilter(c.id)}
                    className={`rounded-full border px-3 py-1 text-[11px] font-bold ${selectedCatFilter === c.id ? "border-transparent bg-purple-700 text-white" : "border-slate-200 bg-slate-50 text-slate-700"}`}
                  >
                    #{c.displayOrder} {c.name} ({products.filter((p) => p.categoryId === c.id).length})
                  </button>
                ))}
            </div>

            {viewMode === "table" ? (
              <div className="overflow-x-auto rounded-2xl border border-slate-200">
                <table className="w-full min-w-[820px] text-left text-xs">
                  <thead className="border-b border-slate-200 bg-slate-50 text-[11px] font-bold text-slate-600">
                    <tr>
                      <th className="px-3 py-2.5">Ordem</th>
                      <th className="px-3 py-2.5">Nome &amp; Categoria</th>
                      <th className="px-3 py-2.5">Preços de Venda &amp; Custos</th>
                      <th className="px-3 py-2.5">Destaques</th>
                      <th className="px-3 py-2.5 text-right">Ações</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 bg-white">
                    {[...products]
                      .filter((p) => selectedCatFilter === "all" || p.categoryId === selectedCatFilter)
                      .sort((a, b) => {
                        const ca = categories.find((c) => c.id === a.categoryId)?.displayOrder ?? 999;
                        const cb = categories.find((c) => c.id === b.categoryId)?.displayOrder ?? 999;
                        return ca - cb || (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.name.localeCompare(b.name);
                      })
                      .map((p) => {
                      const matchedSheet = techSheetSummaries.find((ts) => ts.productId === p.id);
                      return (
                        <tr key={p.id} className="hover:bg-slate-50/70">
                          <td className="px-3 py-2.5">
                            <div className="flex items-center gap-1">
                              <span className="rounded bg-slate-100 px-1.5 py-0.5 font-mono text-[10px] font-bold">#{p.displayOrder ?? 1}</span>
                              <button type="button" title="Subir na categoria" onClick={() => void reorderProductInCategory(p.categoryId, p.id, -1)} className="rounded p-0.5 hover:bg-slate-200">
                                <ArrowUp className="h-3 w-3" />
                              </button>
                              <button type="button" title="Descer na categoria" onClick={() => void reorderProductInCategory(p.categoryId, p.id, 1)} className="rounded p-0.5 hover:bg-slate-200">
                                <ArrowDown className="h-3 w-3" />
                              </button>
                            </div>
                          </td>
                          <td className="px-3 py-2.5">
                            <div className="flex items-center gap-2.5">
                              <ProductMediaFrame src={p.primaryImageUrl} alt={p.name} className="h-10 w-10 shrink-0 rounded-lg border border-slate-200 bg-white" iconSize={16} />
                              <div>
                                <p className="font-extrabold text-slate-900">{p.name}</p>
                                <div className="mt-0.5 flex items-center gap-1">
                                  <select
                                    aria-label={`Mover ${p.name} para categoria`}
                                    value={p.categoryId}
                                    onChange={(e) => void moveProductCategory(p.id, e.target.value)}
                                    className="rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 text-[10px] font-semibold text-slate-700"
                                  >
                                    {[...categories].sort((a, b) => a.displayOrder - b.displayOrder).map((cat) => (
                                      <option key={cat.id} value={cat.id}>{cat.name}</option>
                                    ))}
                                  </select>
                                  <span className="text-[10px] text-slate-400">· {p.modifierGroupIds.length} grp</span>
                                </div>
                              </div>
                            </div>
                          </td>
                          <td className="px-3 py-2.5 text-slate-700">
                            {p.variations.length > 0 ? (
                              <div className="space-y-0.5">
                                {p.variations.map((v) => {
                                  const freeRule = p.variationRules.find((r) => r.variationId === v.id && !r.isHidden && r.freeAllowance > 0);
                                  const costMatch = v.description?.match(/Custo\s*(?:estimado)?\s*R\$\s*([\d.,]+)/i);
                                  const costVal = v.costPrice && v.costPrice > 0 ? v.costPrice : costMatch ? Number(costMatch[1].replace(",", ".")) : (matchedSheet?.costPerPortion ?? 0);
                                  const profit = v.price - costVal;
                                  return (
                                    <div key={v.id} className="text-[11px]">
                                      <strong>{v.variationName}:</strong> Venda R$ {v.price.toFixed(2)}
                                      {costVal > 0 && <> · Custo R$ {costVal.toFixed(2)}</>}
                                      {freeRule && <> ({freeRule.freeAllowance} acomp.)</>}
                                      {costVal > 0 && (
                                        <span className={`ml-1 font-bold ${profit >= 0 ? "text-emerald-700" : "text-rose-700"}`}>
                                          {profit >= 0 ? `+R$ ${profit.toFixed(2)}` : `-R$ ${Math.abs(profit).toFixed(2)}`}
                                        </span>
                                      )}
                                    </div>
                                  );
                                })}
                              </div>
                            ) : (
                              <div>
                                <span>Venda R$ {p.basePrice.toFixed(2)}</span>
                                {matchedSheet && (
                                  <span className="ml-1 font-bold text-emerald-700">
                                    · Custo R$ {matchedSheet.costPerPortion.toFixed(2)} (+R$ {matchedSheet.profitPerUnit.toFixed(2)})
                                  </span>
                                )}
                              </div>
                            )}
                          </td>
                          <td className="px-3 py-2.5">
                            <div className="flex flex-wrap gap-1">
                              {p.isFeatured && <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">Mais Vendido</span>}
                              {p.isNew && <span className="rounded-full bg-blue-100 px-2 py-0.5 text-[10px] font-bold text-blue-800">Lançamento</span>}
                              {p.promotionalPrice && p.promotionalPrice < p.basePrice ? (
                                <span className="rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-extrabold text-rose-800">
                                  De R$ {p.basePrice.toFixed(2)} → R$ {p.promotionalPrice.toFixed(2)} (-{Math.round(((p.basePrice - p.promotionalPrice) / p.basePrice) * 100)}%)
                                </span>
                              ) : p.tagsCsv?.toLowerCase().includes("promo") ? (
                                <span className="rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-bold text-rose-800">Promoção</span>
                              ) : null}
                              {p.trackInventory && (
                                <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${(p.stockQuantity ?? 0) > 0 ? "bg-emerald-100 text-emerald-800" : "bg-rose-600 text-white"}`}>
                                  {(p.stockQuantity ?? 0) > 0 ? `Estoque: ${p.stockQuantity}` : "Esgotado"}
                                </span>
                              )}
                              {p.servesPeopleCount && (
                                <span className="rounded-full bg-slate-200 px-2 py-0.5 text-[10px] font-bold text-slate-700">
                                  Serve {p.servesPeopleCount}p
                                </span>
                              )}
                              {p.availabilityScheduleJson && (
                                <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">⏰ Horário</span>
                              )}
                              {!p.isFeatured && !p.isNew && !p.tagsCsv?.toLowerCase().includes("promo") && !p.promotionalPrice && !p.trackInventory && (
                                <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] text-slate-500">Padrão</span>
                              )}
                            </div>
                          </td>
                          <td className="px-3 py-2.5 text-right">
                            <div className="inline-flex flex-wrap justify-end gap-1">
                              <button
                                type="button"
                                onClick={() => void toggleBestSeller(p)}
                                className={`rounded-lg border px-2 py-1 text-[11px] font-semibold ${
                                  p.isFeatured ? "border-amber-400 bg-amber-100 text-amber-900" : "border-slate-300 bg-white text-slate-600"
                                }`}
                              >
                                <Star className="inline h-3 w-3" /> {p.isFeatured ? "No Topo" : "+ Mais Vendido"}
                              </button>
                              {matchedSheet && onOpenTechSheetPdf && (
                                <button
                                  type="button"
                                  onClick={() => onOpenTechSheetPdf(matchedSheet)}
                                  className="rounded-lg border border-indigo-200 bg-indigo-50 px-2 py-1 text-[11px] font-bold text-indigo-800"
                                >
                                  Receita &amp; PDF
                                </button>
                              )}
                              <button
                                type="button"
                                onClick={() => {
                                  setModalProduct(p);
                                  setProdModalOpen(true);
                                }}
                                className="rounded-lg border border-slate-300 bg-white px-2 py-1 text-[11px] font-bold text-slate-800"
                              >
                                Editar
                              </button>
                              <button type="button" onClick={() => toggleProduct(p)} className="rounded-lg border border-slate-300 bg-white px-2 py-1 text-[11px]">
                                {p.status === "Active" ? <Pause className="inline h-3 w-3" /> : <Play className="inline h-3 w-3" />}
                              </button>
                              <button type="button" onClick={() => deleteProduct(p.id)} className="rounded-lg border border-rose-200 bg-white px-2 py-1 text-[11px] text-rose-600">
                                <Trash2 className="inline h-3 w-3" /> Excluir
                              </button>
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            ) : (
            <div className="space-y-3">
              {[...products]
                .filter((p) => selectedCatFilter === "all" || p.categoryId === selectedCatFilter)
                .sort((a, b) => {
                  const ca = categories.find((c) => c.id === a.categoryId)?.displayOrder ?? 999;
                  const cb = categories.find((c) => c.id === b.categoryId)?.displayOrder ?? 999;
                  return ca - cb || (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.name.localeCompare(b.name);
                })
                .map((p) => {
                const isExpanded = expandedProdId === p.id;
                const catNameLookup = categories.find((c) => c.id === p.categoryId)?.name || "";
                const matchedSheet = techSheetSummaries.find((ts) => ts.productId === p.id);
                return (
                  <div key={p.id} className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <ProductMediaFrame src={p.primaryImageUrl} alt={p.name} className="h-14 w-14 shrink-0 rounded-xl border border-slate-200 bg-white" iconSize={20} />
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-1.5">
                          <span className="text-sm font-extrabold text-slate-900">{p.name}</span>
                          {catNameLookup && <span className="text-[11px] text-slate-500">({catNameLookup})</span>}
                          <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${p.status === "Active" ? "bg-emerald-100 text-emerald-800" : "bg-amber-100 text-amber-800"}`}>
                            {p.status}
                          </span>
                          {p.isFeatured && <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">Mais Vendido</span>}
                          {p.isNew && <span className="rounded-full bg-blue-100 px-2 py-0.5 text-[10px] font-bold text-blue-800">Lançamento</span>}
                          {p.promotionalPrice && p.promotionalPrice < p.basePrice ? (
                            <span className="rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-extrabold text-rose-800">
                              De R$ {p.basePrice.toFixed(2)} por R$ {p.promotionalPrice.toFixed(2)} (-{Math.round(((p.basePrice - p.promotionalPrice) / p.basePrice) * 100)}%)
                            </span>
                          ) : p.tagsCsv?.toLowerCase().includes("promo") ? (
                            <span className="rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-bold text-rose-800">Promoção</span>
                          ) : null}
                          {p.trackInventory && (
                            <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${(p.stockQuantity ?? 0) > 0 ? "bg-teal-100 text-teal-800" : "bg-rose-600 text-white"}`}>
                              {(p.stockQuantity ?? 0) > 0 ? `Estoque: ${p.stockQuantity} un` : "Esgotado"}
                            </span>
                          )}
                          {p.servesPeopleCount && (
                            <span className="rounded-full bg-slate-200 px-2 py-0.5 text-[10px] font-bold text-slate-700">
                              Serve {p.servesPeopleCount} {p.servesPeopleCount === 1 ? "pessoa" : "pessoas"}
                            </span>
                          )}
                          {p.dietaryTagsCsv && p.dietaryTagsCsv.split(",").filter(Boolean).map((tag) => (
                            <span key={tag} className="rounded-full bg-lime-100 px-2 py-0.5 text-[10px] font-bold uppercase text-lime-800">
                              {tag}
                            </span>
                          ))}
                          {p.availabilityScheduleJson && (
                            <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">⏰ Programado</span>
                          )}
                          {matchedSheet && (
                            matchedSheet.isLossAlert ? (
                              <span className="inline-flex items-center gap-1 rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-extrabold text-rose-800">
                                <AlertTriangle className="h-3 w-3" /> Prejuízo (CMV {matchedSheet.cmvPercent}%)
                              </span>
                            ) : matchedSheet.isLowMarginAlert ? (
                              <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">
                                Margem {matchedSheet.marginPercent}%
                              </span>
                            ) : (
                              <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-bold text-emerald-800">
                                Margem {matchedSheet.marginPercent}% (+R$ {matchedSheet.profitPerUnit.toFixed(2)})
                              </span>
                            )
                          )}
                        </div>
                        <p className="mt-0.5 text-xs text-slate-600">
                          {p.variations.length > 0
                            ? p.variations.map((v) => `${v.variationName}: R$ ${v.price.toFixed(2)}`).join(" · ")
                            : <>Preço base: <strong>R$ {p.basePrice.toFixed(2)}</strong></>}
                          {" "}· {p.modifierGroupIds.length} grupo(s)
                        </p>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        <span className="rounded bg-white px-1.5 py-0.5 font-mono text-[10px] font-bold text-slate-600 border border-slate-200">#{p.displayOrder ?? 1}</span>
                        <button type="button" title="Subir na categoria" onClick={() => void reorderProductInCategory(p.categoryId, p.id, -1)} className="rounded-lg border border-slate-300 bg-white p-1 hover:bg-slate-100">
                          <ArrowUp className="h-3 w-3" />
                        </button>
                        <button type="button" title="Descer na categoria" onClick={() => void reorderProductInCategory(p.categoryId, p.id, 1)} className="rounded-lg border border-slate-300 bg-white p-1 hover:bg-slate-100">
                          <ArrowDown className="h-3 w-3" />
                        </button>
                        <select
                          aria-label={`Mover ${p.name} para categoria`}
                          value={p.categoryId}
                          onChange={(e) => void moveProductCategory(p.id, e.target.value)}
                          className="rounded-lg border border-slate-300 bg-white px-2 py-1 text-[11px] font-semibold text-slate-700"
                        >
                          {[...categories].sort((a, b) => a.displayOrder - b.displayOrder).map((cat) => (
                            <option key={cat.id} value={cat.id}>Mover: {cat.name}</option>
                          ))}
                        </select>
                        <button
                          type="button"
                          onClick={() => void toggleBestSeller(p)}
                          title={p.isFeatured ? "Remover de Mais Vendidos" : "Fixar em Mais Vendidos"}
                          className={`inline-flex items-center gap-1 rounded-lg border px-2 py-1 text-xs font-semibold ${
                            p.isFeatured ? "border-amber-400 bg-amber-100 text-amber-900" : "border-slate-300 bg-white text-slate-600"
                          }`}
                        >
                          <Star className="h-3.5 w-3.5" /> {p.isFeatured ? "No Topo" : "+ Topo"}
                        </button>
                        {matchedSheet && onOpenTechSheetPdf && (
                          <button
                            type="button"
                            onClick={() => onOpenTechSheetPdf(matchedSheet)}
                            className="inline-flex items-center gap-1 rounded-lg border border-indigo-200 bg-indigo-50 px-2.5 py-1 text-xs font-bold text-indigo-800 hover:bg-indigo-100"
                          >
                            <FileText className="h-3.5 w-3.5" /> Receita & FT PDF
                          </button>
                        )}
                        <button
                          type="button"
                          onClick={() => {
                            setModalProduct(p);
                            setProdModalOpen(true);
                          }}
                          className="rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-xs font-bold text-slate-800 hover:bg-slate-100"
                        >
                          Editar
                        </button>
                        <button
                          type="button"
                          onClick={() => setExpandedProdId(isExpanded ? null : p.id)}
                          className="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700"
                        >
                          <Sliders className="h-3.5 w-3.5" /> Regras
                        </button>
                        <button type="button" onClick={() => toggleProduct(p)} className="rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold">
                          {p.status === "Active" ? <Pause className="inline h-3.5 w-3.5" /> : <Play className="inline h-3.5 w-3.5" />}
                        </button>
                        <button type="button" onClick={() => deleteProduct(p.id)} className="rounded-lg border border-rose-200 bg-white px-2.5 py-1 text-xs font-semibold text-rose-600">
                          <Trash2 className="inline h-3.5 w-3.5" />
                        </button>
                      </div>
                    </div>

                    {isExpanded && (
                      <div className="mt-4 space-y-4 border-t border-slate-200 pt-4 text-xs">
                        {/* Tamanhos / Variações */}
                        <div>
                          <p className="mb-2 font-bold text-slate-800">Tamanhos / Variações</p>
                          <div className="mb-2 flex flex-wrap gap-2">
                            {p.variations.map((v) => (
                              <span key={v.id} className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-1.5 font-semibold">
                                {v.variationName} (R$ {v.price.toFixed(2)}, máx {v.maxFlavors} sabor)
                                <button type="button" onClick={() => removeVariation(p.id, v.id)} className="text-rose-600">
                                  ×
                                </button>
                              </span>
                            ))}
                          </div>
                          <form onSubmit={(e) => addVariation(e, p.id)} className="flex flex-wrap gap-2">
                            <input value={varName} onChange={(e) => setVarName(e.target.value)} required placeholder="Nome (ex: Copo 500ml)" className="rounded-lg border bg-white px-2.5 py-1.5" />
                            <input type="number" step="0.01" value={varPrice} onChange={(e) => setVarPrice(e.target.value)} required placeholder="Preço" className="w-24 rounded-lg border bg-white px-2.5 py-1.5" />
                            <input value={varDesc} onChange={(e) => setVarDesc(e.target.value)} placeholder="Descrição curta" className="rounded-lg border bg-white px-2.5 py-1.5" />
                            <input type="number" value={varMeasureAmt} onChange={(e) => setVarMeasureAmt(e.target.value)} placeholder="Medida" className="w-20 rounded-lg border bg-white px-2.5 py-1.5" />
                            <input value={varMeasureUnit} onChange={(e) => setVarMeasureUnit(e.target.value)} placeholder="Un (ml/fatias)" className="w-20 rounded-lg border bg-white px-2.5 py-1.5" />
                            <input type="number" value={varMaxFlavors} onChange={(e) => setVarMaxFlavors(e.target.value)} placeholder="Sabores" className="w-16 rounded-lg border bg-white px-2.5 py-1.5" />
                            <button type="submit" className="rounded-lg bg-slate-900 px-3 py-1.5 font-bold text-white">
                              + Tamanho
                            </button>
                          </form>
                        </div>

                        {/* Grupos Vinculados */}
                        <div>
                          <p className="mb-2 font-bold text-slate-800">Grupos de Personalização Vinculados</p>
                          <div className="flex flex-wrap gap-2">
                            {modifierGroups.map((g) => {
                              const linked = p.modifierGroupIds.includes(g.id);
                              return (
                                <button
                                  key={g.id}
                                  type="button"
                                  onClick={() => toggleGroupLink(p, g.id)}
                                  className={`rounded-xl border px-3 py-1.5 font-semibold ${
                                    linked ? "border-emerald-600 bg-emerald-50 text-emerald-900" : "border-slate-200 bg-white text-slate-600"
                                  }`}
                                >
                                  {linked ? "✓ " : "+ "} {g.name}
                                </button>
                              );
                            })}
                          </div>
                        </div>

                        {/* Regras Específicas por Tamanho */}
                        <div>
                          <p className="mb-2 font-bold text-slate-800">Limites Dinâmicos por Tamanho (ex: 300ml = 3 grátis, 500ml = 5 grátis)</p>
                          {p.variationRules.length > 0 && (
                            <div className="mb-2 flex flex-wrap gap-2">
                              {p.variationRules.map((r) => {
                                const vName = p.variations.find((v) => v.id === r.variationId)?.variationName || "Padrão";
                                const gName = modifierGroups.find((g) => g.id === r.modifierGroupId)?.name || "Grupo";
                                return (
                                  <span key={r.id} className="rounded-lg bg-blue-50 px-2.5 py-1 font-semibold text-blue-900">
                                    {vName} → {gName}: min {r.minSelections} / máx {r.maxSelections} / grátis {r.freeAllowance}
                                  </span>
                                );
                              })}
                            </div>
                          )}
                          <form onSubmit={(e) => saveVariationRule(e, p.id)} className="flex flex-wrap items-center gap-2">
                            <select value={ruleVarId} onChange={(e) => setRuleVarId(e.target.value)} className="rounded-lg border bg-white px-2.5 py-1.5">
                              <option value="">Todos os tamanhos</option>
                              {p.variations.map((v) => (
                                <option key={v.id} value={v.id}>
                                  {v.variationName}
                                </option>
                              ))}
                            </select>
                            <select value={ruleGroupId} onChange={(e) => setRuleGroupId(e.target.value)} required className="rounded-lg border bg-white px-2.5 py-1.5">
                              <option value="">Selecione o grupo...</option>
                              {modifierGroups.map((g) => (
                                <option key={g.id} value={g.id}>
                                  {g.name}
                                </option>
                              ))}
                            </select>
                            <input type="number" value={ruleMin} onChange={(e) => setRuleMin(e.target.value)} placeholder="Mín" className="w-16 rounded-lg border bg-white px-2 py-1.5" />
                            <input type="number" value={ruleMax} onChange={(e) => setRuleMax(e.target.value)} placeholder="Máx" className="w-16 rounded-lg border bg-white px-2 py-1.5" />
                            <input type="number" value={ruleFree} onChange={(e) => setRuleFree(e.target.value)} placeholder="Grátis" className="w-16 rounded-lg border bg-white px-2 py-1.5" />
                            <label className="flex items-center gap-1">
                              <input type="checkbox" checked={ruleRequired} onChange={(e) => setRuleRequired(e.target.checked)} /> Obrigatório
                            </label>
                            <button type="submit" className="rounded-lg bg-blue-700 px-3 py-1.5 font-bold text-white">
                              Salvar Regra
                            </button>
                          </form>
                        </div>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
            )}
          </section>
        </div>

        {/* Coluna Lateral de Cadastro Categoria & Produto */}
        <div className="space-y-6">
          <form onSubmit={saveCategory} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-sm">
            <h3 className="flex items-center gap-1.5 font-extrabold text-slate-900">
              <Layers className="h-4 w-4" /> {editingCatId ? "Editar Categoria" : "Nova Categoria"}
            </h3>
            <input value={catName} onChange={(e) => setCatName(e.target.value)} required placeholder="Nome da categoria" className="w-full rounded-xl border border-slate-300 px-3 py-2" />
            <input value={catDesc} onChange={(e) => setCatDesc(e.target.value)} placeholder="Descrição (opcional)" className="w-full rounded-xl border border-slate-300 px-3 py-2" />
            <input value={catNotes} onChange={(e) => setCatNotes(e.target.value)} placeholder="Regra comercial da categoria (opcional)" className="w-full rounded-xl border border-slate-300 px-3 py-2" />
            <div className="flex gap-2">
              <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="flex-1 rounded-xl py-2 font-bold text-white">
                {editingCatId ? "Salvar" : "Criar Categoria"}
              </button>
              {editingCatId && (
                <button type="button" onClick={() => setEditingCatId(null)} className="rounded-xl border px-3 py-2 text-xs">
                  Cancelar
                </button>
              )}
            </div>
          </form>

          <form onSubmit={saveProduct} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-sm">
            <h3 className="flex items-center gap-1.5 font-extrabold text-slate-900">
              <Plus className="h-4 w-4" /> {editingProdId ? "Editar Produto" : "Novo Produto"}
            </h3>
            <select value={prodCatId || categories[0]?.id || ""} onChange={(e) => setProdCatId(e.target.value)} required className="w-full rounded-xl border border-slate-300 px-3 py-2">
              {categories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
            <input value={prodName} onChange={(e) => setProdName(e.target.value)} required placeholder="Nome do produto" className="w-full rounded-xl border border-slate-300 px-3 py-2" />
            <textarea rows={2} value={prodDesc} onChange={(e) => setProdDesc(e.target.value)} placeholder="Descrição comercial" className="w-full rounded-xl border border-slate-300 px-3 py-2" />
            <div className="grid grid-cols-2 gap-2">
              <input type="number" step="0.01" value={prodPrice} onChange={(e) => setProdPrice(e.target.value)} required placeholder="Preço base" className="rounded-xl border border-slate-300 px-3 py-2" />
              <input value={prodSku} onChange={(e) => setProdSku(e.target.value)} placeholder="SKU (opcional)" className="rounded-xl border border-slate-300 px-3 py-2" />
            </div>
            <label className="block text-xs font-semibold text-slate-600">
              <ImageIcon className="mr-1 inline h-3.5 w-3.5" /> Foto do Produto (Upload do PC ou URL)
              <div className="mt-1 flex items-center gap-2">
                {resolveMediaUrl(prodImgUrl) && (
                  <img src={resolveMediaUrl(prodImgUrl)!} alt="Preview" className="h-10 w-10 rounded-lg border object-cover" />
                )}
                <label className="inline-flex cursor-pointer items-center gap-1.5 rounded-xl border border-slate-300 bg-slate-50 px-3 py-2 text-xs font-bold text-slate-800 hover:bg-slate-100">
                  <Upload className="h-3.5 w-3.5" /> {sidebarUploadingImg ? "Enviando..." : "Escolher do PC"}
                  <input type="file" accept="image/*" onChange={handleSidebarPhotoUpload} className="hidden" />
                </label>
              </div>
              <input value={prodImgUrl} onChange={(e) => setProdImgUrl(e.target.value)} placeholder="URL ou /api/v1/public/media/..." className="mt-1.5 w-full rounded-xl border border-slate-300 px-3 py-1.5 text-[11px]" />
            </label>
            <div className="grid grid-cols-2 gap-2">
              <select value={prodPricingStrategy} onChange={(e) => setProdPricingStrategy(e.target.value)} className="rounded-xl border border-slate-300 px-2.5 py-2 text-xs">
                <option value="1">Preço Fixo + Adicionais</option>
                <option value="2">Pizza: Maior Sabor</option>
                <option value="3">Pizza: Média Sabores</option>
                <option value="4">Proporcional Sabores</option>
              </select>
              <input type="number" value={prodPrepMin} onChange={(e) => setProdPrepMin(e.target.value)} placeholder="Tempo min" className="rounded-xl border border-slate-300 px-3 py-2 text-xs" />
            </div>
            <div className="grid grid-cols-2 gap-2 text-xs font-medium text-slate-700">
              <label className="flex items-center gap-1.5"><input type="checkbox" checked={prodFeatured} onChange={(e) => setProdFeatured(e.target.checked)} /> Destaque / Popular</label>
              <label className="flex items-center gap-1.5"><input type="checkbox" checked={prodNew} onChange={(e) => setProdNew(e.target.checked)} /> Lançamento</label>
              <label className="flex items-center gap-1.5"><input type="checkbox" checked={prodRecommended} onChange={(e) => setProdRecommended(e.target.checked)} /> Recomendado</label>
              <label className="flex items-center gap-1.5"><input type="checkbox" checked={prodAllowsNotes} onChange={(e) => setProdAllowsNotes(e.target.checked)} /> Aceita obs.</label>
            </div>
            <div className="flex gap-2">
              <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="flex-1 rounded-xl py-2.5 font-bold text-white">
                {editingProdId ? "Salvar Produto" : "Adicionar Produto"}
              </button>
              {editingProdId && (
                <button type="button" onClick={() => setEditingProdId(null)} className="rounded-xl border px-3 py-2 text-xs">
                  Cancelar
                </button>
              )}
            </div>
          </form>
        </div>
      </div>

      {catToDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/70 p-4 backdrop-blur-sm">
          <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl space-y-4 text-xs">
            <h3 className="text-base font-extrabold text-slate-900">Excluir Categoria &quot;{catToDelete.name}&quot;</h3>
            <p className="text-slate-600">
              Esta categoria possui <strong>{products.filter((p) => p.categoryId === catToDelete.id).length}</strong> produto(s). Escolha para qual categoria deseja transferi-los antes de excluir:
            </p>
            <select
              value={moveTargetCatId}
              onChange={(e) => setMoveTargetCatId(e.target.value)}
              className="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm font-semibold"
            >
              {categories
                .filter((c) => c.id !== catToDelete.id)
                .map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
            </select>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => void confirmDeleteWithMove()}
                className="flex-1 rounded-xl bg-rose-600 py-2.5 font-extrabold text-white hover:bg-rose-700"
              >
                Mover Produtos &amp; Excluir Categoria
              </button>
              <button
                type="button"
                onClick={() => setCatToDelete(null)}
                className="rounded-xl border border-slate-300 px-4 py-2.5 font-bold text-slate-700"
              >
                Cancelar
              </button>
            </div>
          </div>
        </div>
      )}

      {catModalOpen && (
        <AdminCategoryEditModal
          token={token}
          category={modalCategory}
          nextDisplayOrder={categories.length + 1}
          onClose={() => setCatModalOpen(false)}
          onNotify={onNotify}
          onSaved={onReload}
        />
      )}

      {prodModalOpen && (
        <AdminProductFullModal
          token={token}
          segmentCode={segmentCode}
          categories={categories}
          modifierGroups={modifierGroups}
          product={modalProduct}
          estimatedSheetCost={techSheetSummaries.find((s) => s.productId === modalProduct?.id)?.costPerPortion}
          onClose={() => setProdModalOpen(false)}
          onNotify={onNotify}
          onSaved={onReload}
        />
      )}

      <AdminFlyerModal
        open={flyerOpen}
        slug={slug}
        storeTradeName={storeTradeName}
        logoUrl={logoUrl}
        slogan={slogan}
        primaryColorHex={primaryColorHex}
        accentColorHex={accentColorHex}
        categories={categories}
        products={products}
        onClose={() => setFlyerOpen(false)}
      />

      <AdminCatalogAndSheetsExportModal
        open={exportCenterOpen}
        slug={slug}
        storeTradeName={storeTradeName}
        formattedCnpj={formattedCnpj}
        logoUrl={logoUrl}
        slogan={slogan}
        primaryColorHex={primaryColorHex}
        accentColorHex={accentColorHex}
        storeAddress={storeAddress}
        whatsAppNumber={whatsAppNumber}
        instagramHandle={instagramHandle}
        categories={categories}
        products={products}
        modifierGroups={modifierGroups}
        techSheets={techSheetSummaries}
        combos={combos}
        onNotify={onNotify}
        onClose={() => setExportCenterOpen(false)}
      />
    </div>
  );
}
