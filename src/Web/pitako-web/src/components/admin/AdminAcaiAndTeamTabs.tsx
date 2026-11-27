"use client";

import { ArrowDown, ArrowUp, Edit3, FolderPlus, Pause, Play, Plus, Trash2, Users } from "lucide-react";
import { useState } from "react";
import { apiFetch } from "@/lib/api";

export interface AdminAcaiConfigDto {
  cupSizes: Array<{
    id: string;
    name: string;
    volumeMl: number;
    description?: string | null;
    basePrice: { amount: number };
    costPrice?: { amount: number };
    freeToppingsLimit: number;
    minFreeToppings?: number;
    allowFreeToppings?: boolean;
    allowPaidAddons?: boolean;
    maxPaidAddonsPerCup?: number | null;
    displayOrder?: number;
    isActive: boolean;
  }>;
  addonCategories?: Array<{
    id: string;
    name: string;
    kind: number;
    isFreeByDefault: boolean;
    description?: string | null;
    displayOrder: number;
    isActive: boolean;
  }>;
  baseFlavors: Array<{ id: string; name: string; extraPrice: { amount: number }; isActive: boolean }>;
  freeToppings: Array<{
    id: string;
    name: string;
    description?: string | null;
    costPrice?: { amount: number };
    addonCategoryId?: string | null;
    categoryName?: string | null;
    maxPortionsPerCup: number;
    displayOrder?: number;
    isActive: boolean;
  }>;
  paidAddons: Array<{
    id: string;
    name: string;
    description?: string | null;
    additionalPrice: { amount: number };
    costPrice?: { amount: number };
    addonCategoryId?: string | null;
    categoryName?: string | null;
    maxQuantityPerCup: number;
    displayOrder?: number;
    isActive: boolean;
  }>;
}

const ACAI_CAT_KIND_LABELS: Record<number, string> = {
  1: "Acompanhamentos Grátis",
  2: "Adicionais Pagos",
  3: "Frutas",
  4: "Cremes & Coberturas",
  5: "Complementos Secos"
};

