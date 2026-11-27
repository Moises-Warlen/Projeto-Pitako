"use client";

import { Bot, Check, Megaphone, MessageCircle, Power, QrCode, Send, Sparkles } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "@/lib/api";

export interface WhatsAppDashboardDto {
  channel: {
    id: string;
    storeTradeName: string;
    displayPhoneNumber: string;
    phoneNumberId: string;
    autoReplyMode: string;
    menuUrl?: string | null;
    isActive: boolean;
    lastWebhookUtc?: string | null;
  } | null;
  chatAgentName: string;
  aiEnabled: boolean;
  hasGeminiApiKey: boolean;
  geminiApiKeyMasked?: string | null;
  metrics: {
    messagesReceived: number;
    aiReplies: number;
    messagesSent: number;
    humanHandoffs: number;
    profanityBlocked: number;
    menuReturns: number;
    customersInBase: number;
    optOuts: number;
  };
  conversations: Array<{
    id: string;
    customerName: string;
    customerPhone: string;
    status: string;
    humanHandoffReason?: string | null;
    lastMessagePreview: string;
    unreadCount: number;
    startedAtUtc: string;
    lastInteractionAtUtc: string;
    messages: Array<{
      id: string;
      direction: string;
      contentText: string;
      deliveryStatus: string;
      deliveryError?: string | null;
      groundedByFacts: boolean;
      sentAtUtc: string;
    }>;
  }>;
  campaigns: Array<{
    id: string;
    title: string;
    messageText: string;
    audienceCount: number;
    createdAtUtc: string;
    status: string;
  }>;
  weeklySchedule?: {
    enabled: boolean;
    dayOfWeek: string;
    time: string;
  };
  feedbacks?: Array<{
    id: string;
    customerPhone: string;
    messageText: string;
    createdAtUtc: string;
  }>;
}

const CHAT_SHORTCUTS = [
  { label: "🔥 Em Preparo", text: "Seu pedido já entrou em preparo na nossa cozinha! Logo sairá quentinho." },
  { label: "🛵 Saiu p/ Entrega", text: "Oba! Nosso entregador já saiu com o seu pedido. Fique atento ao portão/campainha!" },
  { label: "📍 Ponto de Ref.", text: "Poderia confirmar um ponto de referência próximo ao seu endereço para facilitar para o motoboy?" },
  { label: "✅ Pagamento Aprovado", text: "Pagamento confirmado com sucesso! Muito obrigado pela preferência." },
  { label: "🛍️ Pronto no Balcão", text: "Seu pedido está prontinho esperando por você aqui no nosso balcão de retirada!" },
  { label: "⭐ Agradecimento", text: "Agradecemos pelo seu pedido! Esperamos que goste muito. Qualquer dúvida, estamos por aqui." }
];

