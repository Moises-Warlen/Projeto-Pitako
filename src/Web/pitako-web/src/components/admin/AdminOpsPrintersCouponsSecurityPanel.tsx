"use client";

import { Copy, CreditCard, MessageCircle, Printer, Save, ShieldAlert, Tag, Ticket, Trash2 } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "@/lib/api";
import { StoreOperatingHoursEntry } from "@/types/menu";
import { AdminCouponDto } from "./AdminCommercialAndStoreTab";

const DAY_NAMES = ["Domingo", "Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado"];

export function AdminPrintersPanel({
  token,
  onNotify
}: {
  token: string;
  onNotify: (msg: string) => void;
}) {
  const [printers, setPrinters] = useState<
    Array<{
      id: string;
      printerCode: string;
      displayName: string;
      sector: string;
      connectionKind: string;
      endpointOrPort: string;
      paperWidthMm: number;
      status: string;
      isAutoPrintEnabled: boolean;
    }>
  >([]);
  const [testPreview, setTestPreview] = useState<{
    printerCode: string;
    storeLogoUrl?: string | null;
    storeTradeName: string;
    statusMessage: string;
    previewTicketText: string;
  } | null>(null);

  const loadPrinters = useCallback(async () => {
    if (!token) return;
    const res = await apiFetch<{ printers: typeof printers }>("/api/v1/printing/printers", {}, token);
    if (res.ok && res.data) setPrinters(res.data.printers || []);
  }, [token]);

  useEffect(() => {
    void loadPrinters();
  }, [loadPrinters]);

  async function toggleAuto(id: string) {
    const res = await apiFetch(`/api/v1/printing/printers/${id}/toggle-auto`, { method: "POST" }, token);
    if (res.ok) {
      onNotify("Status automático da impressora atualizado.");
      await loadPrinters();
    }
  }

  async function runTest(id: string) {
    const res = await apiFetch<typeof testPreview>(`/api/v1/printing/printers/${id}/test`, { method: "POST" }, token);
    if (res.ok && res.data) {
      setTestPreview(res.data);
      onNotify(res.data.statusMessage);
    } else onNotify(res.errorMessage || "Erro ao testar impressora.");
  }

  return (
    <div className="space-y-5">
      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h2 className="flex items-center gap-2 text-base font-extrabold text-slate-900">
          <Printer className="h-5 w-5 text-slate-700" /> Gerenciamento de Impressoras Térmicas
        </h2>
        <p className="mt-1 text-xs text-slate-500">
          Configure suas estações de impressão térmica (1ª Via Produção/Cozinha e 2ª Via Caixa/Entrega) utilizando exclusivamente logo e CNPJ da loja.
        </p>

        <div className="mt-4 space-y-3">
          {printers.map((p) => (
            <div
              key={p.id}
              className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-slate-50 p-4"
            >
              <div>
                <p className="text-sm font-extrabold text-slate-900">{p.displayName}</p>
                <p className="text-xs text-slate-500">
                  Setor: <strong>{p.sector.toUpperCase()}</strong> • Bobina: {p.paperWidthMm}mm • {p.endpointOrPort}
                </p>
                <p className={`mt-1 text-xs font-extrabold ${p.isAutoPrintEnabled ? "text-emerald-700" : "text-amber-700"}`}>
                  {p.isAutoPrintEnabled
                    ? "⚡ Auto-Impressão ATIVA (sai automático)"
                    : "⏸️ Auto-Impressão DESLIGADA (impressão somente manual)"}
                </p>
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => void toggleAuto(p.id)}
                  className={`rounded-xl px-3.5 py-2 text-xs font-bold text-white ${
                    p.isAutoPrintEnabled ? "bg-rose-600 hover:bg-rose-700" : "bg-emerald-600 hover:bg-emerald-700"
                  }`}
                >
                  {p.isAutoPrintEnabled ? "Desativar Auto" : "Ativar Auto"}
                </button>
                <button
                  type="button"
                  onClick={() => void runTest(p.id)}
                  className="rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs font-bold text-slate-800 hover:bg-slate-100"
                >
                  Imprimir Teste
                </button>
              </div>
            </div>
          ))}
        </div>
      </div>

      {testPreview && (
        <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <p className="text-xs font-extrabold text-emerald-700">{testPreview.statusMessage}</p>
          <div className="mt-3 max-w-sm rounded-xl border border-amber-300 bg-amber-50 p-4 font-mono text-[11px] text-slate-900">
            {testPreview.storeLogoUrl && (
              <img
                src={testPreview.storeLogoUrl}
                alt={testPreview.storeTradeName}
                className="mx-auto mb-2 h-12 w-12 object-contain"
              />
            )}
            <pre className="whitespace-pre-wrap">{testPreview.previewTicketText}</pre>
          </div>
        </div>
      )}
    </div>
  );
}

