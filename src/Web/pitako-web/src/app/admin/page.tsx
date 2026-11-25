"use client";

import { useEffect, useState } from "react";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

interface BrandingResponse {
  tradeName: string;
  cnpj?: string;
  draft: {
    logoUrl?: string;
    slogan?: string;
    primaryColorHex: string;
    secondaryColorHex: string;
    accentColorHex: string;
    menuLayoutMode: string;
  };
}

export default function StoreAdminPage() {
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: "Painel do Estabelecimento",
    formattedCnpj: "CNPJ configurado na loja",
    primaryColorHex: "#4C1D95",
    secondaryColorHex: "#FAF5FF",
    accentColorHex: "#F59E0B"
  });
  const [primaryHex, setPrimaryHex] = useState("#4C1D95");
  const [accentHex, setAccentHex] = useState("#F59E0B");
  const [slogan, setSlogan] = useState("");
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") {
      setMsg("Faça login como usuário do estabelecimento em /login para editar o branding e catálogo ao vivo.");
      return;
    }

    apiFetch<BrandingResponse>("/api/v1/store/branding", {}, session.accessToken).then((res) => {
      if (res.ok && res.data) {
        setTheme({
          tradeName: res.data.tradeName,
          formattedCnpj: res.data.cnpj,
          logoUrl: res.data.draft.logoUrl,
          primaryColorHex: res.data.draft.primaryColorHex,
          secondaryColorHex: res.data.draft.secondaryColorHex,
          accentColorHex: res.data.draft.accentColorHex
        });
        setPrimaryHex(res.data.draft.primaryColorHex);
        setAccentHex(res.data.draft.accentColorHex);
        setSlogan(res.data.draft.slogan || "");
        setMsg(null);
      } else {
        setMsg(res.errorMessage || "Não foi possível carregar identidade visual.");
      }
    });
  }, []);

  async function handlePublishBranding(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;

    const draftRes = await apiFetch(
      "/api/v1/store/branding/draft",
      {
        method: "PUT",
        body: JSON.stringify({
          logoUrl: theme.logoUrl || "/store-assets/tropical-logo.svg",
          coverImageUrl: "/store-assets/tropical-cover.svg",
          slogan,
          primaryColorHex: primaryHex,
          secondaryColorHex: theme.secondaryColorHex,
          accentColorHex: accentHex,
          menuLayoutMode: "grid"
        })
      },
      session.accessToken
    );

    if (!draftRes.ok) {
      setMsg(draftRes.errorMessage || "Erro ao atualizar rascunho.");
      return;
    }

    const pubRes = await apiFetch("/api/v1/store/branding/publish", { method: "POST" }, session.accessToken);
    if (pubRes.ok) {
      setTheme((prev) => ({ ...prev, primaryColorHex: primaryHex, accentColorHex: accentHex }));
      setMsg("Identidade visual exclusiva da loja publicada sem interferir na plataforma PITAKO.");
    } else {
      setMsg(pubRes.errorMessage || "Erro ao publicar.");
    }
  }

  return (
    <StoreBrandShell theme={theme} activeSection="/admin">
      {msg && (
        <div className="mb-6 rounded-xl border border-slate-300 bg-white p-4 text-sm text-slate-700 shadow-sm">
          {msg}
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-2">
        <section className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
          <h1 className="text-lg font-bold text-slate-900">Identidade Visual Própria (Whitelabel)</h1>
          <p className="mt-1 text-xs text-slate-500">
            Logo, cores e slogan exclusivos deste estabelecimento (nunca herdam a marca PITAKO).
          </p>
          <form onSubmit={handlePublishBranding} className="mt-4 space-y-4">
            <div>
              <label className="block text-xs font-medium text-slate-600">Slogan da Loja</label>
              <input
                value={slogan}
                onChange={(e) => setSlogan(e.target.value)}
                className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-medium text-slate-600">Cor Primária</label>
                <input
                  value={primaryHex}
                  onChange={(e) => setPrimaryHex(e.target.value)}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm font-mono"
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-slate-600">Cor Destaque</label>
                <input
                  value={accentHex}
                  onChange={(e) => setAccentHex(e.target.value)}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm font-mono"
                />
              </div>
            </div>
            <button
              type="submit"
              style={{ backgroundColor: "var(--store-primary)" }}
              className="rounded-lg px-4 py-2 text-sm font-semibold text-white"
            >
              Salvar Rascunho e Publicar
            </button>
          </form>
        </section>

        <section className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
          <h2 className="text-lg font-bold text-slate-900">Módulos Corporativos Habilitados</h2>
          <ul className="mt-3 space-y-2 text-sm text-slate-700">
            <li>• Marketplaces Multi-Loja (iFood, Rappi, Aiqfome, Delivery Much, 99Food, Keeta, Food To Save) com relatório parcial real</li>
            <li>• Atendimento WhatsApp Business Cloud API com IA restrita aos fatos do tenant</li>
            <li>• Relatórios operacionais e exportação auditada em PDF, Excel (.xlsx) e CSV</li>
            <li>• Backup cifrado AES-256-GCM, migração assistida (Consumer/Goomer/Saipos) e LGPD</li>
            <li>• Operação offline PDV com fila ordenada e idempotência SHA-256</li>
          </ul>
        </section>
      </div>
    </StoreBrandShell>
  );
}
