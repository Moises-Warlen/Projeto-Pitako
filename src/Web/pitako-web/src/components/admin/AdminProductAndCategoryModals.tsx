"use client";

import { Plus, Trash2, Upload, X } from "lucide-react";
import { useState } from "react";
import { apiFetch, compressImageFileToDataUrl, resolveMediaUrl } from "@/lib/api";
import { AdminCategoryDto, AdminModifierGroupSummary, AdminProductDto } from "./AdminCatalogTab";

export interface ModifierGroupDetailedDto extends AdminModifierGroupSummary {
  groupType?: number;
  options?: Array<{ id: string; name: string; additionalPrice: number }>;
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
  const [name, setName] = useState(category?.name || "");
  const [description, setDescription] = useState(category?.description || "");
  const [displayOrder, setDisplayOrder] = useState(String(category?.displayOrder ?? nextDisplayOrder));
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
      ruleNotes: category?.ruleNotes || null
    };

    const res = category
      ? await apiFetch(`/api/v1/catalog/categories/${category.id}`, { method: "PUT", body: JSON.stringify(payload) }, token)
      : await apiFetch("/api/v1/catalog/categories", { method: "POST", body: JSON.stringify(payload) }, token);

    setSaving(false);
    if (res.ok) {
      onNotify(category ? `Categoria "${name}" atualizada (ordem ${payload.displayOrder})!` : "Nova categoria criada!");
      await onSaved();
      onClose();
    } else {
      onNotify(res.errorMessage || "Erro ao salvar categoria.");
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/70 p-4 backdrop-blur-sm">
      <form onSubmit={handleSave} className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-2xl space-y-4">
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
            <label className="font-bold text-slate-700">Nome da Categoria</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              placeholder="Ex: Combos que valem a pena"
              className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm font-semibold"
            />
          </div>
          <div>
            <label className="font-bold text-slate-700">Subtítulo / Frase de Destaque</label>
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Ex: Mais sabor e muito mais economia para o seu momento"
              className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2.5 text-xs"
            />
          </div>
          <div>
            <label className="font-bold text-slate-700">Ordem de Exibição (1º, 2º, 3º...)</label>
            <input
              type="number"
              min="1"
              value={displayOrder}
              onChange={(e) => setDisplayOrder(e.target.value)}
              className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2.5 font-mono text-sm font-bold"
            />
          </div>
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

interface SizeFormRow {
  id?: string | null;
  variationName: string;
  measureAmount: string;
  measureUnit: string;
  price: string;
  costAmount: string;
  freeAllowance: string;
  allowPaidExtras: boolean;
}

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

  const freeGroups = modifierGroups.filter((g) => g.selectionKind !== 3 && g.groupType !== 2);
  const paidGroups = modifierGroups.filter((g) => g.selectionKind === 3 || g.groupType === 2);

  const [name, setName] = useState(product?.name || "");
  const [categoryId, setCategoryId] = useState(product?.categoryId || categories[0]?.id || "");
  const [isNew, setIsNew] = useState(Boolean(product?.isNew));
  const [isPromotion, setIsPromotion] = useState(Boolean(product?.tagsCsv?.toLowerCase().includes("promo")));
  const [description, setDescription] = useState(product?.description || "");
  const [basePrice, setBasePrice] = useState(product ? String(product.basePrice) : "19.90");
  const [imageUrl, setImageUrl] = useState(product?.primaryImageUrl || "");
  const [uploadingImg, setUploadingImg] = useState(false);

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

  const [sizes, setSizes] = useState<SizeFormRow[]>(() => {
    if (product && product.variations.length > 0) {
      return product.variations.map((v) => {
        const freeRule = product.variationRules.find((r) => r.variationId === v.id && freeGroups.some((fg) => fg.id === r.modifierGroupId));
        const paidRule = product.variationRules.find((r) => r.variationId === v.id && paidGroups.some((pg) => pg.id === r.modifierGroupId));
        const costMatch = v.description?.match(/Custo\s*(?:estimado)?\s*R\$\s*([\d.,]+)/i);
        return {
          id: v.id,
          variationName: v.variationName,
          measureAmount: v.measureAmount ? String(v.measureAmount) : "",
          measureUnit: v.measureUnit || labels.defaultUnit,
          price: String(v.price),
          costAmount: costMatch ? costMatch[1].replace(",", ".") : estimatedSheetCost ? String(estimatedSheetCost) : "0",
          freeAllowance: String(freeRule?.freeAllowance ?? Math.max(0, v.maxFlavors || 3)),
          allowPaidExtras: paidRule ? !paidRule.isHidden : true
        };
      });
    }
    if (segmentCode?.toLowerCase() === "pizzaria") {
      return [
        { variationName: "Broto (4 fatias)", measureAmount: "4", measureUnit: "fatias", price: basePrice || "32.90", costAmount: "11.50", freeAllowance: "2", allowPaidExtras: true },
        { variationName: "Grande (8 fatias)", measureAmount: "8", measureUnit: "fatias", price: "49.90", costAmount: "16.80", freeAllowance: "2", allowPaidExtras: true }
      ];
    }
    if (segmentCode?.toLowerCase() === "acaiteria" || !segmentCode) {
      return [
        { variationName: "300 ml", measureAmount: "300", measureUnit: "ml", price: "15.90", costAmount: "6.80", freeAllowance: "3", allowPaidExtras: true },
        { variationName: "500 ml", measureAmount: "500", measureUnit: "ml", price: "19.90", costAmount: "9.40", freeAllowance: "4", allowPaidExtras: true },
        { variationName: "700 ml", measureAmount: "700", measureUnit: "ml", price: "26.90", costAmount: "12.50", freeAllowance: "5", allowPaidExtras: true }
      ];
    }
    return [
      { variationName: "Individual", measureAmount: "1", measureUnit: labels.defaultUnit, price: basePrice || "24.90", costAmount: String(estimatedSheetCost ?? 8.5), freeAllowance: "2", allowPaidExtras: true }
    ];
  });

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
        measureAmount: segmentCode?.toLowerCase() === "pizzaria" ? "12" : "1000",
        measureUnit: labels.defaultUnit,
        price: "34.90",
        costAmount: "14.00",
        freeAllowance: "4",
        allowPaidExtras: true
      }
    ]);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim() || !categoryId) return;
    setSaving(true);

    let targetId = product?.id;
    const minPrice = sizes.length > 0 ? Math.min(...sizes.map((s) => Number(s.price) || 1)) : Number(basePrice) || 1;

    if (!targetId) {
      const createRes = await apiFetch<{ id: string }>(
        "/api/v1/catalog/products",
        {
          method: "POST",
          body: JSON.stringify({
            categoryId,
            name: name.trim(),
            basePrice: Math.max(0.01, minPrice),
            description: description.trim() || null,
            isFeatured: false,
            displayOrder: 99,
            isNew,
            isRecommended: false,
            pricingStrategy: segmentCode?.toLowerCase() === "pizzaria" ? 2 : 1,
            allowsNotes: true
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
          basePrice: Math.max(0.01, minPrice),
          isNew,
          isPromotion,
          isFeatured: product?.isFeatured ?? false,
          displayOrder: 0,
          pricingStrategy: product?.pricingStrategy ?? (segmentCode?.toLowerCase() === "pizzaria" ? 2 : 1),
          imageUrl: imageUrl || null,
          attachedGroupIds,
          sizes: sizes.map((s) => ({
            id: s.id || null,
            variationName: s.variationName.trim() || `${s.measureAmount} ${s.measureUnit}`.trim(),
            price: Math.max(0.01, Number(s.price) || minPrice),
            measureAmount: s.measureAmount ? Number(s.measureAmount) : null,
            measureUnit: s.measureUnit || null,
            maxFlavors: segmentCode?.toLowerCase() === "pizzaria" ? Math.max(1, Number(s.freeAllowance) || 2) : 1,
            freeAllowance: Math.max(0, Number(s.freeAllowance) || 0),
            costAmount: s.costAmount ? Number(s.costAmount) : null,
            allowPaidExtras: s.allowPaidExtras
          }))
        })
      },
      token
    );

    setSaving(false);
    if (res.ok) {
      onNotify(`Produto "${name}" salvo com fotos, tamanhos e regras!`);
      await onSaved();
      onClose();
    } else {
      onNotify(res.errorMessage || "Erro ao salvar configuração do produto.");
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/75 p-3 backdrop-blur-sm sm:p-6">
      <form
        onSubmit={handleSubmit}
        className="flex max-h-[92vh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl"
      >
        <div className="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <h2 className="text-base font-extrabold text-slate-900">
            {product ? `Editar ${product.name}` : "Novo Produto Completo"}
          </h2>
          <button type="button" onClick={onClose} className="rounded-lg p-1 text-slate-400 hover:bg-slate-100">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5 text-xs">
          <div className="grid gap-3 sm:grid-cols-2">
            <div>
              <label className="font-bold text-slate-700">Nome do Produto</label>
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                placeholder="Ex: Monte do seu jeito"
                className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 text-sm font-semibold"
              />
            </div>
            <div>
              <label className="font-bold text-slate-700">Categoria</label>
              <select
                value={categoryId}
                onChange={(e) => setCategoryId(e.target.value)}
                className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 text-sm font-semibold"
              >
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="grid gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3 sm:grid-cols-2">
            <label className="flex cursor-pointer items-center gap-2 font-bold text-slate-800">
              <input type="checkbox" checked={isNew} onChange={(e) => setIsNew(e.target.checked)} />
              Marcar como Lançamento & Novidade
            </label>
            <label className="flex cursor-pointer items-center gap-2 font-bold text-slate-800">
              <input type="checkbox" checked={isPromotion} onChange={(e) => setIsPromotion(e.target.checked)} />
              Marcar como Produto em Promoção
            </label>
          </div>

          <div>
            <label className="font-bold text-slate-700">Descrição dos Ingredientes</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
              placeholder="Use sua criatividade e monte do seu jeito!"
              className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2 text-xs"
            />
          </div>

          {/* Foto do Produto por Upload do Computador */}
          <div>
            <label className="font-bold text-slate-700">Foto do Produto (Escolher do Computador)</label>
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
                      <div>
                        <span className="font-bold text-slate-900">{grp.name}</span>
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
                          <span className="font-bold text-slate-900">{grp.name}</span>
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

          {/* Tamanhos, Preços, Inclusos e Custos */}
          <div>
            <p className="font-extrabold text-slate-900">{labels.sizesTitle}</p>
            <p className="mt-0.5 text-[11px] text-slate-500">{labels.sizesHint}</p>

            {sizes.length === 0 && (
              <div className="mt-2">
                <label className="font-bold text-slate-700">Preço Único de Venda (R$)</label>
                <input
                  type="number"
                  step="0.01"
                  value={basePrice}
                  onChange={(e) => setBasePrice(e.target.value)}
                  className="mt-1 w-40 rounded-xl border border-slate-300 px-3 py-2 font-mono font-bold"
                />
              </div>
            )}

            <div className="mt-3 grid gap-3 sm:grid-cols-2">
              {sizes.map((sz, idx) => (
                <div key={idx} className="rounded-2xl border border-slate-200 bg-slate-50 p-3.5 space-y-2.5">
                  <div className="flex items-center justify-between gap-2">
                    <input
                      value={sz.variationName}
                      onChange={(e) => {
                        const copy = [...sizes];
                        copy[idx] = { ...sz, variationName: e.target.value };
                        setSizes(copy);
                      }}
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
                      <label className="text-[10px] font-bold text-slate-600">{labels.allowanceLabel}</label>
                      <input
                        type="number"
                        min="0"
                        value={sz.freeAllowance}
                        onChange={(e) => {
                          const copy = [...sizes];
                          copy[idx] = { ...sz, freeAllowance: e.target.value };
                          setSizes(copy);
                        }}
                        className="mt-0.5 w-full rounded-lg border border-slate-200 bg-white px-2 py-1 font-mono"
                      />
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
                      <label className="text-[10px] font-bold text-slate-600">Custo Insumos (R$)</label>
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

                  <label className="flex cursor-pointer items-center gap-1.5 border-t border-slate-200 pt-2 text-[11px] font-semibold text-slate-700">
                    <input
                      type="checkbox"
                      checked={sz.allowPaidExtras}
                      onChange={(e) => {
                        const copy = [...sizes];
                        copy[idx] = { ...sz, allowPaidExtras: e.target.checked };
                        setSizes(copy);
                      }}
                    />
                    Permitir Adicionais Extras neste tamanho
                  </label>
                </div>
              ))}
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
