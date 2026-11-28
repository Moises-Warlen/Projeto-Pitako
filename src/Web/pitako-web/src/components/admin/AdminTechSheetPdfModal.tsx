"use client";

import { AlertTriangle, CheckCircle2, FileSpreadsheet, FileText, Plus, Printer, Save, Trash2, X } from "lucide-react";
import { useEffect, useState } from "react";
import { apiFetch, resolveMediaUrl } from "@/lib/api";
import { AdminProductDto } from "./AdminCatalogTab";
import { downloadSingleSheetExcelHtml } from "./AdminCatalogAndSheetsExportModal";

export interface TechnicalSheetSummaryItem {
  id: string;
  productId?: string | null;
  productName: string;
  productSellingPrice: number;
  name: string;
  yieldQuantity: number;
  yieldUnitCode: string;
  ingredientsTotalCost: number;
  packagingsTotalCost: number;
  totalCost: number;
  costPerPortion: number;
  profitPerUnit: number;
  marginPercent: number;
  cmvPercent: number;
  isLossAlert: boolean;
  isLowMarginAlert: boolean;
  notes?: string | null;
  version: number;
  ingredients: Array<{
    supplyItemId: string;
    name: string;
    quantity: number;
    unitCode: string;
    lossPercent: number;
    calculatedCost: number;
  }>;
  packagings: Array<{
    supplyItemId?: string | null;
    description: string;
    quantity: number;
    unitCode: string;
    calculatedCost: number;
  }>;
}

interface EditableIngRow {
  supplyItemId?: string | null;
  name: string;
  quantity: string;
  unitCode: string;
  unitPrice: string;
  lossPercent: string;
}

