"use client";

import { AlertCircle, Check, Clock, Flame, Minus, Plus, Sparkles, X } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
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
      setVariationId(product.variations[0]?.id || "");
      setCounts({});
      setNotes("");
      setQuantity(1);
    }
  }, [product, editingItem]);

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
      if (g.selectionKind === 6 && activeVariation && activeVariation.maxFlavors > 0) {
        maxSel = override ? Math.min(override.maxSelections, activeVariation.maxFlavors) : activeVariation.maxFlavors;
      }

      map.set(g.id, {
        minSelections: override ? override.minSelections : g.minSelections,
        maxSelections: maxSel,
        freeAllowance: override ? override.freeAllowance : g.freeAllowance,
        isRequired: override ? override.isRequired : g.isRequired,
        isHidden: override ? override.isHidden : false
      });
    }
    return map;
  }, [product, attachedGroups, activeVariation]);

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

    // Para escolha única (maxSelections === 1), clicar seleciona exclusivamente esta opção
    if (rule.maxSelections === 1 && option.maxQuantity === 1 && delta > 0) {
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

    const nextVal = Math.max(0, Math.min(option.maxQuantity, current + delta));
    if (nextVal === current) return;

    if (delta > 0) {
      const conflict = findConflictName(option);
      if (conflict) {
        setErrorMsg(`"${option.name}" não pode ser combinado com "${conflict}".`);
        return;
      }

      const m = groupMetrics.get(group.id) ?? { selectedTotal: 0, selectedDistinct: 0 };
      const projectedCount =
        group.countingMode === 1
          ? m.selectedDistinct + (current === 0 ? 1 : 0)
          : m.selectedTotal + delta;

      if (rule.maxSelections > 0 && projectedCount > rule.maxSelections) {
        setErrorMsg(`"${group.name}" permite no máximo ${rule.maxSelections} escolha(s) neste tamanho.`);
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
        <div className="relative flex items-start gap-4 border-b border-slate-100 p-4 sm:p-5">
          <ProductMediaFrame src={product.primaryImageUrl} alt={product.name} className="h-24 w-24 shrink-0 rounded-2xl" iconSize={30} />
          <div className="min-w-0 flex-1 pr-8">
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
            </div>
            <h2 className="mt-1 text-lg font-extrabold text-slate-900 sm:text-xl">{product.name}</h2>
            {product.description && <p className="mt-1 text-xs leading-relaxed text-slate-600 sm:text-sm">{product.description}</p>}
            <p className="mt-1.5 text-sm font-bold text-slate-900">A partir de {formatBrl(product.effectivePrice)}</p>
          </div>
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
          {/* Variações / Tamanhos */}
          {product.variations.length > 0 && (
            <section>
              <div className="mb-2.5 flex items-center justify-between">
                <h3 className="text-sm font-bold text-slate-900">Escolha o Tamanho / Variação</h3>
                <span className="rounded-full bg-slate-900 px-2.5 py-0.5 text-[11px] font-semibold text-white">Obrigatório</span>
              </div>
              <div className="grid gap-2 sm:grid-cols-2">
                {product.variations.map((v) => {
                  const selected = v.id === activeVariation?.id;
                  return (
                    <button
                      key={v.id}
                      type="button"
                      onClick={() => {
                        setVariationId(v.id);
                        setErrorMsg(null);
                      }}
                      style={selected ? { borderColor: "var(--store-primary)", backgroundColor: "color-mix(in srgb, var(--store-primary) 7%, white)" } : undefined}
                      className={`flex items-center justify-between rounded-xl border p-3 text-left transition ${
                        selected ? "ring-1 ring-[var(--store-primary)]" : "border-slate-200 hover:border-slate-300"
                      }`}
                    >
                      <div className="min-w-0 pr-2">
                        <p className="text-sm font-bold text-slate-900">{v.variationName}</p>
                        {v.description && <p className="text-xs text-slate-500">{v.description}</p>}
                      </div>
                      <span className="shrink-0 font-mono text-sm font-extrabold text-slate-900">{formatBrl(v.price)}</span>
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
            const usedCount = group.countingMode === 1 ? m.selectedDistinct : m.selectedTotal;
            const hasFreePool = rule.freeAllowance > 0;
            const freeRemaining = Math.max(0, rule.freeAllowance - m.selectedTotal);
            const isSatisfied =
              (!rule.isRequired && rule.minSelections === 0) ||
              usedCount >= Math.max(rule.minSelections, rule.isRequired ? 1 : 0);

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
                    {hasFreePool && (
                      <span
                        className={`rounded-full px-2.5 py-0.5 text-[11px] font-bold ${
                          freeRemaining > 0 ? "bg-emerald-100 text-emerald-800" : "bg-slate-200 text-slate-700"
                        }`}
                      >
                        {freeRemaining > 0
                          ? `Grátis restantes: ${freeRemaining}/${rule.freeAllowance}`
                          : `Limite grátis atingido (${rule.freeAllowance}/${rule.freeAllowance})`}
                      </span>
                    )}
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
                    const singleSelectMode = rule.maxSelections === 1 && opt.maxQuantity === 1;
                    const unavailable = !opt.isAvailable;

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

                        {singleSelectMode ? (
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
              disabled={submitting}
              onClick={handleConfirm}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="flex flex-1 items-center justify-between rounded-xl px-5 py-3 text-sm font-bold text-white shadow-md transition hover:opacity-95 disabled:opacity-50"
            >
              <span>{editingItem ? "Salvar Alterações" : "Adicionar à Sacola"}</span>
              <span className="font-mono">{formatBrl(estimatedUnitPrice * quantity)}</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
