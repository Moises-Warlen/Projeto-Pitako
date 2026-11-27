"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
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
  const [forgotOpen, setForgotOpen] = useState(false);
  const [forgotEmail, setForgotEmail] = useState("");
  const [forgotLoading, setForgotLoading] = useState(false);
  const [forgotResult, setForgotResult] = useState<{ text: string; link?: string | null } | null>(null);

  useEffect(() => {
    if (!statusMsg) return;
    const t = setTimeout(() => setStatusMsg(null), 2800);
    return () => clearTimeout(t);
  }, [statusMsg]);

  async function handleForgotSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!forgotEmail.trim()) return;
    setForgotLoading(true);
    setForgotResult(null);
    const res = await apiFetch<{ accepted: boolean; message: string; resetLinkOutbox?: string | null }>(
      "/api/v1/auth/password-recovery",
      {
        method: "POST",
        body: JSON.stringify({
          email: forgotEmail.trim(),
          storeSlug: mode === "store" ? storeSlug.trim() || null : null,
          webBaseUrl: typeof window !== "undefined" ? window.location.origin : "http://localhost:3000"
        })
      }
    );
    setForgotLoading(false);
    if (res.ok && res.data) {
      setForgotResult({ text: res.data.message, link: res.data.resetLinkOutbox });
    } else {
      setForgotResult({ text: res.errorMessage || "Não foi possível enviar a recuperação de senha." });
    }
  }

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const loja = params.get("loja");
    if (loja && loja.trim()) {
      setMode("store");
      setStoreSlug(loja.trim().toLowerCase());
      setEmail("");
    }
  }, []);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setStatusMsg(null);

    const endpoint = mode === "saas" ? "/api/v1/auth/login-saas" : "/api/v1/auth/login-store";
    const body = mode === "saas" ? { email, password } : { storeSlug, email, password };

    try {
      const res = await apiFetch<LoginApiResponse>(endpoint, {
        method: "POST",
        body: JSON.stringify(body)
      });

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
    } finally {
      setLoading(false);
    }
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
              autoComplete="email"
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
              autoComplete="current-password"
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

          <div className="pt-1 text-center">
            <button
              type="button"
              onClick={() => {
                setForgotOpen((prev) => !prev);
                setForgotEmail(email);
                setForgotResult(null);
              }}
              className="text-xs font-medium text-zinc-400 underline hover:text-white"
            >
              Esqueci minha senha
            </button>
          </div>
        </form>

        {forgotOpen && (
          <form onSubmit={handleForgotSubmit} className="mt-5 space-y-3 rounded-xl border border-zinc-800 bg-zinc-950 p-4">
            <p className="text-xs font-bold text-white">Recuperar senha por e-mail cadastrado</p>
            <p className="text-[11px] text-zinc-400">
              Informe seu e-mail cadastrado para receber o link de redefinição da senha no seu e-mail.
            </p>
            <input
              type="email"
              autoComplete="email"
              required
              value={forgotEmail}
              onChange={(e) => setForgotEmail(e.target.value)}
              placeholder="seu@email.com.br"
              className="w-full rounded-lg border border-zinc-700 bg-zinc-900 px-3 py-2 text-xs text-white"
            />
            {forgotResult && (
              <div className="rounded-lg border border-emerald-500/40 bg-emerald-950/50 p-2.5 text-[11px] text-emerald-200">
                <p>{forgotResult.text}</p>
                {forgotResult.link && (
                  <Link
                    href={forgotResult.link.replace(/^https?:\/\/[^/]+/, "")}
                    className="mt-1.5 inline-block font-bold text-amber-300 underline"
                  >
                    Abrir tela de nova senha
                  </Link>
                )}
              </div>
            )}
            <button
              type="submit"
              disabled={forgotLoading}
              className="w-full rounded-lg bg-zinc-800 py-2 text-xs font-semibold text-white hover:bg-zinc-700 disabled:opacity-50"
            >
              {forgotLoading ? "Enviando e-mail..." : "Enviar recuperação para meu e-mail"}
            </button>
          </form>
        )}
      </div>
    </PitakoBrandShell>
  );
}