function fmtBrl(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

export function AdminTechSheetPdfModal({
  token,
  sheet,
  product,
  supplies = [],
  storeTradeName,
  onSaved,
  onNotify,
  onClose
}: {
  token?: string;
  sheet: TechnicalSheetSummaryItem | null;
  product?: AdminProductDto;
  supplies?: Array<{ id: string; name: string; contentUnitCode: string; costPerContentUnit: number }>;
  storeTradeName: string;
  onSaved?: () => Promise<void>;
  onNotify?: (msg: string) => void;
  onClose: () => void;
}) {
  const [rows, setRows] = useState<EditableIngRow[]>([]);
  const [notes, setNotes] = useState("");
  const [pickSupplyId, setPickSupplyId] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!sheet) return;
    setNotes(sheet.notes || "");
    setRows(
      sheet.ingredients.map((i) => ({
        supplyItemId: i.supplyItemId,
        name: i.name,
        quantity: String(i.quantity),
        unitCode: i.unitCode,
        unitPrice: i.quantity > 0 ? (i.calculatedCost / i.quantity).toFixed(4) : "0.0200",
        lossPercent: String(i.lossPercent ?? 0)
      }))
    );
  }, [sheet]);

  if (!sheet) return null;

  const liveIngTotal = rows.reduce(
    (sum, r) => sum + (Number(r.quantity) || 0) * (Number(r.unitPrice) || 0),
    0
  );
  const liveTotalCost = liveIngTotal + sheet.packagingsTotalCost;
  const liveProfit = sheet.productSellingPrice - liveTotalCost;
  const liveMarginPct = sheet.productSellingPrice > 0 ? (liveProfit / sheet.productSellingPrice) * 100 : 0;

  async function saveSheet() {
    if (!token || !sheet) return;
    setSaving(true);
    const res = await apiFetch<{ costPerPortion: number }>(
      `/api/v1/inventory/technical-sheets/${sheet.id}/quick-ingredients`,
      {
        method: "PUT",
        body: JSON.stringify({
          name: sheet.name,
          yieldQuantity: sheet.yieldQuantity,
          yieldUnitCode: sheet.yieldUnitCode,
          notes,
          ingredients: rows.map((r) => ({
            supplyItemId: r.supplyItemId || null,
            newSupplyName: r.supplyItemId ? null : r.name,
            quantity: Math.max(0.001, Number(r.quantity) || 1),
            unitCode: r.unitCode || "g",
            unitPriceAmount: Math.max(0.001, Number(r.unitPrice) || 0.02),
            lossPercent: Number(r.lossPercent) || 0
          })),
          packagings: sheet.packagings.map((p) => ({
            supplyItemId: p.supplyItemId || null,
            description: p.description,
            quantity: p.quantity,
            unitCode: p.unitCode,
            costAmount: p.calculatedCost
          }))
        })
      },
      token
    );
    setSaving(false);
    if (res.ok) {
      onNotify?.(`Ficha técnica salva! Novo CMV: ${fmtBrl(res.data?.costPerPortion ?? liveTotalCost)}`);
      await onSaved?.();
      onClose();
    } else {
      onNotify?.(res.errorMessage || "Erro ao salvar ficha técnica.");
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/70 p-4 backdrop-blur-sm">
      <div className="max-h-[92vh] w-full max-w-3xl overflow-y-auto rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200 px-6 py-4 print:hidden">
          <div className="flex items-center gap-2">
            <FileText className="h-5 w-5 text-slate-700" />
            <div>
              <h2 className="text-base font-extrabold text-slate-900">{sheet.productName} — Ficha Técnica Editável</h2>
              <p className="text-xs text-slate-500">{storeTradeName} • Versão v{sheet.version}</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {token && (
              <button
                type="button"
                disabled={saving}
                onClick={() => void saveSheet()}
                className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-extrabold text-white hover:bg-emerald-700 disabled:opacity-50"
              >
                <Save className="h-3.5 w-3.5" /> {saving ? "Salvando..." : "Salvar Ficha Técnica"}
              </button>
            )}
            <button
              type="button"
              onClick={() => {
                downloadSingleSheetExcelHtml(
                  sheet,
                  product,
                  storeTradeName,
                  resolveMediaUrl(product?.primaryImageUrl) || undefined
                );
                onNotify?.("Ficha técnica exportada para Excel (.xls)!");
              }}
              className="inline-flex items-center gap-1.5 rounded-xl bg-teal-600 px-3.5 py-2 text-xs font-bold text-white shadow-sm hover:bg-teal-700"
            >
              <FileSpreadsheet className="h-3.5 w-3.5" /> Excel (.xls)
            </button>
            <button
              type="button"
              onClick={() => window.print()}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="inline-flex items-center gap-1.5 rounded-xl px-3.5 py-2 text-xs font-bold text-white shadow-sm hover:opacity-90"
            >
              <Printer className="h-3.5 w-3.5" /> Exportar PDF
            </button>
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg border border-slate-200 p-1.5 text-slate-500 hover:bg-slate-100"
              aria-label="Fechar ficha técnica"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        <div className="space-y-5 p-6">
          <div className="rounded-xl border border-amber-200 bg-amber-50/70 p-3 text-xs text-amber-950 print:hidden">
            💡 <strong>Edição Livre de Insumos:</strong> altere quantidades, valores unitários, exclua itens ou puxe insumos já cadastrados — o custo CMV e a margem recalculam na hora!
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-slate-50 p-4">
            <div>
              <p className="text-[11px] font-bold uppercase tracking-wide text-slate-500">{storeTradeName}</p>
              <h3 className="text-lg font-extrabold text-slate-900">{sheet.name}</h3>
              <p className="text-xs text-slate-600">
                Venda: <strong>{fmtBrl(sheet.productSellingPrice)}</strong> · Custo CMV: <strong className="text-rose-700">{fmtBrl(liveTotalCost)}</strong> · Lucro: <strong className={liveProfit >= 0 ? "text-emerald-700" : "text-rose-700"}>{fmtBrl(liveProfit)} ({liveMarginPct.toFixed(1)}%)</strong>
              </p>
            </div>
            <div>
              {liveProfit < 0 ? (
                <span className="inline-flex items-center gap-1 rounded-full bg-rose-100 px-3 py-1 text-xs font-extrabold text-rose-800">
                  <AlertTriangle className="h-3.5 w-3.5" /> ALERTA DE PREJUÍZO
                </span>
              ) : liveMarginPct < 25 ? (
                <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-3 py-1 text-xs font-extrabold text-amber-800">
                  <AlertTriangle className="h-3.5 w-3.5" /> MARGEM BAIXA ({liveMarginPct.toFixed(1)}%)
                </span>
              ) : (
                <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-3 py-1 text-xs font-extrabold text-emerald-800">
                  <CheckCircle2 className="h-3.5 w-3.5" /> SAUDÁVEL ({liveMarginPct.toFixed(1)}%)
                </span>
              )}
            </div>
          </div>

          <div className="overflow-x-auto rounded-xl border border-slate-200">
            <table className="w-full text-left text-xs">
              <thead className="bg-slate-50 text-slate-600">
                <tr>
                  <th className="px-3 py-2 font-bold">Insumo / Ingrediente</th>
                  <th className="px-3 py-2 font-bold">Qtd</th>
                  <th className="px-3 py-2 font-bold">Unid.</th>
                  <th className="px-3 py-2 font-bold">Unitário (R$)</th>
                  <th className="px-3 py-2 text-right font-bold">Total</th>
                  <th className="px-2 py-2 print:hidden" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, idx) => {
                  const rowTotal = (Number(r.quantity) || 0) * (Number(r.unitPrice) || 0);
                  return (
                    <tr key={idx}>
                      <td className="px-3 py-2">
                        <input
                          value={r.name}
                          onChange={(e) => {
                            const copy = [...rows];
                            copy[idx] = { ...r, name: e.target.value };
                            setRows(copy);
                          }}
                          className="w-full rounded-lg border border-slate-200 px-2 py-1 font-medium text-slate-900"
                        />
                      </td>
                      <td className="px-3 py-2">
                        <input
                          type="number"
                          step="0.1"
                          value={r.quantity}
                          onChange={(e) => {
                            const copy = [...rows];
                            copy[idx] = { ...r, quantity: e.target.value };
                            setRows(copy);
                          }}
                          className="w-20 rounded-lg border border-slate-200 px-2 py-1 font-mono"
                        />
                      </td>
                      <td className="px-3 py-2">
                        <input
                          value={r.unitCode}
                          onChange={(e) => {
                            const copy = [...rows];
                            copy[idx] = { ...r, unitCode: e.target.value };
                            setRows(copy);
                          }}
                          className="w-16 rounded-lg border border-slate-200 px-2 py-1 font-mono"
                        />
                      </td>
                      <td className="px-3 py-2">
                        <input
                          type="number"
                          step="0.0001"
                          value={r.unitPrice}
                          onChange={(e) => {
                            const copy = [...rows];
                            copy[idx] = { ...r, unitPrice: e.target.value };
                            setRows(copy);
                          }}
                          className="w-24 rounded-lg border border-slate-200 px-2 py-1 font-mono"
                        />
                      </td>
                      <td className="px-3 py-2 text-right font-mono font-extrabold text-amber-700">{fmtBrl(rowTotal)}</td>
                      <td className="px-2 py-2 print:hidden">
                        <button
                          type="button"
                          onClick={() => setRows(rows.filter((_, i) => i !== idx))}
                          className="rounded-lg p-1 text-rose-600 hover:bg-rose-50"
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      </td>
                    </tr>
                  );
                })}
                {sheet.packagings.map((pkg, idx) => (
                  <tr key={`pkg-${idx}`} className="bg-slate-50/70">
                    <td className="px-3 py-2 font-medium text-slate-700">[Embalagem] {pkg.description}</td>
                    <td className="px-3 py-2 font-mono">{pkg.quantity}</td>
                    <td className="px-3 py-2 font-mono">{pkg.unitCode}</td>
                    <td className="px-3 py-2 font-mono">{fmtBrl(pkg.calculatedCost)}</td>
                    <td className="px-3 py-2 text-right font-mono font-bold text-slate-800">{fmtBrl(pkg.calculatedCost)}</td>
                    <td className="print:hidden" />
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex flex-wrap items-center gap-2 rounded-xl border border-dashed border-slate-300 p-3 print:hidden">
            <button
              type="button"
              onClick={() =>
                setRows([
                  ...rows,
                  { supplyItemId: null, name: "Novo Insumo", quantity: "30", unitCode: "g", unitPrice: "0.04", lossPercent: "0" }
                ])
              }
              className="inline-flex items-center gap-1 rounded-xl border border-slate-300 bg-white px-3 py-1.5 text-xs font-bold text-slate-800 hover:bg-slate-100"
            >
              <Plus className="h-3.5 w-3.5" /> Inserir Novo Insumo
            </button>

            <select
              value={pickSupplyId}
              onChange={(e) => {
                const sid = e.target.value;
                setPickSupplyId("");
                const sup = supplies.find((x) => x.id === sid);
                if (sup) {
                  setRows([
                    ...rows,
                    {
                      supplyItemId: sup.id,
                      name: sup.name,
                      quantity: "30",
                      unitCode: sup.contentUnitCode,
                      unitPrice: String(sup.costPerContentUnit || 0.02),
                      lossPercent: "0"
                    }
                  ]);
                }
              }}
              className="flex-1 rounded-xl border border-slate-300 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700"
            >
              <option value="">⚡ Puxar de item já cadastrado no sistema...</option>
              {supplies.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name} ({fmtBrl(s.costPerContentUnit)}/{s.contentUnitCode})
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="text-xs font-bold text-slate-700">Observações de Preparo</label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 text-xs"
            />
          </div>
        </div>
      </div>
    </div>
  );
}
