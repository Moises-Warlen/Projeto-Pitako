"use client";

import { ArrowDown, ArrowUp, Edit3, Pause, Play, Plus, Sliders, Trash2, Upload } from "lucide-react";
import { useRef, useState } from "react";
import { apiFetch, compressImageFileToDataUrl, resolveMediaUrl } from "@/lib/api";

export interface AdminModifierOptionDto {
  id: string;
  name: string;
  description?: string | null;
  additionalPrice: number;
  costPrice?: number;
  maxQuantity: number;
  displayOrder: number;
  isAvailable: boolean;
  supplyItemId?: string | null;
  consumptionQuantity: number;
  consumptionUnitCode?: string | null;
  lossPercent: number;
  replacesLabel?: string | null;
  incompatibleOptionIdsCsv?: string | null;
  imageUrl?: string | null;
  sku?: string | null;
}

export interface AdminModifierGroupDto {
  id: string;
  name: string;
  groupType: number;
  selectionKind: number;
  minSelections: number;
  maxSelections: number;
  freeAllowance: number;
  isRequired: boolean;
  countingMode: number;
  categoryTag?: string | null;
  displayOrder: number;
  isActive: boolean;
  options: AdminModifierOptionDto[];
}

export interface SupplyLookupDto {
  id: string;
  name: string;
  contentUnitCode: string;
}

const GROUP_KIND_LABELS: Record<number, string> = {
  1: "Escolha Obrigatória",
  2: "Acompanhamentos Gratuitos",
  3: "Adicionais Pagos",
  4: "Remoção de Ingredientes",
  5: "Substituição",
  6: "Seleção de Sabores (Pizza/Sorvete)"
};

const CATEGORY_TAG_OPTIONS = [
  { value: "gratis", label: "Grátis / Acompanhamentos" },
  { value: "frutas", label: "Frutas Frescas" },
  { value: "pagos", label: "Adicionais Pagos" },
  { value: "cremes", label: "Cremes & Chocolates" },
  { value: "caldas", label: "Caldas & Coberturas" },
  { value: "complementos", label: "Complementos Secos" },
  { value: "confeitos", label: "Confeitos & Bombons" },
  { value: "fitness", label: "Linha Fitness" },
  { value: "outros", label: "Outros" }
];

