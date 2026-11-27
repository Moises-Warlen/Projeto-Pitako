"use client";

import Link from "next/link";
import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { PitakoBrandShell } from "@/components/PitakoBrandShell";
import { apiFetch } from "@/lib/api";

function ResetPasswordInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [token, setToken] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [msg, setMsg] = useState<{ type: "ok" | "err"; text: string } | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const t = searchParams.get("token");
    if (t) setToken(t);
  }, [searchParams]);

  useEffect(() => {
    if (!msg) return;
    const timer = setTimeout(() => setMsg(null), 2800);
    return () => clearTimeout(timer);
  }, [msg]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (newPassword !== confirmPassword) {
      setMsg({ type: "err", text: "As senhas informadas não conferem." });
      return;
    }
    setLoading(true);
    const res = await apiFetch<{ success: boolean }>(
      "/api/v1/auth/password-reset/complete",
      { method: "POST", body: JSON.stringify({ token: token.trim(), newPassword }) }
    );
    setLoading(false);

    if (res.ok) {
      setMsg({ type: "ok", text: "Senha redefinida com sucesso! Redirecionando para o login..." });
      setTimeout(() => router.push("/login"), 2000);
    } else {
      setMsg({ type: "err", text: res.errorMessage || "Token inválido ou senha fora do padrão de segurança." });
    }
  }

  return (
    <div className="mx-auto max-w-md rounded-2xl border border-zinc-800 bg-zinc-900 p-6">
      <h2 className="text-lg font-bold text-white">Definir Nova Senha</h2>
      <p className="mt-1 text-xs text-zinc-400">
        Informe o token recebido no seu e-mail cadastrado e escolha sua nova senha de acesso.
      </p>

      <form onSubmit={handleSubmit} className="mt-5 space-y-4">
        <div>
          <label className="mb-1 block text-xs font-medium text-zinc-300">Token de Recuperação</label>
          <input
            value={token}
            onChange={(e) => setToken(e.target.value)}
            required
            placeholder="Cole o token do e-mail..."
            className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 font-mono text-xs text-white"
          />
        </div>
        <div>
          <label className="mb-1 block text-xs font-medium text-zinc-300">Nova Senha (mín. 8 caracteres, maiúscula, número e símbolo)</label>
          <input
            type="password"
            autoComplete="new-password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            required
            className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm text-white"
          />
        </div>
        <div>
          <label className="mb-1 block text-xs font-medium text-zinc-300">Confirmar Nova Senha</label>
          <input
            type="password"
            autoComplete="new-password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            required
            className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm text-white"
          />
        </div>

        {msg && (
          <div
            className={`rounded-lg border p-3 text-xs ${
              msg.type === "ok"
                ? "border-emerald-500/40 bg-emerald-950/50 text-emerald-200"
                : "border-red-500/40 bg-red-950/50 text-red-300"
            }`}
          >
            {msg.text}
          </div>
        )}

        <button
          type="submit"
          disabled={loading}
          className="w-full rounded-lg bg-red-600 py-2.5 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-50"
        >
          {loading ? "Salvando..." : "Redefinir Senha"}
        </button>

        <div className="pt-2 text-center">
          <Link href="/login" className="text-xs text-zinc-400 hover:text-white underline">
            Voltar para o Login
          </Link>
        </div>
      </form>
    </div>
  );
}

export default function ResetPasswordPage() {
  return (
    <PitakoBrandShell
      title="Recuperação de Acesso"
      subtitle="Redefinição segura com token único enviado para o e-mail cadastrado."
    >
      <Suspense fallback={<p className="text-center text-xs text-zinc-400">Carregando...</p>}>
        <ResetPasswordInner />
      </Suspense>
    </PitakoBrandShell>
  );
}
