"use client";

import { Download, ExternalLink, Image as ImageIcon, Printer, QrCode, X } from "lucide-react";
import Link from "next/link";
import QRCode from "qrcode";
import { useEffect, useState } from "react";
import { resolveMediaUrl } from "@/lib/api";
import { AdminCategoryDto, AdminProductDto } from "./AdminCatalogTab";

type FlyerFormat = "feed" | "stories" | "a4";

async function toSafeDataUrl(url?: string | null): Promise<string | null> {
  const resolved = resolveMediaUrl(url);
  if (!resolved) return null;
  if (resolved.startsWith("data:")) return resolved;
  try {
    const resp = await fetch(resolved);
    if (!resp.ok) return null;
    const blob = await resp.blob();
    return await new Promise<string | null>((resolve) => {
      const reader = new FileReader();
      reader.onloadend = () => resolve(typeof reader.result === "string" ? reader.result : null);
      reader.onerror = () => resolve(null);
      reader.readAsDataURL(blob);
    });
  } catch {
    return null;
  }
}

async function loadImageObj(src: string): Promise<HTMLImageElement | null> {
  return await new Promise((resolve) => {
    const img = new Image();
    img.crossOrigin = "anonymous";
    img.onload = () => resolve(img);
    img.onerror = () => resolve(null);
    img.src = src;
  });
}

function drawContainImage(
  ctx: CanvasRenderingContext2D,
  img: HTMLImageElement,
  boxX: number,
  boxY: number,
  boxW: number,
  boxH: number
) {
  const scale = Math.min(boxW / img.width, boxH / img.height);
  const drawW = img.width * scale;
  const drawH = img.height * scale;
  const dx = boxX + (boxW - drawW) / 2;
  const dy = boxY + (boxH - drawH) / 2;
  ctx.drawImage(img, dx, dy, drawW, drawH);
}

