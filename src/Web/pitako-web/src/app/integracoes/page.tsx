"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { StoreBrandShell } from "@/components/StoreBrandShell";
import { API_BASE_URL, apiFetch, loadSession } from "@/lib/api";

interface CapabilityDto {
  provider: number;
  providerKey: string;
  displayName: string;
  authKind: string;
  supportsOrderConfirmation: boolean;
  supportsOrderDispatch: boolean;
  supportsOrderCancellation: boolean;
  supportsItemPauseToggle: boolean;
  supportsCatalogPriceSync: boolean;
  supportsStoreOpenCloseToggle: boolean;
  supportsWebhookHmacSignature: boolean;
  supportsBulkMenuActions: boolean;
}

interface PartnerAccountDto {
  id: string;
  provider: number;
  providerName: string;
  accountLabel: string;
  clientOrMerchantIdentifier: string;
  isActive: boolean;
  lastHealthCheckUtc?: string | null;
}

interface StoreBindingDto {
  id: string;
  partnerAccountId: string;
  provider: number;
  providerName: string;
  externalMerchantId: string;
  externalStoreName: string;
  status: string;
  statusCode: number;
  autoAcceptOrders: boolean;
  lastSyncUtc?: string | null;
  lastError?: string | null;
}

interface InboundEventDto {
  id: string;
  provider: string;
  externalEventId: string;
  externalOrderId: string;
  externalMerchantId: string;
  eventType: string;
  syncStatus: string;
  signatureVerified: boolean;
  normalizedCustomerName?: string | null;
  normalizedTotal: number;
  internalOrderId?: string | null;
  failureReason?: string | null;
  receivedAtUtc: string;
}

interface CatalogMappingDto {
  id: string;
  storeBindingId: string;
  internalProductId: string;
  internalItemTitle: string;
  externalSkuCode: string;
  lastSyncedPrice: number;
  isPausedExternally: boolean;
}

interface OverviewDto {
  storeSlug: string;
  storeTradeName: string;
  capabilities: CapabilityDto[];
  accounts: PartnerAccountDto[];
  bindings: StoreBindingDto[];
  inboundEvents: InboundEventDto[];
  mappings: CatalogMappingDto[];
}

interface CatalogQuickProduct {
  id: string;
  name: string;
  basePrice: number;
}

