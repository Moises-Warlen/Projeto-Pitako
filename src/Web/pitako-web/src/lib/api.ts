export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_URL?.replace(/\/+$/, "") || "http://localhost:5000";

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