export function AdminCustomerChatPanel({
  token,
  storeTradeName,
  prefillMessage,
  onNotify
}: {
  token: string;
  storeTradeName: string;
  prefillMessage?: string | null;
  onNotify: (msg: string) => void;
}) {
  const [data, setData] = useState<WhatsAppDashboardDto | null>(null);
  const [selectedId, setSelectedId] = useState<string>("");
  const [agentName, setAgentName] = useState(storeTradeName);
  const [replyText, setReplyText] = useState(prefillMessage || "");
  const [sending, setSending] = useState(false);
  const [loadErr, setLoadErr] = useState<string | null>(null);

  useEffect(() => {
    if (prefillMessage) setReplyText(prefillMessage);
  }, [prefillMessage]);

  const loadData = useCallback(async () => {
    if (!token) return;
    const res = await apiFetch<WhatsAppDashboardDto>("/api/v1/whatsapp/dashboard", {}, token);
    if (res.ok && res.data) {
      setLoadErr(null);
      setData(res.data);
      if (res.data.chatAgentName) setAgentName(res.data.chatAgentName);
      if (!selectedId && res.data.conversations.length > 0) {
        setSelectedId(res.data.conversations[0].id);
      }
    } else if (!res.ok) {
      setLoadErr(res.errorMessage || "Erro ao carregar conversas.");
    }
  }, [selectedId, token]);

  useEffect(() => {
    void loadData();
    const timer = setInterval(() => {
      void loadData();
    }, 4500);
    return () => clearInterval(timer);
  }, [loadData]);

  const activeConv = data?.conversations.find((c) => c.id === selectedId) || data?.conversations[0] || null;

  async function saveAgentName() {
    const res = await apiFetch(
      "/api/v1/whatsapp/channel",
      {
        method: "PUT",
        body: JSON.stringify({
          displayPhoneNumber: data?.channel?.displayPhoneNumber,
          isActive: data?.channel?.isActive ?? true,
          aiEnabled: data?.aiEnabled ?? true,
          chatAgentName: agentName
        })
      },
      token
    );
    if (res.ok) {
      onNotify("Nome do atendente salvo!");
      await loadData();
    }
  }

  async function sendReply(e: React.FormEvent) {
    e.preventDefault();
    if (!activeConv || !replyText.trim()) return;
    setSending(true);
    const res = await apiFetch(
      `/api/v1/whatsapp/conversations/${activeConv.id}/reply`,
      { method: "POST", body: JSON.stringify({ messageText: replyText.trim() }) },
      token
    );
    setSending(false);
    if (res.ok) {
      setReplyText("");
      onNotify("Mensagem enviada ao cliente!");
      await loadData();
    } else onNotify(res.errorMessage || "Erro ao enviar resposta.");
  }

  async function resolveConv(id: string) {
    const res = await apiFetch(`/api/v1/whatsapp/conversations/${id}/resolve`, { method: "POST" }, token);
    if (res.ok) {
      onNotify("Conversa marcada como resolvida.");
      await loadData();
    }
  }

  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="mb-3 flex items-center justify-between">
          <h3 className="text-sm font-extrabold text-slate-900">Conversas com Clientes</h3>
          <button
            type="button"
            onClick={() => void loadData()}
            className="rounded-lg border border-slate-200 px-2.5 py-1 text-[11px] font-bold text-slate-600 hover:bg-slate-50"
          >
            Atualizar
          </button>
        </div>

        {loadErr && (
          <div className="mb-2 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-[11px] font-bold text-rose-800">
            {loadErr}
          </div>
        )}

        {(!data || data.conversations.length === 0) ? (
          <div className="rounded-xl border border-dashed border-slate-200 p-6 text-center text-xs text-slate-500">
            Nenhuma conversa recente ainda. As mensagens enviadas pelo Chat da Vitrine ou Bot WhatsApp aparecem aqui em tempo real.
          </div>
        ) : (
          <div className="space-y-2">
            {data.conversations.map((c) => {
              const selected = activeConv?.id === c.id;
              return (
                <button
                  key={c.id}
                  type="button"
                  onClick={() => setSelectedId(c.id)}
                  className={`w-full rounded-xl border p-3 text-left transition ${
                    selected ? "border-slate-900 bg-slate-900 text-white" : "border-slate-200 bg-slate-50 hover:bg-slate-100"
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="truncate text-xs font-extrabold">{c.customerName}</span>
                    <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${selected ? "bg-white/20 text-white" : "bg-white text-slate-700"}`}>
                      {c.status}
                    </span>
                  </div>
                  <p className={`mt-1 text-[11px] ${selected ? "text-slate-300" : "text-slate-500"}`}>{c.customerPhone}</p>
                  <p className={`mt-1 line-clamp-1 text-xs ${selected ? "text-slate-200" : "text-slate-700"}`}>{c.lastMessagePreview}</p>
                </button>
              );
            })}
          </div>
        )}
      </div>

      <div className="flex flex-col rounded-2xl border border-slate-200 bg-white shadow-sm lg:col-span-2">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 px-4 py-3">
          <div>
            <p className="text-sm font-extrabold text-slate-900">
              {activeConv ? `${activeConv.customerName} (${activeConv.customerPhone})` : "Selecione uma conversa"}
            </p>
            {activeConv?.humanHandoffReason && (
              <p className="text-[11px] font-semibold text-amber-700">Motivo transbordo: {activeConv.humanHandoffReason}</p>
            )}
          </div>
          <div className="flex items-center gap-2">
            <span className="text-[11px] font-semibold text-slate-500">Atendente:</span>
            <input
              value={agentName}
              onChange={(e) => setAgentName(e.target.value)}
              className="w-32 rounded-lg border border-slate-200 px-2.5 py-1 text-xs"
            />
            <button
              type="button"
              onClick={() => void saveAgentName()}
              className="rounded-lg bg-emerald-600 px-2.5 py-1 text-xs font-bold text-white hover:bg-emerald-700"
            >
              Salvar
            </button>
            {activeConv && activeConv.status !== "Resolved" && (
              <button
                type="button"
                onClick={() => void resolveConv(activeConv.id)}
                className="inline-flex items-center gap-1 rounded-lg border border-slate-200 px-2.5 py-1 text-xs font-bold text-slate-700 hover:bg-slate-50"
              >
                <Check className="h-3.5 w-3.5" /> Encerrar
              </button>
            )}
          </div>
        </div>

        <div className="min-h-[270px] flex-1 space-y-2.5 overflow-y-auto bg-slate-50 p-4 text-xs">
          {!activeConv ? (
            <div className="flex h-56 items-center justify-center text-slate-400">
              Selecione uma conversa ao lado para responder o cliente.
            </div>
          ) : (
            activeConv.messages.map((m) => {
              const isCustomer = m.direction === "Inbound";
              return (
                <div key={m.id} className={`flex ${isCustomer ? "justify-start" : "justify-end"}`}>
                  <div
                    className={`max-w-[80%] rounded-2xl px-3.5 py-2.5 shadow-sm ${
                      isCustomer ? "border border-slate-200 bg-white text-slate-900" : "bg-slate-900 text-white"
                    }`}
                  >
                    <p className="text-[10px] font-bold opacity-75">
                      {isCustomer ? activeConv.customerName : m.direction === "OutboundAiGrounded" ? "Bot IA" : agentName}
                    </p>
                    <p className="mt-0.5 whitespace-pre-wrap leading-relaxed">{m.contentText}</p>
                    <p className="mt-1 text-[10px] opacity-70">
                      {new Date(m.sentAtUtc).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}
                    </p>
                  </div>
                </div>
              );
            })
          )}
        </div>

        <div className="border-t border-slate-200 bg-white p-3">
          <div className="mb-2 flex flex-wrap items-center gap-1.5">
            <span className="text-[11px] font-bold text-slate-500">⚡ Atalhos:</span>
            {CHAT_SHORTCUTS.map((sc) => (
              <button
                key={sc.label}
                type="button"
                onClick={() => setReplyText(sc.text)}
                className="rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-bold text-slate-700 hover:bg-slate-100"
              >
                {sc.label}
              </button>
            ))}
          </div>

          <form onSubmit={sendReply} className="flex items-center gap-2">
            <input
              value={replyText}
              onChange={(e) => setReplyText(e.target.value)}
              placeholder="Digite sua resposta ou use um atalho rápido..."
              className="flex-1 rounded-xl border border-slate-200 px-3 py-2 text-xs outline-none focus:border-slate-400"
            />
            <button
              type="submit"
              disabled={sending || !activeConv}
              className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-bold text-white hover:bg-emerald-700 disabled:opacity-50"
            >
              <Send className="h-3.5 w-3.5" /> Enviar
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}

