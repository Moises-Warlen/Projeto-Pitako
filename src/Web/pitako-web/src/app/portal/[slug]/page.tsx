"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { LockKeyhole, ExternalLink } from "lucide-react";
import { StoreBrandShell, StoreBrandTheme } from "@/components/StoreBrandShell";
import { apiFetch, saveSession } from "@/lib/api";

interface PublicStoreInfo {
  tradeName: string;
  formattedCnpj?: string;
  branding?: {
    logoUrl?: string | null;
    slogan?: string | null;
    primaryColorHex: string;
    secondaryColorHex: string;
    accentColorHex: string;
  };
}

interface LoginApiResponse {
  accessToken: string;
  scope: number;
  tenantId?: string | null;
}

export default function StorePortalLoginPage({ params }: { params: { slug: string } }) {
  const router = useRouter();
  const slug = decodeURIComponent(params.slug).trim().toLowerCase();

  const [theme, setTheme] = useState<StoreBrandTheme>({
    tradeName: `Portal ${slug}`,
    primaryColorHex: "#1E293B",
    secondaryColorHex: "#F8FAFC",
    accentColorHex: "#0F766E"
  });
  const [storeNotFound, setStoreNotFound] = useState(false);
  const [slogan, setSlogan] = useState<string | null>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [statusMsg, setStatusMsg] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    apiFetch<PublicStoreInfo>(`/api/v1/public/store/${encodeURIComponent(slug)}`).then((res) => {
      if (res.status === 404) {
        setStoreNotFound(true);
        setStatusMsg(`Estabelecimento "${slug}" não foi encontrado.`);
        return;
      }
      if (!res.ok) {
        setStatusMsg(res.errorMessage || "Não foi possível conectar ao servidor da loja.");
        return;
      }
      if (res.data) {
        setStoreNotFound(false);
        setSlogan(res.data.branding?.slogan ?? null);
        setTheme({
          tradeName: res.data.tradeName,
          formattedCnpj: res.data.formattedCnpj,
          logoUrl: res.data.branding?.logoUrl,
          primaryColorHex: res.data.branding?.primaryColorHex || "#1E293B",
          secondaryColorHex: res.data.branding?.secondaryColorHex || "#F8FAFC",
          accentColorHex: res.data.branding?.accentColorHex || "#0F766E"
        });
      }
    });
  }, [slug]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setStatusMsg(null);

    try {
      const res = await apiFetch<LoginApiResponse>("/api/v1/auth/login-store", {
        method: "POST",
        body: JSON.stringify({
          storeSlug: slug,
          email,
          password
        })
      });

      if (!res.ok || !res.data) {
        setStatusMsg(res.errorMessage || "E-mail ou senha inválidos para esta loja.");
        return;
      }

      saveSession({
        accessToken: res.data.accessToken,
        scope: "Establishment",
        tenantId: res.data.tenantId ?? undefined,
        storeSlug: slug,
        email
      });

      router.push("/admin");
    } finally {
      setLoading(false);
    }
  }

  return (
    <StoreBrandShell theme={theme} hideOpsNav={true}>
      <div className="mx-auto max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        <div className="mb-5 flex items-center gap-3 border-b border-slate-100 pb-4">
          <div
            style={{ backgroundColor: theme.primaryColorHex }}
            className="flex h-10 w-10 items-center justify-center rounded-xl text-white"
          >
            <LockKeyhole className="h-5 w-5" />
          </div>
          <div>
            <h1 className="text-lg font-bold text-slate-900">Portal do Estabelecimento</h1>
            <p className="text-xs text-slate-500">
              {slogan || `Acesso exclusivo da equipe · Loja ${slug}`}
            </p>
          </div>
        </div>

        {storeNotFound ? (
          <div className="space-y-4">
            <div className="rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">
              {statusMsg}
            </div>
            <Link
              href="/login"
              className="inline-block rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-800"
            >
              Ir para Login Geral
            </Link>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-600">
              Loja conectada: <strong className="text-slate-900">{theme.tradeName}</strong> (<code>{slug}</code>)
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium text-slate-700">E-mail do Usuário</label>
              <input
                type="email"
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                placeholder="seu-email@sua-loja.com.br"
                className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-slate-500 focus:outline-none"
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium text-slate-700">Senha</label>
              <input
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                placeholder="Digite sua senha"
                className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-slate-500 focus:outline-none"
              />
            </div>

            {statusMsg && (
              <div className="rounded-lg border border-rose-200 bg-rose-50 p-3 text-xs text-rose-700">
                {statusMsg}
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              style={{ backgroundColor: theme.primaryColorHex }}
              className="w-full rounded-lg py-2.5 text-sm font-semibold text-white transition hover:opacity-95 disabled:opacity-50"
            >
              {loading ? "Entrando..." : "Entrar no Painel da Loja"}
            </button>
          </form>
        )}

        <div className="mt-5 flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-4 text-xs text-slate-500">
          <Link href={`/loja/${slug}`} className="inline-flex items-center gap-1 font-medium text-slate-700 hover:underline">
            Ver Cardápio Público <ExternalLink className="h-3.5 w-3.5" />
          </Link>
          <Link href="/login" className="hover:underline">
            Entrar com outra conta
          </Link>
        </div>
      </div>
    </StoreBrandShell>
  );
}
