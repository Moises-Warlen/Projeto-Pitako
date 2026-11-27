"use client";

import { Download, ExternalLink, Printer, X } from "lucide-react";
import Link from "next/link";
import { resolveMediaUrl } from "@/lib/api";
import { AdminCategoryDto, AdminProductDto } from "./AdminCatalogTab";

export function AdminFlyerModal({
  open,
  slug,
  storeTradeName,
  logoUrl,
  slogan,
  primaryColorHex,
  accentColorHex,
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
  categories: AdminCategoryDto[];
  products: AdminProductDto[];
  onClose: () => void;
}) {
  if (!open) return null;

  const sortedCategories = [...categories].sort((a, b) => a.displayOrder - b.displayOrder);

  function downloadFlyerPng() {
    const canvas = document.createElement("canvas");
    const width = 1080;
    const rows = Math.max(4, products.slice(0, 18).length);
    const height = 280 + Math.ceil(rows / 3) * 200;
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    ctx.fillStyle = primaryColorHex || "#4C1D95";
    ctx.fillRect(0, 0, width, height);

    ctx.fillStyle = "#FFFFFF";
    ctx.font = "bold 44px system-ui, sans-serif";
    ctx.fillText(storeTradeName.toUpperCase(), 60, 90);

    ctx.fillStyle = accentColorHex || "#F59E0B";
    ctx.font = "bold 22px system-ui, sans-serif";
    ctx.fillText(slogan || "Cardápio Digital & Delivery Próprio", 60, 132);

    ctx.fillStyle = "rgba(255,255,255,0.96)";
    ctx.fillRect(48, 175, width - 96, height - 220);

    let x = 76;
    let y = 230;
    products.slice(0, 18).forEach((p, idx) => {
      ctx.fillStyle = "#1E293B";
      ctx.font = "bold 20px system-ui, sans-serif";
      ctx.fillText(p.name.slice(0, 24), x, y);

      ctx.fillStyle = "#0F766E";
      ctx.font = "bold 22px monospace";
      const priceText = p.variations.length > 0
        ? p.variations.slice(0, 2).map((v) => `${v.variationName.split(" ")[0]} R$${v.price.toFixed(2).replace(".", ",")}`).join(" | ")
        : `R$ ${p.basePrice.toFixed(2).replace(".", ",")}`;
      ctx.fillText(priceText, x, y + 32);

      if ((idx + 1) % 3 === 0) {
        x = 76;
        y += 170;
      } else {
        x += 320;
      }
    });

    const a = document.createElement("a");
    a.href = canvas.toDataURL("image/png");
    a.download = `panfleto-${slug}.png`;
    a.click();
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/80 p-3 backdrop-blur-sm">
      <div className="flex max-h-[94vh] w-full max-w-5xl flex-col overflow-hidden rounded-2xl border border-slate-700 bg-slate-950 shadow-2xl">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-800 px-5 py-3 text-white print:hidden">
          <p className="text-xs font-extrabold">{storeTradeName} • Panfleto Oficial</p>
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => window.print()}
              className="inline-flex items-center gap-1.5 rounded-xl bg-amber-400 px-3 py-1.5 text-xs font-extrabold text-slate-950 hover:bg-amber-300"
            >
              <Printer className="h-3.5 w-3.5" /> Imprimir / Salvar PDF
            </button>
            <button
              type="button"
              onClick={downloadFlyerPng}
              className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-500 px-3 py-1.5 text-xs font-extrabold text-white hover:bg-emerald-400"
            >
              <Download className="h-3.5 w-3.5" /> Baixar Arte em Imagem (PNG)
            </button>
            <Link
              href={`/loja/${slug}`}
              target="_blank"
              className="inline-flex items-center gap-1 rounded-xl bg-white/10 px-3 py-1.5 text-xs font-bold text-white hover:bg-white/20"
            >
              Ver Cardápio Online <ExternalLink className="h-3.5 w-3.5" />
            </Link>
            <button type="button" onClick={onClose} className="rounded-lg bg-white/10 p-1.5 text-white hover:bg-white/20">
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
                </div>
              </div>
              <div className="rounded-2xl bg-white/15 px-4 py-2 text-right text-xs">
                <p className="font-bold">Peça Online Agora:</p>
                <p className="font-mono text-amber-300">/loja/{slug}</p>
              </div>
            </div>

            <div className="mt-6 space-y-6">
              {sortedCategories.map((cat) => {
                const catProducts = products.filter((p) => p.categoryId === cat.id && p.status === "Active");
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

                    <div className="grid gap-3 sm:grid-cols-3 md:grid-cols-4">
                      {catProducts.map((p) => (
                        <div key={p.id} className="flex flex-col justify-between rounded-xl border border-slate-200 bg-slate-50 p-2.5 text-center">
                          {resolveMediaUrl(p.primaryImageUrl) && (
                            <img
                              src={resolveMediaUrl(p.primaryImageUrl)!}
                              alt={p.name}
                              className="mx-auto mb-2 h-24 w-full rounded-lg object-cover"
                            />
                          )}
                          <p
                            style={{ backgroundColor: accentColorHex }}
                            className="rounded-md px-2 py-0.5 text-[11px] font-extrabold text-slate-950"
                          >
                            {p.name}
                          </p>
                          <div className="mt-2 space-y-0.5 text-[11px]">
                            {p.variations.length > 0 ? (
                              p.variations.map((v) => (
                                <div key={v.id} className="flex justify-between rounded bg-slate-900 px-2 py-0.5 font-mono font-bold text-white">
                                  <span>{v.variationName}</span>
                                  <span>R$ {v.price.toFixed(2).replace(".", ",")}</span>
                                </div>
                              ))
                            ) : (
                              <div className="rounded bg-slate-900 px-2 py-1 font-mono text-xs font-extrabold text-white">
                                R$ {p.basePrice.toFixed(2).replace(".", ",")}
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