export function AdminWhatsAppBotPanel({
  token,
  storeTradeName,
  onNotify
}: {
  token: string;
  storeTradeName: string;
  onNotify: (msg: string) => void;
}) {
  const [data, setData] = useState<WhatsAppDashboardDto | null>(null);
  const [phoneInput, setPhoneInput] = useState("");
  const [aiEnabled, setAiEnabled] = useState(true);
  const [geminiKey, setGeminiKey] = useState("");
  const [testPhone, setTestPhone] = useState("16991098863");
  const [testMsg, setTestMsg] = useState(`Olá! Esta é uma mensagem de teste da ${storeTradeName}.`);
  const [testFeedback, setTestFeedback] = useState<string | null>(null);
  const [campTitle, setCampTitle] = useState("Oferta do Fim de Semana");
  const [campText, setCampText] = useState(`Aproveite hoje na ${storeTradeName}! Peça no cardápio digital com desconto especial.`);
  const [schedEnabled, setSchedEnabled] = useState(false);
  const [schedDay, setSchedDay] = useState("Sexta-feira");
  const [schedHour, setSchedHour] = useState("17:00");

  const load = useCallback(async () => {
    if (!token) return;
    const res = await apiFetch<WhatsAppDashboardDto>("/api/v1/whatsapp/dashboard", {}, token);
    if (res.ok && res.data) {
      setData(res.data);
      setPhoneInput(res.data.channel?.displayPhoneNumber || "");
      setAiEnabled(res.data.aiEnabled);
      if (res.data.weeklySchedule) {
        setSchedEnabled(res.data.weeklySchedule.enabled);
        setSchedDay(res.data.weeklySchedule.dayOfWeek || "Sexta-feira");
        setSchedHour(res.data.weeklySchedule.time || "17:00");
      }
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  async function updateChannel(isActive: boolean, nextAi = aiEnabled) {
    const res = await apiFetch(
      "/api/v1/whatsapp/channel",
      {
        method: "PUT",
        body: JSON.stringify({
          displayPhoneNumber: phoneInput,
          isActive,
          aiEnabled: nextAi,
          geminiApiKey: geminiKey || null
        })
      },
      token
    );
    if (res.ok) {
      setGeminiKey("");
      onNotify(isActive ? "Configuração WhatsApp & IA atualizada!" : "Bot WhatsApp desconectado.");
      await load();
    } else onNotify(res.errorMessage || "Erro ao atualizar canal WhatsApp.");
  }

  async function sendTest(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch<{ deliveredToExternalMeta: boolean; note: string }>(
      "/api/v1/whatsapp/test-message",
      { method: "POST", body: JSON.stringify({ targetPhone: testPhone, messageText: testMsg }) },
      token
    );
    if (res.ok && res.data) {
      setTestFeedback(
        res.data.deliveredToExternalMeta
          ? "Enviado via Meta Cloud API com sucesso."
          : `Mensagem registrada no inbox da loja (${res.data.note})`
      );
      await load();
    } else onNotify(res.errorMessage || "Erro no teste WhatsApp.");
  }

  async function createCampaign(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch(
      "/api/v1/whatsapp/campaigns",
      { method: "POST", body: JSON.stringify({ title: campTitle, messageText: campText }) },
      token
    );
    if (res.ok) {
      onNotify("Campanha LGPD salva na fila!");
      await load();
    }
  }

  const m = data?.metrics;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div>
          <h2 className="flex items-center gap-2 text-base font-extrabold text-slate-900">
            <Bot className="h-5 w-5 text-emerald-600" /> Bot WhatsApp & Atendente Virtual com IA
          </h2>
          <p className="text-xs text-slate-500">
            Atendimento 24h, consulta dinâmica ao cardápio, rastreamento de pedidos e notificações automáticas de status.
          </p>
        </div>
        <span
          className={`inline-flex items-center gap-1.5 rounded-full px-3.5 py-1.5 text-xs font-extrabold ${
            data?.channel?.isActive ? "bg-emerald-100 text-emerald-800" : "bg-slate-200 text-slate-700"
          }`}
        >
          <span className={`h-2 w-2 rounded-full ${data?.channel?.isActive ? "bg-emerald-500" : "bg-slate-500"}`} />
          {data?.channel?.isActive ? `Conectado (${data.channel.displayPhoneNumber})` : "Desconectado"}
        </span>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h3 className="flex items-center gap-2 text-sm font-extrabold text-slate-900">
            <QrCode className="h-4 w-4 text-slate-600" /> Conexão com o WhatsApp
          </h3>
          <p className="mt-1 text-xs text-slate-500">Número oficial Meta Cloud API ou gateway da loja.</p>

          <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-6 text-center">
            {data?.channel?.isActive ? (
              <div className="space-y-2">
                <MessageCircle className="mx-auto h-10 w-10 text-emerald-600" />
                <p className="text-sm font-extrabold text-emerald-700">WhatsApp Conectado!</p>
                <p className="text-xs text-slate-600">Número ativo: {data.channel.displayPhoneNumber}</p>
              </div>
            ) : (
              <div className="space-y-2">
                <QrCode className="mx-auto h-12 w-12 text-slate-400" />
                <p className="text-xs font-semibold text-slate-600">Canal pausado — clique em Conectar Número abaixo.</p>
              </div>
            )}
          </div>

          <div className="mt-4 flex flex-wrap items-center gap-2">
            <input
              value={phoneInput}
              onChange={(e) => setPhoneInput(e.target.value)}
              placeholder="+5516991098863"
              className="flex-1 rounded-xl border border-slate-200 px-3 py-2 text-xs font-mono"
            />
            <button
              type="button"
              onClick={() => void updateChannel(true)}
              className="rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-bold text-white hover:bg-emerald-700"
            >
              Conectar / Salvar
            </button>
            <button
              type="button"
              onClick={() => void updateChannel(false)}
              className="inline-flex items-center gap-1 rounded-xl bg-rose-600 px-3.5 py-2 text-xs font-bold text-white hover:bg-rose-700"
            >
              <Power className="h-3.5 w-3.5" /> Desconectar
            </button>
          </div>
        </div>

        <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <div className="flex items-center justify-between">
            <h3 className="flex items-center gap-2 text-sm font-extrabold text-slate-900">
              <Sparkles className="h-4 w-4 text-violet-600" /> Cérebro com Inteligência Artificial
            </h3>
            <label className="flex items-center gap-2 text-xs font-extrabold text-slate-700">
              <input
                type="checkbox"
                checked={aiEnabled}
                onChange={(e) => {
                  setAiEnabled(e.target.checked);
                  void updateChannel(data?.channel?.isActive ?? true, e.target.checked);
                }}
              />
              IA Ativa
            </label>
          </div>

          <ul className="mt-3 space-y-1.5 rounded-xl bg-slate-50 p-3.5 text-xs text-slate-700">
            <li>• <strong>Catálogo em Tempo Real:</strong> preços e itens oficiais da loja.</li>
            <li>• <strong>Rastreio Automático:</strong> identifica pedidos pelo telefone do cliente.</li>
            <li>• <strong>Venda Ativa (Upsell):</strong> sugere adicionais, combos e cupons.</li>
            <li>• <strong>Transbordo Humano:</strong> solicita gerente no Chat de Clientes quando necessário.</li>
          </ul>

          <div className="mt-3">
            <label className="text-[11px] font-bold text-slate-600">
              Chave API Google Gemini (Opcional — motor local já ativo) {data?.geminiApiKeyMasked && `• Atual: ${data.geminiApiKeyMasked}`}
            </label>
            <div className="mt-1 flex gap-2">
              <input
                type="password"
                value={geminiKey}
                onChange={(e) => setGeminiKey(e.target.value)}
                placeholder="Cole uma nova AIza... para atualizar"
                className="flex-1 rounded-xl border border-slate-200 px-3 py-2 text-xs"
              />
              <button
                type="button"
                onClick={() => void updateChannel(data?.channel?.isActive ?? true)}
                className="rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-bold text-white hover:bg-emerald-700"
              >
                Salvar Chave
              </button>
            </div>
          </div>

          <form onSubmit={sendTest} className="mt-4 border-t border-slate-100 pt-3">
            <p className="mb-1.5 text-[11px] font-extrabold text-slate-700">⚡ Enviar Mensagem de Teste</p>
            <div className="flex flex-wrap gap-2">
              <input
                value={testPhone}
                onChange={(e) => setTestPhone(e.target.value)}
                placeholder="DDD + Telefone"
                className="w-36 rounded-xl border border-slate-200 px-2.5 py-1.5 text-xs font-mono"
              />
              <input
                value={testMsg}
                onChange={(e) => setTestMsg(e.target.value)}
                className="flex-1 rounded-xl border border-slate-200 px-2.5 py-1.5 text-xs"
              />
              <button
                type="submit"
                className="rounded-xl border border-slate-300 bg-slate-900 px-3 py-1.5 text-xs font-bold text-white"
              >
                Enviar
              </button>
            </div>
            {testFeedback && <p className="mt-1.5 text-[11px] font-semibold text-emerald-700">{testFeedback}</p>}
          </form>
        </div>
      </div>

      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <p className="mb-3 text-xs font-extrabold uppercase tracking-wide text-slate-500">Desempenho do Atendimento Automático</p>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-8">
          {[
            { label: "Msg. Recebidas", value: m?.messagesReceived ?? 0 },
            { label: "Respostas IA", value: m?.aiReplies ?? 0 },
            { label: "Msg. Enviadas", value: m?.messagesSent ?? 0 },
            { label: "Transbordos", value: m?.humanHandoffs ?? 0 },
            { label: "Filtro Ofensas", value: m?.profanityBlocked ?? 0 },
            { label: "Retornos Menu", value: m?.menuReturns ?? 0 },
            { label: "Clientes Base", value: m?.customersInBase ?? 0 },
            { label: "Descadastros", value: m?.optOuts ?? 0 }
          ].map((k) => (
            <div key={k.label} className="rounded-xl border border-slate-200 bg-slate-50 p-3 text-center">
              <p className="text-[11px] font-semibold text-slate-500">{k.label}</p>
              <p className="mt-1 font-mono text-xl font-extrabold text-slate-900">{k.value}</p>
            </div>
          ))}
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <div className="rounded-2xl border border-amber-200 bg-white p-5 shadow-sm space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-sm font-extrabold text-amber-800">⏰ Programação Automática Semanal</h3>
            <label className="flex items-center gap-2 text-xs font-bold text-slate-700">
              <input type="checkbox" checked={schedEnabled} onChange={(e) => setSchedEnabled(e.target.checked)} />
              Ativar Disparo Automático Semanal
            </label>
          </div>
          <div className="grid grid-cols-2 gap-2 text-xs">
            <label className="font-bold text-slate-700">
              Dia da Semana
              <select value={schedDay} onChange={(e) => setSchedDay(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2">
                {["Domingo", "Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado"].map((d) => (
                  <option key={d} value={d}>{d}</option>
                ))}
              </select>
            </label>
            <label className="font-bold text-slate-700">
              Horário
              <input type="time" value={schedHour} onChange={(e) => setSchedHour(e.target.value)} className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2 font-mono" />
            </label>
          </div>
          <button
            type="button"
            onClick={async () => {
              const r = await apiFetch("/api/v1/whatsapp/weekly-schedule", { method: "PUT", body: JSON.stringify({ enabled: schedEnabled, dayOfWeek: schedDay, time: schedHour }) }, token);
              if (r.ok) {
                onNotify("Programação semanal salva com sucesso!");
                await load();
              }
            }}
            className="w-full rounded-xl bg-emerald-600 py-2 text-xs font-extrabold text-white hover:bg-emerald-700"
          >
            Salvar Programação
          </button>
        </div>

        <form onSubmit={createCampaign} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h3 className="flex items-center gap-2 text-sm font-extrabold text-slate-900">
            <Megaphone className="h-4 w-4 text-rose-600" /> Criador de Ofertas & Reengajamento LGPD
          </h3>
          <p className="mt-1 text-xs text-slate-500">
            Dispara somente para clientes que já interagiram com a loja, com opção de descadastro automático (&quot;sair&quot;).
          </p>
          <div className="mt-3 space-y-2">
            <input
              value={campTitle}
              onChange={(e) => setCampTitle(e.target.value)}
              placeholder="Produto / Título da oferta"
              className="w-full rounded-xl border border-slate-200 px-3 py-2 text-xs font-bold"
            />
            <textarea
              value={campText}
              onChange={(e) => setCampText(e.target.value)}
              rows={2}
              className="w-full rounded-xl border border-slate-200 px-3 py-2 text-xs"
            />
            <button
              type="submit"
              style={{ backgroundColor: "var(--store-primary)" }}
              className="w-full rounded-xl px-4 py-2 text-xs font-bold text-white hover:opacity-90"
            >
              Disparar / Registrar Campanha ({m?.customersInBase ?? 0})
            </button>
          </div>
        </form>
      </div>

      {/* Ouvidoria WhatsApp */}
      <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div className="mb-3 flex items-center justify-between">
          <h3 className="text-sm font-extrabold text-slate-900">
            📋 Ouvidoria WhatsApp & Chat: Sugestões & Reclamações ({data?.feedbacks?.length ?? 0})
          </h3>
        </div>
        {(data?.feedbacks || []).length === 0 ? (
          <p className="text-xs text-slate-400">Nenhuma reclamação ou ocorrência pendente na Ouvidoria.</p>
        ) : (
          <div className="space-y-2">
            {data!.feedbacks!.map((fb) => (
              <div key={fb.id} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-rose-200 bg-rose-50/70 px-3.5 py-2.5 text-xs">
                <div>
                  <span className="rounded bg-rose-600 px-2 py-0.5 text-[10px] font-bold text-white">Ocorrência</span>{" "}
                  <strong className="font-mono">{fb.customerPhone}</strong>
                  <p className="mt-1 text-slate-800">&quot;{fb.messageText}&quot;</p>
                </div>
                <button
                  type="button"
                  onClick={async () => {
                    await apiFetch(`/api/v1/whatsapp/feedbacks/${fb.id}/resolve`, { method: "POST" }, token);
                    onNotify("Ocorrência marcada como resolvida.");
                    await load();
                  }}
                  className="rounded-lg border border-slate-300 bg-white px-3 py-1 font-bold text-slate-800 hover:bg-slate-100"
                >
                  Resolver
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      {(data?.campaigns || []).length > 0 && (
        <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h3 className="text-sm font-extrabold text-slate-900">Campanhas Registradas ({data?.campaigns.length ?? 0})</h3>
          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            {data!.campaigns.map((c) => (
              <div key={c.id} className="rounded-xl border border-slate-100 bg-slate-50 p-3 text-xs">
                <div className="flex items-center justify-between font-bold text-slate-900">
                  <span>{c.title}</span>
                  <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] text-emerald-800">{c.status}</span>
                </div>
                <p className="mt-1 text-slate-600">{c.messageText}</p>
                <p className="mt-1 text-[11px] text-slate-400">Alcance estimado: {c.audienceCount} clientes</p>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
