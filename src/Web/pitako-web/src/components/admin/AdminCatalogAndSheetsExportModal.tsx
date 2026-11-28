"use client";

import { Download, FileSpreadsheet, FileText, Layers, Printer, Sparkles, X } from "lucide-react";
import QRCode from "qrcode";
import { useEffect, useMemo, useState } from "react";
import { resolveMediaUrl } from "@/lib/api";
import { AdminCategoryDto, AdminProductDto } from "./AdminCatalogTab";
import { ModifierGroupDetailedDto } from "./AdminProductAndCategoryModals";
import { TechnicalSheetSummaryItem } from "./AdminTechSheetPdfModal";

export interface AdminComboExportDto {
  id: string;
  name: string;
  description?: string | null;
  comboPrice: number;
  primaryImageUrl?: string | null;
  isFeatured: boolean;
  displayOrder: number;
  isActive: boolean;
  items: Array<{ productId: string; quantity: number; variationId?: string | null; notes?: string | null }>;
}

type ExportTabMode = "all_sheets" | "single_sheet" | "full_catalog";
type BundleFilter = "both" | "products" | "combos";

function escHtml(raw?: string | null): string {
  return String(raw ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function fmtMoney(v: number): string {
  return Number(v || 0).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

async function resolveDataUriOrUrl(rawUrl?: string | null): Promise<string> {
  const full = resolveMediaUrl(rawUrl);
  if (!full) return "";
  if (full.startsWith("data:")) return full;
  try {
    const res = await fetch(full);
    if (!res.ok) return full;
    const blob = await res.blob();
    return await new Promise<string>((resolve) => {
      const reader = new FileReader();
      reader.onloadend = () => resolve(typeof reader.result === "string" ? reader.result : full);
      reader.onerror = () => resolve(full);
      reader.readAsDataURL(blob);
    });
  } catch {
    return full;
  }
}

export function downloadSingleSheetExcelHtml(
  sheet: TechnicalSheetSummaryItem,
  product: AdminProductDto | undefined,
  storeTradeName: string,
  photoSrc?: string
) {
  const imgCell = photoSrc
    ? `<img src="${escHtml(photoSrc)}" width="72" height="72" style="object-fit:cover;border-radius:8px;" />`
    : "Sem foto";
  const ingRows = sheet.ingredients
    .map(
      (i) =>
        `<tr><td>${escHtml(i.name)}</td><td>${i.quantity}</td><td>${escHtml(i.unitCode)}</td><td>${i.lossPercent}%</td><td>${fmtMoney(i.calculatedCost)}</td></tr>`
    )
    .join("");
  const packRows = sheet.packagings
    .map(
      (p) =>
        `<tr><td>Embalagem: ${escHtml(p.description)}</td><td>${p.quantity}</td><td>${escHtml(p.unitCode)}</td><td>0%</td><td>${fmtMoney(p.calculatedCost)}</td></tr>`
    )
    .join("");

  const html = `
<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
<head><meta charset="utf-8" /></head>
<body>
  <table border="1" cellspacing="0" cellpadding="6" style="font-family:Arial,sans-serif;font-size:12px;">
    <tr style="background:#1e293b;color:#ffffff;font-weight:bold;">
      <td colspan="5">${escHtml(storeTradeName)} — Ficha Técnica Individual: ${escHtml(sheet.productName)}</td>
    </tr>
    <tr>
      <td rowspan="3" align="center" valign="middle">${imgCell}</td>
      <td><strong>Produto</strong></td><td>${escHtml(sheet.productName)}</td>
      <td><strong>SKU</strong></td><td>${escHtml(product?.sku || "—")}</td>
    </tr>
    <tr>
      <td><strong>Preço de Venda</strong></td><td>${fmtMoney(sheet.productSellingPrice)}</td>
      <td><strong>Custo Total / CMV</strong></td><td>${fmtMoney(sheet.costPerPortion)} (${sheet.cmvPercent.toFixed(1)}%)</td>
    </tr>
    <tr>
      <td><strong>Lucro Unitário</strong></td><td>${fmtMoney(sheet.profitPerUnit)}</td>
      <td><strong>Margem %</strong></td><td>${sheet.marginPercent.toFixed(1)}%</td>
    </tr>
    <tr style="background:#f1f5f9;font-weight:bold;">
      <td>Insumo / Embalagem</td><td>Quantidade</td><td>Unidade</td><td>Perda %</td><td>Custo Total</td>
    </tr>
    ${ingRows}
    ${packRows}
    ${sheet.notes ? `<tr><td><strong>Observações</strong></td><td colspan="4">${escHtml(sheet.notes)}</td></tr>` : ""}
  </table>
</body>
</html>`;

  const blob = new Blob(["\uFEFF" + html], { type: "application/vnd.ms-excel;charset=utf-8;" });
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  const safeSlug = sheet.productName.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "produto";
  a.download = `ficha-tecnica-${safeSlug}.xls`;
  a.click();
  URL.revokeObjectURL(a.href);
}

export function AdminCatalogAndSheetsExportModal({
  open,
  slug,
  storeTradeName,
  formattedCnpj,
  logoUrl,
  slogan,
  primaryColorHex,
  accentColorHex,
  storeAddress,
  whatsAppNumber,
  instagramHandle,
  categories,
  products,
  modifierGroups,
  techSheets,
  combos,
  onNotify,
  onClose
}: {
  open: boolean;
  slug: string;
  storeTradeName: string;
  formattedCnpj?: string;
  logoUrl?: string | null;
  slogan?: string | null;
  primaryColorHex: string;
  accentColorHex: string;
  storeAddress?: string;
  whatsAppNumber?: string;
  instagramHandle?: string;
  categories: AdminCategoryDto[];
  products: AdminProductDto[];
  modifierGroups: ModifierGroupDetailedDto[];
  techSheets: TechnicalSheetSummaryItem[];
  combos: AdminComboExportDto[];
  onNotify: (msg: string) => void;
  onClose: () => void;
}) {
  const [mode, setMode] = useState<ExportTabMode>("all_sheets");
  const [bundleFilter, setBundleFilter] = useState<BundleFilter>("both");
  const [selectedProductId, setSelectedProductId] = useState<string>(products[0]?.id || "");
  const [busyExport, setBusyExport] = useState(false);
  const [menuQrDataUrl, setMenuQrDataUrl] = useState<string>("");

  const orderedCategories = useMemo(
    () => [...categories].sort((a, b) => a.displayOrder - b.displayOrder),
    [categories]
  );

  const orderedProducts = useMemo(() => {
    return [...products].sort((a, b) => {
      const ca = categories.find((c) => c.id === a.categoryId)?.displayOrder ?? 999;
      const cb = categories.find((c) => c.id === b.categoryId)?.displayOrder ?? 999;
      return ca - cb || (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.name.localeCompare(b.name);
    });
  }, [categories, products]);

  const sheetByProductId = useMemo(() => {
    const map = new Map<string, TechnicalSheetSummaryItem>();
    techSheets.forEach((s) => {
      if (s.productId) map.set(s.productId, s);
    });
    return map;
  }, [techSheets]);

  useEffect(() => {
    if (!open) return;
    if (!selectedProductId && orderedProducts.length > 0) {
      setSelectedProductId(orderedProducts[0].id);
    }
    const targetUrl =
      typeof window !== "undefined" ? `${window.location.origin}/loja/${slug}` : `https://pitako.com.br/loja/${slug}`;
    QRCode.toDataURL(targetUrl, { width: 180, margin: 1 })
      .then(setMenuQrDataUrl)
      .catch(() => setMenuQrDataUrl(""));
  }, [open, orderedProducts, selectedProductId, slug]);

  if (!open) return null;

  function computeComboSummary(combo: AdminComboExportDto) {
    let originalSum = 0;
    let estimatedCost = 0;
    const itemRows = combo.items.map((it) => {
      const prod = products.find((p) => p.id === it.productId);
      const variant = it.variationId ? prod?.variations.find((v) => v.id === it.variationId) : undefined;
      const unitSell = variant ? variant.price : prod?.basePrice ?? 0;
      const sheet = prod ? sheetByProductId.get(prod.id) : undefined;
      const unitCost = variant?.costPrice && variant.costPrice > 0 ? variant.costPrice : sheet?.costPerPortion ?? 0;
      originalSum += unitSell * it.quantity;
      estimatedCost += unitCost * it.quantity;
      return {
        productName: prod?.name || "Produto removido",
        variationName: variant?.variationName || null,
        quantity: it.quantity,
        unitSell,
        unitCost,
        notes: it.notes,
        imageUrl: prod?.primaryImageUrl
      };
    });
    const profit = combo.comboPrice - estimatedCost;
    const marginPct = combo.comboPrice > 0 ? (profit / combo.comboPrice) * 100 : 0;
    const savings = Math.max(0, originalSum - combo.comboPrice);
    const discountPct = originalSum > 0 ? Math.round((savings / originalSum) * 100) : 0;
    return { itemRows, originalSum, estimatedCost, profit, marginPct, savings, discountPct };
  }

  async function buildImageLookup(targetProducts: AdminProductDto[], targetCombos: AdminComboExportDto[]) {
    const urls = new Set<string>();
    if (logoUrl) urls.add(logoUrl);
    targetProducts.forEach((p) => {
      if (p.primaryImageUrl) urls.add(p.primaryImageUrl);
    });
    targetCombos.forEach((c) => {
      if (c.primaryImageUrl) urls.add(c.primaryImageUrl);
    });
    const entries = await Promise.all(
      Array.from(urls).map(async (u) => [u, await resolveDataUriOrUrl(u)] as const)
    );
    return new Map<string, string>(entries);
  }

  function openPrintWindow(title: string, bodyHtml: string) {
    const popup = window.open("", "_blank", "width=1024,height=760");
    if (!popup) {
      onNotify("Permita pop-ups no navegador para gerar o PDF.");
      return;
    }
    popup.document.write(`<!DOCTYPE html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8" />
  <title>${escHtml(title)}</title>
  <style>
    @page { size: A4; margin: 12mm; }
    * { box-sizing: border-box; }
    body { font-family: Arial, Helvetica, sans-serif; color: #0f172a; margin: 0; font-size: 11px; line-height: 1.4; }
    .header { display: flex; justify-content: space-between; align-items: center; padding: 14px 18px; border-radius: 12px; background: ${primaryColorHex || "#1e293b"}; color: #fff; margin-bottom: 16px; }
    .header-left { display: flex; align-items: center; gap: 12px; }
    .logo { width: 52px; height: 52px; object-fit: contain; border-radius: 8px; background: rgba(255,255,255,0.12); padding: 4px; }
    .card { border: 1px solid #cbd5e1; border-radius: 10px; padding: 12px; margin-bottom: 14px; break-inside: avoid; page-break-inside: avoid; background: #ffffff; }
    .page-break { page-break-after: always; break-after: page; }
    .prod-top { display: flex; gap: 12px; align-items: flex-start; border-bottom: 1px solid #e2e8f0; padding-bottom: 8px; margin-bottom: 8px; }
    .photo { width: 72px; height: 72px; object-fit: cover; border-radius: 8px; border: 1px solid #cbd5e1; flex-shrink: 0; }
    .no-photo { width: 72px; height: 72px; border-radius: 8px; border: 1px dashed #cbd5e1; display: flex; align-items: center; justify-content: center; color: #94a3b8; font-size: 9px; flex-shrink: 0; text-align: center; }
    .badge { display: inline-block; padding: 2px 7px; border-radius: 999px; font-size: 9px; font-weight: bold; margin-right: 4px; }
    .badge-ok { background: #dcfce7; color: #166534; }
    .badge-warn { background: #fef3c7; color: #92400e; }
    .badge-err { background: #ffe4e6; color: #9f1239; }
    .badge-cat { background: #ede9fe; color: #5b21b6; }
    table { width: 100%; border-collapse: collapse; margin-top: 6px; font-size: 10px; }
    th, td { border: 1px solid #e2e8f0; padding: 4px 6px; text-align: left; }
    th { background: #f8fafc; font-weight: bold; color: #475569; }
    .cat-banner { background: #f1f5f9; border-left: 5px solid ${accentColorHex || "#f59e0b"}; padding: 8px 12px; margin: 16px 0 10px; font-size: 14px; font-weight: bold; break-after: avoid; }
    .catalog-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; }
    .print-bar { position: sticky; top: 0; background: #0f172a; color: #fff; padding: 10px 16px; display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; z-index: 10; }
    @media print { .print-bar { display: none; } }
  </style>
</head>
<body>
  <div class="print-bar">
    <span><strong>${escHtml(title)}</strong> — Use &quot;Salvar como PDF&quot; ou sua impressora</span>
    <button onclick="window.print()" style="background:#f59e0b;color:#0f172a;border:none;padding:6px 14px;border-radius:8px;font-weight:bold;cursor:pointer;">Imprimir / Salvar PDF</button>
  </div>
  <div style="padding: 4px 8px;">${bodyHtml}</div>
  <script>setTimeout(function(){ window.print(); }, 450);</script>
</body>
</html>`);
    popup.document.close();
  }

  async function handleExportTechSheetsPdf(separatePages: boolean, targetOnlyId?: string) {
    setBusyExport(true);
    try {
      const targetProducts = targetOnlyId
        ? orderedProducts.filter((p) => p.id === targetOnlyId)
        : bundleFilter === "combos"
          ? []
          : orderedProducts;
      const targetCombos = targetOnlyId
        ? []
        : bundleFilter === "products"
          ? []
          : combos;
      const imgMap = await buildImageLookup(targetProducts, targetCombos);
      const resolvedLogo = logoUrl ? imgMap.get(logoUrl) || "" : "";

      const headerHtml = `
<div class="header">
  <div class="header-left">
    ${resolvedLogo ? `<img src="${escHtml(resolvedLogo)}" class="logo" />` : ""}
    <div>
      <div style="font-size:17px;font-weight:bold;">${escHtml(storeTradeName)} — Ficha Técnica Completa</div>
      <div style="font-size:11px;opacity:0.9;">${formattedCnpj ? `CNPJ ${escHtml(formattedCnpj)} • ` : ""}Gerado em ${new Date().toLocaleString("pt-BR")}</div>
    </div>
  </div>
  <div style="text-align:right;font-size:11px;">
    <div><strong>${targetProducts.length}</strong> produto(s)</div>
    <div><strong>${targetCombos.length}</strong> combo(s)</div>
  </div>
</div>`;

      const productBlocks = targetProducts
        .map((p, idx) => {
          const catName = categories.find((c) => c.id === p.categoryId)?.name || "Sem categoria";
          const sheet = sheetByProductId.get(p.id);
          const photo = p.primaryImageUrl ? imgMap.get(p.primaryImageUrl) || "" : "";
          const cost = sheet?.costPerPortion ?? 0;
          const profit = p.basePrice - cost;
          const margin = p.basePrice > 0 ? (profit / p.basePrice) * 100 : 0;
          const cmv = p.basePrice > 0 ? (cost / p.basePrice) * 100 : 0;
          const badge = !sheet
            ? `<span class="badge badge-warn">Sem ficha cadastrada</span>`
            : profit < 0
              ? `<span class="badge badge-err">PREJUÍZO (${margin.toFixed(1)}%)</span>`
              : margin < 25
                ? `<span class="badge badge-warn">MARGEM BAIXA (${margin.toFixed(1)}%)</span>`
                : `<span class="badge badge-ok">SAUDÁVEL (${margin.toFixed(1)}%)</span>`;

          const varHtml =
            p.variations.length > 0
              ? `<table><thead><tr><th>Tamanho / Variação</th><th>SKU</th><th>Preço Venda</th><th>Custo Estimado</th><th>Lucro</th><th>Acompanhamentos</th></tr></thead><tbody>${p.variations
                  .map((v) => {
                    const vCost = v.costPrice && v.costPrice > 0 ? v.costPrice : cost;
                    const vProfit = v.price - vCost;
                    const freeRule = p.variationRules.find((r) => r.variationId === v.id && !r.isHidden && r.freeAllowance > 0);
                    return `<tr><td><strong>${escHtml(v.variationName)}</strong></td><td>${escHtml(v.sku || "—")}</td><td>${fmtMoney(v.price)}</td><td>${fmtMoney(vCost)}</td><td>${fmtMoney(vProfit)}</td><td>${freeRule ? `${freeRule.freeAllowance} grátis` : "—"}</td></tr>`;
                  })
                  .join("")}</tbody></table>`
              : "";

          const ingHtml = sheet
            ? `<table><thead><tr><th>Insumo / Embalagem</th><th>Qtd</th><th>Unid.</th><th>Perda %</th><th>Custo</th></tr></thead><tbody>${sheet.ingredients
                .map(
                  (i) =>
                    `<tr><td>${escHtml(i.name)}</td><td>${i.quantity}</td><td>${escHtml(i.unitCode)}</td><td>${i.lossPercent}%</td><td>${fmtMoney(i.calculatedCost)}</td></tr>`
                )
                .join("")}${sheet.packagings
                .map(
                  (pk) =>
                    `<tr><td>Embalagem: ${escHtml(pk.description)}</td><td>${pk.quantity}</td><td>${escHtml(pk.unitCode)}</td><td>0%</td><td>${fmtMoney(pk.calculatedCost)}</td></tr>`
                )
                .join("")}</tbody></table>`
            : `<p style="color:#64748b;font-size:10px;margin:6px 0 0;">Este produto ainda não possui insumos detalhados na ficha técnica.</p>`;

          const pageBreakClass = separatePages && idx < targetProducts.length + targetCombos.length - 1 ? "page-break" : "";

          return `
<div class="card ${pageBreakClass}">
  <div class="prod-top">
    ${photo ? `<img src="${escHtml(photo)}" class="photo" />` : `<div class="no-photo">Sem Foto</div>`}
    <div style="flex:1;">
      <div style="display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap;">
        <strong style="font-size:13px;">${escHtml(p.name)}</strong>
        <div><span class="badge badge-cat">${escHtml(catName)}</span>${badge}</div>
      </div>
      <div style="color:#475569;margin-top:2px;">${escHtml(p.description || "Sem descrição cadastrada.")}</div>
      <div style="margin-top:5px;">
        <strong>SKU:</strong> ${escHtml(p.sku || "—")} &nbsp;|&nbsp;
        <strong>Venda Base:</strong> ${fmtMoney(p.basePrice)}
        ${p.promotionalPrice ? ` (Promo: ${fmtMoney(p.promotionalPrice)})` : ""} &nbsp;|&nbsp;
        <strong>CMV:</strong> ${fmtMoney(cost)} (${cmv.toFixed(1)}%) &nbsp;|&nbsp;
        <strong>Lucro:</strong> ${fmtMoney(profit)}
      </div>
    </div>
  </div>
  ${varHtml}
  ${ingHtml}
  ${sheet?.notes ? `<div style="margin-top:6px;font-size:10px;color:#475569;"><strong>Modo de preparo / Notas:</strong> ${escHtml(sheet.notes)}</div>` : ""}
</div>`;
        })
        .join("");

      const comboBlocks =
        targetCombos.length > 0
          ? `<div class="cat-banner">Combos &amp; Promoções (${targetCombos.length})</div>` +
            targetCombos
              .map((c, idx) => {
                const info = computeComboSummary(c);
                const photo = c.primaryImageUrl ? imgMap.get(c.primaryImageUrl) || "" : "";
                const pageBreakClass = separatePages && idx < targetCombos.length - 1 ? "page-break" : "";
                return `
<div class="card ${pageBreakClass}">
  <div class="prod-top">
    ${photo ? `<img src="${escHtml(photo)}" class="photo" />` : `<div class="no-photo">Sem Foto</div>`}
    <div style="flex:1;">
      <div style="display:flex;justify-content:space-between;align-items:center;">
        <strong style="font-size:13px;">COMBO: ${escHtml(c.name)}</strong>
        <span class="badge ${info.profit >= 0 ? "badge-ok" : "badge-err"}">Margem ${info.marginPct.toFixed(1)}%</span>
      </div>
      <div style="color:#475569;margin-top:2px;">${escHtml(c.description || "Combo promocional")}</div>
      <div style="margin-top:5px;">
        <strong>Preço Combo:</strong> ${fmtMoney(c.comboPrice)} &nbsp;|&nbsp;
        <strong>Soma Avulso:</strong> ${fmtMoney(info.originalSum)} (Economia ${fmtMoney(info.savings)} / ${info.discountPct}% OFF) &nbsp;|&nbsp;
        <strong>Custo Est.:</strong> ${fmtMoney(info.estimatedCost)} &nbsp;|&nbsp;
        <strong>Lucro Est.:</strong> ${fmtMoney(info.profit)}
      </div>
    </div>
  </div>
  <table>
    <thead><tr><th>Item do Combo</th><th>Variação</th><th>Qtd</th><th>Venda Avulsa</th><th>Custo Unit.</th></tr></thead>
    <tbody>
      ${info.itemRows
        .map(
          (r) =>
            `<tr><td>${escHtml(r.productName)}</td><td>${escHtml(r.variationName || "Padrão")}</td><td>${r.quantity}</td><td>${fmtMoney(r.unitSell)}</td><td>${fmtMoney(r.unitCost)}</td></tr>`
        )
        .join("")}
    </tbody>
  </table>
</div>`;
              })
              .join("")
          : "";

      openPrintWindow(
        `${storeTradeName} — Fichas Técnicas`,
        headerHtml + productBlocks + comboBlocks
      );
      onNotify("Prévia PDF pronta para salvar ou imprimir!");
    } finally {
      setBusyExport(false);
    }
  }

  async function handleExportExcelWithPhotos(targetOnlyId?: string) {
    setBusyExport(true);
    try {
      const targetProducts = targetOnlyId
        ? orderedProducts.filter((p) => p.id === targetOnlyId)
        : bundleFilter === "combos"
          ? []
          : orderedProducts;
      const targetCombos = targetOnlyId
        ? []
        : bundleFilter === "products"
          ? []
          : combos;
      const imgMap = await buildImageLookup(targetProducts, targetCombos);

      const prodRows = targetProducts
        .map((p) => {
          const catName = categories.find((c) => c.id === p.categoryId)?.name || "Sem categoria";
          const sheet = sheetByProductId.get(p.id);
          const photo = p.primaryImageUrl ? imgMap.get(p.primaryImageUrl) || "" : "";
          const imgHtml = photo ? `<img src="${escHtml(photo)}" width="64" height="64" style="object-fit:cover;" />` : "Sem foto";
          const cost = sheet?.costPerPortion ?? 0;
          const profit = p.basePrice - cost;
          const margin = p.basePrice > 0 ? (profit / p.basePrice) * 100 : 0;
          const cmv = p.basePrice > 0 ? (cost / p.basePrice) * 100 : 0;
          const varsText =
            p.variations.length > 0
              ? p.variations.map((v) => `${v.variationName}: ${fmtMoney(v.price)} (Custo ${fmtMoney(v.costPrice ?? cost)})`).join("<br/>")
              : "Preço único";
          const ingText = sheet
            ? [
                ...sheet.ingredients.map((i) => `${i.name}: ${i.quantity}${i.unitCode} (${fmtMoney(i.calculatedCost)})`),
                ...sheet.packagings.map((pk) => `Embalagem ${pk.description}: ${pk.quantity}${pk.unitCode} (${fmtMoney(pk.calculatedCost)})`)
              ].join("<br/>")
            : "Sem ficha detalhada";

          return `<tr>
            <td align="center" valign="middle">${imgHtml}</td>
            <td><strong>${escHtml(p.name)}</strong><br/>SKU: ${escHtml(p.sku || "—")}</td>
            <td>${escHtml(catName)}</td>
            <td>${escHtml(p.description || "")}</td>
            <td>${varsText}</td>
            <td>${fmtMoney(p.basePrice)}</td>
            <td>${fmtMoney(cost)}</td>
            <td>${fmtMoney(profit)}</td>
            <td>${margin.toFixed(1)}% (CMV ${cmv.toFixed(1)}%)</td>
            <td>${ingText}</td>
            <td>${escHtml(sheet?.notes || "")}</td>
          </tr>`;
        })
        .join("");

      const comboRows = targetCombos
        .map((c) => {
          const info = computeComboSummary(c);
          const photo = c.primaryImageUrl ? imgMap.get(c.primaryImageUrl) || "" : "";
          const imgHtml = photo ? `<img src="${escHtml(photo)}" width="64" height="64" style="object-fit:cover;" />` : "Sem foto";
          const itemsDesc = info.itemRows
            .map((r) => `${r.quantity}x ${r.productName}${r.variationName ? ` (${r.variationName})` : ""} — Custo ${fmtMoney(r.unitCost)}`)
            .join("<br/>");
          return `<tr style="background:#fffbeb;">
            <td align="center" valign="middle">${imgHtml}</td>
            <td><strong>COMBO: ${escHtml(c.name)}</strong></td>
            <td>Combos &amp; Promoções</td>
            <td>${escHtml(c.description || "")}</td>
            <td>Soma avulso: ${fmtMoney(info.originalSum)} (-${info.discountPct}%)</td>
            <td>${fmtMoney(c.comboPrice)}</td>
            <td>${fmtMoney(info.estimatedCost)}</td>
            <td>${fmtMoney(info.profit)}</td>
            <td>${info.marginPct.toFixed(1)}%</td>
            <td>${itemsDesc}</td>
            <td>Economia cliente: ${fmtMoney(info.savings)}</td>
          </tr>`;
        })
        .join("");

      const html = `
<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
<head><meta charset="utf-8" /></head>
<body>
  <table border="1" cellspacing="0" cellpadding="6" style="font-family:Arial,sans-serif;font-size:12px;">
    <tr style="background:#1e293b;color:#ffffff;font-weight:bold;font-size:14px;">
      <td colspan="11">${escHtml(storeTradeName)} — Fichas Técnicas Detalhadas com Fotos (${new Date().toLocaleDateString("pt-BR")})</td>
    </tr>
    <tr style="background:#e2e8f0;font-weight:bold;">
      <td>Foto</td>
      <td>Produto / Combo (SKU)</td>
      <td>Categoria</td>
      <td>Descrição</td>
      <td>Tamanhos / Variações</td>
      <td>Venda (R$)</td>
      <td>Custo CMV (R$)</td>
      <td>Lucro (R$)</td>
      <td>Margem / CMV %</td>
      <td>Ingredientes / Embalagens / Itens</td>
      <td>Notas / Detalhes</td>
    </tr>
    ${prodRows}
    ${comboRows}
  </table>
</body>
</html>`;

      const blob = new Blob(["\uFEFF" + html], { type: "application/vnd.ms-excel;charset=utf-8;" });
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = targetOnlyId
        ? `ficha-produto-${slug}.xls`
        : `ficha-tecnica-completa-${slug}.xls`;
      a.click();
      URL.revokeObjectURL(a.href);
      onNotify("Planilha Excel (.xls) com fotos e detalhes baixada!");
    } finally {
      setBusyExport(false);
    }
  }

  async function handleExportFullCatalogPdf() {
    setBusyExport(true);
    try {
      const imgMap = await buildImageLookup(orderedProducts, combos);
      const resolvedLogo = logoUrl ? imgMap.get(logoUrl) || "" : "";

      const headerHtml = `
<div class="header">
  <div class="header-left">
    ${resolvedLogo ? `<img src="${escHtml(resolvedLogo)}" class="logo" />` : ""}
    <div>
      <div style="font-size:20px;font-weight:bold;">${escHtml(storeTradeName)} — Catálogo Completo</div>
      <div style="font-size:12px;opacity:0.95;">${escHtml(slogan || "Cardápio Digital & Delivery Oficial")}</div>
      <div style="font-size:10px;opacity:0.85;margin-top:3px;">
        ${formattedCnpj ? `CNPJ ${escHtml(formattedCnpj)} • ` : ""}
        ${storeAddress ? `${escHtml(storeAddress)} • ` : ""}
        ${whatsAppNumber ? `WhatsApp: ${escHtml(whatsAppNumber)} ` : ""}
        ${instagramHandle ? `• ${escHtml(instagramHandle)}` : ""}
      </div>
    </div>
  </div>
  ${menuQrDataUrl ? `<div style="text-align:center;background:#fff;padding:6px;border-radius:8px;color:#0f172a;"><img src="${menuQrDataUrl}" width="72" height="72" /><div style="font-size:8px;font-weight:bold;">Peça pelo QR Code</div></div>` : ""}
</div>`;

      const catSections = orderedCategories
        .map((c) => {
          const catProds = orderedProducts.filter((p) => p.categoryId === c.id && p.status === "Active");
          if (catProds.length === 0) return "";
          const cardsHtml = catProds
            .map((p) => {
              const photo = p.primaryImageUrl ? imgMap.get(p.primaryImageUrl) || "" : "";
              const groupNames = p.modifierGroupIds
                .map((gid) => modifierGroups.find((g) => g.id === gid)?.name)
                .filter(Boolean);
              const pricesHtml =
                p.variations.length > 0
                  ? p.variations
                      .map((v) => `<span style="display:inline-block;margin-right:8px;"><strong>${escHtml(v.variationName)}:</strong> ${fmtMoney(v.price)}</span>`)
                      .join("")
                  : p.promotionalPrice && p.promotionalPrice < p.basePrice
                    ? `<span style="text-decoration:line-through;color:#94a3b8;">${fmtMoney(p.basePrice)}</span> <strong style="color:#be123c;font-size:12px;">${fmtMoney(p.promotionalPrice)}</strong>`
                    : `<strong style="font-size:12px;color:#0f766e;">${fmtMoney(p.basePrice)}</strong>`;

              return `
<div class="card" style="margin-bottom:0;">
  <div class="prod-top" style="border-bottom:none;padding-bottom:0;margin-bottom:0;">
    ${photo ? `<img src="${escHtml(photo)}" class="photo" />` : `<div class="no-photo">Sem Foto</div>`}
    <div style="flex:1;">
      <div style="font-size:12px;font-weight:bold;">
        ${escHtml(p.name)}
        ${p.isFeatured ? `<span class="badge badge-warn">★ Mais Vendido</span>` : ""}
        ${p.isNew ? `<span class="badge badge-ok">Novidade</span>` : ""}
      </div>
      ${p.description ? `<div style="color:#475569;font-size:10px;margin-top:2px;">${escHtml(p.description)}</div>` : ""}
      <div style="margin-top:4px;font-size:11px;">${pricesHtml}</div>
      ${p.servesPeopleCount ? `<div style="font-size:9px;color:#64748b;margin-top:2px;">Serve ${p.servesPeopleCount} pessoa(s) ${p.dietaryTagsCsv ? `• ${escHtml(p.dietaryTagsCsv)}` : ""}</div>` : ""}
      ${groupNames.length > 0 ? `<div style="font-size:9px;color:#6d28d9;margin-top:2px;">Opções: ${escHtml(groupNames.join(", "))}</div>` : ""}
    </div>
  </div>
</div>`;
            })
            .join("");

          return `<div class="cat-banner">${escHtml(c.name)}${c.description ? ` — <span style="font-weight:normal;font-size:11px;color:#475569;">${escHtml(c.description)}</span>` : ""}</div><div class="catalog-grid">${cardsHtml}</div>`;
        })
        .join("");

      const combosHtml =
        combos.filter((x) => x.isActive).length > 0
          ? `<div class="cat-banner">Combos Especiais (${combos.filter((x) => x.isActive).length})</div><div class="catalog-grid">` +
            combos
              .filter((x) => x.isActive)
              .map((c) => {
                const info = computeComboSummary(c);
                const photo = c.primaryImageUrl ? imgMap.get(c.primaryImageUrl) || "" : "";
                return `
<div class="card" style="margin-bottom:0;">
  <div class="prod-top" style="border-bottom:none;padding-bottom:0;margin-bottom:0;">
    ${photo ? `<img src="${escHtml(photo)}" class="photo" />` : `<div class="no-photo">Combo</div>`}
    <div style="flex:1;">
      <div style="font-size:12px;font-weight:bold;">${escHtml(c.name)} <span class="badge badge-err">-${info.discountPct}%</span></div>
      <div style="color:#475569;font-size:10px;margin-top:2px;">${escHtml(c.description || info.itemRows.map((r) => `${r.quantity}x ${r.productName}`).join(" + "))}</div>
      <div style="margin-top:4px;font-size:11px;"><strong style="color:#0f766e;">${fmtMoney(c.comboPrice)}</strong> <span style="color:#64748b;font-size:10px;">(Economia ${fmtMoney(info.savings)})</span></div>
    </div>
  </div>
</div>`;
              })
              .join("") +
            `</div>`
          : "";

      openPrintWindow(`${storeTradeName} — Catálogo Completo PDF`, headerHtml + catSections + combosHtml);
      onNotify("Catálogo completo em PDF pronto para salvar/imprimir!");
    } finally {
      setBusyExport(false);
    }
  }

  const singleProd = orderedProducts.find((p) => p.id === selectedProductId) || orderedProducts[0];
  const singleSheet = singleProd ? sheetByProductId.get(singleProd.id) : undefined;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/75 p-4 backdrop-blur-sm">
      <div className="flex max-h-[92vh] w-full max-w-4xl flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200 px-6 py-4">
          <div className="flex items-center gap-2">
            <FileSpreadsheet className="h-5 w-5 text-emerald-600" />
            <div>
              <h2 className="text-base font-extrabold text-slate-900">Exportar Fichas Técnicas &amp; Catálogo Completo</h2>
              <p className="text-xs text-slate-500">
                {storeTradeName} • {products.length} produtos, {techSheets.length} fichas e {combos.length} combos com fotos
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Fechar janela de exportação"
            className="rounded-lg border border-slate-200 p-1.5 text-slate-500 hover:bg-slate-100"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="flex flex-wrap gap-2 border-b border-slate-100 bg-slate-50 px-6 py-3">
          {[
            { key: "all_sheets", label: "1. Ficha Técnica Completa (Produtos + Combos)" },
            { key: "single_sheet", label: "2. Ficha Separada por Produto" },
            { key: "full_catalog", label: "3. Gerar Catálogo Completo em PDF" }
          ].map((t) => (
            <button
              key={t.key}
              type="button"
              onClick={() => setMode(t.key as ExportTabMode)}
              className={`rounded-xl px-3.5 py-2 text-xs font-extrabold transition ${
                mode === t.key ? "bg-slate-900 text-white shadow-sm" : "border border-slate-200 bg-white text-slate-700 hover:bg-slate-100"
              }`}
            >
              {t.label}
            </button>
          ))}
        </div>

        <div className="flex-1 space-y-5 overflow-y-auto p-6 text-xs">
          {mode === "all_sheets" && (
            <>
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4 space-y-3">
                <p className="font-extrabold text-slate-900">Escolha o que incluir na exportação detalhada:</p>
                <div className="flex flex-wrap gap-2">
                  {[
                    { key: "both", label: `Todos os Produtos + Combos Juntos (${products.length} prod. + ${combos.length} combos)` },
                    { key: "products", label: `Somente Produtos (${products.length})` },
                    { key: "combos", label: `Somente Combos (${combos.length})` }
                  ].map((opt) => (
                    <button
                      key={opt.key}
                      type="button"
                      onClick={() => setBundleFilter(opt.key as BundleFilter)}
                      className={`rounded-xl border px-3 py-2 font-bold ${
                        bundleFilter === opt.key ? "border-emerald-600 bg-emerald-600 text-white" : "border-slate-300 bg-white text-slate-700"
                      }`}
                    >
                      {opt.label}
                    </button>
                  ))}
                </div>
                <p className="text-[11px] text-slate-600">
                  Inclui foto de cada produto/combo, categoria, SKU, preços por tamanho, custos de insumos/embalagens, perdas, lucro, margem % e alertas.
                </p>
              </div>

              <div className="flex flex-wrap gap-3">
                <button
                  type="button"
                  disabled={busyExport}
                  onClick={() => void handleExportTechSheetsPdf(false)}
                  className="inline-flex items-center gap-2 rounded-xl bg-rose-600 px-4 py-2.5 text-xs font-extrabold text-white shadow-sm hover:bg-rose-700 disabled:opacity-50"
                >
                  <Printer className="h-4 w-4" /> Exportar Tudo em PDF (Contínuo)
                </button>
                <button
                  type="button"
                  disabled={busyExport}
                  onClick={() => void handleExportTechSheetsPdf(true)}
                  className="inline-flex items-center gap-2 rounded-xl bg-indigo-600 px-4 py-2.5 text-xs font-extrabold text-white shadow-sm hover:bg-indigo-700 disabled:opacity-50"
                >
                  <Layers className="h-4 w-4" /> Exportar PDF Separado (1 Produto por Página)
                </button>
                <button
                  type="button"
                  disabled={busyExport}
                  onClick={() => void handleExportExcelWithPhotos()}
                  className="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2.5 text-xs font-extrabold text-white shadow-sm hover:bg-emerald-700 disabled:opacity-50"
                >
                  <FileSpreadsheet className="h-4 w-4" /> Exportar Excel com Fotos (.xls)
                </button>
              </div>
            </>
          )}

          {mode === "single_sheet" && (
            <div className="space-y-4">
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4 space-y-3">
                <label className="block font-extrabold text-slate-900">
                  Selecione o Produto para exportar a Ficha Técnica individual:
                  <select
                    value={singleProd?.id || ""}
                    onChange={(e) => setSelectedProductId(e.target.value)}
                    className="mt-1.5 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-semibold text-slate-900"
                  >
                    {orderedProducts.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name} — {fmtMoney(p.basePrice)} ({sheetByProductId.has(p.id) ? "Com ficha" : "Sem ingredientes"})
                      </option>
                    ))}
                  </select>
                </label>

                {singleProd && (
                  <div className="flex flex-wrap items-center gap-4 rounded-xl border border-slate-200 bg-white p-3">
                    {resolveMediaUrl(singleProd.primaryImageUrl) ? (
                      <img
                        src={resolveMediaUrl(singleProd.primaryImageUrl)!}
                        alt={singleProd.name}
                        className="h-16 w-16 rounded-xl border border-slate-200 object-cover"
                      />
                    ) : (
                      <div className="flex h-16 w-16 items-center justify-center rounded-xl border border-dashed border-slate-300 text-[10px] text-slate-400">
                        Sem foto
                      </div>
                    )}
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-extrabold text-slate-900">{singleProd.name}</p>
                      <p className="text-slate-600">{singleProd.description || "Sem descrição"}</p>
                      <p className="mt-1 font-mono text-[11px]">
                        Venda: <strong>{fmtMoney(singleProd.basePrice)}</strong>
                        {singleSheet && (
                          <> · Custo CMV: <strong className="text-rose-700">{fmtMoney(singleSheet.costPerPortion)}</strong> · Lucro: <strong className="text-emerald-700">{fmtMoney(singleSheet.profitPerUnit)} ({singleSheet.marginPercent.toFixed(1)}%)</strong></>
                        )}
                      </p>
                    </div>
                  </div>
                )}
              </div>

              {singleProd && (
                <div className="flex flex-wrap gap-3">
                  <button
                    type="button"
                    disabled={busyExport}
                    onClick={() => void handleExportTechSheetsPdf(false, singleProd.id)}
                    className="inline-flex items-center gap-2 rounded-xl bg-rose-600 px-4 py-2.5 text-xs font-extrabold text-white hover:bg-rose-700 disabled:opacity-50"
                  >
                    <FileText className="h-4 w-4" /> Baixar PDF deste Produto
                  </button>
                  <button
                    type="button"
                    disabled={busyExport}
                    onClick={() => void handleExportExcelWithPhotos(singleProd.id)}
                    className="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2.5 text-xs font-extrabold text-white hover:bg-emerald-700 disabled:opacity-50"
                  >
                    <Download className="h-4 w-4" /> Baixar Excel com Foto deste Produto (.xls)
                  </button>
                </div>
              )}
            </div>
          )}

          {mode === "full_catalog" && (
            <div className="space-y-4">
              <div className="rounded-2xl border border-amber-200 bg-amber-50/70 p-4 text-slate-800 space-y-2">
                <p className="flex items-center gap-1.5 text-sm font-extrabold text-amber-950">
                  <Sparkles className="h-4 w-4 text-amber-600" /> Catálogo Profissional Completo em PDF (com Fotos &amp; QR Code)
                </p>
                <p>
                  Gera um catálogo completo pronto para imprimir ou enviar para clientes no WhatsApp: capa com logo da loja, endereço, WhatsApp, <strong>QR Code direto para /loja/{slug}</strong>, todas as categorias na ordem da vitrine, fotos dos produtos, tamanhos/variações, opcionais e seção de Combos Especiais.
                </p>
              </div>

              <div className="flex flex-wrap gap-3">
                <button
                  type="button"
                  disabled={busyExport}
                  onClick={() => void handleExportFullCatalogPdf()}
                  className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-5 py-3 text-xs font-extrabold text-white shadow-sm hover:bg-slate-800 disabled:opacity-50"
                >
                  <Printer className="h-4 w-4" /> {busyExport ? "Preparando fotos e PDF..." : "Gerar Catálogo Completo em PDF"}
                </button>
                <button
                  type="button"
                  disabled={busyExport}
                  onClick={() => void handleExportExcelWithPhotos()}
                  className="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-3 text-xs font-extrabold text-white shadow-sm hover:bg-emerald-700 disabled:opacity-50"
                >
                  <FileSpreadsheet className="h-4 w-4" /> Exportar Catálogo &amp; Custos para Excel (.xls)
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
