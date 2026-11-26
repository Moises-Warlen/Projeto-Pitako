"use client";

import { Calculator, Edit3, Package, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { apiFetch } from "@/lib/api";

export interface AdminSupplyDto {
  id: string;
  name: string;
  sku?: string | null;
  supplierName?: string | null;
  purchasePrice: number;
  purchaseQuantity: number;
  purchaseUnitCode: string;
  contentQuantityPerPackage: number;
  contentUnitCode: string;
  costPerContentUnit: number;
  defaultLossPercent: number;
  isActive: boolean;
  quantityOnHand: number;
  controlUnitCode?: string | null;
  minimumStockQuantity: number;
}

export interface AdminTechnicalSheetDto {
  id: string;
  productId?: string | null;
  name: string;
  yieldQuantity: number;
  yieldUnitCode: string;
  notes?: string | null;
  ingredientsTotalCost: number;
  packagingsTotalCost: number;
  totalCost: number;
  costPerPortion: number;
  ingredients: Array<{
    id: string;
    supplyItemId: string;
    supplyItemNameSnapshot: string;
    quantity: number;
    lossPercent: number;
    grossQuantity: number;
    unitCode: string;
    calculatedCost: number;
  }>;
}

export function AdminInventoryTab({
  token,
  supplies,
  sheets,
  products,
  onNotify,
  onReload
}: {
  token: string;
  supplies: AdminSupplyDto[];
  sheets: AdminTechnicalSheetDto[];
  products: Array<{ id: string; name: string }>;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  // Insumo CRUD
  const [editingSupId, setEditingSupId] = useState<string | null>(null);
  const [supName, setSupName] = useState("");
  const [supPrice, setSupPrice] = useState("45.00");
  const [supPurchQty, setSupPurchQty] = useState("1");
  const [supPurchUnit, setSupPurchUnit] = useState("kg");
  const [supContentQty, setSupContentQty] = useState("1000");
  const [supContentUnit, setSupContentUnit] = useState("g");
  const [supLossPct, setSupLossPct] = useState("5");
  const [supMinStock, setSupMinStock] = useState("500");

  // Entrada rápida de estoque (compra)
  const [buySupplyId, setBuySupplyId] = useState("");
  const [buyQty, setBuyQty] = useState("5");
  const [buyUnit, setBuyUnit] = useState("kg");
  const [buyPrice, setBuyPrice] = useState("45.00");

  // Ficha Técnica CRUD
  const [editingSheetId, setEditingSheetId] = useState<string | null>(null);
  const [sheetName, setSheetName] = useState("");
  const [sheetProdId, setSheetProdId] = useState("");
  const [sheetYield, setSheetYield] = useState("1");
  const [sheetYieldUnit, setSheetYieldUnit] = useState("un");
  const [sheetIngSupplyId, setSheetIngSupplyId] = useState("");
  const [sheetIngQty, setSheetIngQty] = useState("150");
  const [sheetIngUnit, setSheetIngUnit] = useState("g");
  const [sheetIngLoss, setSheetIngLoss] = useState("5");
  const [sheetIngredients, setSheetIngredients] = useState<
    Array<{ supplyItemId: string; quantity: number; unitCode: string; lossPercent: number }>
  >([]);

  const [simResult, setSimResult] = useState<{ suggestedPrice: number; grossProfit: number } | null>(null);

  async function saveSupply(e: React.FormEvent) {
    e.preventDefault();
    const payload = {
      name: supName,
      purchasePrice: Number(supPrice),
      purchaseQuantity: Number(supPurchQty),
      purchaseUnitCode: supPurchUnit,
      contentQuantityPerPackage: Number(supContentQty),
      contentUnitCode: supContentUnit,
      defaultLossPercent: Number(supLossPct),
      isActive: true,
      controlUnitCode: supContentUnit,
      minimumStockQuantity: Number(supMinStock),
      allowNegativeStock: false
    };
    const res = editingSupId
      ? await apiFetch(`/api/v1/inventory/supplies/${editingSupId}`, { method: "PUT", body: JSON.stringify(payload) }, token)
      : await apiFetch("/api/v1/inventory/supplies", { method: "POST", body: JSON.stringify(payload) }, token);

    if (res.ok) {
      setEditingSupId(null);
      setSupName("");
      onNotify("Insumo salvo e fichas técnicas recalculadas!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar insumo.");
  }

  async function deleteSupply(id: string) {
    const res = await apiFetch(`/api/v1/inventory/supplies/${id}`, { method: "DELETE" }, token);
    if (res.ok) {
      onNotify("Insumo excluído!");
      await onReload();
    } else onNotify(res.errorMessage || "Insumo vinculado não pode ser excluído.");
  }

  async function receivePurchase(e: React.FormEvent) {
    e.preventDefault();
    const sid = buySupplyId || supplies[0]?.id;
    if (!sid) return;
    const res = await apiFetch(
      "/api/v1/inventory/purchases",
      {
        method: "POST",
        body: JSON.stringify({
          supplierName: "Fornecedor Padrão",
          invoiceNumber: `NF-${Date.now().toString().slice(-5)}`,
          lines: [{ supplyItemId: sid, purchasedQuantity: Number(buyQty), purchaseUnitCode: buyUnit, unitPriceAmount: Number(buyPrice) }],
          updateSupplyPrice: true
        })
      },
      token
    );
    if (res.ok) {
      onNotify("Entrada de estoque registrada!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao lançar compra.");
  }

  function addIngredientRow() {
    const sid = sheetIngSupplyId || supplies[0]?.id;
    if (!sid) return;
    setSheetIngredients((prev) => [
      ...prev,
      { supplyItemId: sid, quantity: Number(sheetIngQty) || 1, unitCode: sheetIngUnit || "g", lossPercent: Number(sheetIngLoss) || 0 }
    ]);
  }

  async function saveTechnicalSheet(e: React.FormEvent) {
    e.preventDefault();
    if (sheetIngredients.length === 0) {
      onNotify("Adicione ao menos 1 ingrediente à ficha técnica.");
      return;
    }
    const payload = {
      name: sheetName,
      yieldQuantity: Number(sheetYield) || 1,
      yieldUnitCode: sheetYieldUnit || "un",
      productId: sheetProdId || null,
      ingredients: sheetIngredients,
      packagings: []
    };

    const res = editingSheetId
      ? await apiFetch(`/api/v1/inventory/technical-sheets/${editingSheetId}`, { method: "PUT", body: JSON.stringify(payload) }, token)
      : await apiFetch("/api/v1/inventory/technical-sheets", { method: "POST", body: JSON.stringify(payload) }, token);

    if (res.ok) {
      setEditingSheetId(null);
      setSheetName("");
      setSheetIngredients([]);
      onNotify("Ficha técnica salva com cálculo real de custos e perdas!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar ficha técnica.");
  }

  async function deleteSheet(id: string) {
    const res = await apiFetch(`/api/v1/inventory/technical-sheets/${id}`, { method: "DELETE" }, token);
    if (res.ok) {
      onNotify("Ficha técnica excluída!");
      await onReload();
    }
  }

  async function simulatePrice(sheetId: string) {
    const res = await apiFetch<{ suggestedSellingPrice: number; estimatedNetProfitAmount: number }>(
      `/api/v1/inventory/technical-sheets/${sheetId}/simulate-price`,
      {
        method: "POST",
        body: JSON.stringify({
          taxPercent: 6,
          channelFeePercent: 8,
          operationalOverheadPercent: 12,
          desiredNetMarginPercent: 25
        })
      },
      token
    );
    if (res.ok && res.data) {
      setSimResult({ suggestedPrice: res.data.suggestedSellingPrice, grossProfit: res.data.estimatedNetProfitAmount });
    }
  }

  return (
    <div className="space-y-6">
      {/* Insumos & Estoque */}
      <div className="grid gap-6 lg:grid-cols-3">
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-2">
          <h3 className="mb-3 flex items-center gap-2 text-base font-extrabold text-slate-900">
            <Package className="h-4 w-4" /> Insumos e Saldo em Estoque ({supplies.length})
          </h3>
          <div className="space-y-2 text-xs">
            {supplies.map((s) => (
              <div key={s.id} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-slate-100 bg-slate-50 px-3.5 py-2.5">
                <div>
                  <p className="font-bold text-slate-900">{s.name}</p>
                  <p className="text-slate-500">
                    Compra: R$ {s.purchasePrice.toFixed(2)} / {s.purchaseQuantity}
                    {s.purchaseUnitCode} · Perda padrão {s.defaultLossPercent}% · Saldo:{" "}
                    <strong className={s.quantityOnHand <= s.minimumStockQuantity ? "text-rose-700" : "text-emerald-700"}>
                      {s.quantityOnHand.toFixed(1)} {s.controlUnitCode || s.contentUnitCode}
                    </strong>
                  </p>
                </div>
                <div className="flex gap-1.5">
                  <button
                    type="button"
                    onClick={() => {
                      setEditingSupId(s.id);
                      setSupName(s.name);
                      setSupPrice(String(s.purchasePrice));
                      setSupPurchQty(String(s.purchaseQuantity));
                      setSupPurchUnit(s.purchaseUnitCode);
                      setSupContentQty(String(s.contentQuantityPerPackage));
                      setSupContentUnit(s.contentUnitCode);
                      setSupLossPct(String(s.defaultLossPercent));
                      setSupMinStock(String(s.minimumStockQuantity));
                    }}
                    className="rounded-lg border bg-white px-2.5 py-1 font-semibold"
                  >
                    <Edit3 className="inline h-3 w-3" /> Editar
                  </button>
                  <button type="button" onClick={() => deleteSupply(s.id)} className="rounded-lg border border-rose-200 bg-white px-2 py-1 text-rose-600">
                    <Trash2 className="inline h-3 w-3" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>

        <div className="space-y-4">
          <form onSubmit={saveSupply} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-2.5 text-xs">
            <h4 className="text-sm font-extrabold text-slate-900">{editingSupId ? "Editar Insumo" : "Novo Insumo"}</h4>
            <input value={supName} onChange={(e) => setSupName(e.target.value)} required placeholder="Ex: Polpa de Açaí, Leite Ninho" className="w-full rounded-xl border px-3 py-2 text-sm" />
            <div className="grid grid-cols-3 gap-2">
              <label>Preço R$<input type="number" step="0.01" value={supPrice} onChange={(e) => setSupPrice(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
              <label>Qtd Comp.<input type="number" step="0.01" value={supPurchQty} onChange={(e) => setSupPurchQty(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
              <label>Un Comp.<input value={supPurchUnit} onChange={(e) => setSupPurchUnit(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
            </div>
            <div className="grid grid-cols-3 gap-2">
              <label>Conteúdo<input type="number" step="0.01" value={supContentQty} onChange={(e) => setSupContentQty(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
              <label>Un Uso<input value={supContentUnit} onChange={(e) => setSupContentUnit(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
              <label>Perda %<input type="number" step="0.1" value={supLossPct} onChange={(e) => setSupLossPct(e.target.value)} className="mt-1 w-full rounded-lg border px-2 py-1.5" /></label>
            </div>
            <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2.5 text-sm font-bold text-white">
              {editingSupId ? "Salvar Insumo" : "Cadastrar Insumo"}
            </button>
          </form>

          {supplies.length > 0 && (
            <form onSubmit={receivePurchase} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm space-y-2 text-xs">
              <h4 className="text-sm font-extrabold text-slate-900">Dar Entrada no Estoque</h4>
              <select value={buySupplyId || supplies[0]?.id || ""} onChange={(e) => setBuySupplyId(e.target.value)} className="w-full rounded-xl border px-2.5 py-2">
                {supplies.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
              <div className="grid grid-cols-3 gap-2">
                <input type="number" step="0.01" value={buyQty} onChange={(e) => setBuyQty(e.target.value)} placeholder="Qtd" className="rounded-lg border px-2 py-1.5" />
                <input value={buyUnit} onChange={(e) => setBuyUnit(e.target.value)} placeholder="Un" className="rounded-lg border px-2 py-1.5" />
                <input type="number" step="0.01" value={buyPrice} onChange={(e) => setBuyPrice(e.target.value)} placeholder="R$ Un" className="rounded-lg border px-2 py-1.5" />
              </div>
              <button type="submit" className="w-full rounded-xl bg-emerald-600 py-2 font-bold text-white">
                Registrar Compra
              </button>
            </form>
          )}
        </div>
      </div>

      {/* Fichas Técnicas */}
      <div className="grid gap-6 lg:grid-cols-3">
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-2">
          <h3 className="mb-3 text-base font-extrabold text-slate-900">Fichas Técnicas & Custos Reais ({sheets.length})</h3>
          {simResult && (
            <div className="mb-3 rounded-xl border border-blue-200 bg-blue-50 px-4 py-2.5 text-xs font-bold text-blue-900">
              Simulação (25% margem líquida): Preço sugerido R$ {simResult.suggestedPrice.toFixed(2)} · Lucro estimado R$ {simResult.grossProfit.toFixed(2)}
            </div>
          )}
          <div className="space-y-3 text-xs">
            {sheets.map((sh) => (
              <div key={sh.id} className="rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <p className="text-sm font-extrabold text-slate-900">{sh.name}</p>
                    <p className="text-slate-600">
                      Custo total: <strong>R$ {sh.totalCost.toFixed(2)}</strong> · Custo por porção:{" "}
                      <strong className="text-emerald-700">R$ {sh.costPerPortion.toFixed(2)}</strong>
                    </p>
                  </div>
                  <div className="flex gap-1.5">
                    <button type="button" onClick={() => simulatePrice(sh.id)} className="inline-flex items-center gap-1 rounded-lg border bg-white px-2.5 py-1 font-semibold">
                      <Calculator className="h-3.5 w-3.5" /> Simular Preço
                    </button>
                    <button type="button" onClick={() => deleteSheet(sh.id)} className="rounded-lg border border-rose-200 bg-white px-2.5 py-1 text-rose-600">
                      <Trash2 className="inline h-3.5 w-3.5" />
                    </button>
                  </div>
                </div>
                <div className="mt-2 flex flex-wrap gap-1.5">
                  {sh.ingredients.map((ing) => (
                    <span key={ing.id} className="rounded-lg bg-white px-2 py-1 border border-slate-200">
                      {ing.supplyItemNameSnapshot}: {ing.quantity}
                      {ing.unitCode} (+{ing.lossPercent}% perda = {ing.grossQuantity}
                      {ing.unitCode}) · R$ {ing.calculatedCost.toFixed(2)}
                    </span>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </section>

        <form onSubmit={saveTechnicalSheet} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-xs">
          <h4 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
            <Plus className="h-4 w-4" /> Nova Ficha Técnica
          </h4>
          <input value={sheetName} onChange={(e) => setSheetName(e.target.value)} required placeholder="Ex: Receita Açaí 500ml" className="w-full rounded-xl border px-3 py-2 text-sm" />
          <select value={sheetProdId} onChange={(e) => setSheetProdId(e.target.value)} className="w-full rounded-xl border px-2.5 py-2">
            <option value="">Vincular a produto do cardápio (opcional)...</option>
            {products.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
          <div className="grid grid-cols-2 gap-2">
            <input type="number" step="0.1" value={sheetYield} onChange={(e) => setSheetYield(e.target.value)} placeholder="Rendimento" className="rounded-lg border px-2.5 py-1.5" />
            <input value={sheetYieldUnit} onChange={(e) => setSheetYieldUnit(e.target.value)} placeholder="Un (un/porcao)" className="rounded-lg border px-2.5 py-1.5" />
          </div>

          <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 space-y-2">
            <p className="font-bold text-slate-700">Adicionar Ingrediente</p>
            <select value={sheetIngSupplyId || supplies[0]?.id || ""} onChange={(e) => setSheetIngSupplyId(e.target.value)} className="w-full rounded-lg border bg-white px-2 py-1.5">
              {supplies.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
            <div className="grid grid-cols-3 gap-1.5">
              <input type="number" step="0.1" value={sheetIngQty} onChange={(e) => setSheetIngQty(e.target.value)} placeholder="Qtd" className="rounded-lg border bg-white px-2 py-1.5" />
              <input value={sheetIngUnit} onChange={(e) => setSheetIngUnit(e.target.value)} placeholder="Un" className="rounded-lg border bg-white px-2 py-1.5" />
              <input type="number" step="0.1" value={sheetIngLoss} onChange={(e) => setSheetIngLoss(e.target.value)} placeholder="Perda %" className="rounded-lg border bg-white px-2 py-1.5" />
            </div>
            <button type="button" onClick={addIngredientRow} className="w-full rounded-lg border border-slate-300 bg-white py-1.5 font-bold text-slate-800">
              + Incluir Ingrediente
            </button>
          </div>

          {sheetIngredients.length > 0 && (
            <ul className="space-y-1 text-slate-700">
              {sheetIngredients.map((row, idx) => {
                const nm = supplies.find((s) => s.id === row.supplyItemId)?.name || row.supplyItemId;
                return (
                  <li key={idx} className="flex justify-between">
                    <span>{nm}: {row.quantity}{row.unitCode} ({row.lossPercent}% perda)</span>
                    <button type="button" onClick={() => setSheetIngredients((p) => p.filter((_, i) => i !== idx))} className="text-rose-600">×</button>
                  </li>
                );
              })}
            </ul>
          )}

          <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2.5 text-sm font-bold text-white">
            Salvar Ficha Técnica
          </button>
        </form>
      </div>
    </div>
  );
}
