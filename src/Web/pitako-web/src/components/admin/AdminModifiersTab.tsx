"use client";

import { Edit3, Pause, Play, Plus, Sliders, Trash2 } from "lucide-react";
import { useState } from "react";
import { apiFetch } from "@/lib/api";

export interface AdminModifierOptionDto {
  id: string;
  name: string;
  description?: string | null;
  additionalPrice: number;
  maxQuantity: number;
  displayOrder: number;
  isAvailable: boolean;
  supplyItemId?: string | null;
  consumptionQuantity: number;
  consumptionUnitCode?: string | null;
  lossPercent: number;
  replacesLabel?: string | null;
  incompatibleOptionIdsCsv?: string | null;
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
  const [gMin, setGMin] = useState("0");
  const [gMax, setGMax] = useState("5");
  const [gFree, setGFree] = useState("3");
  const [gCounting, setGCounting] = useState("2");
  const [gRequired, setGRequired] = useState(false);

  // Opção por grupo
  const [activeGroupForOpt, setActiveGroupForOpt] = useState<string | null>(groups[0]?.id || null);
  const [editingOptId, setEditingOptId] = useState<string | null>(null);
  const [optName, setOptName] = useState("");
  const [optPrice, setOptPrice] = useState("0");
  const [optMaxQty, setOptMaxQty] = useState("2");
  const [optSupplyId, setOptSupplyId] = useState("");
  const [optConsumeQty, setOptConsumeQty] = useState("30");
  const [optConsumeUnit, setOptConsumeUnit] = useState("g");
  const [optLossPct, setOptLossPct] = useState("0");
  const [optReplaces, setOptReplaces] = useState("");
  const [optIncompatibleCsv, setOptIncompatibleCsv] = useState("");

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
      freeAllowance: Number(gFree),
      isRequired: gRequired,
      countingMode: Number(gCounting),
      displayOrder: groups.length + 1,
      isActive: true
    };

    const res = editingGroupId
      ? await apiFetch(`/api/v1/catalog/modifier-groups/${editingGroupId}`, { method: "PUT", body: JSON.stringify(body) }, token)
      : await apiFetch("/api/v1/catalog/modifier-groups", { method: "POST", body: JSON.stringify(body) }, token);

    if (res.ok) {
      setEditingGroupId(null);
      setGName("");
      onNotify(editingGroupId ? "Grupo atualizado!" : "Grupo criado! Agora adicione opções ou vincule aos produtos.");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar grupo.");
  }

  async function deleteGroup(id: string) {
    const res = await apiFetch(`/api/v1/catalog/modifier-groups/${id}`, { method: "DELETE" }, token);
    if (res.ok) {
      onNotify("Grupo excluído!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao excluir grupo.");
  }

  async function saveOption(e: React.FormEvent) {
    e.preventDefault();
    const groupId = activeGroupForOpt || groups[0]?.id;
    if (!groupId) return;

    const input = {
      name: optName,
      additionalPrice: Number(optPrice),
      maxQuantity: Number(optMaxQty) || 1,
      displayOrder: 0,
      supplyItemId: optSupplyId || null,
      consumptionQuantity: optSupplyId ? Number(optConsumeQty) || 0 : 0,
      consumptionUnitCode: optSupplyId ? optConsumeUnit || "g" : null,
      lossPercent: optSupplyId ? Number(optLossPct) || 0 : 0,
      replacesLabel: optReplaces || null,
      incompatibleOptionIdsCsv: optIncompatibleCsv || null
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
      setOptPrice("0");
      setOptReplaces("");
      setOptIncompatibleCsv("");
      onNotify("Ingrediente / opção salvo com integração de estoque!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar opção.");
  }

  async function toggleOptionAvailability(groupId: string, opt: AdminModifierOptionDto) {
    const input = {
      name: opt.name,
      additionalPrice: opt.additionalPrice,
      maxQuantity: opt.maxQuantity,
      displayOrder: opt.displayOrder,
      description: opt.description,
      supplyItemId: opt.supplyItemId,
      consumptionQuantity: opt.consumptionQuantity,
      consumptionUnitCode: opt.consumptionUnitCode,
      lossPercent: opt.lossPercent,
      replacesLabel: opt.replacesLabel,
      incompatibleOptionIdsCsv: opt.incompatibleOptionIdsCsv
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

  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <div className="space-y-4 lg:col-span-2">
        {groups.map((g) => (
          <section key={g.id} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <h3 className="text-base font-extrabold text-slate-900">{g.name}</h3>
                <p className="text-xs text-slate-500">
                  {GROUP_KIND_LABELS[g.selectionKind] || "Personalização"} · Mín {g.minSelections} / Máx {g.maxSelections} · Grátis: {g.freeAllowance}
                  {g.isRequired ? " · Obrigatório" : ""}
                </p>
              </div>
              <div className="flex gap-1.5">
                <button
                  type="button"
                  onClick={() => {
                    setEditingGroupId(g.id);
                    setGName(g.name);
                    setGKind(String(g.selectionKind));
                    setGMin(String(g.minSelections));
                    setGMax(String(g.maxSelections));
                    setGFree(String(g.freeAllowance));
                    setGCounting(String(g.countingMode));
                    setGRequired(g.isRequired);
                  }}
                  className="rounded-lg border px-2.5 py-1 text-xs font-semibold"
                >
                  <Edit3 className="inline h-3.5 w-3.5" /> Editar
                </button>
                <button type="button" onClick={() => deleteGroup(g.id)} className="rounded-lg border border-rose-200 px-2.5 py-1 text-xs text-rose-600">
                  <Trash2 className="inline h-3.5 w-3.5" />
                </button>
              </div>
            </div>

            <div className="mt-3 space-y-1.5">
              {g.options.map((o) => (
                <div key={o.id} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-slate-100 bg-slate-50 px-3 py-2 text-xs">
                  <div>
                    <span className="font-bold text-slate-900">{o.name}</span> ·{" "}
                    <span className="font-mono text-slate-700">{o.additionalPrice > 0 ? `+R$ ${o.additionalPrice.toFixed(2)}` : "Grátis"}</span>
                    {o.supplyItemId && (
                      <span className="ml-2 rounded bg-emerald-50 px-1.5 py-0.5 text-[10px] font-semibold text-emerald-800">
                        Baixa {o.consumptionQuantity} {o.consumptionUnitCode} ({o.lossPercent}% perda)
                      </span>
                    )}
                    {!o.isAvailable && <span className="ml-2 rounded bg-rose-100 px-1.5 py-0.5 text-[10px] font-bold text-rose-700">Pausado</span>}
                  </div>
                  <div className="flex items-center gap-1">
                    <button
                      type="button"
                      onClick={() => {
                        setActiveGroupForOpt(g.id);
                        setEditingOptId(o.id);
                        setOptName(o.name);
                        setOptPrice(String(o.additionalPrice));
                        setOptMaxQty(String(o.maxQuantity));
                        setOptSupplyId(o.supplyItemId || "");
                        setOptConsumeQty(String(o.consumptionQuantity || 0));
                        setOptConsumeUnit(o.consumptionUnitCode || "g");
                        setOptLossPct(String(o.lossPercent || 0));
                        setOptReplaces(o.replacesLabel || "");
                        setOptIncompatibleCsv(o.incompatibleOptionIdsCsv || "");
                      }}
                      className="rounded px-2 py-1 text-slate-700 hover:bg-slate-200"
                    >
                      Editar
                    </button>
                    <button type="button" onClick={() => toggleOptionAvailability(g.id, o)} className="rounded px-2 py-1 hover:bg-slate-200">
                      {o.isAvailable ? <Pause className="inline h-3 w-3" /> : <Play className="inline h-3 w-3" />}
                    </button>
                    <button type="button" onClick={() => deleteOption(g.id, o.id)} className="rounded px-2 py-1 text-rose-600 hover:bg-rose-50">
                      <Trash2 className="inline h-3 w-3" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </section>
        ))}
      </div>

      {/* Formulários Laterais: Grupo & Opção */}
      <div className="space-y-6">
        <form onSubmit={saveGroup} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-xs">
          <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
            <Sliders className="h-4 w-4" /> {editingGroupId ? "Editar Grupo" : "Novo Grupo Universal"}
          </h3>
          <input value={gName} onChange={(e) => setGName(e.target.value)} required placeholder="Ex: Acompanhamentos Grátis / Borda" className="w-full rounded-xl border border-slate-300 px-3 py-2 text-sm" />
          <select value={gKind} onChange={(e) => setGKind(e.target.value)} className="w-full rounded-xl border border-slate-300 px-3 py-2">
            {Object.entries(GROUP_KIND_LABELS).map(([k, lbl]) => (
              <option key={k} value={k}>
                {lbl}
              </option>
            ))}
          </select>
          <div className="grid grid-cols-3 gap-2">
            <label>Mín<input type="number" value={gMin} onChange={(e) => setGMin(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
            <label>Máx<input type="number" value={gMax} onChange={(e) => setGMax(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
            <label>Grátis<input type="number" value={gFree} onChange={(e) => setGFree(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
          </div>
          <select value={gCounting} onChange={(e) => setGCounting(e.target.value)} className="w-full rounded-xl border border-slate-300 px-3 py-2">
            <option value="2">Contagem por porções totais</option>
            <option value="1">Contagem por opções distintas</option>
          </select>
          <label className="flex items-center gap-2 font-semibold">
            <input type="checkbox" checked={gRequired} onChange={(e) => setGRequired(e.target.checked)} /> Seleção Obrigatória
          </label>
          <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2.5 text-sm font-bold text-white">
            {editingGroupId ? "Salvar Grupo" : "Criar Grupo"}
          </button>
        </form>

        <form onSubmit={saveOption} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-xs">
          <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
            <Plus className="h-4 w-4" /> {editingOptId ? "Editar Ingrediente / Opção" : "Nova Opção + Ficha/Estoque"}
          </h3>
          <select value={activeGroupForOpt || groups[0]?.id || ""} onChange={(e) => setActiveGroupForOpt(e.target.value)} required className="w-full rounded-xl border border-slate-300 px-3 py-2">
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
              </option>
            ))}
          </select>
          <input value={optName} onChange={(e) => setOptName(e.target.value)} required placeholder="Nome (ex: Leite Ninho, Bacon Extra)" className="w-full rounded-xl border border-slate-300 px-3 py-2 text-sm" />
          <div className="grid grid-cols-2 gap-2">
            <label>Preço Extra<input type="number" step="0.01" value={optPrice} onChange={(e) => setOptPrice(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
            <label>Qtd Máx<input type="number" value={optMaxQty} onChange={(e) => setOptMaxQty(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
          </div>
          <label className="block font-semibold text-slate-700">
            Vincular Insumo para Baixa Automática de Estoque
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
          <input value={optReplaces} onChange={(e) => setOptReplaces(e.target.value)} placeholder="Substitui item (opcional)" className="w-full rounded-xl border border-slate-300 px-3 py-2" />
          <input value={optIncompatibleCsv} onChange={(e) => setOptIncompatibleCsv(e.target.value)} placeholder="IDs incompatíveis CSV (opcional)" className="w-full rounded-xl border border-slate-300 px-3 py-2" />
          <div className="flex gap-2">
            <button type="submit" style={{ backgroundColor: "var(--store-accent)" }} className="flex-1 rounded-xl py-2.5 text-sm font-bold text-white">
              {editingOptId ? "Salvar Opção" : "Adicionar Opção"}
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
