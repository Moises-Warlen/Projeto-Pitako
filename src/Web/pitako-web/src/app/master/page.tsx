"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { PitakoBrandShell } from "@/components/PitakoBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

interface PlanItem {
  id: string;
  code: string;
  name: string;
  price: number;
  isActive: boolean;
  unlimitedEstablishments: boolean;
  maxEstablishments: number | null;
  modules: string[];
}

interface CustomerRow {
  id: string;
  customerName: string;
  document: string;
  billingEmail: string;
  planName: string;
  subscriptionStatus: string;
}

interface EstablishmentRow {
  id: string;
  customerAccountId?: string;
  tradeName: string;
  legalName: string;
  slug: string;
  segmentCode: string;
  cnpj: string;
  status: string;
}

interface SegmentOption {
  code: string;
  displayName: string;
  supportsAcaiBuilder: boolean;
  supportsWeightScale: boolean;
  supportsTableService: boolean;
  supportsFractionalItems: boolean;
}

interface MasterOverviewResponse {
  customers: CustomerRow[];
  establishments: EstablishmentRow[];
  segments: SegmentOption[];
}

const ALL_MODULES = [
  { code: "tenants", label: "Identidade & Lojas" },
  { code: "catalog", label: "Cardápio & Produtos" },
  { code: "acai", label: "Montador de Açaí" },
  { code: "weighing", label: "Balança por Quilo" },
  { code: "inventory", label: "Estoque & Insumos" },
  { code: "coupons", label: "Cupons de Desconto" },
  { code: "orders", label: "Pedidos & Kanban" },
  { code: "pdv", label: "PDV & Caixa" },
  { code: "dining_hall", label: "Salão, Mesas, QR & Totem" },
  { code: "delivery", label: "Delivery & Entregadores" },
  { code: "marketplaces", label: "Marketplaces (iFood, Rappi...)" },
  { code: "printing", label: "Impressoras Térmicas" },
  { code: "whatsapp", label: "WhatsApp Bot & IA" },
  { code: "reports", label: "Relatórios PDF/Excel/CSV" },
  { code: "finance", label: "Financeiro & DRE" },
  { code: "security", label: "Usuários & Segurança" },
  { code: "data_lifecycle", label: "Backup, Migração & LGPD" }
];