export function AdminFlyerModal({
  open,
  slug,
  storeTradeName,
  logoUrl,
  slogan,
  primaryColorHex,
  accentColorHex,
  storeAddress,
  whatsAppNumber,
  instagramHandle,
  categories,
  products,
  onClose
}: {
  open: boolean;
  slug: string;
  storeTradeName: string;
  logoUrl?: string | null;
  slogan?: string | null;
  primaryColorHex: string;
  accentColorHex: string;
  storeAddress?: string;
  whatsAppNumber?: string;
  instagramHandle?: string;
  categories: AdminCategoryDto[];
  products: AdminProductDto[];
  onClose: () => void;
}) {
  const [format, setFormat] = useState<FlyerFormat>("feed");
  const [showDescriptions, setShowDescriptions] = useState(true);
  const [includeQr, setIncludeQr] = useState(true);
  const [qrDataUrl, setQrDataUrl] = useState("");
  const [busyPng, setBusyPng] = useState(false);

  useEffect(() => {
    if (!open) return;
    const menuUrl =
      typeof window !== "undefined" ? `${window.location.origin}/loja/${slug}` : `https://pitako.com.br/loja/${slug}`;
    QRCode.toDataURL(menuUrl, { width: 180, margin: 1 })
      .then(setQrDataUrl)
      .catch(() => setQrDataUrl(""));
  }, [open, slug]);

  if (!open) return null;

  const sortedCategories = [...categories].sort((a, b) => a.displayOrder - b.displayOrder);
  const activeProducts = products.filter((p) => p.status === "Active");

  async function downloadFlyerPng() {
    setBusyPng(true);
    try {
      const isStories = format === "stories";
      const width = 1080;
      const maxItems = isStories ? 9 : 12;
      const featured = activeProducts.slice(0, maxItems);
      const cols = isStories ? 3 : 3;
      const rows = Math.max(2, Math.ceil(featured.length / cols));
      const cardH = isStories ? 360 : 300;
      const height = Math.max(isStories ? 1920 : 1350, 380 + rows * (cardH + 24) + 120);

      const canvas = document.createElement("canvas");
      canvas.width = width;
      canvas.height = height;
      const ctx = canvas.getContext("2d");
      if (!ctx) return;

      // Fundo na cor primária da loja
      ctx.fillStyle = primaryColorHex || "#4C1D95";
      ctx.fillRect(0, 0, width, height);

      // Faixa superior
      ctx.fillStyle = "rgba(255,255,255,0.08)";
      ctx.fillRect(0, 0, width, 240);

      // Logo oficial da loja preservando proporção
      let titleX = 56;
      const safeLogo = await toSafeDataUrl(logoUrl);
      if (safeLogo) {
        const logoImg = await loadImageObj(safeLogo);
        if (logoImg) {
          ctx.fillStyle = "#FFFFFF";
          ctx.beginPath();
          ctx.roundRect(52, 46, 144, 144, 24);
          ctx.fill();
          drawContainImage(ctx, logoImg, 64, 58, 120, 120);
          titleX = 220;
        }
      }

      ctx.fillStyle = "#FFFFFF";
      ctx.font = "bold 46px system-ui, -apple-system, sans-serif";
      ctx.fillText(storeTradeName.slice(0, 26), titleX, 108);

      ctx.fillStyle = accentColorHex || "#F59E0B";
      ctx.font = "bold 22px system-ui, -apple-system, sans-serif";
      ctx.fillText((slogan || "Cardápio Digital & Delivery Próprio").slice(0, 56), titleX, 148);

      ctx.fillStyle = "rgba(255,255,255,0.88)";
      ctx.font = "18px system-ui, -apple-system, sans-serif";
      const contactLine = [
        whatsAppNumber ? `WhatsApp: ${whatsAppNumber}` : "",
        instagramHandle || "",
        storeAddress || ""
      ]
        .filter(Boolean)
        .join(" · ");
      if (contactLine) ctx.fillText(contactLine.slice(0, 62), titleX, 184);

      if (includeQr && qrDataUrl) {
        const qrImg = await loadImageObj(qrDataUrl);
        if (qrImg) {
          ctx.fillStyle = "#FFFFFF";
          ctx.beginPath();
          ctx.roundRect(width - 196, 44, 144, 144, 18);
          ctx.fill();
          drawContainImage(ctx, qrImg, width - 186, 54, 124, 124);
        }
      }

      // Container branco interno dos produtos
      ctx.fillStyle = "#F8FAFC";
      ctx.beginPath();
      ctx.roundRect(40, 256, width - 80, height - 340, 28);
      ctx.fill();

      const cardW = (width - 80 - 64 - (cols - 1) * 20) / cols;
      for (let i = 0; i < featured.length; i++) {
        const p = featured[i];
        const col = i % cols;
        const row = Math.floor(i / cols);
        const cx = 72 + col * (cardW + 20);
        const cy = 288 + row * (cardH + 22);

        ctx.fillStyle = "#FFFFFF";
        ctx.beginPath();
        ctx.roundRect(cx, cy, cardW, cardH, 18);
        ctx.fill();
        ctx.strokeStyle = "#E2E8F0";
        ctx.lineWidth = 2;
        ctx.stroke();

        // Moldura da imagem do produto SEM CORTES (contain)
        const imgBoxH = isStories ? 190 : 152;
        ctx.fillStyle = "#FFFFFF";
        ctx.beginPath();
        ctx.roundRect(cx + 12, cy + 12, cardW - 24, imgBoxH, 12);
        ctx.fill();

        const prodDataUrl = await toSafeDataUrl(p.primaryImageUrl);
        if (prodDataUrl) {
          const prodImg = await loadImageObj(prodDataUrl);
          if (prodImg) {
            drawContainImage(ctx, prodImg, cx + 16, cy + 16, cardW - 32, imgBoxH - 8);
          }
        } else {
          ctx.fillStyle = "#CBD5E1";
          ctx.font = "bold 18px system-ui, sans-serif";
          ctx.fillText("Sem Imagem", cx + cardW / 2 - 52, cy + imgBoxH / 2 + 12);
        }

        // Nome do produto
        ctx.fillStyle = "#0F172A";
        ctx.font = "bold 20px system-ui, sans-serif";
        ctx.fillText(p.name.slice(0, 24), cx + 16, cy + imgBoxH + 42);

        if (showDescriptions && p.description) {
          ctx.fillStyle = "#64748B";
          ctx.font = "14px system-ui, sans-serif";
          ctx.fillText(p.description.slice(0, 34), cx + 16, cy + imgBoxH + 66);
        }

        // Faixa de preço
        const priceY = cy + cardH - 52;
        ctx.fillStyle = primaryColorHex || "#0F172A";
        ctx.beginPath();
        ctx.roundRect(cx + 14, priceY, cardW - 28, 38, 10);
        ctx.fill();

        ctx.fillStyle = "#FFFFFF";
        ctx.font = "bold 19px monospace";
        const lowestPrice =
          p.variations.length > 0 ? Math.min(...p.variations.map((v) => v.price)) : p.promotionalPrice ?? p.basePrice;
        const priceLabel =
          p.variations.length > 1
            ? `A partir de R$ ${lowestPrice.toFixed(2).replace(".", ",")}`
            : `R$ ${lowestPrice.toFixed(2).replace(".", ",")}`;
        ctx.fillText(priceLabel, cx + 26, priceY + 26);
      }

      // Rodapé com link da loja
      ctx.fillStyle = "#FFFFFF";
      ctx.font = "bold 20px system-ui, sans-serif";
      ctx.fillText(`Peça online: /loja/${slug}`, 56, height - 38);

      const a = document.createElement("a");
      a.href = canvas.toDataURL("image/png");
      a.download = `panfleto-${slug}-${format}.png`;
      a.click();
    } finally {
      setBusyPng(false);
    }
  }

  async function handlePrintCleanFlyer() {
    const popup = window.open("", "_blank", "width=1020,height=800");
    if (!popup) {
      window.print();
      return;
    }
    const esc = (v: string | number | null | undefined) =>
      String(v ?? "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");

    const safeLogo = await toSafeDataUrl(logoUrl);
    const categoriesHtml = (
      await Promise.all(
        sortedCategories.map(async (cat) => {
          const catProds = activeProducts.filter((p) => p.categoryId === cat.id);
          if (catProds.length === 0) return "";
          const cards = (
            await Promise.all(
              catProds.map(async (p) => {
                const safeImg = await toSafeDataUrl(p.primaryImageUrl);
                const priceLines =
                  p.variations.length > 0
                    ? p.variations
                        .map(
                          (v) =>
                            `<div class="price-row"><span>${esc(v.variationName)}</span><span>R$ ${v.price
                              .toFixed(2)
                              .replace(".", ",")}</span></div>`
                        )
                        .join("")
                    : `<div class="price-single">R$ ${(p.promotionalPrice ?? p.basePrice).toFixed(2).replace(".", ",")}</div>`;

                return `<div class="card">
                  <div class="img-wrap">
                    ${safeImg ? `<img src="${esc(safeImg)}" alt="${esc(p.name)}" />` : `<div class="no-img">Produto</div>`}
                  </div>
                  <div class="prod-name">${esc(p.name)}</div>
                  ${showDescriptions && p.description ? `<div class="prod-desc">${esc(p.description)}</div>` : ""}
                  <div class="prices">${priceLines}</div>
                </div>`;
              })
            )
          ).join("");

          return `<section class="cat-box">
            <div class="cat-pill">${esc(cat.name)}</div>
            ${cat.description ? `<p class="cat-desc">${esc(cat.description)}</p>` : ""}
            <div class="grid">${cards}</div>
          </section>`;
        })
      )
    ).join("");

    popup.document.write(`<!DOCTYPE html><html lang="pt-BR"><head><meta charset="utf-8"><title>${esc(
      storeTradeName
    )} | Panfleto Oficial</title><style>
      @page { size: A4; margin: 10mm; }
      body { font-family: Arial, sans-serif; margin: 0; color: #0f172a; background: #fff; }
      .sheet { background: ${esc(primaryColorHex || "#4C1D95")}; border-radius: 18px; padding: 18px; color: #fff; position: relative; }
      .hdr { display: flex; justify-content: space-between; align-items: center; gap: 16px; border-bottom: 1px solid rgba(255,255,255,0.22); padding-bottom: 14px; margin-bottom: 16px; }
      .hdr-left { display: flex; align-items: center; gap: 14px; }
      .logo { width: 68px; height: 68px; object-fit: contain; background: #fff; border-radius: 14px; padding: 4px; }
      .title { font-size: 22px; font-weight: 900; margin: 0; }
      .slogan { font-size: 12px; font-weight: 700; color: ${esc(accentColorHex || "#F59E0B")}; margin-top: 2px; text-transform: uppercase; }
      .contact { font-size: 10px; opacity: 0.9; margin-top: 4px; }
      .qr { background: #fff; padding: 6px; border-radius: 10px; text-align: center; color: #0f172a; font-size: 9px; font-weight: bold; }
      .cat-box { background: #fff; color: #0f172a; border-radius: 14px; padding: 14px; margin-bottom: 14px; break-inside: avoid; }
      .cat-pill { display: inline-block; background: ${esc(primaryColorHex || "#4C1D95")}; color: #fff; padding: 4px 14px; border-radius: 999px; font-size: 11px; font-weight: 800; text-transform: uppercase; }
      .cat-desc { font-size: 10px; color: #64748b; margin: 4px 0 8px; }
      .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin-top: 10px; }
      .card { border: 1px solid #e2e8f0; border-radius: 12px; padding: 8px; display: flex; flex-direction: column; justify-content: space-between; background: #f8fafc; break-inside: avoid; }
      .img-wrap { height: 96px; width: 100%; background: #fff; border-radius: 8px; border: 1px solid #f1f5f9; display: flex; align-items: center; justify-content: center; padding: 4px; box-sizing: border-box; margin-bottom: 6px; }
      .img-wrap img { max-width: 100%; max-height: 100%; width: auto; height: auto; object-fit: contain; }
      .no-img { font-size: 10px; color: #94a3b8; }
      .prod-name { font-size: 11px; font-weight: 800; color: #0f172a; margin-bottom: 2px; }
      .prod-desc { font-size: 9.5px; color: #475569; margin-bottom: 6px; line-height: 1.3; }
      .prices { margin-top: auto; }
      .price-row { display: flex; justify-content: space-between; background: #0f172a; color: #fff; padding: 3px 6px; border-radius: 5px; font-size: 10px; font-weight: 700; margin-top: 2px; font-family: monospace; }
      .price-single { background: #0f172a; color: #fff; padding: 4px 8px; border-radius: 6px; font-size: 11px; font-weight: 800; text-align: center; font-family: monospace; }
    </style></head><body>
      <div class="sheet">
        <div class="hdr">
          <div class="hdr-left">
            ${safeLogo ? `<img src="${esc(safeLogo)}" class="logo" alt="${esc(storeTradeName)}" />` : ""}
            <div>
              <h1 class="title">${esc(storeTradeName)}</h1>
              <div class="slogan">${esc(slogan || "Cardápio Digital & Delivery Próprio")}</div>
              <div class="contact">${esc([storeAddress, whatsAppNumber ? `WhatsApp: ${whatsAppNumber}` : "", instagramHandle].filter(Boolean).join(" · "))}</div>
            </div>
          </div>
          ${includeQr && qrDataUrl ? `<div class="qr"><img src="${qrDataUrl}" width="64" height="64" /><div>Peça Online</div></div>` : ""}
        </div>
        ${categoriesHtml}
      </div>
      <script>window.onload=function(){setTimeout(function(){window.print();},260);};</script>
    </body></html>`);
    popup.document.close();
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/80 p-3 backdrop-blur-sm">
      <div className="flex max-h-[94vh] w-full max-w-5xl flex-col overflow-hidden rounded-2xl border border-slate-700 bg-slate-950 shadow-2xl">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-800 px-5 py-3 text-white print:hidden">
          <div>
            <p className="text-xs font-extrabold">{storeTradeName} · Panfleto de Divulgação Oficial</p>
            <p className="text-[11px] text-slate-400">Imagens completas preservando proporção, com logo, cores e QR Code exclusivos da loja.</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <select
              value={format}
              onChange={(e) => setFormat(e.target.value as FlyerFormat)}
              aria-label="Formato do panfleto"
              className="rounded-xl border border-slate-700 bg-slate-900 px-2.5 py-1.5 text-xs font-bold text-white"
            >
              <option value="feed">Post Digital (1080×1350)</option>
              <option value="stories">Stories / WhatsApp (1080×1920)</option>
              <option value="a4">Impressão A4</option>
            </select>
            <label className="inline-flex items-center gap-1.5 rounded-xl bg-white/10 px-2.5 py-1.5 text-[11px] font-bold">
              <input type="checkbox" checked={showDescriptions} onChange={(e) => setShowDescriptions(e.target.checked)} />
              Descrições
            </label>
            <label className="inline-flex items-center gap-1.5 rounded-xl bg-white/10 px-2.5 py-1.5 text-[11px] font-bold">
              <QrCode className="h-3.5 w-3.5" />
              <input type="checkbox" checked={includeQr} onChange={(e) => setIncludeQr(e.target.checked)} />
              QR Code
            </label>
            <button
              type="button"
              onClick={() => void handlePrintCleanFlyer()}
              className="inline-flex items-center gap-1.5 rounded-xl bg-amber-400 px-3 py-1.5 text-xs font-extrabold text-slate-950 hover:bg-amber-300"
            >
              <Printer className="h-3.5 w-3.5" /> Imprimir / Salvar PDF
            </button>
            <button
              type="button"
              disabled={busyPng}
              onClick={() => void downloadFlyerPng()}
              className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-500 px-3 py-1.5 text-xs font-extrabold text-white hover:bg-emerald-400 disabled:opacity-50"
            >
              <Download className="h-3.5 w-3.5" /> {busyPng ? "Gerando PNG..." : "Baixar Imagem PNG"}
            </button>
            <Link
              href={`/loja/${slug}`}
              target="_blank"
              className="inline-flex items-center gap-1 rounded-xl bg-white/10 px-3 py-1.5 text-xs font-bold text-white hover:bg-white/20"
            >
              Ver Cardápio Online <ExternalLink className="h-3.5 w-3.5" />
            </Link>
            <button type="button" onClick={onClose} aria-label="Fechar panfleto" className="rounded-lg bg-white/10 p-1.5 text-white hover:bg-white/20">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        <div className="flex-1 overflow-y-auto p-6">
          <div
            style={{ backgroundColor: primaryColorHex, borderColor: accentColorHex }}
            className="mx-auto max-w-4xl rounded-3xl border-2 p-6 text-white shadow-xl"
          >
            <div className="flex flex-wrap items-center justify-between gap-4 border-b border-white/20 pb-5">
              <div className="flex items-center gap-4">
                {resolveMediaUrl(logoUrl) && (
                  <img
                    src={resolveMediaUrl(logoUrl)!}
                    alt={storeTradeName}
                    className="h-16 w-16 rounded-2xl bg-white/95 object-contain p-1.5 shadow"
                  />
                )}
                <div>
                  <h1 className="text-2xl font-black tracking-tight">{storeTradeName}</h1>
                  <p style={{ color: accentColorHex }} className="text-xs font-extrabold uppercase tracking-wider">
                    {slogan || "Cardápio Digital & Delivery Próprio"}
                  </p>
                  {(storeAddress || whatsAppNumber || instagramHandle) && (
                    <p className="mt-1 text-[11px] text-white/85">
                      {[storeAddress, whatsAppNumber ? `WhatsApp: ${whatsAppNumber}` : "", instagramHandle]
                        .filter(Boolean)
                        .join(" · ")}
                    </p>
                  )}
                </div>
              </div>
              <div className="flex items-center gap-3 rounded-2xl bg-white/15 px-4 py-2 text-right text-xs">
                <div>
                  <p className="font-bold">Peça Online Agora:</p>
                  <p className="font-mono text-amber-300">/loja/{slug}</p>
                </div>
                {includeQr && qrDataUrl && (
                  <img src={qrDataUrl} alt="QR Code da loja" className="h-14 w-14 rounded-lg bg-white p-1" />
                )}
              </div>
            </div>

            <div className="mt-6 space-y-6">
              {sortedCategories.map((cat) => {
                const catProducts = activeProducts.filter((p) => p.categoryId === cat.id);
                if (catProducts.length === 0) return null;
                return (
                  <div key={cat.id} className="rounded-2xl bg-white p-4 text-slate-900 shadow-lg">
                    <div className="mb-3 text-center">
                      <span
                        style={{ backgroundColor: primaryColorHex }}
                        className="inline-block rounded-full px-5 py-1 text-xs font-black uppercase text-white"
                      >
                        {cat.name}
                      </span>
                      {cat.description && <p className="mt-1 text-[11px] text-slate-500">{cat.description}</p>}
                    </div>

                    <div className="grid gap-3 sm:grid-cols-2 md:grid-cols-3">
                      {catProducts.map((p) => (
                        <div key={p.id} className="flex flex-col justify-between rounded-xl border border-slate-200 bg-slate-50 p-3 text-center">
                          <div className="mb-2 flex h-32 w-full items-center justify-center overflow-hidden rounded-lg border border-slate-100 bg-white p-1.5">
                            {resolveMediaUrl(p.primaryImageUrl) ? (
                              <img
                                src={resolveMediaUrl(p.primaryImageUrl)!}
                                alt={p.name}
                                className="h-full w-full object-contain"
                              />
                            ) : (
                              <div className="flex flex-col items-center gap-1 text-[11px] text-slate-400">
                                <ImageIcon className="h-5 w-5" />
                                Sem foto
                              </div>
                            )}
                          </div>
                          <div>
                            <p
                              style={{ backgroundColor: accentColorHex }}
                              className="rounded-md px-2 py-1 text-xs font-extrabold text-slate-950"
                            >
                              {p.name}
                            </p>
                            {showDescriptions && p.description && (
                              <p className="mt-1.5 line-clamp-2 text-[11px] text-slate-600">{p.description}</p>
                            )}
                          </div>
                          <div className="mt-2.5 space-y-1 text-[11px]">
                            {p.variations.length > 0 ? (
                              p.variations.map((v) => (
                                <div key={v.id} className="flex justify-between rounded bg-slate-900 px-2 py-1 font-mono font-bold text-white">
                                  <span>{v.variationName}</span>
                                  <span>R$ {v.price.toFixed(2).replace(".", ",")}</span>
                                </div>
                              ))
                            ) : (
                              <div className="rounded bg-slate-900 px-2 py-1 font-mono text-xs font-extrabold text-white">
                                R$ {(p.promotionalPrice ?? p.basePrice).toFixed(2).replace(".", ",")}
                              </div>
                            )}
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
