"use client";

import { AlertTriangle, CheckCircle2, FileText, Printer, X } from "lucide-react";

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

function fmtBrl(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

export function AdminTechSheetPdfModal({
  sheet,
  storeTradeName,
  onClose
}: {
  sheet: TechnicalSheetSummaryItem | null;
  storeTradeName: string;
  onClose: () => void;
}) {
  if (!sheet) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/70 p-4 backdrop-blur-sm">
      <div className="max-h-[92vh] w-full max-w-2xl overflow-y-auto rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div className="flex items-center justify-between border-b border-slate-200 px-6 py-4 print:hidden">
          <div className="flex items-center gap-2">
            <FileText className="h-5 w-5 text-slate-700" />
            <div>
              <h2 className="text-base font-extrabold text-slate-900">Ficha Técnica & Receita — {sheet.productName}</h2>
              <p className="text-xs text-slate-500">Versão v{sheet.version} • Base CMV oficial do estabelecimento</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => window.print()}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="inline-flex items-center gap-1.5 rounded-xl px-3.5 py-2 text-xs font-bold text-white shadow-sm hover:opacity-90"
            >
              <Printer className="h-3.5 w-3.5" /> Imprimir / Salvar PDF
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
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-slate-50 p-4 border border-slate-200">
            <div>
              <p className="text-[11px] font-bold uppercase tracking-wide text-slate-500">{storeTradeName}</p>
              <h3 className="text-lg font-extrabold text-slate-900">{sheet.name}</h3>
              <p className="text-xs text-slate-600">
                Rendimento: {sheet.yieldQuantity} {sheet.yieldUnitCode}
              </p>
            </div>
            <div className="text-right">
              {sheet.isLossAlert ? (
                <span className="inline-flex items-center gap-1 rounded-full bg-rose-100 px-3 py-1 text-xs font-extrabold text-rose-800">
                  <AlertTriangle className="h-3.5 w-3.5" /> ALERTA DE PREJUÍZO
                </span>
              ) : sheet.isLowMarginAlert ? (
                <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-3 py-1 text-xs font-extrabold text-amber-800">
                  <AlertTriangle className="h-3.5 w-3.5" /> MARGEM BAIXA ({sheet.marginPercent}%)
                </span>
              ) : (
                <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-3 py-1 text-xs font-extrabold text-emerald-800">
                  <CheckCircle2 className="h-3.5 w-3.5" /> MARGEM SAUDÁVEL ({sheet.marginPercent}%)
                </span>
              )}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <div className="rounded-xl border border-slate-200 p-3">
              <p className="text-[11px] font-semibold text-slate-500">Preço de Venda</p>
              <p className="mt-1 font-mono text-base font-extrabold text-slate-900">{fmtBrl(sheet.productSellingPrice)}</p>
            </div>
            <div className="rounded-xl border border-slate-200 p-3">
              <p className="text-[11px] font-semibold text-slate-500">Custo CMV / Porção</p>
              <p className="mt-1 font-mono text-base font-extrabold text-rose-700">{fmtBrl(sheet.costPerPortion)}</p>
            </div>
            <div className="rounded-xl border border-slate-200 p-3">
              <p className="text-[11px] font-semibold text-slate-500">Lucro Bruto Unit.</p>
              <p className={`mt-1 font-mono text-base font-extrabold ${sheet.profitPerUnit >= 0 ? "text-emerald-700" : "text-rose-700"}`}>
                {fmtBrl(sheet.profitPerUnit)}
              </p>
            </div>
            <div className="rounded-xl border border-slate-200 p-3">
              <p className="text-[11px] font-semibold text-slate-500">CMV sobre Venda</p>
              <p className="mt-1 font-mono text-base font-extrabold text-slate-900">{sheet.cmvPercent}%</p>
            </div>
          </div>

          <div>
            <h4 className="mb-2 text-xs font-extrabold uppercase tracking-wide text-slate-700">Insumos & Ingredientes da Receita</h4>
            <div className="overflow-hidden rounded-xl border border-slate-200">
              <table className="w-full text-left text-xs">
                <thead className="bg-slate-50 text-slate-600">
                  <tr>
                    <th className="px-3 py-2 font-bold">Insumo</th>
                    <th className="px-3 py-2 font-bold">Quantidade</th>
                    <th className="px-3 py-2 font-bold">Perda</th>
                    <th className="px-3 py-2 text-right font-bold">Custo Rateado</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {sheet.ingredients.map((ing) => (
                    <tr key={`${ing.supplyItemId}-${ing.name}`}>
                      <td className="px-3 py-2 font-medium text-slate-900">{ing.name}</td>
                      <td className="px-3 py-2 font-mono text-slate-700">
                        {ing.quantity} {ing.unitCode}
                      </td>
                      <td className="px-3 py-2 font-mono text-slate-500">{ing.lossPercent}%</td>
                      <td className="px-3 py-2 text-right font-mono font-bold text-slate-900">{fmtBrl(ing.calculatedCost)}</td>
                    </tr>
                  ))}
                  {sheet.packagings.map((pkg, idx) => (
                    <tr key={`pkg-${idx}`} className="bg-slate-50/50">
                      <td className="px-3 py-2 font-medium text-slate-700">[Embalagem] {pkg.description}</td>
                      <td className="px-3 py-2 font-mono text-slate-600">
                        {pkg.quantity} {pkg.unitCode}
                      </td>
                      <td className="px-3 py-2 text-slate-400">—</td>
                      <td className="px-3 py-2 text-right font-mono font-bold text-slate-900">{fmtBrl(pkg.calculatedCost)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {sheet.notes && (
            <div className="rounded-xl border border-slate-200 bg-slate-50 p-3.5 text-xs text-slate-700">
              <p className="font-extrabold text-slate-900">Modo de Preparo / Observações Técnicas</p>
              <p className="mt-1 whitespace-pre-wrap leading-relaxed">{sheet.notes}</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