export function AdminModifiersTab({
  token,
  groups,
  supplies,
  onNotify,
  onReload
}: {
  token: string;
  groups: AdminModifierGroupDto[];
  supplies: SupplyLookupDto[];
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [editingGroupId, setEditingGroupId] = useState<string | null>(null);
  const [gName, setGName] = useState("");
  const [gKind, setGKind] = useState("2");
  const [gCategoryTag, setGCategoryTag] = useState("frutas");
  const [gMin, setGMin] = useState("0");
  const [gMax, setGMax] = useState("5");
  const [gFree, setGFree] = useState("3");
  const [gCounting, setGCounting] = useState("2");
  const [gRequired, setGRequired] = useState(false);

  // Opção por grupo
  const [activeGroupForOpt, setActiveGroupForOpt] = useState<string | null>(groups[0]?.id || null);
  const [editingOptId, setEditingOptId] = useState<string | null>(null);
  const [optName, setOptName] = useState("");
  const [optDescription, setOptDescription] = useState("");
  const [optCostPrice, setOptCostPrice] = useState("0");
  const [optPrice, setOptPrice] = useState("0");
  const [optMaxQty, setOptMaxQty] = useState("2");
  const [optSupplyId, setOptSupplyId] = useState("");
  const [optConsumeQty, setOptConsumeQty] = useState("30");
  const [optConsumeUnit, setOptConsumeUnit] = useState("g");
  const [optLossPct, setOptLossPct] = useState("0");
  const [optReplaces, setOptReplaces] = useState("");
  const [optIncompatibleCsv, setOptIncompatibleCsv] = useState("");
  const [optImageUrl, setOptImageUrl] = useState("");
  const [optSku, setOptSku] = useState("");
  const [uploadingOptImg, setUploadingOptImg] = useState(false);

  const groupFormRef = useRef<HTMLFormElement | null>(null);
  const groupNameInputRef = useRef<HTMLInputElement | null>(null);
  const optionFormRef = useRef<HTMLFormElement | null>(null);
  const optNameInputRef = useRef<HTMLInputElement | null>(null);

  function focusEditorCard(target: "group" | "option") {
    const form = target === "group" ? groupFormRef.current : optionFormRef.current;
    const input = target === "group" ? groupNameInputRef.current : optNameInputRef.current;
    window.requestAnimationFrame(() => {
      form?.scrollIntoView({ behavior: "smooth", block: "nearest" });
      input?.focus();
    });
  }

  async function handleOptionPhotoUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploadingOptImg(true);
    try {
      const compressed = await compressImageFileToDataUrl(file, 360, 0.75);
      const up = await apiFetch<{ url: string }>(
        "/api/v1/catalog/upload-image",
        { method: "POST", body: JSON.stringify({ base64DataUrl: compressed, fileName: file.name }) },
        token
      );
      if (up.ok && up.data?.url) {
        setOptImageUrl(up.data.url);
        onNotify("Foto do complemento carregada!");
      } else {
        onNotify(up.errorMessage || "Erro ao enviar foto do complemento.");
      }
    } catch {
      onNotify("Não foi possível ler o arquivo de imagem.");
    }
    setUploadingOptImg(false);
  }

  function applySelectionModePreset(mode: "radio" | "checkbox" | "quantity") {
    if (mode === "radio") {
      setGMin("1");
      setGMax("1");
      setGCounting("1");
      setGRequired(true);
    } else if (mode === "checkbox") {
      if (Number(gMax) <= 1) setGMax("5");
      setGCounting("1");
    } else {
      if (Number(gMax) <= 1) setGMax("5");
      setGCounting("2");
    }
  }

  async function saveGroup(e: React.FormEvent) {
    e.preventDefault();
    const kindNum = Number(gKind);
    const typeNum = kindNum === 3 ? 1 : 2;
    const body = {
      name: gName,
      type: typeNum,
      selectionKind: kindNum,
      minSelections: Number(gMin),
      maxSelections: Number(gMax),
      freeAllowance: kindNum === 3 ? 0 : Number(gFree),
      isRequired: gRequired,
      countingMode: Number(gCounting),
      categoryTag: gCategoryTag || null,
      displayOrder: editingGroupId ? (groups.find((g) => g.id === editingGroupId)?.displayOrder ?? groups.length + 1) : groups.length + 1,
      isActive: true
    };

    const res = editingGroupId
      ? await apiFetch(`/api/v1/catalog/modifier-groups/${editingGroupId}`, { method: "PUT", body: JSON.stringify(body) }, token)
      : await apiFetch("/api/v1/catalog/modifier-groups", { method: "POST", body: JSON.stringify(body) }, token);

    if (res.ok) {
      setEditingGroupId(null);
      setGName("");
      onNotify(editingGroupId ? "Categoria de adicionais atualizada!" : "Categoria de adicionais criada! Agora adicione itens.");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar grupo.");
  }

  async function moveGroup(idx: number, dir: -1 | 1) {
    const ordered = [...groups].sort((a, b) => a.displayOrder - b.displayOrder);
    const targetIdx = idx + dir;
    if (targetIdx < 0 || targetIdx >= ordered.length) return;
    const [moved] = ordered.splice(idx, 1);
    ordered.splice(targetIdx, 0, moved);
    const payload: Record<string, number> = {};
    ordered.forEach((g, i) => {
      payload[g.id] = i + 1;
    });
    const res = await apiFetch("/api/v1/catalog/modifier-groups/reorder", { method: "POST", body: JSON.stringify(payload) }, token);
    if (res.ok) {
      onNotify("Ordem das categorias de adicionais atualizada!");
      await onReload();
    }
  }

  async function reorderOption(group: AdminModifierGroupDto, optId: string, dir: -1 | 1) {
    const ordered = [...group.options].sort((a, b) => a.displayOrder - b.displayOrder);
    const idx = ordered.findIndex((o) => o.id === optId);
    const targetIdx = idx + dir;
    if (idx < 0 || targetIdx < 0 || targetIdx >= ordered.length) return;
    const [moved] = ordered.splice(idx, 1);
    ordered.splice(targetIdx, 0, moved);
    const payload: Record<string, number> = {};
    ordered.forEach((o, i) => {
      payload[o.id] = i + 1;
    });
    const res = await apiFetch(`/api/v1/catalog/modifier-groups/${group.id}/options/reorder`, { method: "POST", body: JSON.stringify(payload) }, token);
    if (res.ok) {
      onNotify("Ordem dos adicionais atualizada!");
      await onReload();
    }
  }

  async function moveOptionToGroup(sourceGroupId: string, optionId: string, targetGroupId: string) {
    const res = await apiFetch(
      `/api/v1/catalog/modifier-groups/${sourceGroupId}/options/${optionId}/move`,
      { method: "POST", body: JSON.stringify({ targetGroupId }) },
      token
    );
    if (res.ok) {
      const targetName = groups.find((g) => g.id === targetGroupId)?.name || "outra categoria";
      onNotify(`Adicional movido para "${targetName}"!`);
      await onReload();
    } else {
      onNotify(res.errorMessage || "Erro ao mover adicional.");
    }
  }

  async function deleteGroup(id: string) {
    const res = await apiFetch(`/api/v1/catalog/modifier-groups/${id}`, { method: "DELETE" }, token);
    if (res.ok) {
      onNotify("Categoria excluída!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao excluir grupo.");
  }

  async function saveOption(e: React.FormEvent) {
    e.preventDefault();
    const groupId = activeGroupForOpt || groups[0]?.id;
    if (!groupId) return;

    const currentGroup = groups.find((g) => g.id === groupId);
    const input = {
      name: optName,
      description: optDescription.trim() || null,
      additionalPrice: Number(optPrice) || 0,
      costPrice: Number(optCostPrice) || 0,
      maxQuantity: Number(optMaxQty) || 1,
      displayOrder: editingOptId ? (currentGroup?.options.find((o) => o.id === editingOptId)?.displayOrder ?? 0) : (currentGroup?.options.length ?? 0) + 1,
      supplyItemId: optSupplyId || null,
      consumptionQuantity: optSupplyId ? Number(optConsumeQty) || 0 : 0,
      consumptionUnitCode: optSupplyId ? optConsumeUnit || "g" : null,
      lossPercent: optSupplyId ? Number(optLossPct) || 0 : 0,
      replacesLabel: optReplaces || null,
      incompatibleOptionIdsCsv: optIncompatibleCsv || null,
      imageUrl: optImageUrl.trim() || null,
      sku: optSku.trim() || null
    };

    const res = editingOptId
      ? await apiFetch(
          `/api/v1/catalog/modifier-groups/${groupId}/options/${editingOptId}`,
          { method: "PUT", body: JSON.stringify({ input, isAvailable: true }) },
          token
        )
      : await apiFetch(`/api/v1/catalog/modifier-groups/${groupId}/options`, { method: "POST", body: JSON.stringify(input) }, token);

    if (res.ok) {
      setEditingOptId(null);
      setOptName("");
      setOptDescription("");
      setOptCostPrice("0");
      setOptPrice("0");
      setOptReplaces("");
      setOptIncompatibleCsv("");
      setOptImageUrl("");
      setOptSku("");
      onNotify("Adicional / ingrediente salvo!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar opção.");
  }

  async function toggleOptionAvailability(groupId: string, opt: AdminModifierOptionDto) {
    const input = {
      name: opt.name,
      additionalPrice: opt.additionalPrice,
      costPrice: opt.costPrice ?? 0,
      maxQuantity: opt.maxQuantity,
      displayOrder: opt.displayOrder,
      description: opt.description,
      supplyItemId: opt.supplyItemId,
      consumptionQuantity: opt.consumptionQuantity,
      consumptionUnitCode: opt.consumptionUnitCode,
      lossPercent: opt.lossPercent,
      replacesLabel: opt.replacesLabel,
      incompatibleOptionIdsCsv: opt.incompatibleOptionIdsCsv,
      imageUrl: opt.imageUrl || null,
      sku: opt.sku || null
    };
    await apiFetch(
      `/api/v1/catalog/modifier-groups/${groupId}/options/${opt.id}`,
      { method: "PUT", body: JSON.stringify({ input, isAvailable: !opt.isAvailable }) },
      token
    );
    await onReload();
  }

  async function deleteOption(groupId: string, optId: string) {
    await apiFetch(`/api/v1/catalog/modifier-groups/${groupId}/options/${optId}`, { method: "DELETE" }, token);
    await onReload();
  }

  const orderedGroups = [...groups].sort((a, b) => a.displayOrder - b.displayOrder);

  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <div className="space-y-4 lg:col-span-2">
        {orderedGroups.map((g, gIdx) => (
          <section
            key={g.id}
            className={`rounded-2xl border bg-white p-5 shadow-sm transition-all ${
              editingGroupId === g.id
                ? "border-2 border-amber-500 ring-2 ring-amber-200"
                : "border-slate-200"
            }`}
          >
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <span className="rounded bg-slate-100 px-1.5 py-0.5 font-mono text-[10px] font-bold">#{g.displayOrder}</span>
                  <h3 className="text-base font-extrabold text-slate-900">{g.name}</h3>
                  {editingGroupId === g.id && (
                    <span className="rounded-full bg-amber-500 px-2 py-0.5 text-[10px] font-extrabold uppercase text-white">
                      Editando →
                    </span>
                  )}
                  <span className={`rounded-full px-2 py-0.5 text-[10px] font-extrabold uppercase ${g.isRequired || g.minSelections >= 1 ? "bg-rose-100 text-rose-800" : "bg-slate-200 text-slate-700"}`}>
                    {g.isRequired || g.minSelections >= 1 ? "Obrigatório" : "Opcional"}
                  </span>
                  {g.categoryTag && (
                    <span className="rounded-full bg-purple-100 px-2 py-0.5 text-[10px] font-bold uppercase text-purple-800">{g.categoryTag}</span>
                  )}
                </div>
                <p className="mt-0.5 text-xs text-slate-500">
                  {GROUP_KIND_LABELS[g.selectionKind] || "Personalização"} ·{" "}
                  {g.maxSelections === 1 ? "Escolha Única (Radio)" : g.countingMode === 1 ? "Múltipla Simples (Checkbox)" : "Múltipla c/ Qtd (- 0 +)"} · Mín {g.minSelections} / Máx {g.maxSelections} · Grátis: {g.freeAllowance}
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-1">
                <button type="button" title="Subir categoria" onClick={() => void moveGroup(gIdx, -1)} className="rounded-lg border px-2 py-1 hover:bg-slate-100">
                  <ArrowUp className="h-3.5 w-3.5" />
                </button>
                <button type="button" title="Descer categoria" onClick={() => void moveGroup(gIdx, 1)} className="rounded-lg border px-2 py-1 hover:bg-slate-100">
                  <ArrowDown className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setEditingOptId(null);
                    setEditingGroupId(g.id);
                    setGName(g.name);
                    setGKind(String(g.selectionKind));
                    setGCategoryTag(g.categoryTag || "outros");
                    setGMin(String(g.minSelections));
                    setGMax(String(g.maxSelections));
                    setGFree(String(g.freeAllowance));
                    setGCounting(String(g.countingMode));
                    setGRequired(g.isRequired);
                    focusEditorCard("group");
                  }}
                  className={`rounded-lg border px-2.5 py-1 text-xs font-semibold ${
                    editingGroupId === g.id ? "border-amber-500 bg-amber-500 text-white" : "hover:bg-slate-50"
                  }`}
                >
                  <Edit3 className="inline h-3.5 w-3.5" /> Editar
                </button>
                <button type="button" onClick={() => deleteGroup(g.id)} className="rounded-lg border border-rose-200 px-2.5 py-1 text-xs text-rose-600">
                  <Trash2 className="inline h-3.5 w-3.5" />
                </button>
              </div>
            </div>

            <div className="mt-3 space-y-1.5">
              {[...g.options]
                .sort((a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name))
                .map((o) => {
                  const cost = o.costPrice ?? 0;
                  const profit = o.additionalPrice - cost;
                  const isEditingOpt = editingOptId === o.id;
                  return (
                    <div
                      key={o.id}
                      className={`flex flex-wrap items-center justify-between gap-2 rounded-xl border px-3 py-2 text-xs transition-all ${
                        isEditingOpt
                          ? "border-2 border-amber-500 bg-amber-50/70 ring-2 ring-amber-200"
                          : "border-slate-100 bg-slate-50"
                      }`}
                    >
                      {resolveMediaUrl(o.imageUrl) && (
                        <img
                          src={resolveMediaUrl(o.imageUrl)!}
                          alt={o.name}
                          className="h-9 w-9 shrink-0 rounded-lg border border-slate-200 bg-white object-cover"
                        />
                      )}
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-1.5">
                          <span className="font-bold text-slate-900">{o.name}</span>
                          {o.sku && (
                            <span className="rounded bg-slate-200 px-1.5 py-0.5 font-mono text-[10px] font-bold text-slate-700">
                              {o.sku}
                            </span>
                          )}
                          <span className="rounded bg-white px-1.5 py-0.5 font-mono text-[11px] font-bold text-slate-700 border border-slate-200">
                            {o.additionalPrice > 0 ? `Venda +R$ ${o.additionalPrice.toFixed(2)}` : "Grátis (R$ 0,00)"}
                          </span>
                          {cost > 0 && (
                            <span className="rounded bg-rose-50 px-1.5 py-0.5 font-mono text-[10px] font-semibold text-rose-700">
                              Custo R$ {cost.toFixed(2)}
                            </span>
                          )}
                          {o.additionalPrice > 0 && cost > 0 && (
                            <span className={`rounded px-1.5 py-0.5 font-mono text-[10px] font-bold ${profit >= 0 ? "bg-emerald-50 text-emerald-700" : "bg-rose-100 text-rose-800"}`}>
                              Lucro {profit >= 0 ? `+R$ ${profit.toFixed(2)}` : `-R$ ${Math.abs(profit).toFixed(2)}`}
                            </span>
                          )}
                          {!o.isAvailable && <span className="rounded bg-rose-100 px-1.5 py-0.5 text-[10px] font-bold text-rose-700">Pausado</span>}
                        </div>
                        {o.description && <p className="text-[11px] text-slate-500">{o.description}</p>}
                      </div>
                      <div className="flex flex-wrap items-center gap-1">
                        <button type="button" title="Subir item" onClick={() => void reorderOption(g, o.id, -1)} className="rounded p-1 hover:bg-slate-200">
                          <ArrowUp className="h-3 w-3" />
                        </button>
                        <button type="button" title="Descer item" onClick={() => void reorderOption(g, o.id, 1)} className="rounded p-1 hover:bg-slate-200">
                          <ArrowDown className="h-3 w-3" />
                        </button>
                        {groups.length > 1 && (
                          <select
                            aria-label={`Mover ${o.name} para categoria`}
                            value={g.id}
                            onChange={(e) => void moveOptionToGroup(g.id, o.id, e.target.value)}
                            className="rounded border border-slate-200 bg-white px-1.5 py-1 text-[10px] font-semibold text-slate-700"
                          >
                            {orderedGroups.map((dest) => (
                              <option key={dest.id} value={dest.id}>
                                {dest.id === g.id ? `Cat: ${dest.name}` : `Mover → ${dest.name}`}
                              </option>
                            ))}
                          </select>
                        )}
                        <button
                          type="button"
                          onClick={() => {
                            setEditingGroupId(null);
                            setActiveGroupForOpt(g.id);
                            setEditingOptId(o.id);
                            setOptName(o.name);
                            setOptDescription(o.description || "");
                            setOptCostPrice(String(o.costPrice ?? 0));
                            setOptPrice(String(o.additionalPrice));
                            setOptMaxQty(String(o.maxQuantity));
                            setOptSupplyId(o.supplyItemId || "");
                            setOptConsumeQty(String(o.consumptionQuantity || 0));
                            setOptConsumeUnit(o.consumptionUnitCode || "g");
                            setOptLossPct(String(o.lossPercent || 0));
                            setOptReplaces(o.replacesLabel || "");
                            setOptIncompatibleCsv(o.incompatibleOptionIdsCsv || "");
                            setOptImageUrl(o.imageUrl || "");
                            setOptSku(o.sku || "");
                            focusEditorCard("option");
                          }}
                          className={`rounded px-2 py-1 font-semibold ${
                            isEditingOpt
                              ? "bg-amber-500 text-white"
                              : "text-slate-700 hover:bg-slate-200"
                          }`}
                        >
                          {isEditingOpt ? "Editando →" : "Editar"}
                        </button>
                        <button type="button" onClick={() => toggleOptionAvailability(g.id, o)} className="rounded px-2 py-1 hover:bg-slate-200">
                          {o.isAvailable ? <Pause className="inline h-3 w-3" /> : <Play className="inline h-3 w-3" />}
                        </button>
                        <button type="button" onClick={() => deleteOption(g.id, o.id)} className="rounded px-2 py-1 text-rose-600 hover:bg-rose-50">
                          <Trash2 className="inline h-3 w-3" />
                        </button>
                      </div>
                    </div>
                  );
                })}
            </div>
          </section>
        ))}
      </div>

      {/* Formulários Laterais: Categoria de Adicionais & Item Adicional */}
      <div className="space-y-6 lg:sticky lg:top-4 lg:self-start">
        <form
          ref={groupFormRef}
          onSubmit={saveGroup}
          className={`space-y-3 rounded-2xl border p-5 text-xs transition-all ${
            editingGroupId
              ? "border-2 border-amber-500 bg-amber-50/40 shadow-lg ring-4 ring-amber-200/70"
              : "border-slate-200 bg-white shadow-sm"
          }`}
        >
          <div className="flex items-center justify-between gap-2">
            <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
              <Sliders className="h-4 w-4" /> {editingGroupId ? "Editar Categoria de Adicionais" : "Nova Categoria de Adicionais"}
            </h3>
            {editingGroupId && (
              <span className="rounded-full bg-amber-500 px-2.5 py-0.5 text-[10px] font-extrabold uppercase text-white">
                Editando
              </span>
            )}
          </div>
          <input ref={groupNameInputRef} value={gName} onChange={(e) => setGName(e.target.value)} required placeholder="Ex: Frutas / Acompanhamentos Grátis / Pagos" className="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm" />
          <label className="block font-semibold text-slate-700">
            Tipo de Cobrança
            <select value={gKind} onChange={(e) => setGKind(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2">
              {Object.entries(GROUP_KIND_LABELS).map(([k, lbl]) => (
                <option key={k} value={k}>
                  {lbl}
                </option>
              ))}
            </select>
          </label>
          <label className="block font-semibold text-slate-700">
            Segmentação (ex: Frutas, Cremes, Caldas)
            <select value={gCategoryTag} onChange={(e) => setGCategoryTag(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-300 px-3 py-2">
              {CATEGORY_TAG_OPTIONS.map((tag) => (
                <option key={tag.value} value={tag.value}>
                  {tag.label}
                </option>
              ))}
            </select>
          </label>
          <div className="space-y-1.5">
            <span className="block font-semibold text-slate-700">Modo de Seleção (iFood)</span>
            <div className="grid grid-cols-3 gap-1.5">
              <button
                type="button"
                onClick={() => applySelectionModePreset("radio")}
                className={`rounded-lg border px-2 py-1.5 text-[11px] font-bold ${
                  Number(gMax) === 1 ? "border-indigo-600 bg-indigo-50 text-indigo-900" : "border-slate-200 bg-white text-slate-700"
                }`}
              >
                Única (Radio)
              </button>
              <button
                type="button"
                onClick={() => applySelectionModePreset("checkbox")}
                className={`rounded-lg border px-2 py-1.5 text-[11px] font-bold ${
                  Number(gMax) > 1 && gCounting === "1" ? "border-indigo-600 bg-indigo-50 text-indigo-900" : "border-slate-200 bg-white text-slate-700"
                }`}
              >
                Múltipla (Check)
              </button>
              <button
                type="button"
                onClick={() => applySelectionModePreset("quantity")}
                className={`rounded-lg border px-2 py-1.5 text-[11px] font-bold ${
                  Number(gMax) > 1 && gCounting === "2" ? "border-indigo-600 bg-indigo-50 text-indigo-900" : "border-slate-200 bg-white text-slate-700"
                }`}
              >
                Qtd (- 0 +)
              </button>
            </div>
          </div>
          <div className="grid grid-cols-3 gap-2">
            <label>Mín<input type="number" value={gMin} onChange={(e) => setGMin(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
            <label>Máx<input type="number" value={gMax} onChange={(e) => setGMax(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
            <label>Grátis<input type="number" value={gFree} onChange={(e) => setGFree(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
          </div>
          <select value={gCounting} onChange={(e) => setGCounting(e.target.value)} className="w-full rounded-xl border border-slate-300 px-3 py-2">
            <option value="2">Contagem por porções totais (- 0 +)</option>
            <option value="1">Contagem por opções distintas (Checkbox/Radio)</option>
          </select>
          <label className="flex items-center gap-2 font-semibold">
            <input type="checkbox" checked={gRequired} onChange={(e) => setGRequired(e.target.checked)} /> Seleção Obrigatória
          </label>
          <div className="flex gap-2">
            <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="flex-1 rounded-xl py-2.5 text-sm font-bold text-white">
              {editingGroupId ? "Salvar Categoria" : "Criar Categoria"}
            </button>
            {editingGroupId && (
              <button type="button" onClick={() => setEditingGroupId(null)} className="rounded-xl border px-3 py-2">
                Cancelar
              </button>
            )}
          </div>
        </form>

        <form
          ref={optionFormRef}
          onSubmit={saveOption}
          className={`space-y-3 rounded-2xl border p-5 text-xs transition-all ${
            editingOptId
              ? "border-2 border-amber-500 bg-amber-50/40 shadow-lg ring-4 ring-amber-200/70"
              : "border-slate-200 bg-white shadow-sm"
          }`}
        >
          <div className="flex items-center justify-between gap-2">
            <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
              <Plus className="h-4 w-4" /> {editingOptId ? "Editar Adicional / Fruta" : "Novo Adicional / Fruta"}
            </h3>
            {editingOptId && (
              <span className="rounded-full bg-amber-500 px-2.5 py-0.5 text-[10px] font-extrabold uppercase text-white">
                Editando
              </span>
            )}
          </div>
          <label className="block font-semibold text-slate-700">
            Categoria de Adicionais
            <select value={activeGroupForOpt || groups[0]?.id || ""} onChange={(e) => setActiveGroupForOpt(e.target.value)} required className="mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2">
              {orderedGroups.map((g) => (
                <option key={g.id} value={g.id}>
                  {g.name} ({GROUP_KIND_LABELS[g.selectionKind] || "Grupo"})
                </option>
              ))}
            </select>
          </label>
          <input ref={optNameInputRef} value={optName} onChange={(e) => setOptName(e.target.value)} required placeholder="Nome (ex: Morango Fresco, Nutella, Leite Ninho)" className="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm" />
          <div className="grid grid-cols-3 gap-2">
            <input value={optDescription} onChange={(e) => setOptDescription(e.target.value)} placeholder="Descrição / Porção" className="col-span-2 rounded-xl border border-slate-300 px-3 py-2" />
            <input value={optSku} onChange={(e) => setOptSku(e.target.value)} placeholder="SKU PDV" className="rounded-xl border border-slate-300 px-2.5 py-2 font-mono uppercase" />
          </div>
          <div className="flex flex-wrap items-center gap-2 rounded-xl border border-slate-200 bg-slate-50 p-2">
            {resolveMediaUrl(optImageUrl) && (
              <img src={resolveMediaUrl(optImageUrl)!} alt="Preview opção" className="h-9 w-9 rounded-lg border border-slate-200 object-cover" />
            )}
            <label className="inline-flex cursor-pointer items-center gap-1 rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 font-bold text-slate-700 hover:bg-slate-100">
              <Upload className="h-3 w-3" /> {uploadingOptImg ? "Enviando..." : "Foto (PC)"}
              <input type="file" accept="image/*" onChange={handleOptionPhotoUpload} className="hidden" />
            </label>
            <input value={optImageUrl} onChange={(e) => setOptImageUrl(e.target.value)} placeholder="Ou URL da foto (opcional)" className="min-w-0 flex-1 rounded-lg border border-slate-200 bg-white px-2 py-1 text-[11px]" />
          </div>
          <div className="grid grid-cols-3 gap-2">
            <label>Custo (R$)<input type="number" step="0.01" value={optCostPrice} onChange={(e) => setOptCostPrice(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5 font-mono text-rose-700" /></label>
            <label>Venda (R$)<input type="number" step="0.01" value={optPrice} onChange={(e) => setOptPrice(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5 font-mono font-bold text-emerald-700" /></label>
            <label>Qtd Máx<input type="number" value={optMaxQty} onChange={(e) => setOptMaxQty(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5 font-mono" /></label>
          </div>
          <label className="block font-semibold text-slate-700">
            Vincular Insumo (Opcional)
            <select value={optSupplyId} onChange={(e) => setOptSupplyId(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-300 px-2.5 py-2">
              <option value="">Sem vínculo de estoque</option>
              {supplies.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name} ({s.contentUnitCode})
                </option>
              ))}
            </select>
          </label>
          {optSupplyId && (
            <div className="grid grid-cols-3 gap-2">
              <label>Consumo<input type="number" step="0.01" value={optConsumeQty} onChange={(e) => setOptConsumeQty(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
              <label>Unidade<input value={optConsumeUnit} onChange={(e) => setOptConsumeUnit(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
              <label>Perda %<input type="number" step="0.1" value={optLossPct} onChange={(e) => setOptLossPct(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
            </div>
          )}
          <div className="flex gap-2">
            <button type="submit" style={{ backgroundColor: "var(--store-accent)" }} className="flex-1 rounded-xl py-2.5 text-sm font-bold text-white">
              {editingOptId ? "Salvar Adicional" : "Adicionar à Categoria"}
            </button>
            {editingOptId && (
              <button type="button" onClick={() => setEditingOptId(null)} className="rounded-xl border px-3 py-2">
                Cancelar
              </button>
            )}
          </div>
        </form>
      </div>
    </div>
  );
}