export default function MasterPanelPage() {
  const [plans, setPlans] = useState<PlanItem[]>([]);
  const [overview, setOverview] = useState<MasterOverviewResponse>({
    customers: [],
    establishments: [],
    segments: []
  });
  const [feedback, setFeedback] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"stores" | "customers" | "plans">("stores");

  // Form Passo 1 — Plano
  const [planCode, setPlanCode] = useState("");
  const [planName, setPlanName] = useState("");
  const [planPrice, setPlanPrice] = useState("299.90");
  const [isUnlimited, setIsUnlimited] = useState(true);
  const [maxStores, setMaxStores] = useState("1");
  const [selectedModules, setSelectedModules] = useState<string[]>(ALL_MODULES.map((m) => m.code));

  // Form Passo 2 — Cliente + Assinatura
  const [customerName, setCustomerName] = useState("");
  const [customerDoc, setCustomerDoc] = useState("11222333000181");
  const [customerEmail, setCustomerEmail] = useState("");
  const [customerPhone, setCustomerPhone] = useState("11999998888");
  const [customerPlanId, setCustomerPlanId] = useState("");

  // Form Passo 3 — Nova Loja + Modalidade + Admin inicial
  const [storeCustomerId, setStoreCustomerId] = useState("");
  const [storeLegalName, setStoreLegalName] = useState("");
  const [storeTradeName, setStoreTradeName] = useState("");
  const [storeSlug, setStoreSlug] = useState("");
  const [storeCnpj, setStoreCnpj] = useState("11222333000181");
  const [storeSegment, setStoreSegment] = useState("pizzaria");
  const [storeAdminName, setStoreAdminName] = useState("");
  const [storeAdminEmail, setStoreAdminEmail] = useState("");
  const [storeAdminPassword, setStoreAdminPassword] = useState("Loja@12345");

  async function refreshAll() {
    const session = loadSession();
    if (!session || session.scope !== "Saas") {
      setFeedback("Entre como Administrador Master SaaS em /login (master@pitako.com.br / Master@12345) para gerenciar planos, clientes e lojas.");
      return;
    }

    const [plansRes, custRes] = await Promise.all([
      apiFetch<PlanItem[]>("/api/v1/master/plans", {}, session.accessToken),
      apiFetch<MasterOverviewResponse>("/api/v1/master/customers", {}, session.accessToken)
    ]);

    if (plansRes.ok && plansRes.data) {
      setPlans(plansRes.data);
      if (!customerPlanId && plansRes.data[0]) setCustomerPlanId(plansRes.data[0].id);
    }
    if (custRes.ok && custRes.data) {
      setOverview(custRes.data);
      if (!storeCustomerId && custRes.data.customers[0]) setStoreCustomerId(custRes.data.customers[0].id);
    }
    if (!plansRes.ok) {
      setFeedback(plansRes.errorMessage || "Erro ao carregar Painel Master.");
    } else {
      setFeedback(null);
    }
  }

  useEffect(() => {
    refreshAll();
  }, []);

  function toggleModule(code: string) {
    setSelectedModules((prev) => (prev.includes(code) ? prev.filter((c) => c !== code) : [...prev, code]));
  }

  async function handleCreatePlan(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;

    const res = await apiFetch(
      "/api/v1/master/plans",
      {
        method: "POST",
        body: JSON.stringify({
          code: planCode,
          name: planName,
          priceAmount: Number(planPrice),
          isUnlimited,
          maxEstablishments: isUnlimited ? null : Number(maxStores),
          enabledModules: selectedModules
        })
      },
      session.accessToken
    );

    if (res.ok) {
      setPlanCode("");
      setPlanName("");
      setFeedback("Plano criado! Agora você já pode vincular clientes a ele.");
      refreshAll();
    } else {
      setFeedback(res.errorMessage || "Erro ao criar plano.");
    }
  }

  async function handleCreateCustomer(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;

    const res = await apiFetch<{ customerId: string }>(
      "/api/v1/master/customers-subscriptions",
      {
        method: "POST",
        body: JSON.stringify({
          customerName,
          documentNumber: customerDoc,
          billingEmail: customerEmail,
          phone: customerPhone,
          planId: customerPlanId,
          startWithTrial: false
        })
      },
      session.accessToken
    );

    if (res.ok && res.data) {
      setCustomerName("");
      setCustomerEmail("");
      setStoreCustomerId(res.data.customerId);
      setActiveTab("stores");
      setFeedback("Cliente cadastrado com assinatura ativa! Agora crie a loja dele no formulário ao lado.");
      refreshAll();
    } else {
      setFeedback(res.errorMessage || "Erro ao cadastrar cliente.");
    }
  }

  async function handleCreateEstablishment(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;

    const cleanSlug = storeSlug.trim().toLowerCase().replace(/[^a-z0-9-]/g, "-");
    const estRes = await apiFetch<{ tenant: { id: string; slug: string; tradeName: string } }>(
      "/api/v1/store/establishments",
      {
        method: "POST",
        body: JSON.stringify({
          customerAccountId: storeCustomerId,
          legalName: storeLegalName,
          tradeName: storeTradeName,
          slug: cleanSlug,
          cnpj: storeCnpj,
          segmentCode: storeSegment
        })
      },
      session.accessToken
    );

    if (!estRes.ok || !estRes.data) {
      setFeedback(estRes.errorMessage || "Erro ao criar loja.");
      return;
    }

    const newTenantId = estRes.data.tenant.id;
    if (storeAdminEmail && storeAdminPassword) {
      const userRes = await apiFetch(
        `/api/v1/master/establishments/${newTenantId}/admin-user`,
        {
          method: "POST",
          body: JSON.stringify({
            fullName: storeAdminName || `Admin ${storeTradeName}`,
            email: storeAdminEmail,
            password: storeAdminPassword,
            role: 1
          })
        },
        session.accessToken
      );
      if (!userRes.ok) {
        setFeedback(`Loja '${cleanSlug}' criada, mas houve erro ao criar o login do dono: ${userRes.errorMessage}`);
        refreshAll();
        return;
      }
    }

    setStoreLegalName("");
    setStoreTradeName("");
    setStoreSlug("");
    setStoreAdminName("");
    setStoreAdminEmail("");
    setFeedback(`Loja criada com sucesso! Acesse o cardápio em /loja/${cleanSlug} ou entre no /login com slug '${cleanSlug}'.`);
    refreshAll();
  }

  return (
    <PitakoBrandShell
      title="Painel Master PITAKO — Clientes, Planos, Lojas e Modalidades"
      subtitle="Aqui você controla o SaaS: 1) configura Planos e Módulos, 2) cadastra Clientes, 3) cria as Lojas escolhendo a Modalidade (Pizzaria, Açaíteria, Hamburgueria, etc.)."
    >
      {feedback && (
        <div className="mb-6 rounded-xl border border-amber-500/40 bg-amber-950/40 p-4 text-sm text-amber-100">
          {feedback}
        </div>
      )}

      <div className="mb-6 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => setActiveTab("stores")}
          className={`rounded-lg px-4 py-2 text-sm font-semibold ${
            activeTab === "stores" ? "bg-red-600 text-white" : "bg-zinc-900 text-zinc-300 hover:bg-zinc-800"
          }`}
        >
          1. Lojas & Modalidades ({overview.establishments.length})
        </button>
        <button
          type="button"
          onClick={() => setActiveTab("customers")}
          className={`rounded-lg px-4 py-2 text-sm font-semibold ${
            activeTab === "customers" ? "bg-red-600 text-white" : "bg-zinc-900 text-zinc-300 hover:bg-zinc-800"
          }`}
        >
          2. Clientes / Donos ({overview.customers.length})
        </button>
        <button
          type="button"
          onClick={() => setActiveTab("plans")}
          className={`rounded-lg px-4 py-2 text-sm font-semibold ${
            activeTab === "plans" ? "bg-red-600 text-white" : "bg-zinc-900 text-zinc-300 hover:bg-zinc-800"
          }`}
        >
          3. Planos & Módulos ({plans.length})
        </button>
      </div>

      {activeTab === "stores" && (
        <div className="grid gap-8 lg:grid-cols-3">
          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5 lg:col-span-2">
            <h2 className="mb-4 text-lg font-semibold text-white">Lojas Cadastradas na Plataforma</h2>
            <div className="space-y-3">
              {overview.establishments.map((est) => (
                <div key={est.id} className="rounded-xl border border-zinc-800 bg-zinc-950 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <p className="text-base font-bold text-white">{est.tradeName}</p>
                      <p className="text-xs text-zinc-400">
                        Modalidade: <strong className="uppercase text-red-400">{est.segmentCode}</strong> · Slug:{" "}
                        <code>{est.slug}</code> · CNPJ: {est.cnpj}
                      </p>
                    </div>
                    <div className="flex gap-2">
                      <Link
                        href={`/loja/${est.slug}`}
                        className="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-emerald-700"
                      >
                        Abrir Cardápio /loja/{est.slug}
                      </Link>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5">
            <h2 className="mb-1 text-lg font-semibold text-white">Criar Nova Loja</h2>
            <p className="mb-4 text-xs text-zinc-400">Escolha o cliente dono, a modalidade do negócio e já crie a senha do administrador.</p>
            <form onSubmit={handleCreateEstablishment} className="space-y-3 text-sm">
              <div>
                <label className="mb-1 block text-xs text-zinc-400">Cliente Dono da Loja</label>
                <select
                  value={storeCustomerId}
                  onChange={(e) => setStoreCustomerId(e.target.value)}
                  required
                  className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
                >
                  {overview.customers.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.customerName} ({c.planName})
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-xs text-zinc-400">Modalidade / Segmento</label>
                <select
                  value={storeSegment}
                  onChange={(e) => setStoreSegment(e.target.value)}
                  className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
                >
                  {(overview.segments.length > 0
                    ? overview.segments
                    : [
                        { code: "acaiteria", displayName: "Açaíteria", supportsAcaiBuilder: true, supportsWeightScale: true, supportsTableService: true, supportsFractionalItems: false },
                        { code: "pizzaria", displayName: "Pizzaria", supportsAcaiBuilder: false, supportsWeightScale: false, supportsTableService: true, supportsFractionalItems: true },
                        { code: "hamburgueria", displayName: "Hamburgueria", supportsAcaiBuilder: false, supportsWeightScale: false, supportsTableService: true, supportsFractionalItems: false }
                      ]
                  ).map((seg) => (
                    <option key={seg.code} value={seg.code}>
                      {seg.displayName} {seg.supportsAcaiBuilder ? "(c/ Açaí)" : ""} {seg.supportsWeightScale ? "(c/ Balança)" : ""} {seg.supportsFractionalItems ? "(c/ Meia-a-Meia)" : ""}
                    </option>
                  ))}
                </select>
              </div>
              <input
                value={storeTradeName}
                onChange={(e) => setStoreTradeName(e.target.value)}
                required
                placeholder="Nome Fantasia (ex: Pizzaria Bela Massa)"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <input
                value={storeLegalName}
                onChange={(e) => setStoreLegalName(e.target.value)}
                required
                placeholder="Razão Social"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <input
                value={storeSlug}
                onChange={(e) => setStoreSlug(e.target.value)}
                required
                placeholder="Slug da URL (ex: bela-massa)"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <input
                value={storeCnpj}
                onChange={(e) => setStoreCnpj(e.target.value)}
                required
                placeholder="CNPJ (14 dígitos válidos)"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <div className="border-t border-zinc-800 pt-3">
                <p className="mb-2 text-xs font-semibold text-zinc-300">Login Inicial do Dono da Loja</p>
                <input
                  value={storeAdminName}
                  onChange={(e) => setStoreAdminName(e.target.value)}
                  placeholder="Nome do Administrador"
                  className="mb-2 w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
                />
                <input
                  type="email"
                  value={storeAdminEmail}
                  onChange={(e) => setStoreAdminEmail(e.target.value)}
                  required
                  placeholder="E-mail de acesso"
                  className="mb-2 w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
                />
                <input
                  type="text"
                  value={storeAdminPassword}
                  onChange={(e) => setStoreAdminPassword(e.target.value)}
                  required
                  placeholder="Senha (mín 8 chars, Maiús/Minús/Número)"
                  className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
                />
              </div>
              <button type="submit" className="w-full rounded-lg bg-red-600 py-2.5 font-semibold text-white hover:bg-red-700">
                Criar Loja e Liberar Acesso
              </button>
            </form>
          </section>
        </div>
      )}

      {activeTab === "customers" && (
        <div className="grid gap-8 lg:grid-cols-3">
          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5 lg:col-span-2">
            <h2 className="mb-4 text-lg font-semibold text-white">Clientes Contratantes</h2>
            <div className="space-y-3">
              {overview.customers.map((c) => (
                <div key={c.id} className="rounded-lg border border-zinc-800 bg-zinc-950 p-4 text-sm">
                  <p className="font-bold text-white">{c.customerName}</p>
                  <p className="text-xs text-zinc-400">
                    Doc: {c.document} · E-mail: {c.billingEmail} · Plano: <strong className="text-red-400">{c.planName}</strong> ({c.subscriptionStatus})
                  </p>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5">
            <h2 className="mb-3 text-lg font-semibold text-white">Novo Cliente + Assinatura</h2>
            <form onSubmit={handleCreateCustomer} className="space-y-3 text-sm">
              <input
                value={customerName}
                onChange={(e) => setCustomerName(e.target.value)}
                required
                placeholder="Nome / Empresa Cliente"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <input
                value={customerDoc}
                onChange={(e) => setCustomerDoc(e.target.value)}
                required
                placeholder="CPF ou CNPJ"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <input
                type="email"
                value={customerEmail}
                onChange={(e) => setCustomerEmail(e.target.value)}
                required
                placeholder="E-mail de cobrança"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <input
                value={customerPhone}
                onChange={(e) => setCustomerPhone(e.target.value)}
                placeholder="Telefone / WhatsApp"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <div>
                <label className="mb-1 block text-xs text-zinc-400">Plano Contratado</label>
                <select
                  value={customerPlanId}
                  onChange={(e) => setCustomerPlanId(e.target.value)}
                  required
                  className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
                >
                  {plans.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} (R$ {p.price.toFixed(2)})
                    </option>
                  ))}
                </select>
              </div>
              <button type="submit" className="w-full rounded-lg bg-red-600 py-2.5 font-semibold text-white hover:bg-red-700">
                Cadastrar Cliente
              </button>
            </form>
          </section>
        </div>
      )}

      {activeTab === "plans" && (
        <div className="grid gap-8 lg:grid-cols-3">
          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5 lg:col-span-2">
            <h2 className="mb-4 text-lg font-semibold text-white">Planos Comerciais Configurados</h2>
            <div className="space-y-3">
              {plans.map((p) => (
                <div key={p.id} className="rounded-lg border border-zinc-800 bg-zinc-950 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <span className="text-base font-bold text-white">{p.name}</span>{" "}
                      <span className="text-xs text-zinc-400">({p.code})</span>
                    </div>
                    <span className="rounded-full bg-red-600/20 px-3 py-1 text-xs font-semibold text-red-400">
                      R$ {p.price.toFixed(2)} / mês
                    </span>
                  </div>
                  <p className="mt-2 text-xs text-zinc-300">
                    Limite Lojas:{" "}
                    <strong>{p.unlimitedEstablishments ? "Ilimitado (sem teto artificial)" : `${p.maxEstablishments}`}</strong> ·
                    Módulos: {p.modules.join(", ")}
                  </p>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5">
            <h2 className="mb-4 text-lg font-semibold text-white">Criar Plano & Escolher Módulos</h2>
            <form onSubmit={handleCreatePlan} className="space-y-3 text-sm">
              <input
                value={planCode}
                onChange={(e) => setPlanCode(e.target.value)}
                required
                placeholder="Código (ex: pizzaria-plus)"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <input
                value={planName}
                onChange={(e) => setPlanName(e.target.value)}
                required
                placeholder="Nome comercial"
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <input
                type="number"
                step="0.01"
                value={planPrice}
                onChange={(e) => setPlanPrice(e.target.value)}
                required
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
              />
              <label className="flex items-center gap-2 text-xs text-zinc-200">
                <input type="checkbox" checked={isUnlimited} onChange={(e) => setIsUnlimited(e.target.checked)} />
                Plano Ilimitado Real (sem teto numérico)
              </label>
              {!isUnlimited && (
                <input
                  type="number"
                  min={1}
                  value={maxStores}
                  onChange={(e) => setMaxStores(e.target.value)}
                  placeholder="Máximo de lojas"
                  className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
                />
              )}
              <div className="max-h-52 space-y-1.5 overflow-y-auto rounded-lg border border-zinc-800 bg-zinc-950 p-3">
                <p className="text-xs font-semibold text-zinc-300">Módulos Liberados neste Plano:</p>
                {ALL_MODULES.map((m) => (
                  <label key={m.code} className="flex items-center gap-2 text-xs text-zinc-300">
                    <input
                      type="checkbox"
                      checked={selectedModules.includes(m.code)}
                      onChange={() => toggleModule(m.code)}
                    />
                    {m.label}
                  </label>
                ))}
              </div>
              <button type="submit" className="w-full rounded-lg bg-red-600 py-2 font-semibold text-white hover:bg-red-700">
                Salvar Plano
              </button>
            </form>
          </section>
        </div>
      )}
    </PitakoBrandShell>
  );
}
