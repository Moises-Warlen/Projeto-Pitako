"use client";

import { AlertCircle, Check, Clock, Flame, Minus, Plus, Sparkles, X } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { resolveMediaUrl } from "@/lib/api";
import { MenuModifierGroup, MenuModifierOption, MenuProduct, SmartCartItem } from "@/types/menu";
import { formatBrl, ProductMediaFrame } from "./ProductMediaFrame";

export interface ModifierSelectionPayload {
  modifierGroupId: string;
  modifierOptionId: string;
  quantity: number;
}

interface ProductCustomizationModalProps {
  product: MenuProduct | null;
  allModifierGroups: MenuModifierGroup[];
  editingItem?: SmartCartItem | null;
  onClose: () => void;
  onConfirm: (payload: {
    productId: string;
    variationId?: string;
    modifiers: ModifierSelectionPayload[];
    quantity: number;
    notes?: string;
    editingCartItemId?: string;
  }) => Promise<void> | void;
}

export function ProductCustomizationModal({
  product,
  allModifierGroups,
  editingItem,
  onClose,
  onConfirm
}: ProductCustomizationModalProps) {
  const [variationId, setVariationId] = useState<string>("");
  const [counts, setCounts] = useState<Record<string, number>>({});
  const [notes, setNotes] = useState("");
  const [quantity, setQuantity] = useState(1);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const attachedGroups = useMemo(() => {
    if (!product) return [];
    const byId = new Map(allModifierGroups.map((g) => [g.id, g]));
    return product.modifierGroupIds
      .map((id) => byId.get(id))
      .filter((g): g is MenuModifierGroup => Boolean(g));
  }, [product, allModifierGroups]);

  useEffect(() => {
    if (!product) return;
    setErrorMsg(null);
    if (editingItem) {
      setVariationId(editingItem.variationId || product.variations[0]?.id || "");
      setQuantity(editingItem.quantity || 1);
      setNotes(editingItem.customerNotes || "");
      const restored: Record<string, number> = {};
      try {
        const parsed = JSON.parse(editingItem.selectionsPayloadJson || "{}") as {
          modifiers?: Array<{ groupId?: string; optionId?: string; modifierGroupId?: string; modifierOptionId?: string; quantity?: number; qty?: number }>;
        };
        for (const m of parsed.modifiers ?? []) {
          const gId = m.groupId || m.modifierGroupId;
          const oId = m.optionId || m.modifierOptionId;
          const restoredQty = m.quantity ?? m.qty ?? 0;
          if (gId && oId && restoredQty > 0) restored[`${gId}:${oId}`] = restoredQty;
        }
      } catch {
        // Ignora payload malformado e inicia limpo
      }
      setCounts(restored);
    } else {
      const defaultVar = product.variations.find((v) => v.isInStock !== false) ?? product.variations[0];
      setVariationId(defaultVar?.id || "");
      const initialDefaults: Record<string, number> = {};
      for (const g of attachedGroups) {
        if ((g.isRequired || g.minSelections > 0) && g.maxSelections === 1 && g.categoryTag === "cremes") {
          const freeDef = g.options.find((o) => o.isAvailable && o.additionalPrice === 0);
          if (freeDef) initialDefaults[`${g.id}:${freeDef.id}`] = 1;
        }
      }
      setCounts(initialDefaults);
      setNotes("");
      setQuantity(1);
    }
  }, [product, editingItem, attachedGroups]);

  const isMonteDoSeuJeito = Boolean(
    product &&
      (product.customTemplateKey === "acaiteria-monte" ||
        product.name.trim().toLowerCase().includes("monte do seu jeito"))
  );

  const isMonteInclusosGroup = (g: MenuModifierGroup) =>
    isMonteDoSeuJeito &&
    ((g.categoryTag || "").toLowerCase() === "acompanhamentos" ||
      g.name.toLowerCase().includes("acompanhamentos inclusos"));

  const activeVariation = useMemo(
    () => product?.variations.find((v) => v.id === variationId) ?? product?.variations[0] ?? null,
    [product, variationId]
  );

  const ruleForGroup = useMemo(() => {
    const map = new Map<
      string,
      { minSelections: number; maxSelections: number; freeAllowance: number; isRequired: boolean; isHidden: boolean }
    >();
    if (!product) return map;
    for (const g of attachedGroups) {
      const override =
        product.variationRules.find((r) => r.variationId === activeVariation?.id && r.modifierGroupId === g.id) ??
        product.variationRules.find((r) => !r.variationId && r.modifierGroupId === g.id);

      let maxSel = override ? override.maxSelections : g.maxSelections;
      const freeAllow = override ? override.freeAllowance : g.freeAllowance;
      if (g.selectionKind === 6 && activeVariation && activeVariation.maxFlavors > 0) {
        maxSel = override ? Math.min(override.maxSelections, activeVariation.maxFlavors) : activeVariation.maxFlavors;
      } else if (isMonteInclusosGroup(g) && freeAllow > 0) {
        maxSel = freeAllow;
      }

      map.set(g.id, {
        minSelections: override ? override.minSelections : g.minSelections,
        maxSelections: maxSel,
        freeAllowance: freeAllow,
        isRequired: override ? override.isRequired : g.isRequired,
        isHidden: override ? override.isHidden : false
      });
    }
    return map;
  }, [product, attachedGroups, activeVariation, isMonteDoSeuJeito]);

  const groupMetrics = useMemo(() => {
    const metrics = new Map<string, { selectedTotal: number; selectedDistinct: number }>();
    for (const g of attachedGroups) {
      let total = 0;
      let distinct = 0;
      for (const opt of g.options) {
        const q = counts[`${g.id}:${opt.id}`] ?? 0;
        if (q > 0) {
          total += q;
          distinct += 1;
        }
      }
      metrics.set(g.id, { selectedTotal: total, selectedDistinct: distinct });
    }
    return metrics;
  }, [attachedGroups, counts]);

  const estimatedUnitPrice = useMemo(() => {
    if (!product) return 0;
    let base = activeVariation ? activeVariation.price : product.effectivePrice;
    if (activeVariation && product.discountAmount > 0 && product.price > 0) {
      const ratio = product.effectivePrice / product.price;
      base = Number((activeVariation.price * ratio).toFixed(2));
    }

    let extra = 0;
    for (const g of attachedGroups) {
      const rule = ruleForGroup.get(g.id);
      if (!rule || rule.isHidden) continue;

      const selectedOpts = g.options
        .map((o) => ({ o, qty: counts[`${g.id}:${o.id}`] ?? 0 }))
        .filter((x) => x.qty > 0);

      if (g.selectionKind === 6 && product.pricingStrategyCode > 1 && selectedOpts.length > 0) {
        const prices = selectedOpts.flatMap((x) => Array(x.qty).fill(x.o.additionalPrice));
        if (product.pricingStrategyCode === 2) {
          base = Math.max(base, ...prices);
        } else if (product.pricingStrategyCode === 3) {
          const avg = prices.reduce((a, b) => a + b, 0) / prices.length;
          base = Number(Math.max(base, avg).toFixed(2));
        } else {
          const sum = prices.reduce((a, b) => a + b, 0) / prices.length;
          base = Number(sum.toFixed(2));
        }
        continue;
      }

      let remainingFree = rule.freeAllowance;
      for (const { o, qty } of selectedOpts) {
        if (remainingFree > 0) {
          const freeUsed = Math.min(remainingFree, qty);
          remainingFree -= freeUsed;
          const paid = qty - freeUsed;
          if (paid > 0) extra += o.additionalPrice * paid;
        } else {
          extra += o.additionalPrice * qty;
        }
      }
    }

    return Number((base + extra).toFixed(2));
  }, [product, activeVariation, attachedGroups, ruleForGroup, counts]);

  const missingRequiredGroup = useMemo(() => {
    for (const g of attachedGroups) {
      const rule = ruleForGroup.get(g.id);
      if (!rule || rule.isHidden) continue;
      const metric = groupMetrics.get(g.id) ?? { selectedTotal: 0, selectedDistinct: 0 };
      const used = g.countingMode === 1 ? metric.selectedDistinct : metric.selectedTotal;
      if ((rule.isRequired || rule.minSelections > 0) && used < Math.max(rule.minSelections, rule.isRequired ? 1 : 0)) {
        return g;
      }
    }
    return null;
  }, [attachedGroups, ruleForGroup, groupMetrics]);

  if (!product) return null;

  function findConflictName(opt: MenuModifierOption): string | null {
    if (!opt.incompatibleOptionIds || opt.incompatibleOptionIds.length === 0) return null;
    for (const badId of opt.incompatibleOptionIds) {
      for (const g of attachedGroups) {
        const qty = counts[`${g.id}:${badId}`] ?? 0;
        if (qty > 0) {
          const badOpt = g.options.find((x) => x.id === badId);
          if (badOpt) return badOpt.name;
        }
      }
    }
    // Verifica incompatibilidade reversa declarada nas opções já selecionadas
    for (const g of attachedGroups) {
      for (const existingOpt of g.options) {
        if ((counts[`${g.id}:${existingOpt.id}`] ?? 0) > 0 && existingOpt.incompatibleOptionIds?.includes(opt.id)) {
          return existingOpt.name;
        }
      }
    }
    return null;
  }

  function updateOptionCount(group: MenuModifierGroup, option: MenuModifierOption, delta: number) {
    setErrorMsg(null);
    const rule = ruleForGroup.get(group.id);
    if (!rule) return;

    const key = `${group.id}:${option.id}`;
    const current = counts[key] ?? 0;

    const monteInclusosLock = isMonteInclusosGroup(group);
    const effectiveOptionMax = monteInclusosLock ? 1 : option.maxQuantity;

    // Para escolha única (maxSelections === 1), clicar seleciona exclusivamente esta opção
    if (rule.maxSelections === 1 && effectiveOptionMax === 1 && delta > 0) {
      const conflict = findConflictName(option);
      if (conflict) {
        setErrorMsg(`"${option.name}" não pode ser combinado com "${conflict}".`);
        return;
      }
      const next = { ...counts };
      for (const o of group.options) delete next[`${group.id}:${o.id}`];
      next[key] = current === 1 ? 0 : 1;
      setCounts(next);
      return;
    }

    const nextVal = Math.max(0, Math.min(effectiveOptionMax, current + delta));
    if (nextVal === current) return;

    if (delta > 0) {
      const conflict = findConflictName(option);
      if (conflict) {
        setErrorMsg(`"${option.name}" não pode ser combinado com "${conflict}".`);
        return;
      }

      const m = groupMetrics.get(group.id) ?? { selectedTotal: 0, selectedDistinct: 0 };
      const projectedCount =
        group.countingMode === 1 || monteInclusosLock
          ? m.selectedDistinct + (current === 0 ? 1 : 0)
          : m.selectedTotal + delta;

      if (rule.maxSelections > 0 && projectedCount > rule.maxSelections) {
        setErrorMsg(
          monteInclusosLock
            ? `"${group.name}" permite no máximo 3 opções sem custo adicional.`
            : `"${group.name}" permite no máximo ${rule.maxSelections} escolha(s) neste tamanho.`
        );
        return;
      }
    }

    setCounts((prev) => ({ ...prev, [key]: nextVal }));
  }

  async function handleConfirm() {
    if (!product) return;
    if (missingRequiredGroup) {
      setErrorMsg(`Complete a seleção obrigatória em "${missingRequiredGroup.name}".`);
      return;
    }

    const modifiers: ModifierSelectionPayload[] = [];
    for (const g of attachedGroups) {
      const rule = ruleForGroup.get(g.id);
      if (rule?.isHidden) continue;
      for (const o of g.options) {
        const q = counts[`${g.id}:${o.id}`] ?? 0;
        if (q > 0) {
          modifiers.push({ modifierGroupId: g.id, modifierOptionId: o.id, quantity: q });
        }
      }
    }

    setSubmitting(true);
    setErrorMsg(null);
    try {
      await onConfirm({
        productId: product.id,
        variationId: activeVariation?.id,
        modifiers,
        quantity,
        notes: notes.trim() || undefined,
        editingCartItemId: editingItem?.id
      });
    } catch (e) {
      setErrorMsg(e instanceof Error ? e.message : "Não foi possível validar sua seleção.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div role="dialog" aria-modal="true" aria-label={product.name} className="fixed inset-0 z-50 flex items-end justify-center bg-slate-950/60 backdrop-blur-sm sm:items-center sm:p-4">
      <div className="flex max-h-[92vh] w-full max-w-2xl flex-col overflow-hidden rounded-t-3xl bg-white shadow-2xl sm:rounded-3xl">
        {/* Topo com imagem e badges */}
        <div className="relative flex flex-col gap-4 border-b border-slate-100 p-4 sm:flex-row sm:items-start sm:p-5">
          <ProductMediaFrame
            src={product.primaryImageUrl}
            alt={product.name}
            fit="contain"
            className="h-36 w-full shrink-0 rounded-2xl sm:h-32 sm:w-32 md:h-36 md:w-36"
            iconSize={34}
          />
          <div className="min-w-0 flex-1 sm:pr-12">
            <div className="flex flex-wrap items-center gap-1.5">
              {product.isFeatured && (
                <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-bold text-amber-800">
                  <Flame className="h-3 w-3" /> Mais vendido
                </span>
              )}
              {product.isNew && (
                <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 text-[11px] font-bold text-emerald-800">
                  <Sparkles className="h-3 w-3" /> Novidade
                </span>
              )}
              {product.preparationMinutes && (
                <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-600">
                  <Clock className="h-3 w-3" /> ~{product.preparationMinutes} min
                </span>
              )}
              {product.servesPeopleCount && (
                <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-bold text-slate-700">
                  Serve {product.servesPeopleCount} {product.servesPeopleCount === 1 ? "pessoa" : "pessoas"}
                </span>
              )}
              {(product.dietaryTags ?? []).map((dt) => (
                <span key={dt} className="rounded-full bg-lime-100 px-2 py-0.5 text-[11px] font-bold uppercase text-lime-800">
                  {dt}
                </span>
              ))}
            </div>
            <h2 className="mt-1 text-lg font-extrabold text-slate-900 sm:text-xl">{product.name}</h2>
            {product.description && <p className="mt-1 text-xs leading-relaxed text-slate-600 sm:text-sm">{product.description}</p>}
            <p className="mt-1.5 text-sm font-bold text-slate-900">
              {product.discountAmount > 0 && (
                <span className="mr-2 text-xs text-slate-400 line-through">{formatBrl(product.price)}</span>
              )}
              A partir de {formatBrl(product.effectivePrice)}
              {(product.discountPercent ?? 0) > 0 && (
                <span className="ml-2 rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-extrabold text-rose-800">
                  -{product.discountPercent}%
                </span>
              )}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Voltar para o cardápio"
            className="absolute right-14 top-4 hidden rounded-full border border-slate-200 bg-white px-3 py-1.5 text-xs font-bold text-slate-600 hover:bg-slate-50 sm:inline-flex"
          >
            Voltar para o cardápio
          </button>
          <button
            type="button"
            onClick={onClose}
            aria-label="Fechar janela"
            className="absolute right-4 top-4 rounded-full bg-slate-100 p-2 text-slate-600 hover:bg-slate-200"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* Corpo rolável */}
        <div className="flex-1 space-y-6 overflow-y-auto p-4 sm:p-6">
          {/* Informações completas de composição, rendimento e opções (apenas campos preenchidos) */}
          {((product.ingredients && product.ingredients.length > 0) ||
            product.yieldLabel ||
            attachedGroups.length > 0) && (
            <section className="rounded-2xl border border-slate-200 bg-slate-50/70 p-3.5 text-xs space-y-2">
              {product.ingredients && product.ingredients.length > 0 && (
                <div>
                  <span className="font-extrabold text-slate-800">Ingredientes / Composição: </span>
                  <span className="text-slate-600">{product.ingredients.join(", ")}</span>
                </div>
              )}
              {product.yieldLabel && (
                <div>
                  <span className="font-extrabold text-slate-800">Peso / Rendimento: </span>
                  <span className="text-slate-600">{product.yieldLabel}</span>
                </div>
              )}
              {attachedGroups.length > 0 && (
                <div>
                  <span className="font-extrabold text-slate-800">Grupos de Acompanhamentos / Opções: </span>
                  <span className="text-slate-600">{attachedGroups.map((g) => g.name).join(" · ")}</span>
                </div>
              )}
            </section>
          )}

          {/* Variações / Tamanhos */}
          {product.variations.length > 0 && (
            <section>
              <div className="mb-2.5 flex items-center justify-between">
                <h3 className="text-sm font-bold text-slate-900">1. Escolha o Tamanho</h3>
                <span className="rounded-full bg-slate-900 px-2.5 py-0.5 text-[11px] font-semibold text-white">Obrigatório</span>
              </div>
              <div className={product.variations.every((v) => v.variationName.length <= 16) ? "flex flex-wrap gap-2.5" : "grid gap-2 sm:grid-cols-2"}>
                {product.variations.map((v) => {
                  const selected = v.id === activeVariation?.id;
                  const varOutOfStock = v.isInStock === false;
                  const compact = product.variations.every((x) => x.variationName.length <= 16);
                  return (
                    <button
                      key={v.id}
                      type="button"
                      disabled={varOutOfStock}
                      onClick={() => {
                        if (varOutOfStock) return;
                        setVariationId(v.id);
                        setErrorMsg(null);
                      }}
                      style={
                        selected && !varOutOfStock
                          ? compact
                            ? { borderColor: "var(--store-primary)", backgroundColor: "var(--store-primary)", color: "#ffffff" }
                            : { borderColor: "var(--store-primary)", backgroundColor: "color-mix(in srgb, var(--store-primary) 7%, white)" }
                          : undefined
                      }
                      className={
                        compact
                          ? `rounded-2xl border px-4 py-2.5 text-left transition ${
                              varOutOfStock
                                ? "cursor-not-allowed border-slate-200 bg-slate-100 text-slate-400 opacity-60"
                                : selected
                                  ? "shadow-sm"
                                  : "border-slate-200 bg-white text-slate-800 hover:border-slate-300"
                            }`
                          : `flex items-center justify-between rounded-xl border p-3 text-left transition ${
                              varOutOfStock
                                ? "cursor-not-allowed border-slate-200 bg-slate-100 text-slate-400 opacity-60"
                                : selected
                                  ? "ring-1 ring-[var(--store-primary)]"
                                  : "border-slate-200 hover:border-slate-300"
                            }`
                      }
                    >
                      {compact ? (
                        <>
                          <span className="text-sm font-extrabold">
                            {v.variationName} · {varOutOfStock ? "Esgotado" : formatBrl(v.price)}
                          </span>
                          {v.description && (
                            <span className={`block text-[11px] ${selected ? "text-white/85" : "text-slate-500"}`}>{v.description}</span>
                          )}
                        </>
                      ) : (
                        <>
                          <div className="min-w-0 pr-2">
                            <p className="text-sm font-bold text-slate-900">{v.variationName}</p>
                            {v.description && <p className="text-xs text-slate-500">{v.description}</p>}
                          </div>
                          <span className="shrink-0 font-mono text-sm font-extrabold text-slate-900">
                            {varOutOfStock ? "Esgotado" : formatBrl(v.price)}
                          </span>
                        </>
                      )}
                    </button>
                  );
                })}
              </div>
            </section>
          )}

          {/* Grupos Universais de Ingredientes / Sabores / Adicionais */}
          {attachedGroups.map((group) => {
            const rule = ruleForGroup.get(group.id);
            if (!rule || rule.isHidden) return null;
            const m = groupMetrics.get(group.id) ?? { selectedTotal: 0, selectedDistinct: 0 };
            const monteInclusosLock = isMonteInclusosGroup(group);
            const usedCount = group.countingMode === 1 || monteInclusosLock ? m.selectedDistinct : m.selectedTotal;
            const hasFreePool = rule.freeAllowance > 0;
            const freeRemaining = Math.max(0, rule.freeAllowance - usedCount);
            const isSatisfied =
              (!rule.isRequired && rule.minSelections === 0) ||
              usedCount >= Math.max(rule.minSelections, rule.isRequired ? 1 : 0);
            const isFreeChipsLayout = group.selectionKind === 2 || hasFreePool;
            const isFruitsSelectLayout = (group.categoryTag || "").toLowerCase() === "frutas";
            const isPaidChipsGridLayout = group.selectionKind === 3 && !isFruitsSelectLayout && !isFreeChipsLayout;

            if (isFreeChipsLayout) {
              return (
                <section key={group.id} className="rounded-2xl border border-slate-200 bg-slate-50/80 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <h3 className="text-sm font-extrabold text-slate-900">
                        {group.name}: {monteInclusosLock ? usedCount : m.selectedTotal} de {rule.freeAllowance > 0 ? rule.freeAllowance : rule.maxSelections}
                      </h3>
                      <p className="text-xs text-slate-500">
                        {rule.freeAllowance > 0
                          ? `Selecione até ${rule.freeAllowance} opções sem custo adicional.`
                          : rule.maxSelections > 0
                            ? `Máximo ${rule.maxSelections} opções`
                            : "Opcional"}
                      </p>
                    </div>
                    {hasFreePool && (
                      <span
                        className={`rounded-full px-3 py-1 text-[11px] font-extrabold ${
                          freeRemaining > 0 ? "bg-amber-100 text-amber-900" : "bg-emerald-100 text-emerald-800"
                        }`}
                      >
                        {freeRemaining > 0 ? `Escolha mais ${freeRemaining}` : "Limite completo ✓"}
                      </span>
                    )}
                  </div>

                  {hasFreePool && (
                    <div className="mt-2.5 h-2 w-full overflow-hidden rounded-full bg-slate-200">
                      <div
                        style={{
                          width: `${Math.min(100, (m.selectedTotal / Math.max(1, rule.freeAllowance)) * 100)}%`,
                          backgroundColor: freeRemaining === 0 ? "#059669" : "var(--store-accent)"
                        }}
                        className="h-full rounded-full transition-all duration-200"
                      />
                    </div>
                  )}

                  <div className="mt-3.5 flex flex-wrap gap-2">
                    {group.options.map((opt) => {
                      const qty = counts[`${group.id}:${opt.id}`] ?? 0;
                      const unavailable = !opt.isAvailable;
                      const limitReached = rule.maxSelections > 0 && usedCount >= rule.maxSelections && qty === 0;

                      return (
                        <div key={opt.id} className="inline-flex items-center">
                          <button
                            type="button"
                            disabled={unavailable}
                            onClick={() => {
                              if (limitReached) {
                                setErrorMsg(`Limite de ${rule.maxSelections} acompanhamentos para este tamanho. Escolha um tamanho maior para mais acompanhamentos grátis.`);
                                return;
                              }
                              updateOptionCount(group, opt, qty > 0 ? -qty : 1);
                            }}
                            style={
                              qty > 0
                                ? {
                                    borderColor: "var(--store-primary)",
                                    backgroundColor: "var(--store-primary)",
                                    color: "#ffffff"
                                  }
                                : undefined
                            }
                            className={`inline-flex items-center gap-1.5 rounded-xl border px-3 py-2 text-xs font-bold transition ${
                              unavailable
                                ? "cursor-not-allowed border-slate-200 bg-slate-100 text-slate-400 opacity-60"
                                : qty > 0
                                  ? "shadow-sm"
                                  : limitReached
                                    ? "border-slate-200 bg-white/70 text-slate-400 hover:border-slate-300"
                                    : "border-slate-200 bg-white text-slate-700 hover:border-slate-400"
                            }`}
                          >
                            {qty > 0 && <Check className="h-3.5 w-3.5" />}
                            <span>{opt.name}</span>
                            {opt.additionalPrice > 0 && (
                              <span className={qty > 0 ? "text-white/85" : "text-slate-500"}>+{formatBrl(opt.additionalPrice)}</span>
                            )}
                            {!monteInclusosLock && qty > 1 && <span className="rounded-full bg-white/25 px-1.5 text-[10px]">×{qty}</span>}
                          </button>
                          {!monteInclusosLock && qty > 0 && opt.maxQuantity > 1 && qty < opt.maxQuantity && (!rule.maxSelections || usedCount < rule.maxSelections) && (
                            <button
                              type="button"
                              onClick={() => updateOptionCount(group, opt, 1)}
                              title={`Adicionar +1 porção de ${opt.name}`}
                              className="ml-1 rounded-lg border border-slate-200 bg-white px-1.5 py-1.5 text-[10px] font-extrabold text-slate-700 hover:bg-slate-100"
                            >
                              +1
                            </button>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </section>
              );
            }

            if (isFruitsSelectLayout) {
              const selectedFruits = group.options.filter((opt) => (counts[`${group.id}:${opt.id}`] ?? 0) > 0);
              return (
                <section key={group.id} className="rounded-2xl border border-emerald-200/80 bg-emerald-50/40 p-4">
                  <div className="mb-2.5 flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <h3 className="text-sm font-extrabold text-slate-900">{group.name} (opcional)</h3>
                      <p className="text-xs text-slate-500">Frutas frescas selecionadas fatiadas na hora</p>
                    </div>
                  </div>

                  <select
                    value=""
                    aria-label={group.name}
                    onChange={(e) => {
                      const opt = group.options.find((o) => o.id === e.target.value);
                      if (opt) updateOptionCount(group, opt, 1);
                    }}
                    className="w-full rounded-xl border border-emerald-300 bg-white px-3.5 py-2.5 text-sm font-semibold text-slate-800 shadow-xs focus:border-emerald-500 focus:outline-none"
                  >
                    <option value="">+ Selecionar fruta para o copo...</option>
                    {group.options.map((opt) => (
                      <option key={opt.id} value={opt.id} disabled={!opt.isAvailable}>
                        {opt.name} (+ {formatBrl(opt.additionalPrice)}) {!opt.isAvailable ? "(Indisponível)" : ""}
                      </option>
                    ))}
                  </select>

                  {selectedFruits.length === 0 ? (
                    <p className="mt-2 text-xs text-slate-500">
                      Adicione frutas frescas fatiadas ao seu copo selecionando no menu acima.
                    </p>
                  ) : (
                    <div className="mt-3 flex flex-wrap gap-2">
                      {selectedFruits.map((opt) => {
                        const qty = counts[`${group.id}:${opt.id}`] ?? 0;
                        return (
                          <div
                            key={opt.id}
                            className="inline-flex items-center gap-2 rounded-xl border border-emerald-300 bg-white px-3 py-1.5 text-xs font-bold text-slate-800 shadow-xs"
                          >
                            <span>
                              {opt.name} (+{formatBrl(opt.additionalPrice * qty)})
                            </span>
                            <button
                              type="button"
                              onClick={() => updateOptionCount(group, opt, -1)}
                              className="rounded bg-slate-100 px-1.5 py-0.5 text-slate-700 hover:bg-slate-200"
                            >
                              −
                            </button>
                            <span>{qty}</span>
                            <button
                              type="button"
                              disabled={qty >= opt.maxQuantity}
                              onClick={() => updateOptionCount(group, opt, 1)}
                              className="rounded bg-emerald-600 px-1.5 py-0.5 text-white disabled:opacity-40"
                            >
                              +
                            </button>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </section>
              );
            }

            if (isPaidChipsGridLayout) {
              return (
                <section key={group.id} className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                  <div className="mb-3 flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <h3 className="text-sm font-extrabold text-slate-900">{group.name} (opcional)</h3>
                      <p className="text-xs text-slate-500">
                        {rule.maxSelections > 0 ? `Selecione até ${rule.maxSelections} extras` : "Toque para adicionar ao seu copo"}
                      </p>
                    </div>
                  </div>
                  <div className="grid gap-2 sm:grid-cols-2">
                    {group.options.map((opt) => {
                      const qty = counts[`${group.id}:${opt.id}`] ?? 0;
                      const unavailable = !opt.isAvailable;
                      return (
                        <div
                          key={opt.id}
                          style={
                            qty > 0
                              ? { borderColor: "var(--store-primary)", backgroundColor: "color-mix(in srgb, var(--store-primary) 7%, white)" }
                              : undefined
                          }
                          className={`flex items-center justify-between gap-2 rounded-xl border bg-white px-3 py-2.5 transition ${
                            unavailable ? "opacity-50" : qty > 0 ? "shadow-sm" : "border-slate-200 hover:border-slate-300"
                          }`}
                        >
                          <button
                            type="button"
                            disabled={unavailable}
                            onClick={() => updateOptionCount(group, opt, qty === 0 ? 1 : -1)}
                            className="min-w-0 flex-1 text-left"
                          >
                            <p className="truncate text-xs font-bold text-slate-900">{opt.name}</p>
                            <p className="text-xs font-extrabold text-amber-700">+ {formatBrl(opt.additionalPrice)}</p>
                          </button>

                          {qty > 0 ? (
                            <div className="flex items-center gap-1">
                              <button
                                type="button"
                                onClick={() => updateOptionCount(group, opt, -1)}
                                className="flex h-6 w-6 items-center justify-center rounded-md border border-slate-200 bg-slate-50 text-xs font-bold"
                              >
                                −
                              </button>
                              <span className="w-5 text-center text-xs font-extrabold text-slate-900">{qty}</span>
                              <button
                                type="button"
                                disabled={qty >= opt.maxQuantity}
                                onClick={() => updateOptionCount(group, opt, 1)}
                                style={{ backgroundColor: "var(--store-primary)" }}
                                className="flex h-6 w-6 items-center justify-center rounded-md text-xs font-bold text-white disabled:opacity-40"
                              >
                                +
                              </button>
                            </div>
                          ) : (
                            <button
                              type="button"
                              disabled={unavailable}
                              onClick={() => updateOptionCount(group, opt, 1)}
                              className="rounded-lg border border-slate-200 bg-slate-50 px-2 py-1 text-[11px] font-bold text-slate-700 hover:bg-slate-100"
                            >
                              +
                            </button>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </section>
              );
            }

            return (
              <section key={group.id} className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <h3 className="text-sm font-bold text-slate-900">{group.name}</h3>
                    <p className="text-xs text-slate-500">
                      {rule.minSelections > 0 ? `Mínimo ${rule.minSelections}` : "Opcional"}
                      {rule.maxSelections > 0 ? ` · Máximo ${rule.maxSelections}` : ""}
                      {group.selectionKind === 6 && product.pricingStrategyCode === 2 ? " · Preço pelo maior sabor" : ""}
                    </p>
                  </div>
                  <div className="flex items-center gap-1.5">
                    {(rule.isRequired || rule.minSelections > 0) && (
                      <span
                        className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[11px] font-bold ${
                          isSatisfied ? "bg-emerald-600 text-white" : "bg-amber-100 text-amber-900"
                        }`}
                      >
                        {isSatisfied ? <Check className="h-3 w-3" /> : null}
                        {isSatisfied ? "Completo" : "Obrigatório"}
                      </span>
                    )}
                  </div>
                </div>

                {hasFreePool && (
                  <div className="mt-2.5 h-1.5 w-full overflow-hidden rounded-full bg-slate-200">
                    <div
                      style={{
                        width: `${Math.min(100, (m.selectedTotal / Math.max(1, rule.freeAllowance)) * 100)}%`,
                        backgroundColor: "var(--store-accent)"
                      }}
                      className="h-full transition-all duration-200"
                    />
                  </div>
                )}

                <div className="mt-3 space-y-2">
                  {group.options.map((opt) => {
                    const qty = counts[`${group.id}:${opt.id}`] ?? 0;
                    const radioMode = rule.maxSelections === 1 && opt.maxQuantity === 1;
                    const checkboxMode = !radioMode && group.countingMode === 1 && opt.maxQuantity === 1;
                    const unavailable = !opt.isAvailable;
                    const optImg = resolveMediaUrl(opt.imageUrl);

                    return (
                      <div
                        key={opt.id}
                        style={
                          qty > 0
                            ? {
                                borderColor: "var(--store-primary)",
                                backgroundColor: "color-mix(in srgb, var(--store-primary) 5%, white)"
                              }
                            : undefined
                        }
                        className={`flex items-center justify-between gap-3 rounded-xl border bg-white p-3 transition ${
                          unavailable ? "opacity-50" : qty > 0 ? "shadow-sm" : "border-slate-200"
                        }`}
                      >
                        {optImg && (
                          <img src={optImg} alt={opt.name} className="h-11 w-11 shrink-0 rounded-lg border border-slate-200 object-cover" />
                        )}
                        <div className="min-w-0 flex-1">
                          <div className="flex flex-wrap items-center gap-1.5">
                            <p className="text-sm font-semibold text-slate-900">{opt.name}</p>
                            {opt.replacesLabel && (
                              <span className="rounded bg-blue-50 px-1.5 py-0.5 text-[10px] font-semibold text-blue-700">
                                Substitui {opt.replacesLabel}
                              </span>
                            )}
                            {unavailable && <span className="rounded bg-rose-100 px-1.5 py-0.5 text-[10px] font-bold text-rose-700">Indisponível</span>}
                          </div>
                          {opt.description && <p className="text-xs text-slate-500">{opt.description}</p>}
                          <p className="mt-0.5 text-xs font-bold text-slate-700">
                            {opt.additionalPrice === 0
                              ? "Incluso (Grátis)"
                              : group.selectionKind === 6 && product.pricingStrategyCode > 1
                              ? `Valor sabor: ${formatBrl(opt.additionalPrice)}`
                              : `+ ${formatBrl(opt.additionalPrice)}`}
                          </p>
                        </div>

                        {radioMode ? (
                          <button
                            type="button"
                            disabled={unavailable}
                            onClick={() => updateOptionCount(group, opt, 1)}
                            style={qty > 0 ? { backgroundColor: "var(--store-primary)", borderColor: "var(--store-primary)" } : undefined}
                            className={`flex h-7 w-7 items-center justify-center rounded-full border transition ${
                              qty > 0 ? "text-white" : "border-slate-300 bg-white text-transparent"
                            }`}
                          >
                            <Check className="h-4 w-4" />
                          </button>
                        ) : checkboxMode ? (
                          <button
                            type="button"
                            disabled={unavailable}
                            onClick={() => updateOptionCount(group, opt, qty > 0 ? -1 : 1)}
                            style={qty > 0 ? { backgroundColor: "var(--store-primary)", borderColor: "var(--store-primary)" } : undefined}
                            className={`flex h-6 w-6 items-center justify-center rounded-md border transition ${
                              qty > 0 ? "text-white" : "border-slate-300 bg-white text-transparent"
                            }`}
                          >
                            <Check className="h-3.5 w-3.5" />
                          </button>
                        ) : (
                          <div className="flex items-center gap-1.5">
                            <button
                              type="button"
                              disabled={unavailable || qty === 0}
                              onClick={() => updateOptionCount(group, opt, -1)}
                              className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 bg-slate-50 text-slate-700 disabled:opacity-30"
                            >
                              <Minus className="h-3.5 w-3.5" />
                            </button>
                            <span className="w-6 text-center text-sm font-bold text-slate-900">{qty}</span>
                            <button
                              type="button"
                              disabled={unavailable || qty >= opt.maxQuantity}
                              onClick={() => updateOptionCount(group, opt, 1)}
                              style={{ backgroundColor: "var(--store-primary)" }}
                              className="flex h-8 w-8 items-center justify-center rounded-lg text-white disabled:opacity-40"
                            >
                              <Plus className="h-3.5 w-3.5" />
                            </button>
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              </section>
            );
          })}

          {/* Observações do cliente */}
          {product.allowsNotes && (
            <section>
              <label className="mb-1.5 block text-xs font-bold uppercase tracking-wide text-slate-600">
                Observações de Preparo (Opcional)
              </label>
              <textarea
                rows={2}
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                maxLength={280}
                placeholder="Ex: Sem cebola, caprichar no leite em pó, ponto bem passado..."
                className="w-full rounded-xl border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none"
              />
            </section>
          )}
        </div>

        {/* Rodapé fixo com erro inline, stepper e CTA */}
        <div className="border-t border-slate-200 bg-white p-4 sm:p-5">
          {errorMsg && (
            <div className="mb-3 flex items-center gap-2 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-800">
              <AlertCircle className="h-4 w-4 shrink-0" />
              <span>{errorMsg}</span>
            </div>
          )}

          <div className="flex items-center gap-3">
            <div className="flex items-center rounded-xl border border-slate-300 bg-slate-50 p-1">
              <button
                type="button"
                onClick={() => setQuantity((q) => Math.max(1, q - 1))}
                className="rounded-lg p-2 text-slate-700 hover:bg-white"
              >
                <Minus className="h-4 w-4" />
              </button>
              <span className="w-8 text-center text-sm font-extrabold text-slate-900">{quantity}</span>
              <button
                type="button"
                onClick={() => setQuantity((q) => Math.min(20, q + 1))}
                className="rounded-lg p-2 text-slate-700 hover:bg-white"
              >
                <Plus className="h-4 w-4" />
              </button>
            </div>

            <button
              type="button"
              disabled={submitting || Boolean(missingRequiredGroup) || activeVariation?.isInStock === false || product.isInStock === false}
              onClick={handleConfirm}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="flex flex-1 items-center justify-center gap-2 rounded-xl px-5 py-3 text-sm font-extrabold text-white shadow-md transition hover:opacity-95 disabled:cursor-not-allowed disabled:opacity-50"
            >
              <span>
                {missingRequiredGroup
                  ? `Falta escolher: ${missingRequiredGroup.name}`
                  : editingItem
                    ? "Salvar Alterações"
                    : "Adicionar"}
              </span>
              {!missingRequiredGroup && (
                <>
                  <span>·</span>
                  <span className="font-mono">{formatBrl(estimatedUnitPrice * quantity)}</span>
                </>
              )}
            </button>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="mt-2.5 w-full rounded-xl py-1.5 text-center text-xs font-bold text-slate-500 hover:bg-slate-50 hover:text-slate-700 sm:hidden"
          >
            Voltar para o cardápio
          </button>
        </div>
      </div>
    </div>
  );
}
