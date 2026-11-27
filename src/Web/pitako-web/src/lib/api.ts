export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_URL?.replace(/\/+$/, "") || "http://localhost:5000";

export function resolveMediaUrl(url?: string | null): string | null {
  if (!url) return null;
  const trimmed = url.trim();
  if (!trimmed) return null;
  if (trimmed.startsWith("/api/")) return `${API_BASE_URL}${trimmed}`;
  return trimmed;
}

export async function compressImageFileToDataUrl(file: File, maxDim = 480, quality = 0.72): Promise<string> {
  const dataUrl = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ""));
    reader.onerror = () => reject(new Error("Falha ao ler arquivo selecionado."));
    reader.readAsDataURL(file);
  });

  if (file.type === "image/svg+xml" || dataUrl.length < 45_000) {
    return dataUrl;
  }

  return await new Promise<string>((resolve) => {
    const img = new window.Image();
    img.onload = () => {
      const ratio = Math.min(1, maxDim / Math.max(img.width || 1, img.height || 1));
      const w = Math.max(1, Math.round(img.width * ratio));
      const h = Math.max(1, Math.round(img.height * ratio));
      const canvas = document.createElement("canvas");
      canvas.width = w;
      canvas.height = h;
      const ctx = canvas.getContext("2d");
      if (!ctx) return resolve(dataUrl);
      ctx.drawImage(img, 0, 0, w, h);
      const webp = canvas.toDataURL("image/webp", quality);
      resolve(webp.startsWith("data:image/webp") ? webp : canvas.toDataURL("image/jpeg", quality));
    };
    img.onerror = () => resolve(dataUrl);
    img.src = dataUrl;
  });
}

export interface AuthSession {
  accessToken: string;
  scope: "Saas" | "Establishment";
  tenantId?: string;
  storeSlug?: string;
  email: string;
}

const SESSION_KEY = "pitako_auth_session";

export function loadSession(): AuthSession | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = window.localStorage.getItem(SESSION_KEY);
    return raw ? (JSON.parse(raw) as AuthSession) : null;
  } catch {
    return null;
  }
}

export function saveSession(session: AuthSession): void {
  if (typeof window === "undefined") return;
  window.localStorage.setItem(SESSION_KEY, JSON.stringify(session));
}

export function clearSession(): void {
  if (typeof window === "undefined") return;
  window.localStorage.removeItem(SESSION_KEY);
}

export async function apiFetch<T>(
  path: string,
  options: RequestInit = {},
  token?: string
): Promise<{ ok: boolean; status: number; data?: T; errorMessage?: string }> {
  try {
    const headers = new Headers(options.headers || {});
    if (!headers.has("Content-Type") && options.body && !(options.body instanceof FormData)) {
      headers.set("Content-Type", "application/json");
    }
    if (token) {
      headers.set("Authorization", `Bearer ${token}`);
    }

    const res = await fetch(`${API_BASE_URL}${path}`, {
      ...options,
      headers,
      cache: "no-store"
    });

    if (!res.ok) {
      let detail = `Erro HTTP ${res.status}`;
      try {
        const problem = await res.json();
        detail = problem.detail || problem.title || detail;
      } catch {
        // Sem JSON ProblemDetails
      }
      return { ok: false, status: res.status, errorMessage: detail };
    }

    const contentType = res.headers.get("content-type") || "";
    if (contentType.includes("application/json")) {
      const data = (await res.json()) as T;
      return { ok: true, status: res.status, data };
    }
    return { ok: true, status: res.status };
  } catch {
    return {
      ok: false,
      status: 0,
      errorMessage: `Backend Pitako.Api inacessível em ${API_BASE_URL}. Inicie Pitako.Api para dados ao vivo.`
    };
  }
}
