"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { PitakoBrandShell } from "@/components/PitakoBrandShell";
import { apiFetch, saveSession } from "@/lib/api";

interface LoginApiResponse {
  accessToken: string;
  scope: number;
  tenantId?: string | null;
}

export default function LoginPage() {
  const router = useRouter();
  const [mode, setMode] = useState<"store" | "saas">("store");
  const [storeSlug, setStoreSlug] = useState("acai-demo");
  const [email, setEmail] = useState("admin@tropicalexpress.com.br");
  const [password, setPassword] = useState("");
  const [statusMsg, setStatusMsg] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setStatusMsg(null);

    const endpoint = mode === "saas" ? "/api/v1/auth/login-saas" : "/api/v1/auth/login-store";
    const body = mode === "saas" ? { email, password } : { storeSlug, email, password };

    const res = await apiFetch<LoginApiResponse>(endpoint, {
      method: "POST",
      body: JSON.stringify(body)
    });

    setLoading(false);
    if (!res.ok || !res.data) {
      setStatusMsg(res.errorMessage || "Falha na autenticação.");
      return;
    }

    saveSession({
      accessToken: res.data.accessToken,
      scope: mode === "saas" ? "Saas" : "Establishment",
      tenantId: res.data.tenantId ?? undefined,
      storeSlug: mode === "store" ? storeSlug : undefined,
      email
    });

    router.push(mode === "saas" ? "/master" : "/admin");
  }

  return (
    <PitakoBrandShell
      title="Autenticação Segura"
      subtitle="Sessões individuais separadas entre equipe SaaS PITAKO e operadores dos estabelecimentos."
    >
      <div className="mx-auto max-w-md rounded-2xl border border-zinc-800 bg-zinc-900 p-6">
        <div className="mb-5 grid grid-cols-2 gap-2 rounded-lg bg-zinc-950 p-1">
          <button
            type="button"
            onClick={() => {
              setMode("store");
              setEmail("admin@tropicalexpress.com.br");
            }}
            className={`rounded-md py-2 text-sm font-medium ${
              mode === "store" ? "bg-red-600 text-white" : "text-zinc-400 hover:text-white"
            }`}
          >
            Loja / Estabelecimento
          </button>
          <button
            type="button"
            onClick={() => {
              setMode("saas");
              setEmail("master@pitako.com.br");
            }}
            className={`rounded-md py-2 text-sm font-medium ${
              mode === "saas" ? "bg-red-600 text-white" : "text-zinc-400 hover:text-white"
            }`}
          >
            Painel Master SaaS
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          {mode === "store" && (
            <div>
              <label className="mb-1 block text-xs font-medium text-zinc-300">Slug da Loja</label>
              <input
                value={storeSlug}
                onChange={(e) => setStoreSlug(e.target.value)}
                required
                className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm text-white"
                placeholder="ex: acai-demo"
              />
            </div>
          )}

          <div>
            <label className="mb-1 block text-xs font-medium text-zinc-300">E-mail</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm text-white"
            />
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-zinc-300">Senha</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm text-white"
            />
          </div>

          {statusMsg && (
            <div className="rounded-lg border border-red-500/40 bg-red-950/50 p-3 text-xs text-red-300">
              {statusMsg}
            </div>
          )}

          <button
            type="submit"
            disabled={loading}
            className="w-full rounded-lg bg-red-600 py-2.5 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-50"
          >
            {loading ? "Validando sessão..." : "Entrar"}
          </button>
        </form>
      </div>
    </PitakoBrandShell>
  );
}
