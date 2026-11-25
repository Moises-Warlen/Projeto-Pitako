"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

interface BrandingResponse {
  tradeName: string;
  slug: string;
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

interface CatalogData {
  categories: { id: string; name: string }[];
  products: { id: string; categoryId: string; name: string; basePrice: number; status: string; saleMode: string }[];
}

interface AcaiData {
  cupSizes: { id: string; name: string; volumeMl: number; basePrice: { amount: number }; freeToppingsLimit: number }[];
  baseFlavors: { id: string; name: string }[];
  freeToppings: { id: string; name: string }[];
  paidAddons: { id: string; name: string; additionalPrice: { amount: number } }[];
}

interface StoreUserRow {
  id: string;
  fullName: string;
  normalizedEmail: string;
  role: string;
}

export default function StoreAdminPage() {
  const [tab, setTab] = useState<"catalog" | "acai" | "branding" | "team">("catalog");
  const [slug, setSlug] = useState("acai-demo");
  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: "Painel do Estabelecimento",
    formattedCnpj: "CNPJ da loja",
    primaryColorHex: "#4C1D95",
    secondaryColorHex: "#FAF5FF",
    accentColorHex: "#F59E0B"
  });
  const [msg, setMsg] = useState<string | null>(null);

  // Branding
  const [primaryHex, setPrimaryHex] = useState("#4C1D95");
  const [accentHex, setAccentHex] = useState("#F59E0B");
  const [slogan, setSlogan] = useState("");

  // Catalog
  const [catalog, setCatalog] = useState<CatalogData>({ categories: [], products: [] });
  const [newCatName, setNewCatName] = useState("");
  const [prodCatId, setProdCatId] = useState("");
  const [prodName, setProdName] = useState("");
  const [prodPrice, setProdPrice] = useState("19.90");
  const [prodSaleMode, setProdSaleMode] = useState("1");

  // Acai
  const [acai, setAcai] = useState<AcaiData>({ cupSizes: [], baseFlavors: [], freeToppings: [], paidAddons: [] });
  const [cupName, setCupName] = useState("");
  const [cupMl, setCupMl] = useState("400");
  const [cupPrice, setCupPrice] = useState("18.90");
  const [cupFreeLimit, setCupFreeLimit] = useState("3");
  const [freeName, setFreeName] = useState("");
  const [paidName, setPaidName] = useState("");
  const [paidPrice, setPaidPrice] = useState("4.50");

  // Team
  const [team, setTeam] = useState<StoreUserRow[]>([]);
  const [staffName, setStaffName] = useState("");
  const [staffEmail, setStaffEmail] = useState("");
  const [staffPassword, setStaffPassword] = useState("Equipe@123");
  const [staffRole, setStaffRole] = useState("3");

  async function refreshStoreData() {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") {
      setMsg("Faça login como usuário do estabelecimento em /login (ex.: admin@tropicalexpress.com.br / Store@12345).");
      return;
    }

    const [brandRes, catRes, acaiRes, usersRes] = await Promise.all([
      apiFetch<BrandingResponse>("/api/v1/store/branding", {}, session.accessToken),
      apiFetch<CatalogData>("/api/v1/catalog/products", {}, session.accessToken),
      apiFetch<AcaiData>("/api/v1/acai/config", {}, session.accessToken),
      apiFetch<StoreUserRow[]>("/api/v1/store/users", {}, session.accessToken)
    ]);

    if (brandRes.ok && brandRes.data) {
      setSlug(brandRes.data.slug);
      setTheme({
        tradeName: brandRes.data.tradeName,
        formattedCnpj: brandRes.data.cnpj,
        logoUrl: brandRes.data.draft.logoUrl,
        primaryColorHex: brandRes.data.draft.primaryColorHex,
        secondaryColorHex: brandRes.data.draft.secondaryColorHex,
        accentColorHex: brandRes.data.draft.accentColorHex
      });
      setPrimaryHex(brandRes.data.draft.primaryColorHex);
      setAccentHex(brandRes.data.draft.accentColorHex);
      setSlogan(brandRes.data.draft.slogan || "");
    }
    if (catRes.ok && catRes.data) {
      setCatalog(catRes.data);
      if (!prodCatId && catRes.data.categories[0]) setProdCatId(catRes.data.categories[0].id);
    }
    if (acaiRes.ok && acaiRes.data) setAcai(acaiRes.data);
    if (usersRes.ok && usersRes.data) setTeam(usersRes.data);

    setMsg(brandRes.ok ? null : brandRes.errorMessage || "Erro ao carregar dados da loja.");
  }

  useEffect(() => {
    refreshStoreData();
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
    await apiFetch("/api/v1/store/branding/publish", { method: "POST" }, session.accessToken);
    setMsg("Visual exclusivo da loja publicado! Abra /loja/" + slug + " para ver.");
    refreshStoreData();
  }

  async function handleCreateCategory(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;
    const res = await apiFetch("/api/v1/catalog/categories", { method: "POST", body: JSON.stringify({ name: newCatName, displayOrder: catalog.categories.length + 1 }) }, session.accessToken);
    if (res.ok) {
      setNewCatName("");
      setMsg("Categoria adicionada ao cardápio!");
      refreshStoreData();
    } else setMsg(res.errorMessage || "Erro ao criar categoria.");
  }

  async function handleCreateProduct(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;
    const res = await apiFetch("/api/v1/catalog/products", {
      method: "POST",
      body: JSON.stringify({ categoryId: prodCatId, name: prodName, basePrice: Number(prodPrice), saleMode: Number(prodSaleMode) })
    }, session.accessToken);
    if (res.ok) {
      setProdName("");
      setMsg("Produto criado! Ele já aparece no cardápio público da loja.");
      refreshStoreData();
    } else setMsg(res.errorMessage || "Erro ao criar produto.");
  }

  async function toggleProduct(id: string, isActive: boolean) {
    const session = loadSession();
    if (!session) return;
    await apiFetch(`/api/v1/catalog/products/${id}/${isActive ? "pause" : "reactivate"}`, { method: "POST" }, session.accessToken);
    refreshStoreData();
  }

  async function handleCreateCup(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;
    const res = await apiFetch("/api/v1/acai/cup-sizes", {
      method: "POST",
      body: JSON.stringify({ name: cupName, volumeMl: Number(cupMl), basePrice: Number(cupPrice), freeToppingsLimit: Number(cupFreeLimit) })
    }, session.accessToken);
    if (res.ok) {
      setCupName("");
      setMsg("Copo de açaí criado!");
      refreshStoreData();
    } else setMsg(res.errorMessage || "Erro.");
  }

  async function handleCreateFreeTopping(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;
    await apiFetch("/api/v1/acai/free-toppings", { method: "POST", body: JSON.stringify({ name: freeName, maxPortionsPerCup: 2 }) }, session.accessToken);
    setFreeName("");
    refreshStoreData();
  }

  async function handleCreatePaidAddon(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;
    await apiFetch("/api/v1/acai/paid-addons", { method: "POST", body: JSON.stringify({ name: paidName, additionalPrice: Number(paidPrice), maxQuantityPerCup: 3 }) }, session.accessToken);
    setPaidName("");
    refreshStoreData();
  }

  async function handleCreateStaff(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;
    const res = await apiFetch("/api/v1/store/users", {
      method: "POST",
      body: JSON.stringify({ fullName: staffName, email: staffEmail, password: staffPassword, role: Number(staffRole) })
    }, session.accessToken);
    if (res.ok) {
      setStaffName("");
      setStaffEmail("");
      setMsg("Funcionário criado com sucesso!");
      refreshStoreData();
    } else setMsg(res.errorMessage || "Erro ao criar usuário.");
  }

  return (
    <StoreBrandShell theme={theme} activeSection="/admin">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div>
          <h1 className="text-lg font-bold text-slate-900">Administração da Loja — {theme.tradeName}</h1>
          <p className="text-xs text-slate-500">Cadastre produtos, configure seu montador de açaí, personalize as cores e gerencie sua equipe.</p>
        </div>
        <Link
          href={`/loja/${slug}`}
          style={{ backgroundColor: "var(--store-accent)" }}
          className="rounded-lg px-4 py-2 text-xs font-bold text-white shadow"
        >
          Ver Cardápio da Minha Loja (/loja/{slug})
        </Link>
      </div>

      {msg && <div className="mb-5 rounded-xl border border-slate-300 bg-white p-4 text-sm text-slate-800">{msg}</div>}

      <div className="mb-6 flex flex-wrap gap-2">
        {[
          { key: "catalog", label: "1. Cardápio & Produtos" },
          { key: "acai", label: "2. Montador de Açaí" },
          { key: "branding", label: "3. Cores & Identidade Visual" },
          { key: "team", label: "4. Equipe & Acessos" }
        ].map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key as typeof tab)}
            style={tab === t.key ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
            className="rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm font-semibold text-slate-700"
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "catalog" && (
        <div className="grid gap-6 lg:grid-cols-3">
          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-2">
            <h2 className="mb-3 font-bold text-slate-900">Produtos no Cardápio ({catalog.products.length})</h2>
            <div className="space-y-2.5">
              {catalog.products.map((p) => (
                <div key={p.id} className="flex items-center justify-between rounded-lg border border-slate-100 bg-slate-50 p-3 text-sm">
                  <div>
                    <p className="font-semibold text-slate-900">{p.name}</p>
                    <p className="text-xs text-slate-500">R$ {p.basePrice.toFixed(2)} · {p.saleMode} · Status: {p.status}</p>
                  </div>
                  <button
                    type="button"
                    onClick={() => toggleProduct(p.id, p.status === "Active")}
                    className="rounded-lg border border-slate-300 bg-white px-3 py-1 text-xs font-semibold hover:bg-slate-100"
                  >
                    {p.status === "Active" ? "Pausar" : "Reativar"}
                  </button>
                </div>
              ))}
            </div>
          </section>

          <section className="space-y-5">
            <form onSubmit={handleCreateCategory} className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-2 font-bold text-slate-900">Nova Categoria</h3>
              <input value={newCatName} onChange={(e) => setNewCatName(e.target.value)} required placeholder="Ex: Pizzas Salgadas" className="mb-3 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm" />
              <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-lg py-2 text-sm font-semibold text-white">Criar Categoria</button>
            </form>

            <form onSubmit={handleCreateProduct} className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="mb-2 font-bold text-slate-900">Novo Produto</h3>
              <select value={prodCatId} onChange={(e) => setProdCatId(e.target.value)} required className="mb-2 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm">
                {catalog.categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
              <input value={prodName} onChange={(e) => setProdName(e.target.value)} required placeholder="Nome do produto" className="mb-2 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm" />
              <input type="number" step="0.01" value={prodPrice} onChange={(e) => setProdPrice(e.target.value)} required className="mb-2 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm" />
              <select value={prodSaleMode} onChange={(e) => setProdSaleMode(e.target.value)} className="mb-3 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm">
                <option value="1">Por Unidade</option>
                <option value="2">Por Quilo (Balança)</option>
              </select>
              <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-lg py-2 text-sm font-semibold text-white">Adicionar Produto</button>
            </form>
          </section>
        </div>
      )}

      {tab === "acai" && (
        <div className="grid gap-6 lg:grid-cols-3">
          <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="mb-3 font-bold">Copos / Tamanhos ({acai.cupSizes.length})</h2>
            <ul className="mb-4 space-y-1.5 text-xs text-slate-700">
              {acai.cupSizes.map((c) => <li key={c.id}>• {c.name} ({c.volumeMl}ml) — R$ {c.basePrice.amount.toFixed(2)} ({c.freeToppingsLimit} grátis)</li>)}
            </ul>
            <form onSubmit={handleCreateCup} className="space-y-2 text-sm">
              <input value={cupName} onChange={(e) => setCupName(e.target.value)} required placeholder="Nome (ex: Copo 700ml)" className="w-full rounded-lg border px-3 py-1.5" />
              <input type="number" value={cupMl} onChange={(e) => setCupMl(e.target.value)} required placeholder="ml" className="w-full rounded-lg border px-3 py-1.5" />
              <input type="number" step="0.01" value={cupPrice} onChange={(e) => setCupPrice(e.target.value)} required placeholder="Preço base" className="w-full rounded-lg border px-3 py-1.5" />
              <input type="number" value={cupFreeLimit} onChange={(e) => setCupFreeLimit(e.target.value)} required placeholder="Qtd itens grátis" className="w-full rounded-lg border px-3 py-1.5" />
              <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-lg py-2 font-semibold text-white">Criar Copo</button>
            </form>
          </div>

          <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="mb-3 font-bold">Acompanhamentos Gratuitos ({acai.freeToppings.length})</h2>
            <ul className="mb-4 space-y-1.5 text-xs text-slate-700">
              {acai.freeToppings.map((t) => <li key={t.id}>• {t.name}</li>)}
            </ul>
            <form onSubmit={handleCreateFreeTopping} className="space-y-2 text-sm">
              <input value={freeName} onChange={(e) => setFreeName(e.target.value)} required placeholder="Ex: Paçoca / Leite Condensado" className="w-full rounded-lg border px-3 py-1.5" />
              <button type="submit" className="w-full rounded-lg bg-emerald-600 py-2 font-semibold text-white">Adicionar Grátis</button>
            </form>
          </div>

          <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <h2 className="mb-3 font-bold">Adicionais Pagos ({acai.paidAddons.length})</h2>
            <ul className="mb-4 space-y-1.5 text-xs text-slate-700">
              {acai.paidAddons.map((a) => <li key={a.id}>• {a.name} (+R$ {a.additionalPrice.amount.toFixed(2)})</li>)}
            </ul>
            <form onSubmit={handleCreatePaidAddon} className="space-y-2 text-sm">
              <input value={paidName} onChange={(e) => setPaidName(e.target.value)} required placeholder="Ex: Creme Ninho" className="w-full rounded-lg border px-3 py-1.5" />
              <input type="number" step="0.01" value={paidPrice} onChange={(e) => setPaidPrice(e.target.value)} required className="w-full rounded-lg border px-3 py-1.5" />
              <button type="submit" className="w-full rounded-lg bg-amber-600 py-2 font-semibold text-white">Adicionar Pago</button>
            </form>
          </div>
        </div>
      )}

      {tab === "branding" && (
        <section className="max-w-lg rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
          <h2 className="text-lg font-bold text-slate-900">Identidade Visual Própria (Whitelabel)</h2>
          <form onSubmit={handlePublishBranding} className="mt-4 space-y-4">
            <div>
              <label className="block text-xs font-medium text-slate-600">Slogan da Loja</label>
              <input value={slogan} onChange={(e) => setSlogan(e.target.value)} className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-medium text-slate-600">Cor Primária</label>
                <input value={primaryHex} onChange={(e) => setPrimaryHex(e.target.value)} className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm font-mono" />
              </div>
              <div>
                <label className="block text-xs font-medium text-slate-600">Cor Destaque</label>
                <input value={accentHex} onChange={(e) => setAccentHex(e.target.value)} className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm font-mono" />
              </div>
            </div>
            <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="rounded-lg px-4 py-2 text-sm font-semibold text-white">
              Salvar Rascunho e Publicar
            </button>
          </form>
        </section>
      )}

      {tab === "team" && (
        <div className="grid gap-6 lg:grid-cols-3">
          <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-2">
            <h2 className="mb-3 font-bold">Equipe da Loja ({team.length})</h2>
            <div className="space-y-2 text-sm">
              {team.map((u) => (
                <div key={u.id} className="flex justify-between rounded-lg border border-slate-100 bg-slate-50 p-3">
                  <span><strong>{u.fullName}</strong> ({u.normalizedEmail})</span>
                  <span className="rounded bg-slate-200 px-2 py-0.5 text-xs font-semibold">{u.role}</span>
                </div>
              ))}
            </div>
          </section>

          <form onSubmit={handleCreateStaff} className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-sm">
            <h3 className="font-bold">Novo Operador</h3>
            <input value={staffName} onChange={(e) => setStaffName(e.target.value)} required placeholder="Nome completo" className="w-full rounded-lg border px-3 py-2" />
            <input type="email" value={staffEmail} onChange={(e) => setStaffEmail(e.target.value)} required placeholder="E-mail" className="w-full rounded-lg border px-3 py-2" />
            <input value={staffPassword} onChange={(e) => setStaffPassword(e.target.value)} required placeholder="Senha" className="w-full rounded-lg border px-3 py-2" />
            <select value={staffRole} onChange={(e) => setStaffRole(e.target.value)} className="w-full rounded-lg border px-3 py-2">
              <option value="2">Gerente</option>
              <option value="3">Caixa (PDV)</option>
              <option value="5">Garçom</option>
              <option value="6">Cozinha</option>
              <option value="7">Entregador</option>
              <option value="9">Financeiro</option>
            </select>
            <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-lg py-2 font-semibold text-white">Cadastrar</button>
          </form>
        </div>
      )}
    </StoreBrandShell>
  );
}
