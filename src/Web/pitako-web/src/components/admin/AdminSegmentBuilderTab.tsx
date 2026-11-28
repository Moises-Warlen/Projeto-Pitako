"use client";

import { CheckCircle2, Layers, RefreshCw, Sliders } from "lucide-react";
import { useState } from "react";
import { apiFetch } from "@/lib/api";
import { AdminCategoryDto, AdminProductDto } from "./AdminCatalogTab";
import { ModifierGroupDetailedDto } from "./AdminProductAndCategoryModals";

function formatBrl(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

interface BuilderSpec {
  title: string;
  subtitle: string;
  templateSegment: "pizzaria" | "hamburgueria" | "lanchonete";
  highlights: string[];
  matchKeywords: string[];
}

const BUILDER_SPECS: Record<string, BuilderSpec> = {
  pizzaria: {
    title: "Montador de Pizza (Meia-a-Meia, Massas, Bordas & Sabores)",
    subtitle: "Exclusivo desta modalidade de Pizzaria. Gerencie tamanhos em fatias, bordas recheadas e sabores fracionados sem exibir regras de outros segmentos.",
    templateSegment: "pizzaria",
    highlights: ["Tamanhos e fatias configuráveis (Broto, Média, Grande, Família)", "Suporte a divisão 1/2 (meia-a-meia) pelo maior preço ou média", "Massas, bordas recheadas e adicionais exclusivos de pizzaria"],
    matchKeywords: ["pizza", "borda", "massa", "sabor"]
  },
  hamburgueria: {
    title: "Montador de Hambúrguer (Pães, Carnes/Pontos, Queijos & Combos)",
    subtitle: "Exclusivo desta modalidade de Hamburgueria. Configure tipos de pão, ponto da carne, queijos, extras pagos e combos.",
    templateSegment: "hamburgueria",
    highlights: ["Escolha de pão artesanal e ponto da carne", "Camadas de queijo, bacon, molhos e acompanhamentos", "Combos com batata frita e bebida configuráveis"],
    matchKeywords: ["burger", "hambúrguer", "hamburguer", "pão", "carne", "combo"]
  },
  lanchonete: {
    title: "Montador de Lanche (Pães/Bases, Recheios, Adicionais & Combos)",
    subtitle: "Exclusivo desta modalidade de Lanchonete. Configure montagens rápidas de mistos, baurus, hot-dogs e combos de intervalo.",
    templateSegment: "lanchonete",
    highlights: ["Bases (pão francês, pão de forma, baguete ou brioche)", "Recheios principais, frios e saladas inclusas", "Acompanhamentos e bebidas combinadas"],
    matchKeywords: ["lanche", "misto", "bauru", "cachorro", "combo"]
  }
};

export function AdminSegmentBuilderTab({
  token,
  segmentCode,
  categories,
  products,
  modifierGroups,
  onNotify,
  onReload,
  onOpenCatalog
}: {
  token: string;
  segmentCode: string;
  categories: AdminCategoryDto[];
  products: AdminProductDto[];
  modifierGroups: ModifierGroupDetailedDto[];
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
  onOpenCatalog: () => void;
}) {
  const [applying, setApplying] = useState(false);
  const key = (segmentCode || "").toLowerCase();
  const spec = BUILDER_SPECS[key] ?? BUILDER_SPECS.lanchonete;

  const matchingGroups = modifierGroups.filter((g) => {
    const low = g.name.toLowerCase();
    return spec.matchKeywords.some((kw) => low.includes(kw)) || modifierGroups.length <= 12;
  });

  const matchingProducts = products.filter((p) => {
    const low = `${p.name} ${p.description || ""}`.toLowerCase();
    return spec.matchKeywords.some((kw) => low.includes(kw)) || p.variations.length > 0 || p.modifierGroupIds.length > 0;
  });

  async function applyModalityDefaults() {
    setApplying(true);
    const res = await apiFetch<{ productsCreated: number; modifierGroupsCreated: number; categoriesCreated: number }>(
      "/api/v1/catalog/apply-segment-template",
      { method: "POST", body: JSON.stringify({ segmentCode: spec.templateSegment }) },
      token
    );
    setApplying(false);
    if (res.ok && res.data) {
      onNotify(
        res.data.productsCreated > 0 || res.data.modifierGroupsCreated > 0
          ? `Estrutura padrão (${spec.templateSegment}) aplicada: ${res.data.categoriesCreated} categorias, ${res.data.productsCreated} produtos e ${res.data.modifierGroupsCreated} grupos.`
          : `Os padrões de ${spec.templateSegment} já estão carregados neste cardápio.`
      );
      await onReload();
    } else {
      onNotify(res.errorMessage || "Não foi possível carregar os padrões desta modalidade.");
    }
  }

  return (
    <div className="space-y-6 text-xs">
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="max-w-2xl space-y-1.5">
            <span className="inline-flex items-center gap-1.5 rounded-full bg-indigo-50 px-3 py-1 text-[10px] font-extrabold uppercase tracking-wide text-indigo-800">
              <Sliders className="h-3.5 w-3.5" /> Modalidade Ativa: {segmentCode.toUpperCase()}
            </span>
            <h2 className="text-base font-extrabold text-slate-900">{spec.title}</h2>
            <p className="text-xs text-slate-600">{spec.subtitle}</p>
            <ul className="mt-2 grid gap-1.5 sm:grid-cols-3">
              {spec.highlights.map((h) => (
                <li key={h} className="flex items-start gap-1.5 rounded-xl bg-slate-50 p-2 text-[11px] font-semibold text-slate-700">
                  <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0 text-emerald-600" />
                  <span>{h}</span>
                </li>
              ))}
            </ul>
          </div>

          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={applyModalityDefaults}
              disabled={applying}
              className="inline-flex items-center gap-1.5 rounded-xl bg-slate-900 px-4 py-2 text-xs font-extrabold text-white hover:bg-slate-800 disabled:opacity-50"
            >
              <RefreshCw className={`h-3.5 w-3.5 ${applying ? "animate-spin" : ""}`} />
              {applying ? "Aplicando..." : "Carregar / Atualizar Padrões da Modalidade"}
            </button>
            <button
              type="button"
              onClick={onOpenCatalog}
              className="inline-flex items-center gap-1.5 rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs font-bold text-slate-700 hover:bg-slate-50"
            >
              <Layers className="h-3.5 w-3.5" /> Editar no Cardápio ({categories.length} cats)
            </button>
          </div>
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-2">
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h3 className="mb-3 text-sm font-extrabold text-slate-900">
            Itens Configurados nesta Modalidade ({matchingProducts.length})
          </h3>
          {matchingProducts.length === 0 ? (
            <p className="rounded-xl border border-dashed border-slate-300 bg-slate-50 p-4 text-center text-slate-500">
              Nenhum item montável criado ainda. Clique em &ldquo;Carregar Padrões da Modalidade&rdquo; ou cadastre na aba Cardápio.
            </p>
          ) : (
            <div className="max-h-96 space-y-2 overflow-y-auto pr-1">
              {matchingProducts.map((p) => (
                <div key={p.id} className="rounded-xl border border-slate-200 bg-slate-50/70 p-3">
                  <div className="flex items-center justify-between gap-2">
                    <p className="font-extrabold text-slate-900">{p.name}</p>
                    <span className="font-mono font-extrabold text-emerald-700">{formatBrl(p.basePrice)}</span>
                  </div>
                  {p.variations.length > 0 && (
                    <div className="mt-1.5 flex flex-wrap gap-1.5">
                      {p.variations.map((v) => (
                        <span key={v.id} className="rounded-lg border border-slate-200 bg-white px-2 py-0.5 text-[10px] font-bold text-slate-700">
                          {v.variationName} · {formatBrl(v.price)}
                          {v.maxFlavors > 1 ? ` (até ${v.maxFlavors} sabores)` : ""}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h3 className="mb-3 text-sm font-extrabold text-slate-900">
            Grupos & Etapas desta Modalidade ({matchingGroups.length})
          </h3>
          {matchingGroups.length === 0 ? (
            <p className="rounded-xl border border-dashed border-slate-300 bg-slate-50 p-4 text-center text-slate-500">
              Nenhum grupo específico desta modalidade cadastrado ainda.
            </p>
          ) : (
            <div className="max-h-96 space-y-2 overflow-y-auto pr-1">
              {matchingGroups.map((g) => (
                <div key={g.id} className="rounded-xl border border-slate-200 bg-slate-50/70 p-3">
                  <div className="flex items-center justify-between gap-2">
                    <p className="font-extrabold text-slate-900">{g.name}</p>
                    <span className="rounded-full bg-white px-2 py-0.5 text-[10px] font-bold text-slate-600">
                      {g.isRequired ? "Obrigatório" : "Opcional"} · máx {g.maxSelections}
                    </span>
                  </div>
                  {g.options && g.options.length > 0 && (
                    <p className="mt-1 text-[11px] text-slate-600">
                      {g.options.slice(0, 6).map((o) => o.additionalPrice > 0 ? `${o.name} (+${formatBrl(o.additionalPrice)})` : o.name).join(" · ")}
                      {g.options.length > 6 ? ` · +${g.options.length - 6} opções` : ""}
                    </p>
                  )}
                </div>
              ))}
            </div>
          )}
        </section>
      </div>
    </div>
  );
}