export function AdminAcaiTab({
  token,
  acai,
  onNotify,
  onReload
}: {
  token: string;
  acai: AdminAcaiConfigDto;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  // Copos / Tamanhos
  const [editCupId, setEditCupId] = useState<string | null>(null);
  const [cupName, setCupName] = useState("");
  const [cupDesc, setCupDesc] = useState("");
  const [cupMl, setCupMl] = useState("500");
  const [cupPrice, setCupPrice] = useState("22.00");
  const [cupCost, setCupCost] = useState("9.40");
  const [cupAddonMode, setCupAddonMode] = useState<"both" | "free_only" | "paid_only" | "none">("both");
  const [cupFreeLimit, setCupFreeLimit] = useState("5");
  const [cupMaxPaid, setCupMaxPaid] = useState("6");

  // Categorias de adicionais do Açaí
  const [editAddonCatId, setEditAddonCatId] = useState<string | null>(null);
  const [addonCatName, setAddonCatName] = useState("");
  const [addonCatKind, setAddonCatKind] = useState("3");
  const [addonCatIsFree, setAddonCatIsFree] = useState(true);
  const [addonCatDesc, setAddonCatDesc] = useState("");

  // Acompanhamentos Grátis
  const [editFreeId, setEditFreeId] = useState<string | null>(null);
  const [freeName, setFreeName] = useState("");
  const [freeDesc, setFreeDesc] = useState("");
  const [freeCost, setFreeCost] = useState("0.60");
  const [freeCatName, setFreeCatName] = useState("Acompanhamentos Grátis");
  const [freeCatId, setFreeCatId] = useState("");
  const [freeMax, setFreeMax] = useState("2");

  // Adicionais Pagos
  const [editPaidId, setEditPaidId] = useState<string | null>(null);
  const [paidName, setPaidName] = useState("");
  const [paidDesc, setPaidDesc] = useState("");
  const [paidCost, setPaidCost] = useState("1.50");
  const [paidPrice, setPaidPrice] = useState("5.00");
  const [paidCatName, setPaidCatName] = useState("Adicionais Pagos");
  const [paidCatId, setPaidCatId] = useState("");
  const [paidMax, setPaidMax] = useState("3");

  const addonCategories = [...(acai.addonCategories ?? [])].sort((a, b) => a.displayOrder - b.displayOrder);
  const orderedCups = [...acai.cupSizes].sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.volumeMl - b.volumeMl);

  async function saveCup(e: React.FormEvent) {
    e.preventDefault();
    const allowFree = cupAddonMode === "both" || cupAddonMode === "free_only";
    const allowPaid = cupAddonMode === "both" || cupAddonMode === "paid_only";
    const existingCup = editCupId ? acai.cupSizes.find((c) => c.id === editCupId) : null;
    const body = {
      name: cupName,
      description: cupDesc.trim() || null,
      volumeMl: Number(cupMl) || 500,
      basePrice: Number(cupPrice) || 0,
      costPrice: Number(cupCost) || 0,
      freeToppingsLimit: allowFree ? Math.max(0, Number(cupFreeLimit) || 0) : 0,
      minFreeToppings: 0,
      allowFreeToppings: allowFree,
      allowPaidAddons: allowPaid,
      maxPaidAddonsPerCup: allowPaid && cupMaxPaid ? Number(cupMaxPaid) : null,
      displayOrder: existingCup?.displayOrder ?? acai.cupSizes.length + 1
    };
    const res = editCupId
      ? await apiFetch(`/api/v1/acai/cup-sizes/${editCupId}`, { method: "PUT", body: JSON.stringify(body) }, token)
      : await apiFetch("/api/v1/acai/cup-sizes", { method: "POST", body: JSON.stringify(body) }, token);

    if (res.ok) {
      setEditCupId(null);
      setCupName("");
      setCupDesc("");
      onNotify(editCupId ? "Copo atualizado!" : "Copo criado!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar copo.");
  }

  async function reorderCup(idx: number, dir: -1 | 1) {
    const target = idx + dir;
    if (target < 0 || target >= orderedCups.length) return;
    const copy = [...orderedCups];
    const [moved] = copy.splice(idx, 1);
    copy.splice(target, 0, moved);
    const payload: Record<string, number> = {};
    copy.forEach((c, i) => {
      payload[c.id] = i + 1;
    });
    const res = await apiFetch("/api/v1/acai/cup-sizes/reorder", { method: "POST", body: JSON.stringify(payload) }, token);
    if (res.ok) {
      onNotify("Ordem dos copos atualizada!");
      await onReload();
    }
  }

  async function saveAddonCategory(e: React.FormEvent) {
    e.preventDefault();
    const existingCat = editAddonCatId ? addonCategories.find((c) => c.id === editAddonCatId) : null;
    const body = {
      name: addonCatName.trim(),
      kind: Number(addonCatKind) || 1,
      isFreeByDefault: addonCatIsFree,
      description: addonCatDesc.trim() || null,
      displayOrder: existingCat?.displayOrder ?? addonCategories.length + 1
    };
    const res = editAddonCatId
      ? await apiFetch(`/api/v1/acai/addon-categories/${editAddonCatId}`, { method: "PUT", body: JSON.stringify(body) }, token)
      : await apiFetch("/api/v1/acai/addon-categories", { method: "POST", body: JSON.stringify(body) }, token);

    if (res.ok) {
      setEditAddonCatId(null);
      setAddonCatName("");
      setAddonCatDesc("");
      onNotify(editAddonCatId ? "Categoria de adicionais atualizada!" : "Categoria de adicionais criada!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar categoria de adicionais.");
  }

  async function moveAddon(itemId: string, sourceIsFree: boolean, targetIsFree: boolean, categoryId?: string, categoryName?: string, salePriceIfPaid?: number) {
    const res = await apiFetch(
      "/api/v1/acai/addons/move",
      {
        method: "POST",
        body: JSON.stringify({
          itemId,
          sourceIsFree,
          targetIsFree,
          targetAddonCategoryId: categoryId || null,
          targetCategoryName: categoryName || null,
          salePriceIfPaid: salePriceIfPaid ?? null
        })
      },
      token
    );
    if (res.ok) {
      onNotify(sourceIsFree === targetIsFree ? "Categoria do adicional atualizada!" : targetIsFree ? "Movido para Grátis!" : "Movido para Pagos!");
      await onReload();
    } else {
      onNotify(res.errorMessage || "Erro ao mover adicional.");
    }
  }

  async function saveFree(e: React.FormEvent) {
    e.preventDefault();
    const matchedCat = addonCategories.find((c) => c.id === freeCatId);
    const body = {
      name: freeName,
      description: freeDesc.trim() || null,
      costPrice: Number(freeCost) || 0,
      addonCategoryId: freeCatId || null,
      categoryName: matchedCat?.name || freeCatName.trim() || "Acompanhamentos Grátis",
      maxPortionsPerCup: Number(freeMax) || 1
    };
    const res = editFreeId
      ? await apiFetch(`/api/v1/acai/free-toppings/${editFreeId}`, { method: "PUT", body: JSON.stringify(body) }, token)
      : await apiFetch("/api/v1/acai/free-toppings", { method: "POST", body: JSON.stringify(body) }, token);

    if (res.ok) {
      setEditFreeId(null);
      setFreeName("");
      setFreeDesc("");
      onNotify("Acompanhamento gratuito salvo!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro.");
  }

  async function savePaid(e: React.FormEvent) {
    e.preventDefault();
    const matchedCat = addonCategories.find((c) => c.id === paidCatId);
    const body = {
      name: paidName,
      description: paidDesc.trim() || null,
      costPrice: Number(paidCost) || 0,
      additionalPrice: Number(paidPrice) || 0,
      addonCategoryId: paidCatId || null,
      categoryName: matchedCat?.name || paidCatName.trim() || "Adicionais Pagos",
      maxQuantityPerCup: Number(paidMax) || 3
    };
    const res = editPaidId
      ? await apiFetch(`/api/v1/acai/paid-addons/${editPaidId}`, { method: "PUT", body: JSON.stringify(body) }, token)
      : await apiFetch("/api/v1/acai/paid-addons", { method: "POST", body: JSON.stringify(body) }, token);

    if (res.ok) {
      setEditPaidId(null);
      setPaidName("");
      setPaidDesc("");
      onNotify("Adicional pago salvo!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro.");
  }

  return (
    <div className="space-y-6 text-xs">
      {/* Categorias de Adicionais (Frutas, Grátis, Pagos, Cremes) */}
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
              <FolderPlus className="h-4 w-4 text-purple-700" /> Categorias de Adicionais do Açaí ({addonCategories.length})
            </h3>
            <p className="text-[11px] text-slate-500">Crie categorias como Frutas, Acompanhamentos Grátis, Adicionais Pagos, Cremes &amp; Chocolates.</p>
          </div>
          <div className="flex flex-wrap gap-1.5">
            {[
              { name: "Frutas Frescas", kind: "3", isFree: true },
              { name: "Acompanhamentos Grátis", kind: "1", isFree: true },
              { name: "Cremes & Chocolates", kind: "4", isFree: false },
              { name: "Adicionais Pagos", kind: "2", isFree: false }
            ].map((preset) => (
              <button
                key={preset.name}
                type="button"
                onClick={() => {
                  setEditAddonCatId(null);
                  setAddonCatName(preset.name);
                  setAddonCatKind(preset.kind);
                  setAddonCatIsFree(preset.isFree);
                }}
                className="rounded-lg border border-purple-200 bg-purple-50 px-2.5 py-1 text-[11px] font-bold text-purple-800 hover:bg-purple-100"
              >
                + {preset.name}
              </button>
            ))}
          </div>
        </div>

        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
          {addonCategories.map((cat) => (
            <div key={cat.id} className="flex items-start justify-between gap-2 rounded-xl border border-slate-200 bg-slate-50 p-3">
              <div>
                <p className="font-extrabold text-slate-900">{cat.name}</p>
                <p className="text-[10px] font-semibold text-purple-700">
                  {ACAI_CAT_KIND_LABELS[cat.kind] || "Categoria"} · {cat.isFreeByDefault ? "Padrão Grátis" : "Padrão Pago"}
                </p>
                {cat.description && <p className="mt-0.5 text-[10px] text-slate-500">{cat.description}</p>}
              </div>
              <div className="flex gap-1">
                <button
                  type="button"
                  onClick={() => {
                    setEditAddonCatId(cat.id);
                    setAddonCatName(cat.name);
                    setAddonCatKind(String(cat.kind));
                    setAddonCatIsFree(cat.isFreeByDefault);
                    setAddonCatDesc(cat.description || "");
                  }}
                  className="p-1 text-slate-600 hover:bg-slate-200 rounded"
                >
                  <Edit3 className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  onClick={async () => {
                    await apiFetch(`/api/v1/acai/addon-categories/${cat.id}`, { method: "DELETE" }, token);
                    await onReload();
                  }}
                  className="p-1 text-rose-600 hover:bg-rose-50 rounded"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          ))}
        </div>

        <form onSubmit={saveAddonCategory} className="grid gap-2 border-t pt-3 sm:grid-cols-5">
          <input value={addonCatName} onChange={(e) => setAddonCatName(e.target.value)} required placeholder="Nome (ex: Frutas Frescas)" className="rounded-xl border px-3 py-2" />
          <select value={addonCatKind} onChange={(e) => setAddonCatKind(e.target.value)} className="rounded-xl border px-2.5 py-2">
            {Object.entries(ACAI_CAT_KIND_LABELS).map(([k, lbl]) => (
              <option key={k} value={k}>{lbl}</option>
            ))}
          </select>
          <select value={addonCatIsFree ? "free" : "paid"} onChange={(e) => setAddonCatIsFree(e.target.value === "free")} className="rounded-xl border px-2.5 py-2">
            <option value="free">Itens entram como Grátis</option>
            <option value="paid">Itens entram como Pagos</option>
          </select>
          <input value={addonCatDesc} onChange={(e) => setAddonCatDesc(e.target.value)} placeholder="Descrição opcional" className="rounded-xl border px-3 py-2" />
          <div className="flex gap-1.5">
            <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="flex-1 rounded-xl px-3 py-2 font-bold text-white">
              {editAddonCatId ? "Salvar Cat." : "+ Criar Cat."}
            </button>
            {editAddonCatId && (
              <button type="button" onClick={() => setEditAddonCatId(null)} className="rounded-xl border px-2.5 py-2">×</button>
            )}
          </div>
        </form>
      </section>

      <div className="grid gap-6 lg:grid-cols-3">
        {/* Copos / Tamanhos */}
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
          <h3 className="text-sm font-extrabold text-slate-900">Copos / Tamanhos ({acai.cupSizes.length})</h3>
          <div className="space-y-2">
            {orderedCups.map((c, idx) => {
              const costAmt = c.costPrice?.amount ?? 0;
              const profit = c.basePrice.amount - costAmt;
              const allowFree = c.allowFreeToppings ?? c.freeToppingsLimit > 0;
              const allowPaid = c.allowPaidAddons ?? true;
              const modeBadge =
                allowFree && allowPaid ? "Grátis + Pagos" : allowFree ? "Somente Grátis" : allowPaid ? "Somente Pagos" : "Sem adicionais";
              return (
                <div key={c.id} className="rounded-xl border border-slate-100 bg-slate-50 px-3 py-2.5">
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <p className="font-extrabold text-slate-900">{c.name} ({c.volumeMl}ml)</p>
                      {c.description && <p className="text-[11px] text-slate-500">{c.description}</p>}
                      <p className="mt-0.5 text-slate-600">
                        Venda <strong>R$ {c.basePrice.amount.toFixed(2)}</strong>
                        {costAmt > 0 && <> · Custo R$ {costAmt.toFixed(2)} (<span className="text-emerald-700 font-bold">+R$ {profit.toFixed(2)}</span>)</>}
                      </p>
                      <div className="mt-1 flex flex-wrap gap-1">
                        <span className="rounded bg-purple-100 px-1.5 py-0.5 text-[10px] font-bold text-purple-800">{modeBadge}</span>
                        {allowFree && <span className="rounded bg-emerald-100 px-1.5 py-0.5 text-[10px] font-bold text-emerald-800">até {c.freeToppingsLimit} grátis</span>}
                      </div>
                    </div>
                    <div className="flex items-center gap-0.5">
                      <button type="button" onClick={() => void reorderCup(idx, -1)} className="p-1 text-slate-500 hover:bg-slate-200 rounded"><ArrowUp className="h-3 w-3" /></button>
                      <button type="button" onClick={() => void reorderCup(idx, 1)} className="p-1 text-slate-500 hover:bg-slate-200 rounded"><ArrowDown className="h-3 w-3" /></button>
                      <button
                        type="button"
                        onClick={() => {
                          setEditCupId(c.id);
                          setCupName(c.name);
                          setCupDesc(c.description || "");
                          setCupMl(String(c.volumeMl));
                          setCupPrice(String(c.basePrice.amount));
                          setCupCost(String(c.costPrice?.amount ?? 0));
                          const af = c.allowFreeToppings ?? true;
                          const ap = c.allowPaidAddons ?? true;
                          setCupAddonMode(af && ap ? "both" : af ? "free_only" : ap ? "paid_only" : "none");
                          setCupFreeLimit(String(c.freeToppingsLimit));
                          setCupMaxPaid(c.maxPaidAddonsPerCup ? String(c.maxPaidAddonsPerCup) : "");
                        }}
                        className="p-1 text-slate-600"
                      >
                        <Edit3 className="h-3.5 w-3.5" />
                      </button>
                      <button
                        type="button"
                        onClick={async () => {
                          await apiFetch(`/api/v1/acai/cup-sizes/${c.id}/${c.isActive ? "pause" : "reactivate"}`, { method: "POST" }, token);
                          await onReload();
                        }}
                        className="p-1 text-slate-600"
                      >
                        {c.isActive ? <Pause className="h-3.5 w-3.5" /> : <Play className="h-3.5 w-3.5" />}
                      </button>
                      <button
                        type="button"
                        onClick={async () => {
                          await apiFetch(`/api/v1/acai/cup-sizes/${c.id}`, { method: "DELETE" }, token);
                          await onReload();
                        }}
                        className="p-1 text-rose-600"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
          <form onSubmit={saveCup} className="space-y-2 border-t pt-3">
            <input value={cupName} onChange={(e) => setCupName(e.target.value)} required placeholder="Nome (ex: Copo 500ml)" className="w-full rounded-xl border px-3 py-2" />
            <input value={cupDesc} onChange={(e) => setCupDesc(e.target.value)} placeholder="Descrição do copo (ex: 400g açaí puro + acompanhamentos)" className="w-full rounded-xl border px-3 py-2" />
            <div className="grid grid-cols-3 gap-2">
              <label>Volume (ml)<input type="number" value={cupMl} onChange={(e) => setCupMl(e.target.value)} className="mt-0.5 w-full rounded-lg border px-2 py-1.5" /></label>
              <label>Custo (R$)<input type="number" step="0.01" value={cupCost} onChange={(e) => setCupCost(e.target.value)} className="mt-0.5 w-full rounded-lg border px-2 py-1.5 font-mono text-rose-700" /></label>
              <label>Venda (R$)<input type="number" step="0.01" value={cupPrice} onChange={(e) => setCupPrice(e.target.value)} className="mt-0.5 w-full rounded-lg border px-2 py-1.5 font-mono font-bold text-emerald-700" /></label>
            </div>
            <label className="block font-semibold text-slate-700">
              Tipo de Adicionais neste Copo
              <select value={cupAddonMode} onChange={(e) => setCupAddonMode(e.target.value as "both" | "free_only" | "paid_only" | "none")} className="mt-0.5 w-full rounded-xl border px-2.5 py-2 font-bold">
                <option value="both">Gratuitos + Pagos (Os dois)</option>
                <option value="free_only">Somente Gratuitos</option>
                <option value="paid_only">Somente Pagos</option>
                <option value="none">Sem Adicionais (Copo Puro)</option>
              </select>
            </label>
            <div className="grid grid-cols-2 gap-2">
              {(cupAddonMode === "both" || cupAddonMode === "free_only") && (
                <label>Qtd Grátis<input type="number" min="0" value={cupFreeLimit} onChange={(e) => setCupFreeLimit(e.target.value)} className="mt-0.5 w-full rounded-lg border px-2 py-1.5" /></label>
              )}
              {(cupAddonMode === "both" || cupAddonMode === "paid_only") && (
                <label>Máx Pagos (opc.)<input type="number" min="0" value={cupMaxPaid} onChange={(e) => setCupMaxPaid(e.target.value)} placeholder="Ilimitado" className="mt-0.5 w-full rounded-lg border px-2 py-1.5" /></label>
              )}
            </div>
            <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2 font-bold text-white">
              {editCupId ? "Salvar Copo" : "Criar Copo"}
            </button>
          </form>
        </section>

        {/* Acompanhamentos Grátis */}
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
          <h3 className="text-sm font-extrabold text-slate-900">Acompanhamentos Grátis ({acai.freeToppings.length})</h3>
          <div className="space-y-1.5">
            {acai.freeToppings.map((t) => {
              const costAmt = t.costPrice?.amount ?? 0;
              return (
                <div key={t.id} className="rounded-xl bg-slate-50 px-3 py-2">
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <p className="font-bold text-slate-800">
                        {t.name} <span className="text-[10px] font-normal text-slate-500">(máx {t.maxPortionsPerCup})</span>
                      </p>
                      <div className="flex flex-wrap gap-1.5 text-[10px]">
                        {t.categoryName && <span className="rounded bg-purple-100 px-1.5 py-0.5 font-bold text-purple-800">{t.categoryName}</span>}
                        {costAmt > 0 && <span className="font-mono text-rose-700">Custo R$ {costAmt.toFixed(2)}</span>}
                      </div>
                      {t.description && <p className="text-[10px] text-slate-500">{t.description}</p>}
                    </div>
                    <div className="flex items-center gap-1">
                      <button
                        type="button"
                        title="Mover para Adicionais Pagos"
                        onClick={() => void moveAddon(t.id, true, false, undefined, "Adicionais Pagos", 3.5)}
                        className="rounded border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-[10px] font-bold text-amber-800 hover:bg-amber-100"
                      >
                        → Pago
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          setEditFreeId(t.id);
                          setFreeName(t.name);
                          setFreeDesc(t.description || "");
                          setFreeCost(String(t.costPrice?.amount ?? 0));
                          setFreeCatId(t.addonCategoryId || "");
                          setFreeCatName(t.categoryName || "Acompanhamentos Grátis");
                          setFreeMax(String(t.maxPortionsPerCup));
                        }}
                        className="p-1 text-slate-600"
                      >
                        <Edit3 className="h-3.5 w-3.5" />
                      </button>
                      <button
                        type="button"
                        onClick={async () => {
                          await apiFetch(`/api/v1/acai/free-toppings/${t.id}/${t.isActive ? "pause" : "reactivate"}`, { method: "POST" }, token);
                          await onReload();
                        }}
                        className="p-1 text-slate-600"
                      >
                        {t.isActive ? <Pause className="h-3.5 w-3.5" /> : <Play className="h-3.5 w-3.5" />}
                      </button>
                      <button
                        type="button"
                        onClick={async () => {
                          await apiFetch(`/api/v1/acai/free-toppings/${t.id}`, { method: "DELETE" }, token);
                          await onReload();
                        }}
                        className="p-1 text-rose-600"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
          <form onSubmit={saveFree} className="space-y-2 border-t pt-3">
            <input value={freeName} onChange={(e) => setFreeName(e.target.value)} required placeholder="Nome (ex: Banana / Paçoca / Leite em Pó)" className="w-full rounded-xl border px-3 py-2" />
            <input value={freeDesc} onChange={(e) => setFreeDesc(e.target.value)} placeholder="Descrição / Porção (ex: 30g)" className="w-full rounded-xl border px-3 py-2" />
            <div className="grid grid-cols-2 gap-2">
              {addonCategories.length > 0 ? (
                <select value={freeCatId} onChange={(e) => setFreeCatId(e.target.value)} className="rounded-xl border px-2 py-2">
                  <option value="">Sem categoria / Padrão</option>
                  {addonCategories.map((cat) => (
                    <option key={cat.id} value={cat.id}>{cat.name}</option>
                  ))}
                </select>
              ) : (
                <input value={freeCatName} onChange={(e) => setFreeCatName(e.target.value)} placeholder="Categoria (ex: Frutas)" className="rounded-xl border px-2.5 py-2" />
              )}
              <input type="number" step="0.01" value={freeCost} onChange={(e) => setFreeCost(e.target.value)} placeholder="Custo (R$)" className="rounded-xl border px-2.5 py-2 font-mono text-rose-700" />
            </div>
            <input type="number" value={freeMax} onChange={(e) => setFreeMax(e.target.value)} placeholder="Porções máx por copo" className="w-full rounded-xl border px-3 py-2" />
            <button type="submit" className="w-full rounded-xl bg-emerald-600 py-2 font-bold text-white">
              {editFreeId ? "Salvar Item Grátis" : "Adicionar Grátis"}
            </button>
          </form>
        </section>

        {/* Adicionais Pagos */}
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
          <h3 className="text-sm font-extrabold text-slate-900">Adicionais Pagos ({acai.paidAddons.length})</h3>
          <div className="space-y-1.5">
            {acai.paidAddons.map((a) => {
              const costAmt = a.costPrice?.amount ?? 0;
              const profit = a.additionalPrice.amount - costAmt;
              return (
                <div key={a.id} className="rounded-xl bg-slate-50 px-3 py-2">
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <p className="font-bold text-slate-800">
                        {a.name} (+R$ {a.additionalPrice.amount.toFixed(2)})
                      </p>
                      <div className="flex flex-wrap gap-1.5 text-[10px]">
                        {a.categoryName && <span className="rounded bg-purple-100 px-1.5 py-0.5 font-bold text-purple-800">{a.categoryName}</span>}
                        {costAmt > 0 && <span className="font-mono text-rose-700">Custo R$ {costAmt.toFixed(2)} (<strong className="text-emerald-700">+R$ {profit.toFixed(2)}</strong>)</span>}
                      </div>
                      {a.description && <p className="text-[10px] text-slate-500">{a.description}</p>}
                    </div>
                    <div className="flex items-center gap-1">
                      <button
                        type="button"
                        title="Mover para Acompanhamentos Grátis"
                        onClick={() => void moveAddon(a.id, false, true, undefined, "Acompanhamentos Grátis")}
                        className="rounded border border-emerald-300 bg-emerald-50 px-1.5 py-0.5 text-[10px] font-bold text-emerald-800 hover:bg-emerald-100"
                      >
                        → Grátis
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          setEditPaidId(a.id);
                          setPaidName(a.name);
                          setPaidDesc(a.description || "");
                          setPaidCost(String(a.costPrice?.amount ?? 0));
                          setPaidPrice(String(a.additionalPrice.amount));
                          setPaidCatId(a.addonCategoryId || "");
                          setPaidCatName(a.categoryName || "Adicionais Pagos");
                          setPaidMax(String(a.maxQuantityPerCup));
                        }}
                        className="p-1 text-slate-600"
                      >
                        <Edit3 className="h-3.5 w-3.5" />
                      </button>
                      <button
                        type="button"
                        onClick={async () => {
                          await apiFetch(`/api/v1/acai/paid-addons/${a.id}`, { method: "DELETE" }, token);
                          await onReload();
                        }}
                        className="p-1 text-rose-600"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
          <form onSubmit={savePaid} className="space-y-2 border-t pt-3">
            <input value={paidName} onChange={(e) => setPaidName(e.target.value)} required placeholder="Nome (ex: Morango / Nutella / Pistache)" className="w-full rounded-xl border px-3 py-2" />
            <input value={paidDesc} onChange={(e) => setPaidDesc(e.target.value)} placeholder="Descrição / Porção (ex: 45g)" className="w-full rounded-xl border px-3 py-2" />
            <div className="grid grid-cols-2 gap-2">
              {addonCategories.length > 0 ? (
                <select value={paidCatId} onChange={(e) => setPaidCatId(e.target.value)} className="rounded-xl border px-2 py-2">
                  <option value="">Sem categoria / Padrão</option>
                  {addonCategories.map((cat) => (
                    <option key={cat.id} value={cat.id}>{cat.name}</option>
                  ))}
                </select>
              ) : (
                <input value={paidCatName} onChange={(e) => setPaidCatName(e.target.value)} placeholder="Categoria (ex: Frutas)" className="rounded-xl border px-2.5 py-2" />
              )}
              <input type="number" min="1" value={paidMax} onChange={(e) => setPaidMax(e.target.value)} placeholder="Qtd Máx" className="rounded-xl border px-2.5 py-2" />
            </div>
            <div className="grid grid-cols-2 gap-2">
              <label>Custo (R$)<input type="number" step="0.01" value={paidCost} onChange={(e) => setPaidCost(e.target.value)} className="mt-0.5 w-full rounded-lg border px-2 py-1.5 font-mono text-rose-700" /></label>
              <label>Venda (R$)<input type="number" step="0.01" value={paidPrice} onChange={(e) => setPaidPrice(e.target.value)} required className="mt-0.5 w-full rounded-lg border px-2 py-1.5 font-mono font-bold text-emerald-700" /></label>
            </div>
            <button type="submit" className="w-full rounded-xl bg-amber-600 py-2 font-bold text-white">
              {editPaidId ? "Salvar Adicional" : "Adicionar Pago"}
            </button>
          </form>
        </section>
      </div>
    </div>
  );
}

export function AdminTeamTab({
  token,
  team,
  onNotify,
  onReload
}: {
  token: string;
  team: Array<{ id: string; fullName: string; normalizedEmail: string; role: string }>;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [staffName, setStaffName] = useState("");
  const [staffEmail, setStaffEmail] = useState("");
  const [staffPassword, setStaffPassword] = useState("Operador@2026");
  const [staffRole, setStaffRole] = useState("3");

  async function handleCreateStaff(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch(
      "/api/v1/store/users",
      { method: "POST", body: JSON.stringify({ fullName: staffName, email: staffEmail, password: staffPassword, role: Number(staffRole) }) },
      token
    );
    if (res.ok) {
      setStaffName("");
      setStaffEmail("");
      onNotify("Operador cadastrado com sucesso!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao criar usuário.");
  }

  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-2">
        <h3 className="mb-3 flex items-center gap-2 text-base font-extrabold text-slate-900">
          <Users className="h-4 w-4" /> Equipe da Loja ({team.length})
        </h3>
        <div className="space-y-2 text-xs">
          {team.map((u) => (
            <div key={u.id} className="flex justify-between rounded-xl border border-slate-100 bg-slate-50 px-3.5 py-2.5">
              <span>
                <strong>{u.fullName}</strong> ({u.normalizedEmail})
              </span>
              <span className="rounded bg-slate-200 px-2 py-0.5 font-bold">{u.role}</span>
            </div>
          ))}
        </div>
      </section>

      <form onSubmit={handleCreateStaff} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-xs">
        <h4 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
          <Plus className="h-4 w-4" /> Novo Operador
        </h4>
        <input value={staffName} onChange={(e) => setStaffName(e.target.value)} required placeholder="Nome completo" className="w-full rounded-xl border px-3 py-2 text-sm" />
        <input type="email" value={staffEmail} onChange={(e) => setStaffEmail(e.target.value)} required placeholder="E-mail" className="w-full rounded-xl border px-3 py-2" />
        <input value={staffPassword} onChange={(e) => setStaffPassword(e.target.value)} required placeholder="Senha" className="w-full rounded-xl border px-3 py-2" />
        <select value={staffRole} onChange={(e) => setStaffRole(e.target.value)} className="w-full rounded-xl border px-3 py-2">
          <option value="2">Gerente</option>
          <option value="3">Caixa (PDV)</option>
          <option value="5">Garçom</option>
          <option value="6">Cozinha</option>
          <option value="7">Entregador</option>
          <option value="9">Financeiro</option>
        </select>
        <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2.5 text-sm font-bold text-white">
          Cadastrar
        </button>
      </form>
    </div>
  );
}