export function AdminCouponsPanel({
  token,
  coupons,
  onSendInChat,
  onNotify,
  onReload
}: {
  token: string;
  coupons: AdminCouponDto[];
  onSendInChat: (couponText: string) => void;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [code, setCode] = useState("");
  const [discountType, setDiscountType] = useState("2");
  const [discountValue, setDiscountValue] = useState("10");
  const [minOrder, setMinOrder] = useState("30");
  const [durationPreset, setDurationPreset] = useState("7d");
  const [customUntil, setCustomUntil] = useState(() => new Date(Date.now() + 7 * 86400_000).toISOString().slice(0, 16));
  const [maxTotalUses, setMaxTotalUses] = useState("100");
  const [maxPerCustomer, setMaxPerCustomer] = useState("1");
  const [firstOrderOnly, setFirstOrderOnly] = useState(false);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    const now = new Date();
    let untilMs = now.getTime() + 7 * 86400_000;
    if (durationPreset === "24h") untilMs = now.getTime() + 86400_000;
    else if (durationPreset === "3d") untilMs = now.getTime() + 3 * 86400_000;
    else if (durationPreset === "15d") untilMs = now.getTime() + 15 * 86400_000;
    else if (durationPreset === "30d") untilMs = now.getTime() + 30 * 86400_000;
    else if (durationPreset === "365d") untilMs = now.getTime() + 365 * 86400_000;
    else if (durationPreset === "custom" && customUntil) {
      const parsed = new Date(customUntil).getTime();
      if (!Number.isNaN(parsed) && parsed > now.getTime()) untilMs = parsed;
    }

    const totalLimit = maxTotalUses.trim() && Number(maxTotalUses) > 0 ? Number(maxTotalUses) : null;
    const perCustLimit = firstOrderOnly
      ? 1
      : maxPerCustomer.trim() && Number(maxPerCustomer) > 0
        ? Number(maxPerCustomer)
        : null;

    const res = await apiFetch(
      "/api/v1/coupons",
      {
        method: "POST",
        body: JSON.stringify({
          code: code.trim().toUpperCase(),
          discountType: Number(discountType),
          discountValue: Number(discountValue),
          validFromUtc: now.toISOString(),
          validUntilUtc: new Date(untilMs).toISOString(),
          minimumOrderAmount: Number(minOrder),
          maxTotalUses: totalLimit,
          maxUsesPerCustomer: perCustLimit,
          firstOrderOnly,
          canCombineWithPromotions: false
        })
      },
      token
    );
    if (res.ok) {
      setCode("");
      setFirstOrderOnly(false);
      onNotify("Cupom criado com regras de duração e limite de uso!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao criar cupom.");
  }

  async function handleDelete(id: string) {
    const res = await apiFetch(`/api/v1/coupons/${id}`, { method: "DELETE" }, token);
    if (res.ok) {
      onNotify("Cupom removido.");
      await onReload();
    }
  }

  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <form onSubmit={handleCreate} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h3 className="flex items-center gap-2 text-sm font-extrabold text-slate-900">
          <Ticket className="h-4 w-4 text-emerald-600" /> Criar Novo Cupom
        </h3>
        <div className="mt-4 space-y-3 text-xs">
          <div>
            <label className="font-bold text-slate-700">Código do Cupom</label>
            <input
              value={code}
              onChange={(e) => setCode(e.target.value.toUpperCase())}
              required
              placeholder="EX: BEMVINDO10"
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono font-bold uppercase"
            />
          </div>
          <div>
            <label className="font-bold text-slate-700">Tipo de Desconto</label>
            <select
              value={discountType}
              onChange={(e) => setDiscountType(e.target.value)}
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-bold"
            >
              <option value="2">Valor Fixo (R$)</option>
              <option value="1">Percentual (%)</option>
              <option value="3">Frete Grátis</option>
            </select>
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div>
              <label className="font-bold text-slate-700">Desconto</label>
              <input
                type="number"
                step="0.5"
                value={discountValue}
                onChange={(e) => setDiscountValue(e.target.value)}
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono"
              />
            </div>
            <div>
              <label className="font-bold text-slate-700">Pedido Mínimo</label>
              <input
                type="number"
                step="1"
                value={minOrder}
                onChange={(e) => setMinOrder(e.target.value)}
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono"
              />
            </div>
          </div>
          <div>
            <label className="font-bold text-slate-700">Tempo de Duração</label>
            <select
              value={durationPreset}
              onChange={(e) => setDurationPreset(e.target.value)}
              className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-bold"
            >
              <option value="24h">24 horas</option>
              <option value="3d">3 dias</option>
              <option value="7d">7 dias</option>
              <option value="15d">15 dias</option>
              <option value="30d">30 dias</option>
              <option value="365d">1 ano</option>
              <option value="custom">Data/Hora personalizada</option>
            </select>
            {durationPreset === "custom" && (
              <input
                type="datetime-local"
                value={customUntil}
                onChange={(e) => setCustomUntil(e.target.value)}
                className="mt-2 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono text-xs"
              />
            )}
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div>
              <label className="font-bold text-slate-700">Limite Total de Usos</label>
              <input
                type="number"
                min={0}
                value={maxTotalUses}
                onChange={(e) => setMaxTotalUses(e.target.value)}
                placeholder="0 = Ilimitado"
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono"
              />
            </div>
            <div>
              <label className="font-bold text-slate-700">Usos por Cliente</label>
              <input
                type="number"
                min={0}
                value={firstOrderOnly ? "1" : maxPerCustomer}
                disabled={firstOrderOnly}
                onChange={(e) => setMaxPerCustomer(e.target.value)}
                placeholder="Ex: 1"
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono disabled:opacity-60"
              />
            </div>
          </div>
          <label className="flex cursor-pointer items-center gap-2 rounded-xl border border-indigo-200 bg-indigo-50/70 px-3 py-2 font-bold text-indigo-900">
            <input
              type="checkbox"
              checked={firstOrderOnly}
              onChange={(e) => setFirstOrderOnly(e.target.checked)}
            />
            Cupom exclusivo para o 1º Pedido do cliente
          </label>
          <button
            type="submit"
            style={{ backgroundColor: "var(--store-primary)" }}
            className="w-full rounded-xl px-4 py-2.5 text-xs font-extrabold text-white hover:opacity-90"
          >
            Salvar Cupom
          </button>
        </div>
      </form>

      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-2">
        <h3 className="flex items-center gap-2 text-sm font-extrabold text-slate-900">
          <Tag className="h-4 w-4 text-slate-700" /> Cupons Cadastrados ({coupons.length})
        </h3>
        <div className="mt-4 space-y-2.5">
          {coupons.map((c) => {
            const shareMsg = `Use o cupom ${c.code} e ganhe ${
              c.discountType === 1 ? `${c.discountValue}% de desconto` : c.discountType === 3 ? "Frete Grátis" : `R$ ${c.discountValue.toFixed(2)} OFF`
            } em pedidos acima de R$ ${c.minimumOrderAmount.toFixed(2)}!`;
            return (
              <div
                key={c.id}
                className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3.5 text-xs"
              >
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-mono text-sm font-extrabold text-slate-900">{c.code}</span>
                    <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${c.isActive ? "bg-emerald-100 text-emerald-800" : "bg-slate-200 text-slate-600"}`}>
                      {c.isActive ? "ATIVO" : "INATIVO"}
                    </span>
                    {c.firstOrderOnly && (
                      <span className="rounded-full bg-indigo-100 px-2 py-0.5 text-[10px] font-bold text-indigo-800">
                        1º PEDIDO
                      </span>
                    )}
                  </div>
                  <p className="mt-1 text-slate-600">
                    {c.discountType === 1 ? `${c.discountValue}%` : c.discountType === 3 ? "Frete Grátis" : `R$ ${c.discountValue.toFixed(2)}`} • Mín. R$ {c.minimumOrderAmount.toFixed(2)} • Usos: {c.currentUses}{c.maxTotalUses ? `/${c.maxTotalUses}` : ""}
                    {c.maxUsesPerCustomer ? ` · Máx ${c.maxUsesPerCustomer}x/cliente` : ""}
                    {c.validUntilUtc ? ` · Até ${new Date(c.validUntilUtc).toLocaleDateString("pt-BR")}` : ""}
                  </p>
                </div>
                <div className="flex flex-wrap items-center gap-1.5">
                  <button
                    type="button"
                    onClick={() => {
                      if (typeof navigator !== "undefined" && navigator.clipboard) {
                        void navigator.clipboard.writeText(c.code);
                        onNotify(`Cupom ${c.code} copiado!`);
                      }
                    }}
                    className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 font-bold text-slate-700 hover:bg-slate-100"
                  >
                    <Copy className="h-3 w-3" /> Copiar
                  </button>
                  <button
                    type="button"
                    onClick={() => onSendInChat(shareMsg)}
                    className="inline-flex items-center gap-1 rounded-lg bg-emerald-600 px-2.5 py-1.5 font-bold text-white hover:bg-emerald-700"
                  >
                    <MessageCircle className="h-3 w-3" /> Enviar Chat
                  </button>
                  <button
                    type="button"
                    onClick={() => void handleDelete(c.id)}
                    className="rounded-lg border border-rose-200 bg-rose-50 p-1.5 text-rose-700 hover:bg-rose-100"
                    title="Excluir cupom"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

export function AdminSecurityFirewallPanel({ token }: { token: string }) {
  const [severity, setSeverity] = useState("Todos");
  const [data, setData] = useState<{
    kpis: { criticalBlocks: number; failedLogins: number; unauthorizedOrProfanity: number; totalEvents24h: number };
    events: Array<{ id: string; occurredAtUtc: string; severity: string; eventType: string; ipAddress: string; routeMethod: string; details: string }>;
  } | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    const res = await apiFetch<typeof data>(`/api/v1/security/audit-logs?severity=${encodeURIComponent(severity)}`, {}, token);
    if (res.ok && res.data) setData(res.data);
  }, [severity, token]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div>
          <h2 className="flex items-center gap-2 text-base font-extrabold text-slate-900">
            <ShieldAlert className="h-5 w-5 text-rose-600" /> Firewall, Auditoria & Logs de Segurança
          </h2>
          <p className="text-xs text-slate-500">
            Monitoramento ativo de força bruta (429), acessos 401/403 não autorizados e bloqueios de linguagem imprópria da loja.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => void load()}
            className="rounded-xl border border-slate-200 px-3.5 py-2 text-xs font-bold text-slate-700 hover:bg-slate-50"
          >
            Atualizar Logs
          </button>
          <button
            type="button"
            onClick={async () => {
              await apiFetch("/api/v1/security/audit-logs/clear", { method: "POST" }, token);
              await load();
            }}
            className="rounded-xl border border-rose-200 bg-rose-50 px-3.5 py-2 text-xs font-bold text-rose-700 hover:bg-rose-100"
          >
            Limpar Histórico
          </button>
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
          <p className="text-[11px] font-bold uppercase text-slate-500">Bloqueios Críticos (429)</p>
          <p className="mt-1 font-mono text-2xl font-extrabold text-rose-700">{data?.kpis.criticalBlocks ?? 0}</p>
        </div>
        <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
          <p className="text-[11px] font-bold uppercase text-slate-500">Tentativas de Senha Falha</p>
          <p className="mt-1 font-mono text-2xl font-extrabold text-amber-600">{data?.kpis.failedLogins ?? 0}</p>
        </div>
        <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
          <p className="text-[11px] font-bold uppercase text-slate-500">Acessos 401 / Palavrões</p>
          <p className="mt-1 font-mono text-2xl font-extrabold text-sky-700">{data?.kpis.unauthorizedOrProfanity ?? 0}</p>
        </div>
        <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
          <p className="text-[11px] font-bold uppercase text-slate-500">Total de Eventos (24h)</p>
          <p className="mt-1 font-mono text-2xl font-extrabold text-slate-900">{data?.kpis.totalEvents24h ?? 0}</p>
        </div>
      </div>

      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="mb-3 flex flex-wrap gap-2">
          {["Todos", "Critico", "Alto", "Medio", "Info"].map((sev) => (
            <button
              key={sev}
              type="button"
              onClick={() => setSeverity(sev)}
              style={severity === sev ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
              className={`rounded-lg px-3 py-1 text-xs font-bold ${severity === sev ? "" : "border border-slate-200 text-slate-700"}`}
            >
              {sev}
            </button>
          ))}
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead className="border-b border-slate-200 bg-slate-50 text-slate-600">
              <tr>
                <th className="px-3 py-2 font-bold">Data / Hora</th>
                <th className="px-3 py-2 font-bold">Severidade</th>
                <th className="px-3 py-2 font-bold">Tipo de Evento</th>
                <th className="px-3 py-2 font-bold">IP Origem</th>
                <th className="px-3 py-2 font-bold">Rota / Método</th>
                <th className="px-3 py-2 font-bold">Detalhes</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {(data?.events || []).map((e) => (
                <tr key={e.id}>
                  <td className="whitespace-nowrap px-3 py-2 font-mono text-slate-600">
                    {new Date(e.occurredAtUtc).toLocaleString("pt-BR")}
                  </td>
                  <td className="px-3 py-2">
                    <span
                      className={`rounded-full px-2 py-0.5 text-[10px] font-extrabold ${
                        e.severity === "Critico"
                          ? "bg-rose-100 text-rose-800"
                          : e.severity === "Alto"
                            ? "bg-amber-100 text-amber-800"
                            : "bg-sky-100 text-sky-800"
                      }`}
                    >
                      {e.severity}
                    </span>
                  </td>
                  <td className="px-3 py-2 font-bold text-slate-900">{e.eventType}</td>
                  <td className="px-3 py-2 font-mono text-slate-600">{e.ipAddress}</td>
                  <td className="px-3 py-2 font-mono text-slate-600">{e.routeMethod}</td>
                  <td className="px-3 py-2 text-slate-700">{e.details}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

export function AdminOperationalSettingsPanel({
  token,
  onNotify,
  onReload
}: {
  token: string;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [feePerKm, setFeePerKm] = useState("1.50");
  const [minFreightFee, setMinFreightFee] = useState("5.00");
  const [freeKm, setFreeKm] = useState("2");
  const [storeWhatsApp, setStoreWhatsApp] = useState("");
  const [chatAgentName, setChatAgentName] = useState("");
  const [storeAddress, setStoreAddress] = useState("");
  const [topBannerText, setTopBannerText] = useState("");
  const [autoOpenClose, setAutoOpenClose] = useState(true);
  const [schedule, setSchedule] = useState<StoreOperatingHoursEntry[]>([]);
  const [autoReplyClosed, setAutoReplyClosed] = useState(true);
  const [closedMsg, setClosedMsg] = useState("");
  const [pixKey, setPixKey] = useState("");
  const [mpToken, setMpToken] = useState("");
  const [mpMasked, setMpMasked] = useState<string | null>(null);
  const [waGatewayUrl, setWaGatewayUrl] = useState("");
  const [waGatewayToken, setWaGatewayToken] = useState("");

  useEffect(() => {
    if (!token) return;
    apiFetch<{
      feePerKm: number;
      minFreightFee: number;
      freeFreightUpToKm: number;
      storeWhatsAppPhone: string;
      chatAgentName: string;
      storeAddress: string;
      topBannerText: string;
      autoOpenCloseBySchedule: boolean;
      schedule: StoreOperatingHoursEntry[];
      autoReplyWhenClosedEnabled: boolean;
      autoReplyWhenClosedMessage: string;
      pixKey: string;
      mercadoPagoAccessTokenMasked?: string | null;
      whatsappGatewayUrl?: string | null;
      whatsappGatewayTokenMasked?: string | null;
    }>("/api/v1/store/operational-settings", {}, token).then((res) => {
      if (!res.ok || !res.data) return;
      const d = res.data;
      setFeePerKm(String(d.feePerKm));
      setMinFreightFee(String(d.minFreightFee));
      setFreeKm(String(d.freeFreightUpToKm));
      setStoreWhatsApp(d.storeWhatsAppPhone || "");
      setChatAgentName(d.chatAgentName || "");
      setStoreAddress(d.storeAddress || "");
      setTopBannerText(d.topBannerText || "");
      setAutoOpenClose(Boolean(d.autoOpenCloseBySchedule));
      setSchedule(d.schedule || []);
      setAutoReplyClosed(Boolean(d.autoReplyWhenClosedEnabled));
      setClosedMsg(d.autoReplyWhenClosedMessage || "");
      setPixKey(d.pixKey || "");
      setMpMasked(d.mercadoPagoAccessTokenMasked || null);
      setWaGatewayUrl(d.whatsappGatewayUrl || "");
      if (d.whatsappGatewayTokenMasked) setWaGatewayToken(d.whatsappGatewayTokenMasked);
    });
  }, [token]);

  async function saveAll(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch(
      "/api/v1/store/operational-settings",
      {
        method: "PUT",
        body: JSON.stringify({
          feePerKm: Number(feePerKm),
          minFreightFee: Number(minFreightFee),
          freeFreightUpToKm: Number(freeKm),
          storeWhatsAppPhone: storeWhatsApp,
          chatAgentName,
          storeAddress,
          topBannerText,
          autoOpenCloseBySchedule: autoOpenClose,
          schedule,
          autoReplyWhenClosedEnabled: autoReplyClosed,
          autoReplyWhenClosedMessage: closedMsg,
          pixKey,
          mercadoPagoAccessToken: mpToken || null,
          whatsappGatewayUrl: waGatewayUrl || null,
          whatsappGatewayToken: waGatewayToken || null
        })
      },
      token
    );
    if (res.ok) {
      setMpToken("");
      onNotify("Configurações da loja, PIX, horários e gateways salvas com sucesso!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar configurações.");
  }

  return (
    <form onSubmit={saveAll} className="space-y-6">
      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h3 className="text-sm font-extrabold text-slate-900">Configurações da Loja & Frete</h3>
        <div className="mt-4 grid gap-3 text-xs sm:grid-cols-3">
          <div>
            <label className="font-bold text-slate-700">Taxa de Entrega por KM (R$)</label>
            <input type="number" step="0.1" value={feePerKm} onChange={(e) => setFeePerKm(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono" />
          </div>
          <div>
            <label className="font-bold text-slate-700">Frete Mínimo (R$)</label>
            <input type="number" step="0.5" value={minFreightFee} onChange={(e) => setMinFreightFee(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono" />
          </div>
          <div>
            <label className="font-bold text-slate-700">Distância p/ Frete Grátis (Km)</label>
            <input type="number" step="0.5" value={freeKm} onChange={(e) => setFreeKm(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono" />
          </div>
          <div>
            <label className="font-bold text-slate-700">WhatsApp da Loja (com DDD)</label>
            <input value={storeWhatsApp} onChange={(e) => setStoreWhatsApp(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono" />
          </div>
          <div>
            <label className="font-bold text-slate-700">Nome do Atendente no Chat</label>
            <input value={chatAgentName} onChange={(e) => setChatAgentName(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2" />
          </div>
          <div>
            <label className="font-bold text-slate-700">Endereço (Ponto de Partida)</label>
            <input value={storeAddress} onChange={(e) => setStoreAddress(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2" />
          </div>
          <div className="sm:col-span-3">
            <label className="font-bold text-slate-700">Texto do Banner Promocional do Topo</label>
            <input value={topBannerText} onChange={(e) => setTopBannerText(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2" />
          </div>
        </div>
      </div>

      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-sm font-extrabold text-slate-900">Horários de Funcionamento</h3>
          <label className="flex items-center gap-2 text-xs font-bold text-slate-700">
            <input type="checkbox" checked={autoOpenClose} onChange={(e) => setAutoOpenClose(e.target.checked)} />
            Ativar abrir e fechar automaticamente pelo horário
          </label>
        </div>
        <div className="mt-3 space-y-2 text-xs">
          {schedule.map((entry, idx) => (
            <div key={entry.dayOfWeek} className="flex flex-wrap items-center gap-3 rounded-xl bg-slate-50 px-3 py-2">
              <label className="flex w-36 items-center gap-2 font-bold text-slate-800">
                <input
                  type="checkbox"
                  checked={entry.isOpen}
                  onChange={(e) => {
                    const next = [...schedule];
                    next[idx] = { ...entry, isOpen: e.target.checked };
                    setSchedule(next);
                  }}
                />
                {DAY_NAMES[entry.dayOfWeek] || `Dia ${entry.dayOfWeek}`}
              </label>
              <span>Abre:</span>
              <input
                type="time"
                value={entry.openTime}
                onChange={(e) => {
                  const next = [...schedule];
                  next[idx] = { ...entry, openTime: e.target.value };
                  setSchedule(next);
                }}
                className="rounded-lg border border-slate-200 bg-white px-2 py-1 font-mono"
              />
              <span>Fecha:</span>
              <input
                type="time"
                value={entry.closeTime}
                onChange={(e) => {
                  const next = [...schedule];
                  next[idx] = { ...entry, closeTime: e.target.value };
                  setSchedule(next);
                }}
                className="rounded-lg border border-slate-200 bg-white px-2 py-1 font-mono"
              />
            </div>
          ))}
        </div>
      </div>

      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-sm font-extrabold text-slate-900">Resposta Automática no Chat (Quando a Loja Estiver Fechada)</h3>
          <label className="flex items-center gap-2 text-xs font-bold text-slate-700">
            <input type="checkbox" checked={autoReplyClosed} onChange={(e) => setAutoReplyClosed(e.target.checked)} />
            Ativar Resposta Automática
          </label>
        </div>
        <textarea
          value={closedMsg}
          onChange={(e) => setClosedMsg(e.target.value)}
          rows={2}
          className="mt-3 w-full rounded-xl border border-slate-200 px-3 py-2 text-xs"
        />
        <p className="mt-1 text-[11px] text-slate-400">Dica: use {"{HORARIO}"} para inserir o horário de abertura automaticamente.</p>
      </div>

      <div className="grid gap-6 md:grid-cols-2">
        <div className="rounded-2xl border border-emerald-200 bg-white p-5 shadow-sm">
          <h3 className="flex items-center gap-2 text-sm font-extrabold text-emerald-800">
            <CreditCard className="h-4 w-4" /> Pagamento PIX & Confirmação Automática
          </h3>
          <div className="mt-3 space-y-3 text-xs">
            <div>
              <label className="font-bold text-slate-700">Chave PIX da Loja (Celular / CPF / CNPJ / Aleatória)</label>
              <input value={pixKey} onChange={(e) => setPixKey(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono" />
            </div>
            <div>
              <label className="font-bold text-slate-700">
                Mercado Pago Access Token {mpMasked ? `(Configurado: ${mpMasked})` : "(Opcional)"}
              </label>
              <input
                type="password"
                autoComplete="off"
                value={mpToken}
                onChange={(e) => setMpToken(e.target.value)}
                placeholder="APP_USR-..."
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono"
              />
            </div>
          </div>
        </div>

        <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h3 className="text-sm font-extrabold text-slate-900">Notificações Automáticas via Gateway de WhatsApp (Opcional)</h3>
          <div className="mt-3 space-y-3 text-xs">
            <div>
              <label className="font-bold text-slate-700">URL do Webhook / API WhatsApp</label>
              <input
                value={waGatewayUrl}
                onChange={(e) => setWaGatewayUrl(e.target.value)}
                placeholder="https://sua-api.com/message/sendText"
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono"
              />
            </div>
            <div>
              <label className="font-bold text-slate-700">Token / API Key</label>
              <input
                type="password"
                autoComplete="off"
                value={waGatewayToken}
                onChange={(e) => setWaGatewayToken(e.target.value)}
                placeholder="Token ou Chave do Gateway"
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono"
              />
            </div>
          </div>
        </div>
      </div>

      <button
        type="submit"
        className="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-5 py-3 text-xs font-extrabold text-white shadow-sm hover:bg-emerald-700"
      >
        <Save className="h-4 w-4" /> Salvar Configurações Completas
      </button>
    </form>
  );
}