function brl(v?: number | null): string {
  return Number(v ?? 0).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

export default function StoreMarketplacesPage() {
  const [token, setToken] = useState("");
  const [overview, setOverview] = useState<OverviewDto | null>(null);
  const [catalogProducts, setCatalogProducts] = useState<CatalogQuickProduct[]>([]);
  const [loading, setLoading] = useState(true);
  const [msg, setMsg] = useState<{ type: "ok" | "err" | "info"; text: string } | null>(null);

  useEffect(() => {
    if (!msg) return;
    const t = setTimeout(() => setMsg(null), 2600);
    return () => clearTimeout(t);
  }, [msg]);

  // Connect Account form
  const [provider, setProvider] = useState<number>(1);
  const [accountLabel, setAccountLabel] = useState("Conta Principal iFood");
  const [clientId, setClientId] = useState("");
  const [rawSecret, setRawSecret] = useState("");

  // Bind external store form
  const [bindAccountId, setBindAccountId] = useState("");
  const [extMerchantId, setExtMerchantId] = useState("");
  const [extStoreName, setExtStoreName] = useState("");
  const [autoAcceptOrders, setAutoAcceptOrders] = useState(true);

  // Catalog SKU mapping form
  const [mapBindingId, setMapBindingId] = useState("");
  const [mapProductId, setMapProductId] = useState("");
  const [mapExternalSku, setMapExternalSku] = useState("");

  // Simulate order customer name
  const [simCustomer, setSimCustomer] = useState("");

  const loadData = useCallback(async () => {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") {
      setToken("");
      setLoading(false);
      setMsg({
        type: "info",
        text: "Entre com o usuário da loja (/login ou /portal/[slug]) para gerenciar as integrações iFood, 99Food, Rappi e webhooks desta loja.",
      });
      return;
    }

    setToken(session.accessToken);
    setLoading(true);
    const [ovRes, catRes] = await Promise.all([
      apiFetch<OverviewDto>("/api/v1/marketplaces/overview", {}, session.accessToken),
      apiFetch<{ products: CatalogQuickProduct[] }>("/api/v1/catalog/products", {}, session.accessToken),
    ]);

    if (ovRes.ok && ovRes.data) {
      setOverview(ovRes.data);
      if (ovRes.data.accounts.length > 0 && !bindAccountId) {
        setBindAccountId(ovRes.data.accounts[0].id);
      }
      if (ovRes.data.bindings.length > 0 && !mapBindingId) {
        setMapBindingId(ovRes.data.bindings[0].id);
      }
      setMsg(null);
    } else {
      setMsg({ type: "err", text: ovRes.errorMessage || "Erro ao carregar integrações da loja." });
    }

    if (catRes.ok && catRes.data?.products) {
      setCatalogProducts(catRes.data.products);
      if (catRes.data.products.length > 0 && !mapProductId) {
        setMapProductId(catRes.data.products[0].id);
      }
    }
    setLoading(false);
  }, [bindAccountId, mapBindingId, mapProductId]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  async function handleConnectAccount(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch<PartnerAccountDto>(
      "/api/v1/marketplaces/accounts",
      {
        method: "POST",
        body: JSON.stringify({
          provider,
          accountLabel: accountLabel.trim(),
          clientOrMerchantIdentifier: clientId.trim(),
          rawSecretPayload: rawSecret.trim(),
        }),
      },
      token
    );
    if (res.ok && res.data) {
      setClientId("");
      setRawSecret("");
      setBindAccountId(res.data.id);
      setMsg({ type: "ok", text: `Credencial ${res.data.providerName} salva e cifrada para esta loja!` });
      await loadData();
    } else {
      setMsg({ type: "err", text: res.errorMessage || "Erro ao conectar conta parceira." });
    }
  }

  async function handleBindStore(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch<StoreBindingDto>(
      "/api/v1/marketplaces/bindings",
      {
        method: "POST",
        body: JSON.stringify({
          partnerAccountId: bindAccountId,
          externalMerchantId: extMerchantId.trim(),
          externalStoreName: extStoreName.trim(),
          autoAcceptOrders,
        }),
      },
      token
    );
    if (res.ok && res.data) {
      setExtMerchantId("");
      setExtStoreName("");
      setMapBindingId(res.data.id);
      setMsg({ type: "ok", text: `Loja '${res.data.externalStoreName}' vinculada com sucesso em ${res.data.providerName}!` });
      await loadData();
    } else {
      setMsg({ type: "err", text: res.errorMessage || "Erro ao vincular loja externa." });
    }
  }

  async function handleMapCatalog(e: React.FormEvent) {
    e.preventDefault();
    const prod = catalogProducts.find((p) => p.id === mapProductId);
    if (!prod || !mapBindingId) return;
    const res = await apiFetch(
      "/api/v1/marketplaces/catalog-mappings",
      {
        method: "POST",
        body: JSON.stringify({
          storeBindingId: mapBindingId,
          internalProductId: prod.id,
          internalItemTitle: prod.name,
          externalSkuCode: mapExternalSku.trim(),
          currentPrice: prod.basePrice,
        }),
      },
      token
    );
    if (res.ok) {
      setMapExternalSku("");
      setMsg({ type: "ok", text: `Produto '${prod.name}' vinculado ao SKU ${mapExternalSku.trim()}.` });
      await loadData();
    } else {
      setMsg({ type: "err", text: res.errorMessage || "Erro ao mapear produto no marketplace." });
    }
  }

  async function handleSimulateOrder(binding: StoreBindingDto) {
    const res = await apiFetch<{ orderNumber: string; provider: string; totalAmount: number }>(
      `/api/v1/marketplaces/bindings/${binding.id}/simulate-order`,
      {
        method: "POST",
        body: JSON.stringify({
          customerName: simCustomer.trim() || `Cliente ${binding.providerName}`,
          itemTitle: catalogProducts[0]?.name || "Açaí Especial 500ml",
          unitPrice: catalogProducts[0]?.basePrice || 26.9,
          deliveryFee: 5.0,
        }),
      },
      token
    );
    if (res.ok && res.data) {
      setMsg({
        type: "ok",
        text: `Pedido ${res.data.orderNumber} (${res.data.provider}) recebido com HMAC válido e enviado ao Kanban /pedidos (${brl(res.data.totalAmount)})!`,
      });
      await loadData();
    } else {
      setMsg({ type: "err", text: res.errorMessage || "Erro ao receber pedido do marketplace." });
    }
  }

  async function handleToggleRemoteStore(binding: StoreBindingDto, pauseStore: boolean) {
    const res = await apiFetch<{ succeededCount?: number; failedCount?: number }>(
      `/api/v1/marketplaces/bindings/${binding.id}/toggle-store`,
      {
        method: "POST",
        body: JSON.stringify({ pauseStore, reason: "Ajuste manual no painel da loja" }),
      },
      token
    );
    if (res.ok) {
      setMsg({
        type: "info",
        text: `Solicitação enviada para ${binding.providerName} (${pauseStore ? "Pausar" : "Abrir"}). Sem credencial OAuth de produção configurada no servidor externo, o gateway registra o retorno remoto com transparência.`,
      });
      await loadData();
    } else {
      setMsg({ type: "err", text: res.errorMessage || "Erro ao acionar gateway remoto." });
    }
  }

  const webhookBase = overview?.storeSlug
    ? `${API_BASE_URL}/api/v1/public/webhooks/marketplaces/{provedor}/${overview.storeSlug}`
    : `${API_BASE_URL}/api/v1/public/webhooks/marketplaces/{provedor}/{slug-da-loja}`;

  return (
    <StoreBrandShell activeSection="/integracoes">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-xl font-extrabold text-slate-900">Integrações & Marketplaces da Loja</h1>
            {overview?.storeTradeName && (
              <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-bold text-slate-700">
                {overview.storeTradeName}
              </span>
            )}
          </div>
          <p className="text-xs text-slate-500">
            Cada estabelecimento possui suas próprias credenciais cifradas (AES-GCM), vinculações iFood, 99Food, Rappi, Aiqfome, Delivery Much, Keeta e Food To Save, webhooks exclusivos e entrada direta no Kanban.
          </p>
        </div>
        {token && (
          <div className="flex flex-wrap gap-2">
            <Link
              href="/pedidos"
              className="rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs font-bold text-slate-800 hover:bg-slate-50"
            >
              Ver Pedidos no Kanban (/pedidos)
            </Link>
            <button
              onClick={() => void loadData()}
              className="rounded-xl bg-slate-900 px-3.5 py-2 text-xs font-bold text-white hover:bg-slate-800"
            >
              Atualizar
            </button>
          </div>
        )}
      </div>

      {!token && (
        <div className="mb-6 flex flex-wrap items-center justify-between gap-4 rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950">
          <p>{msg?.text}</p>
          <Link href="/login" className="rounded-lg bg-slate-900 px-4 py-2 text-xs font-bold text-white">
            Entrar no Painel da Loja (/login)
          </Link>
        </div>
      )}

      {token && msg && (
        <div
          className={`mb-5 rounded-xl border p-3.5 text-xs font-semibold ${
            msg.type === "ok"
              ? "border-emerald-200 bg-emerald-50 text-emerald-900"
              : msg.type === "err"
                ? "border-rose-200 bg-rose-50 text-rose-900"
                : "border-slate-200 bg-white text-slate-800"
          }`}
        >
          {msg.text}
        </div>
      )}

      {token && (
        <>
          {/* Webhook Endpoint Banner per Store */}
          <div className="mb-6 rounded-2xl border border-indigo-200 bg-indigo-50/70 p-4 text-xs text-indigo-950">
            <div className="font-bold">Endpoint Webhook Exclusivo desta Loja ({overview?.storeSlug})</div>
            <p className="mt-0.5 text-indigo-800">
              Configure nos portais de desenvolvedor iFood / 99Food / Rappi com validação HMAC-SHA256 automática por tenant:
            </p>
            <code className="mt-2 block overflow-x-auto rounded-lg border border-indigo-200 bg-white px-3 py-2 font-mono text-[11px] text-slate-800">
              POST {webhookBase}
            </code>
          </div>

          {/* Partners Capability Matrix Grid */}
          <section className="mb-6">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-base font-extrabold text-slate-900">Conectores Disponíveis para esta Loja</h2>
              <span className="text-xs text-slate-500">
                Clique em qualquer marketplace abaixo para adicionar uma ou várias contas / lojas vinculadas
              </span>
            </div>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {(overview?.capabilities ?? []).map((cap) => {
                const countBindings = (overview?.bindings ?? []).filter((b) => b.provider === cap.provider).length;
                const isSelected = provider === cap.provider;
                return (
                  <button
                    type="button"
                    key={cap.provider}
                    onClick={() => {
                      setProvider(cap.provider);
                      const nextIndex = countBindings + 1;
                      const storeName = overview?.storeTradeName || "Loja";
                      setAccountLabel(`Conta ${cap.displayName} #${nextIndex} — ${storeName}`);
                      setExtMerchantId(
                        `MERCH-${cap.displayName.toUpperCase().replace(/[^A-Z0-9]/g, "")}-${String(nextIndex).padStart(2, "0")}`
                      );
                      setExtStoreName(`${storeName} (${cap.displayName}${nextIndex > 1 ? ` #${nextIndex}` : ""})`);
                      const el = document.getElementById("marketplace-connect-form");
                      el?.scrollIntoView({ behavior: "smooth", block: "center" });
                    }}
                    className={`rounded-2xl border p-4 text-left shadow-sm transition ${
                      isSelected
                        ? "border-slate-900 bg-slate-50 ring-2 ring-slate-900/15"
                        : "border-slate-200 bg-white hover:border-slate-400"
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-sm font-extrabold text-slate-900">{cap.displayName}</span>
                      <span
                        className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${
                          countBindings > 0 ? "bg-emerald-100 text-emerald-800" : "bg-slate-100 text-slate-600"
                        }`}
                      >
                        {countBindings > 0 ? `${countBindings} loja(s) ativa(s)` : "Disponível"}
                      </span>
                    </div>
                    <p className="mt-1 text-[11px] text-slate-500">Auth: {cap.authKind}</p>
                    <div className="mt-2.5 flex flex-wrap gap-1 text-[10px] font-semibold">
                      {cap.supportsOrderConfirmation && (
                        <span className="rounded bg-slate-100 px-1.5 py-0.5 text-slate-700">Pedidos</span>
                      )}
                      {cap.supportsCatalogPriceSync && (
                        <span className="rounded bg-slate-100 px-1.5 py-0.5 text-slate-700">Preços/Cardápio</span>
                      )}
                      {cap.supportsWebhookHmacSignature && (
                        <span className="rounded bg-emerald-50 px-1.5 py-0.5 text-emerald-700">HMAC-SHA256</span>
                      )}
                    </div>
                    <div className="mt-2.5 text-[11px] font-bold text-indigo-700">
                      {isSelected ? "Selecionado no formulário →" : "+ Adicionar conta / loja"}
                    </div>
                  </button>
                );
              })}
            </div>
          </section>

          {/* Accounts + Bindings Management */}
          <div id="marketplace-connect-form" className="grid gap-6 lg:grid-cols-12">
            {/* Step 1: Partner Credentials */}
            <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-5">
              <h2 className="mb-3 text-base font-extrabold text-slate-900">1. Conectar Credencial de Parceiro</h2>
              <form onSubmit={handleConnectAccount} className="space-y-3 text-xs">
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Marketplace</label>
                  <select
                    value={provider}
                    onChange={(e) => {
                      const p = Number(e.target.value);
                      setProvider(p);
                      const cap = overview?.capabilities.find((c) => c.provider === p);
                      if (cap) setAccountLabel(`Conta ${cap.displayName} — ${overview?.storeTradeName || "Loja"}`);
                    }}
                    className="w-full rounded-xl border border-slate-300 px-3 py-2 font-semibold"
                  >
                    {(overview?.capabilities ?? []).map((c) => (
                      <option key={c.provider} value={c.provider}>
                        {c.displayName} ({c.authKind})
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Apelido da Conta</label>
                  <input
                    required
                    value={accountLabel}
                    onChange={(e) => setAccountLabel(e.target.value)}
                    className="w-full rounded-xl border border-slate-300 px-3 py-2"
                  />
                </div>
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Client ID / App ID / Identificador</label>
                  <input
                    required
                    value={clientId}
                    onChange={(e) => setClientId(e.target.value)}
                    placeholder="Ex: ifood-client-uuid-ou-app-id"
                    className="w-full rounded-xl border border-slate-300 px-3 py-2 font-mono"
                  />
                </div>
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Client Secret / Token / Chave HMAC</label>
                  <input
                    required
                    type="password"
                    autoComplete="off"
                    value={rawSecret}
                    onChange={(e) => setRawSecret(e.target.value)}
                    placeholder="Cifrado em AES-GCM no servidor"
                    className="w-full rounded-xl border border-slate-300 px-3 py-2 font-mono"
                  />
                </div>
                <button
                  type="submit"
                  className="w-full rounded-xl bg-slate-900 py-2.5 text-xs font-bold text-white hover:bg-slate-800"
                >
                  Salvar Credencial Cifrada
                </button>
              </form>

              <div className="mt-5 border-t border-slate-100 pt-3">
                <h3 className="mb-2 text-xs font-bold text-slate-800">
                  Contas Salvas desta Loja ({overview?.accounts.length ?? 0})
                </h3>
                <div className="space-y-1.5 text-xs">
                  {(overview?.accounts ?? []).map((acc) => (
                    <div key={acc.id} className="flex items-center justify-between rounded-lg bg-slate-50 px-3 py-2">
                      <div>
                        <span className="font-bold text-slate-900">{acc.providerName}</span> · {acc.accountLabel}
                        <div className="font-mono text-[10px] text-slate-500">{acc.clientOrMerchantIdentifier}</div>
                      </div>
                      <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-bold text-emerald-800">
                        {acc.isActive ? "Ativa" : "Inativa"}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            </section>

            {/* Step 2: Store Bindings + Test Order Ingestion */}
            <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-7">
              <h2 className="mb-3 text-base font-extrabold text-slate-900">2. Lojas Vinculadas & Entrada ao Vivo no Kanban</h2>
              <form onSubmit={handleBindStore} className="grid gap-2.5 rounded-xl bg-slate-50 p-3.5 text-xs sm:grid-cols-2">
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Conta Parceira</label>
                  <select
                    required
                    value={bindAccountId}
                    onChange={(e) => setBindAccountId(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 bg-white px-2.5 py-2"
                  >
                    {(overview?.accounts ?? []).length === 0 && <option value="">Conecte uma conta primeiro</option>}
                    {(overview?.accounts ?? []).map((acc) => (
                      <option key={acc.id} value={acc.id}>
                        {acc.providerName} — {acc.accountLabel}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Merchant ID no Marketplace</label>
                  <input
                    required
                    value={extMerchantId}
                    onChange={(e) => setExtMerchantId(e.target.value)}
                    placeholder="Ex: MERCH-99FOOD-01"
                    className="w-full rounded-lg border border-slate-300 bg-white px-2.5 py-2 font-mono"
                  />
                </div>
                <div>
                  <label className="mb-1 block font-semibold text-slate-700">Nome da Loja no App</label>
                  <input
                    required
                    value={extStoreName}
                    onChange={(e) => setExtStoreName(e.target.value)}
                    placeholder="Ex: Minha Loja (99Food)"
                    className="w-full rounded-lg border border-slate-300 bg-white px-2.5 py-2"
                  />
                </div>
                <div className="flex items-end justify-between gap-2">
                  <label className="flex cursor-pointer items-center gap-1.5 font-semibold text-slate-700">
                    <input
                      type="checkbox"
                      checked={autoAcceptOrders}
                      onChange={(e) => setAutoAcceptOrders(e.target.checked)}
                      className="h-4 w-4 rounded accent-slate-900"
                    />
                    Aceite Automático
                  </label>
                  <button
                    type="submit"
                    className="rounded-lg bg-slate-900 px-3.5 py-2 font-bold text-white hover:bg-slate-800"
                  >
                    + Vincular Loja
                  </button>
                </div>
              </form>

              <div className="mt-4 flex flex-wrap items-center justify-between gap-2 text-xs">
                <span className="font-bold text-slate-800">
                  Vínculos Ativos ({overview?.bindings.length ?? 0})
                </span>
                <input
                  value={simCustomer}
                  onChange={(e) => setSimCustomer(e.target.value)}
                  placeholder="Nome cliente p/ testar pedido"
                  className="w-52 rounded-lg border border-slate-300 px-2.5 py-1 text-xs"
                />
              </div>

              <div className="mt-2.5 space-y-2.5">
                {loading ? (
                  <p className="text-xs text-slate-400">Carregando...</p>
                ) : (overview?.bindings ?? []).length === 0 ? (
                  <p className="rounded-xl border border-dashed border-slate-200 p-5 text-center text-xs text-slate-500">
                    Nenhuma loja de marketplace vinculada ainda para este estabelecimento.
                  </p>
                ) : (
                  (overview?.bindings ?? []).map((b) => (
                    <div key={b.id} className="rounded-xl border border-slate-200 p-3.5 text-xs">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div>
                          <span className="rounded bg-slate-900 px-2 py-0.5 text-[10px] font-bold text-white">
                            {b.providerName}
                          </span>{" "}
                          <strong className="text-sm text-slate-900">{b.externalStoreName}</strong>
                          <div className="mt-0.5 font-mono text-[11px] text-slate-500">
                            Merchant: {b.externalMerchantId} · Status: {b.status} ·{" "}
                            {b.autoAcceptOrders ? "Aceite Automático ON" : "Aceite Manual"}
                          </div>
                        </div>
                        <div className="flex flex-wrap gap-1.5">
                          <button
                            onClick={() => void handleSimulateOrder(b)}
                            className="rounded-lg bg-emerald-600 px-3 py-1.5 text-[11px] font-bold text-white hover:bg-emerald-700"
                          >
                            Testar Pedido ({b.providerName} → /pedidos)
                          </button>
                          <button
                            onClick={() => void handleToggleRemoteStore(b, b.status !== "Paused")}
                            className="rounded-lg border border-slate-300 px-2.5 py-1.5 text-[11px] font-semibold text-slate-700 hover:bg-slate-50"
                          >
                            {b.status === "Paused" ? "Reabrir Loja" : "Pausar Loja"}
                          </button>
                        </div>
                      </div>
                      {b.lastError && (
                        <p className="mt-2 rounded bg-amber-50 px-2 py-1 text-[11px] text-amber-900">
                          Último retorno gateway: {b.lastError}
                        </p>
                      )}
                    </div>
                  ))
                )}
              </div>
            </section>
          </div>

          {/* Step 3 & 4: SKU Mapping + Inbound Events Log */}
          <div className="mt-6 grid gap-6 lg:grid-cols-2">
            <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h2 className="mb-3 text-base font-extrabold text-slate-900">3. De-Para de Produtos (Cardápio ↔ SKU App)</h2>
              <form onSubmit={handleMapCatalog} className="grid gap-2 text-xs sm:grid-cols-3">
                <select
                  required
                  value={mapBindingId}
                  onChange={(e) => setMapBindingId(e.target.value)}
                  className="rounded-lg border border-slate-300 px-2.5 py-2"
                >
                  {(overview?.bindings ?? []).map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.providerName} · {b.externalStoreName}
                    </option>
                  ))}
                </select>
                <select
                  required
                  value={mapProductId}
                  onChange={(e) => setMapProductId(e.target.value)}
                  className="rounded-lg border border-slate-300 px-2.5 py-2"
                >
                  {catalogProducts.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} ({brl(p.basePrice)})
                    </option>
                  ))}
                </select>
                <div className="flex gap-1.5">
                  <input
                    required
                    value={mapExternalSku}
                    onChange={(e) => setMapExternalSku(e.target.value)}
                    placeholder="SKU Externo"
                    className="min-w-0 flex-1 rounded-lg border border-slate-300 px-2.5 py-2 font-mono"
                  />
                  <button
                    type="submit"
                    className="rounded-lg bg-slate-900 px-3 py-2 font-bold text-white"
                  >
                    Mapear
                  </button>
                </div>
              </form>

              <div className="mt-3 max-h-48 space-y-1.5 overflow-y-auto text-xs">
                {(overview?.mappings ?? []).map((m) => (
                  <div key={m.id} className="flex items-center justify-between rounded-lg bg-slate-50 px-3 py-2">
                    <span className="font-semibold text-slate-900">{m.internalItemTitle}</span>
                    <span className="font-mono text-slate-600">SKU: {m.externalSkuCode}</span>
                    <span className="font-mono font-bold text-emerald-700">{brl(m.lastSyncedPrice)}</span>
                  </div>
                ))}
              </div>
            </section>

            <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <h2 className="mb-3 text-base font-extrabold text-slate-900">
                4. Eventos Recebidos & Webhooks ({overview?.inboundEvents.length ?? 0})
              </h2>
              {(overview?.inboundEvents ?? []).length === 0 ? (
                <p className="text-xs text-slate-500">
                  Clique em &quot;Testar Pedido&quot; ou envie um webhook assinado com HMAC-SHA256 para visualizar os pedidos normalizados aqui e no Kanban.
                </p>
              ) : (
                <div className="max-h-56 space-y-2 overflow-y-auto text-xs">
                  {(overview?.inboundEvents ?? []).map((ev) => (
                    <div key={ev.id} className="rounded-xl border border-slate-200 p-2.5">
                      <div className="flex items-center justify-between">
                        <span className="font-bold text-slate-900">
                          {ev.provider} · {ev.externalOrderId}
                        </span>
                        <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-bold text-emerald-800">
                          {ev.signatureVerified ? "HMAC OK" : "Sem HMAC"} · {ev.syncStatus}
                        </span>
                      </div>
                      <div className="mt-1 flex justify-between text-[11px] text-slate-600">
                        <span>{ev.normalizedCustomerName || "Cliente"}</span>
                        <span className="font-mono font-bold">{brl(ev.normalizedTotal)}</span>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </section>
          </div>
        </>
      )}
    </StoreBrandShell>
  );
}
