"use client";

import { AlertTriangle, ArrowDown, ArrowUp, Edit3, FileSpreadsheet, FileText, Flame, Image as ImageIcon, Layers, Pause, Play, Plus, Sliders, Sparkles, Star, Trash2 } from "lucide-react";
import { useState } from "react";
import { apiFetch } from "@/lib/api";
import { TechnicalSheetSummaryItem } from "./AdminTechSheetPdfModal";

export interface AdminCategoryDto {
  id: string;
  name: string;
  description?: string | null;
  imageUrl?: string | null;
  displayOrder: number;
  segmentTemplateCode?: string | null;
  ruleNotes?: string | null;
  status: string;
}

export interface AdminVariationDto {
  id: string;
  variationName: string;
  description?: string | null;
  price: number;
  measureAmount?: number | null;
  measureUnit?: string | null;
  maxFlavors: number;
  sku?: string | null;
  displayOrder: number;
  isActive: boolean;
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
  status: string;
  saleMode: string;
  pricingStrategy: number;
  isFeatured: boolean;
  isNew: boolean;
  isRecommended: boolean;
  allowsNotes: boolean;
  preparationMinutes?: number | null;
  tagsCsv?: string | null;
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

const SEGMENT_TEMPLATES = [
  { code: "acaiteria", label: "Açaíteria (300/500/700ml/1L)" },
  { code: "pizzaria", label: "Pizzaria (Tamanhos, Sabores e Bordas)" },
  { code: "hamburgueria", label: "Hamburgueria (Pão, Carnes, Ponto)" },
  { code: "pastelaria", label: "Pastelaria (Recheios e Extras)" },
  { code: "sorveteria", label: "Sorveteria (Bolas e Coberturas)" },
  { code: "restaurante", label: "Restaurante / Lanchonete" },
  { code: "adega", label: "Adega & Tabacaria" },
  { code: "padaria", label: "Padaria & Confeitaria" },
  { code: "doceria", label: "Doceria & Bolos" },
  { code: "outros", label: "Modelo Universal Outros" }
];

export function AdminCatalogTab({
  token,
  categories,
  products,
  modifierGroups,
  techSheetSummaries = [],
  onOpenTechSheetPdf,
  onNotify,
  onReload
}: {
  token: string;
  categories: AdminCategoryDto[];
  products: AdminProductDto[];
  modifierGroups: AdminModifierGroupSummary[];
  techSheetSummaries?: TechnicalSheetSummaryItem[];
  onOpenTechSheetPdf?: (sheet: TechnicalSheetSummaryItem) => void;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [tplLoading, setTplLoading] = useState(false);
  const [kitSegmentCode, setKitSegmentCode] = useState("pizzaria");

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

  async function importSpreadsheetKit(code: string) {
    setTplLoading(true);
    const res = await apiFetch<{ segmentCode: string; categoriesCreated: number; suppliesCreated: number; productsCreated: number; sheetsCreated: number; combosCreated: number }>(
      "/api/v1/catalog/import-spreadsheet-kit",
      { method: "POST", body: JSON.stringify({ segmentCode: code }) },
      token
    );
    setTplLoading(false);
    if (res.ok && res.data) {
      onNotify(
        `Kit Ficha Técnica "${res.data.segmentCode}" importado: ${res.data.suppliesCreated} insumos, ${res.data.productsCreated} produtos, ${res.data.sheetsCreated} receitas CMV e ${res.data.combosCreated} combo(s)!`
      );
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao importar kit de planilha.");
  }

  async function toggleBestSeller(p: AdminProductDto) {
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
          isFeatured: !p.isFeatured,
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
      onNotify(!p.isFeatured ? `"${p.name}" destacado em Mais Vendidos!` : `"${p.name}" removido de Mais Vendidos.`);
      await onReload();
    }
  }

  async function applySegmentTemplate(code: string) {
    setTplLoading(true);
    const res = await apiFetch<{ categoriesCreated: number; productsCreated: number; modifierGroupsCreated: number }>(
      "/api/v1/catalog/apply-segment-template",
      { method: "POST", body: JSON.stringify({ segmentCode: code }) },
      token
    );
    setTplLoading(false);
    if (res.ok && res.data) {
      onNotify(
        `Modelo "${code}" aplicado: ${res.data.categoriesCreated} categorias, ${res.data.productsCreated} produtos e ${res.data.modifierGroupsCreated} grupos de personalização criados.`
      );
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao aplicar modelo do segmento.");
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

  async function moveCategory(idx: number, dir: -1 | 1) {
    const targetIdx = idx + dir;
    if (targetIdx < 0 || targetIdx >= categories.length) return;
    const copy = [...categories];
    const [moved] = copy.splice(idx, 1);
    copy.splice(targetIdx, 0, moved);
    const payload: Record<string, number> = {};
    copy.forEach((c, i) => {
      payload[c.id] = i + 1;
    });
    await apiFetch("/api/v1/catalog/categories/reorder", { method: "POST", body: JSON.stringify(payload) }, token);
    await onReload();
  }

  async function deleteCategory(id: string) {
    const res = await apiFetch(`/api/v1/catalog/categories/${id}`, { method: "DELETE" }, token);
    if (res.ok) {
      onNotify("Categoria excluída!");
      await onReload();
    } else onNotify(res.errorMessage || "Não foi possível excluir categoria com produtos.");
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
      {/* Modelos + Kit Planilhas 9 Segmentos */}
      <section className="rounded-2xl border border-slate-200 bg-gradient-to-r from-slate-900 to-slate-800 p-5 text-white shadow-sm space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="inline-flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-emerald-400">
              <Sparkles className="h-3.5 w-3.5" /> Modelos Inteligentes & Kit Planilhas Ficha Técnica (9 Segmentos)
            </p>
            <h2 className="mt-1 text-base font-extrabold">Popule insumos, produtos, receitas CMV e combos prontos em 1 clique</h2>
          </div>

          <div className="flex flex-wrap items-center gap-2 rounded-xl bg-white/10 p-2">
            <FileSpreadsheet className="h-4 w-4 text-emerald-400" />
            <select
              value={kitSegmentCode}
              onChange={(e) => setKitSegmentCode(e.target.value)}
              className="rounded-lg bg-slate-900 px-2.5 py-1.5 text-xs font-bold text-white"
            >
              {SEGMENT_TEMPLATES.map((t) => (
                <option key={t.code} value={t.code}>
                  {t.label}
                </option>
              ))}
            </select>
            <button
              type="button"
              disabled={tplLoading}
              onClick={() => void importSpreadsheetKit(kitSegmentCode)}
              className="rounded-lg bg-emerald-500 px-3 py-1.5 text-xs font-extrabold text-white hover:bg-emerald-400 disabled:opacity-40"
            >
              Importar Kit Planilha (CMV + Estoque)
            </button>
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          {SEGMENT_TEMPLATES.slice(0, 6).map((t) => (
            <button
              key={t.code}
              type="button"
              disabled={tplLoading}
              onClick={() => applySegmentTemplate(t.code)}
              className="rounded-xl bg-white/10 px-3 py-1.5 text-xs font-semibold text-white transition hover:bg-white/20 disabled:opacity-40"
            >
              + Motor {t.label}
            </button>
          ))}
        </div>
      </section>

      {/* Gestor Visual de Mais Vendidos */}
      <section className="rounded-2xl border border-amber-200 bg-amber-50/70 p-4 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="flex items-center gap-1.5 text-xs font-extrabold uppercase tracking-wide text-amber-900">
            <Flame className="h-4 w-4 text-amber-600" /> Gestão de Mais Vendidos & Destaques na Vitrine ({products.filter((x) => x.isFeatured).length} ativos)
          </h3>
          <span className="text-[11px] text-amber-800">Clique na estrela de qualquer produto abaixo para fixar em &quot;Mais Vendidos&quot;.</span>
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-3">
        {/* Lista de Categorias e Produtos */}
        <div className="space-y-6 lg:col-span-2">
          <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h3 className="mb-3 text-base font-extrabold text-slate-900">Categorias ({categories.length})</h3>
            <div className="space-y-2">
              {categories.map((c, idx) => (
                <div key={c.id} className="flex items-center justify-between gap-2 rounded-xl border border-slate-100 bg-slate-50 px-3.5 py-2.5 text-xs">
                  <div>
                    <p className="font-bold text-slate-900">{c.name}</p>
                    {c.description && <p className="text-slate-500">{c.description}</p>}
                  </div>
                  <div className="flex items-center gap-1">
                    <button type="button" onClick={() => moveCategory(idx, -1)} className="rounded p-1 hover:bg-slate-200">
                      <ArrowUp className="h-3.5 w-3.5" />
                    </button>
                    <button type="button" onClick={() => moveCategory(idx, 1)} className="rounded p-1 hover:bg-slate-200">
                      <ArrowDown className="h-3.5 w-3.5" />
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setEditingCatId(c.id);
                        setCatName(c.name);
                        setCatDesc(c.description || "");
                        setCatNotes(c.ruleNotes || "");
                      }}
                      className="rounded p-1 text-slate-600 hover:bg-slate-200"
                    >
                      <Edit3 className="h-3.5 w-3.5" />
                    </button>
                    <button type="button" onClick={() => deleteCategory(c.id)} className="rounded p-1 text-rose-600 hover:bg-rose-50">
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <h3 className="mb-3 text-base font-extrabold text-slate-900">Produtos do Cardápio ({products.length})</h3>
            <div className="space-y-3">
              {products.map((p) => {
                const isExpanded = expandedProdId === p.id;
                return (
                  <div key={p.id} className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-1.5">
                          <span className="text-sm font-extrabold text-slate-900">{p.name}</span>
                          <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${p.status === "Active" ? "bg-emerald-100 text-emerald-800" : "bg-amber-100 text-amber-800"}`}>
                            {p.status}
                          </span>
                          {p.isFeatured && <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">Mais Vendido</span>}
                          {p.isNew && <span className="rounded-full bg-blue-100 px-2 py-0.5 text-[10px] font-bold text-blue-800">Lançamento</span>}
                          {(() => {
                            const matchedSheet = techSheetSummaries.find((ts) => ts.productId === p.id);
                            if (!matchedSheet) return null;
                            if (matchedSheet.isLossAlert) {
                              return (
                                <span className="inline-flex items-center gap-1 rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-extrabold text-rose-800">
                                  <AlertTriangle className="h-3 w-3" /> Prejuízo (CMV {matchedSheet.cmvPercent}%)
                                </span>
                              );
                            }
                            if (matchedSheet.isLowMarginAlert) {
                              return (
                                <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">
                                  Margem {matchedSheet.marginPercent}%
                                </span>
                              );
                            }
                            return (
                              <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-bold text-emerald-800">
                                Margem {matchedSheet.marginPercent}%
                              </span>
                            );
                          })()}
                        </div>
                        <p className="mt-0.5 text-xs text-slate-600">
                          Preço base: <strong>R$ {p.basePrice.toFixed(2)}</strong> · {p.variations.length} tamanho(s) · {p.modifierGroupIds.length} grupo(s)
                        </p>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        <button
                          type="button"
                          onClick={() => void toggleBestSeller(p)}
                          title={p.isFeatured ? "Remover de Mais Vendidos" : "Destacar em Mais Vendidos"}
                          className={`inline-flex items-center gap-1 rounded-lg border px-2 py-1 text-xs font-semibold ${
                            p.isFeatured ? "border-amber-400 bg-amber-100 text-amber-900" : "border-slate-300 bg-white text-slate-600"
                          }`}
                        >
                          <Star className="h-3.5 w-3.5" />
                        </button>
                        {(() => {
                          const matchedSheet = techSheetSummaries.find((ts) => ts.productId === p.id);
                          if (!matchedSheet || !onOpenTechSheetPdf) return null;
                          return (
                            <button
                              type="button"
                              onClick={() => onOpenTechSheetPdf(matchedSheet)}
                              className="inline-flex items-center gap-1 rounded-lg border border-indigo-200 bg-indigo-50 px-2.5 py-1 text-xs font-bold text-indigo-800 hover:bg-indigo-100"
                            >
                              <FileText className="h-3.5 w-3.5" /> Receita & FT PDF
                            </button>
                          );
                        })()}
                        <button
                          type="button"
                          onClick={() => setExpandedProdId(isExpanded ? null : p.id)}
                          className="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700"
                        >
                          <Sliders className="h-3.5 w-3.5" /> Tamanhos & Regras
                        </button>
                        <button type="button" onClick={() => startEditProduct(p)} className="rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold">
                          Editar
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
              <ImageIcon className="mr-1 inline h-3.5 w-3.5" /> URL da Foto Principal
              <input value={prodImgUrl} onChange={(e) => setProdImgUrl(e.target.value)} placeholder="https://..." className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 text-xs" />
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
    </div>
  );
}
