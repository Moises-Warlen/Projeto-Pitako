"use client";

import { useEffect, useState } from "react";
import { PitakoBrandShell } from "@/components/PitakoBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

interface PlanItem {
  id: string;
  code: string;
  name: string;
  price: number;
  currency: string;
  isActive: boolean;
  unlimitedEstablishments: boolean;
  maxEstablishments: number | null;
  modules: string[];
}

export default function MasterPanelPage() {
  const [plans, setPlans] = useState<PlanItem[]>([]);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [price, setPrice] = useState("399.90");
  const [isUnlimited, setIsUnlimited] = useState(true);

  async function refreshPlans() {
    const session = loadSession();
    if (!session || session.scope !== "Saas") {
      setFeedback("Faça login como operador Master SaaS em /login para gerenciar planos ao vivo.");
      return;
    }

    const res = await apiFetch<PlanItem[]>("/api/v1/master/plans", {}, session.accessToken);
    if (res.ok && res.data) {
      setPlans(res.data);
      setFeedback(null);
    } else {
      setFeedback(res.errorMessage || "Não foi possível carregar planos.");
    }
  }

  useEffect(() => {
    refreshPlans();
  }, []);

  async function handleCreatePlan(e: React.FormEvent) {
    e.preventDefault();
    const session = loadSession();
    if (!session) return;

    const res = await apiFetch(
      "/api/v1/master/plans",
      {
        method: "POST",
        body: JSON.stringify({
          code,
          name,
          priceAmount: Number(price),
          isUnlimited
        })
      },
      session.accessToken
    );

    if (res.ok) {
      setCode("");
      setName("");
      setFeedback("Plano criado e habilitado no catálogo SaaS.");
      refreshPlans();
    } else {
      setFeedback(res.errorMessage || "Erro ao criar plano.");
    }
  }

  return (
    <PitakoBrandShell
      title="Painel Administrativo Master — PITAKO"
      subtitle="Administração centralizada de planos configuráveis (com suporte real ao Plano Ilimitado sem teto artificial), assinaturas e saúde multi-tenant."
    >
      {feedback && (
        <div className="mb-6 rounded-xl border border-amber-500/40 bg-amber-950/30 p-4 text-sm text-amber-200">
          {feedback}
        </div>
      )}

      <div className="grid gap-8 lg:grid-cols-3">
        <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5 lg:col-span-2">
          <h2 className="mb-4 text-lg font-semibold text-white">Planos Comerciais Configuráveis</h2>
          {plans.length === 0 ? (
            <p className="text-sm text-zinc-400">
              Nenhum plano carregado. Conecte-se como operador Master em <code>/login</code> com a API ativa.
            </p>
          ) : (
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
                    <strong>
                      {p.unlimitedEstablishments ? "Ilimitado (sem teto artificial)" : `${p.maxEstablishments}`}
                    </strong>{" "}
                    · Módulos ativos: {p.modules.length}
                  </p>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5">
          <h2 className="mb-4 text-lg font-semibold text-white">Novo Plano SaaS</h2>
          <form onSubmit={handleCreatePlan} className="space-y-3">
            <input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              required
              placeholder="Código (ex: enterprise-ilimitado)"
              className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm text-white"
            />
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              placeholder="Nome comercial"
              className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm text-white"
            />
            <input
              type="number"
              step="0.01"
              value={price}
              onChange={(e) => setPrice(e.target.value)}
              required
              className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm text-white"
            />
            <label className="flex items-center gap-2 text-sm text-zinc-200">
              <input
                type="checkbox"
                checked={isUnlimited}
                onChange={(e) => setIsUnlimited(e.target.checked)}
              />
              Plano Ilimitado Real (sem teto numérico)
            </label>
            <button
              type="submit"
              className="w-full rounded-lg bg-red-600 py-2 text-sm font-semibold text-white hover:bg-red-700"
            >
              Salvar Plano
            </button>
          </form>
        </section>
      </div>
    </PitakoBrandShell>
  );
}
