"use client";

import { Bot, Brain, Check, Clock3, Megaphone, MessageCircle, Plus, Power, QrCode, RefreshCw, Search, Send, Sparkles, Trash2 } from "lucide-react";
import QRCode from "qrcode";
import { useCallback, useEffect, useRef, useState } from "react";
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
    customerName?: string;
    messageText: string;
    createdAtUtc: string;
    status?: string;
    updatedAtUtc?: string | null;
  }>;
  qrSession?: {
    sessionId: string;
    qrPayload: string;
    pairingCode: string;
    status: string;
    pairedPhone?: string | null;
    createdAtUtc?: string | null;
    expiresAtUtc?: string | null;
  } | null;
  training?: {
    assistantPersona: string;
    welcomeGreeting: string;
    storeKnowledgeNotes: string;
    faqs: Array<{ id: string; questionTitle: string; keywordsCsv: string; answerText: string; isActive: boolean }>;
  } | null;
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
  initialConversationId,
  onNotify
}: {
  token: string;
  storeTradeName: string;
  prefillMessage?: string | null;
  initialConversationId?: string | null;
  onNotify: (msg: string) => void;
}) {
  const [data, setData] = useState<WhatsAppDashboardDto | null>(null);
  const [selectedId, setSelectedId] = useState<string>(initialConversationId || "");
  const [agentName, setAgentName] = useState(storeTradeName);
  const [replyText, setReplyText] = useState(prefillMessage || "");
  const [sending, setSending] = useState(false);
  const [loadErr, setLoadErr] = useState<string | null>(null);

  useEffect(() => {
    if (prefillMessage) setReplyText(prefillMessage);
  }, [prefillMessage]);

  useEffect(() => {
    if (initialConversationId) setSelectedId(initialConversationId);
  }, [initialConversationId]);

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
                      {isCustomer ? activeConv.customerName : m.direction === "OutboundAiGrounded" ? storeTradeName : agentName}
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

  // Conexão por QR Code
  const [qrModeOpen, setQrModeOpen] = useState(false);
  const [qrDataUrl, setQrDataUrl] = useState<string>("");
  const [qrBusy, setQrBusy] = useState(false);

  // Treinamento do Bot & IA
  const trainingSectionRef = useRef<HTMLDivElement | null>(null);
  const [botPersona, setBotPersona] = useState("Amigável e Acolhedor");
  const [botGreeting, setBotGreeting] = useState("");
  const [botNotes, setBotNotes] = useState("");
  const [botFaqs, setBotFaqs] = useState<Array<{ id: string; questionTitle: string; keywordsCsv: string; answerText: string; isActive: boolean }>>([]);
  const [newFaqTitle, setNewFaqTitle] = useState("");
  const [newFaqKeywords, setNewFaqKeywords] = useState("");
  const [newFaqAnswer, setNewFaqAnswer] = useState("");
  const [savingTraining, setSavingTraining] = useState(false);
  const [simQuestion, setSimQuestion] = useState("Qual a taxa de entrega e formas de pagamento?");
  const [simResult, setSimResult] = useState<{ replyText: string; matchedFaqTitle?: string | null; usedTraining: boolean } | null>(null);
  const [fbSearch, setFbSearch] = useState("");
  const [fbStatusFilter, setFbStatusFilter] = useState<"all" | "unresolved" | "Analyzing" | "Resolved">("all");
  const [fbPage, setFbPage] = useState(1);
  const [simulatingBot, setSimulatingBot] = useState(false);

  const renderQrImage = useCallback(async (payload?: string | null) => {
    if (!payload) {
      setQrDataUrl("");
      return;
    }
    try {
      const url = await QRCode.toDataURL(payload, { width: 220, margin: 1 });
      setQrDataUrl(url);
    } catch {
      setQrDataUrl("");
    }
  }, []);

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
      if (res.data.qrSession?.qrPayload) {
        void renderQrImage(res.data.qrSession.qrPayload);
        if (res.data.qrSession.status === "WaitingScan") setQrModeOpen(true);
      }
      if (res.data.training) {
        setBotPersona(res.data.training.assistantPersona || "Amigável e Acolhedor");
        setBotGreeting(res.data.training.welcomeGreeting || "");
        setBotNotes(res.data.training.storeKnowledgeNotes || "");
        setBotFaqs(res.data.training.faqs || []);
      }
    }
  }, [renderQrImage, token]);

  async function handleGenerateQrCode() {
    setQrBusy(true);
    setQrModeOpen(true);
    try {
      const res = await apiFetch<{ sessionId: string; qrPayload: string; pairingCode: string; status: string }>(
        "/api/v1/whatsapp/qr-session",
        { method: "POST" },
        token
      );
      if (res.ok && res.data) {
        await renderQrImage(res.data.qrPayload);
        onNotify(`QR Code gerado (código ${res.data.pairingCode})! Aponte o WhatsApp e clique em Confirmar.`);
        await load();
      } else {
        onNotify(res.errorMessage || "Erro ao gerar QR Code.");
      }
    } finally {
      setQrBusy(false);
    }
  }

  async function handleConfirmQrPairing() {
    setQrBusy(true);
    try {
      const res = await apiFetch<{ connected: boolean; displayPhoneNumber: string }>(
        "/api/v1/whatsapp/qr-session/confirm",
        { method: "POST", body: JSON.stringify({ displayPhoneNumber: phoneInput || null }) },
        token
      );
      if (res.ok && res.data) {
        setQrModeOpen(false);
        onNotify(`WhatsApp conectado via QR Code (${res.data.displayPhoneNumber})!`);
        await load();
      } else {
        onNotify(res.errorMessage || "Erro ao confirmar pareamento QR.");
      }
    } finally {
      setQrBusy(false);
    }
  }

  async function handleSaveBotTraining(nextFaqs = botFaqs) {
    setSavingTraining(true);
    try {
      const res = await apiFetch<{ saved: boolean }>(
        "/api/v1/whatsapp/training",
        {
          method: "PUT",
          body: JSON.stringify({
            assistantPersona: botPersona,
            welcomeGreeting: botGreeting,
            storeKnowledgeNotes: botNotes,
            faqs: nextFaqs
          })
        },
        token
      );
      if (res.ok) {
        onNotify("Treinamento do Bot salvo e aplicado ao atendimento!");
        await load();
      } else {
        onNotify(res.errorMessage || "Erro ao salvar treinamento.");
      }
    } finally {
      setSavingTraining(false);
    }
  }

  async function handleSimulateBotTraining(e: React.FormEvent) {
    e.preventDefault();
    if (!simQuestion.trim()) return;
    setSimulatingBot(true);
    try {
      const res = await apiFetch<{ replyText: string; matchedFaqTitle?: string | null; usedTraining: boolean }>(
        "/api/v1/whatsapp/training/test",
        { method: "POST", body: JSON.stringify({ question: simQuestion.trim() }) },
        token
      );
      if (res.ok && res.data) {
        setSimResult(res.data);
      } else {
        onNotify(res.errorMessage || "Erro ao testar bot treinado.");
      }
    } finally {
      setSimulatingBot(false);
    }
  }

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
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h3 className="flex items-center gap-2 text-sm font-extrabold text-slate-900">
                <QrCode className="h-4 w-4 text-slate-600" /> Conexão com o WhatsApp
              </h3>
              <p className="mt-1 text-xs text-slate-500">Conecte escaneando o QR Code no WhatsApp ou informe o número oficial.</p>
            </div>
            <button
              type="button"
              disabled={qrBusy}
              onClick={() => void handleGenerateQrCode()}
              className="inline-flex items-center gap-1.5 rounded-xl bg-slate-900 px-3 py-1.5 text-xs font-extrabold text-white hover:bg-slate-800 disabled:opacity-50"
            >
              <QrCode className="h-3.5 w-3.5" /> {qrBusy ? "Gerando..." : "Conectar via QR Code"}
            </button>
          </div>

          <div className="mt-4 rounded-2xl border border-slate-200 bg-slate-50 p-5 text-center">
            {qrModeOpen && qrDataUrl ? (
              <div className="space-y-3">
                <p className="text-xs font-extrabold text-slate-900">Escaneie este QR Code no seu WhatsApp</p>
                <img
                  src={qrDataUrl}
                  alt="QR Code conexão WhatsApp"
                  className="mx-auto h-44 w-44 rounded-xl border-4 border-white bg-white shadow-md"
                />
                {data?.qrSession?.pairingCode && (
                  <p className="font-mono text-xs font-extrabold text-indigo-700">
                    Código de Pareamento: {data.qrSession.pairingCode}
                  </p>
                )}
                <p className="mx-auto max-w-xs text-[11px] text-slate-600">
                  No celular: <strong>WhatsApp → Dispositivos Conectados → Conectar Dispositivo</strong> e aponte a câmera. Depois confirme abaixo:
                </p>
                <div className="flex flex-wrap justify-center gap-2">
                  <button
                    type="button"
                    disabled={qrBusy}
                    onClick={() => void handleConfirmQrPairing()}
                    className="inline-flex items-center gap-1 rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-extrabold text-white hover:bg-emerald-700 disabled:opacity-50"
                  >
                    <Check className="h-3.5 w-3.5" /> Confirmar Conexão QR
                  </button>
                  <button
                    type="button"
                    disabled={qrBusy}
                    onClick={() => void handleGenerateQrCode()}
                    className="inline-flex items-center gap-1 rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-bold text-slate-700 hover:bg-slate-100"
                  >
                    <RefreshCw className="h-3.5 w-3.5" /> Novo QR Code
                  </button>
                  <button
                    type="button"
                    onClick={() => setQrModeOpen(false)}
                    className="rounded-xl border border-slate-200 px-3 py-2 text-xs font-semibold text-slate-500 hover:bg-white"
                  >
                    Fechar
                  </button>
                </div>
              </div>
            ) : data?.channel?.isActive ? (
              <div className="space-y-2">
                <MessageCircle className="mx-auto h-10 w-10 text-emerald-600" />
                <p className="text-sm font-extrabold text-emerald-700">WhatsApp Conectado!</p>
                <p className="text-xs text-slate-600">Número ativo: {data.channel.displayPhoneNumber}</p>
              </div>
            ) : (
              <div className="space-y-2">
                <QrCode className="mx-auto h-12 w-12 text-slate-400" />
                <p className="text-xs font-semibold text-slate-600">
                  Canal desconectado. Clique em <strong>Conectar via QR Code</strong> ou salve o número abaixo.
                </p>
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
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="flex items-center gap-2 text-sm font-extrabold text-slate-900">
              <Sparkles className="h-4 w-4 text-violet-600" /> Cérebro com Inteligência Artificial
            </h3>
            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={() => trainingSectionRef.current?.scrollIntoView({ behavior: "smooth", block: "start" })}
                className="inline-flex items-center gap-1 rounded-lg bg-violet-100 px-2.5 py-1 text-[11px] font-extrabold text-violet-800 hover:bg-violet-200"
              >
                <Brain className="h-3.5 w-3.5" /> Treinar Meu Bot ↓
              </button>
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
          </div>

          <ul className="mt-3 space-y-1.5 rounded-xl bg-slate-50 p-3.5 text-xs text-slate-700">
            <li>• <strong>Catálogo em Tempo Real:</strong> preços e itens oficiais da loja.</li>
            <li>• <strong>Rastreio Automático:</strong> identifica pedidos pelo telefone do cliente.</li>
            <li>• <strong>Venda Ativa (Upsell):</strong> sugere adicionais, combos e cupons.</li>
            <li>• <strong>Transbordo Humano:</strong> solicita gerente no Chat de Clientes quando necessário.</li>
          </ul>

          <div className="mt-3">
            <label className="text-[11px] font-bold text-slate-600">
              Chave API Google Gemini (Opcional, motor local já ativo) {data?.geminiApiKeyMasked && `• Atual: ${data.geminiApiKeyMasked}`}
            </label>
            <div className="mt-1 flex gap-2">
              <input
                type="password"
                autoComplete="off"
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

      {/* Treinar Meu Bot & Base de Conhecimento */}
      <div ref={trainingSectionRef} className="rounded-2xl border-2 border-violet-200 bg-white p-5 shadow-sm space-y-5">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 pb-3">
          <div>
            <h3 className="flex items-center gap-2 text-sm font-extrabold text-violet-950">
              <Brain className="h-5 w-5 text-violet-600" /> Treinar Meu Bot &amp; Base de Conhecimento ({botFaqs.length} regras)
            </h3>
            <p className="text-xs text-slate-500">
              Ensine o tom de atendimento, regras da loja (frete, PIX, alergênicos) e respostas automáticas por palavra-chave usadas no Chat e WhatsApp.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => {
                setBotGreeting((prev) => prev || `Olá! Bem-vindo à ${storeTradeName}! 🍧 Veja nossos destaques de hoje:`);
                setBotNotes((prev) => prev || "Entregamos em toda a cidade com tempo médio de 30-45 min. Aceitamos PIX, Cartão e Dinheiro com troco.");
                if (botFaqs.length === 0) {
                  setBotFaqs([
                    {
                      id: crypto.randomUUID(),
                      questionTitle: "Taxa e Tempo de Entrega",
                      keywordsCsv: "taxa, frete, entrega, demora",
                      answerText: "Entregamos em toda a região! A taxa é calculada no carrinho pelo endereço/bairro e o tempo médio é de 30 a 45 minutos.",
                      isActive: true
                    },
                    {
                      id: crypto.randomUUID(),
                      questionTitle: "Formas de Pagamento & PIX",
                      keywordsCsv: "pix, cartao, cartão, dinheiro, pagamento",
                      answerText: "Aceitamos PIX automático (Copia e Cola na tela), Cartão Crédito/Débito na entrega e Dinheiro com opção de troco!",
                      isActive: true
                    }
                  ]);
                }
                onNotify("Exemplos de treinamento preenchidos! Clique em Salvar Treinamento para aplicar.");
              }}
              className="rounded-xl border border-violet-300 bg-violet-50 px-3 py-1.5 text-xs font-bold text-violet-800 hover:bg-violet-100"
            >
              Carregar Exemplos Prontos
            </button>
            <button
              type="button"
              disabled={savingTraining}
              onClick={() => void handleSaveBotTraining()}
              className="rounded-xl bg-violet-700 px-4 py-1.5 text-xs font-extrabold text-white shadow-sm hover:bg-violet-800 disabled:opacity-50"
            >
              {savingTraining ? "Salvando..." : "Salvar Treinamento do Bot"}
            </button>
          </div>
        </div>

        <div className="grid gap-5 lg:grid-cols-2">
          <div className="space-y-3 text-xs">
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="block font-bold text-slate-700">
                Tom / Personalidade do Bot
                <select
                  value={botPersona}
                  onChange={(e) => setBotPersona(e.target.value)}
                  className="mt-1 w-full rounded-xl border border-slate-200 bg-white px-3 py-2"
                >
                  <option value="Amigável e Acolhedor">Amigável e Acolhedor</option>
                  <option value="Rápido e Direto">Rápido e Direto</option>
                  <option value="Vendedor / Sugestivo">Vendedor / Sugestivo</option>
                  <option value="Formal">Formal</option>
                </select>
              </label>
              <label className="block font-bold text-slate-700">
                Saudação / Abertura do Bot
                <input
                  value={botGreeting}
                  onChange={(e) => setBotGreeting(e.target.value)}
                  placeholder={`Ex: Seja bem-vindo(a) à ${storeTradeName}! Em que posso ajudar hoje?`}
                  className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2"
                />
              </label>
            </div>

            <label className="block font-bold text-slate-700">
              Conhecimento Geral da Loja (Regras, Taxas, Bairros, Diferenciais)
              <textarea
                rows={3}
                value={botNotes}
                onChange={(e) => setBotNotes(e.target.value)}
                placeholder="Ex: Nosso açaí é 100% artesanal sem xarope de guaraná. Entregas de seg a dom das 11h às 23h. Pedidos acima de R$ 60 têm entrega grátis!"
                className="mt-1 w-full rounded-xl border border-slate-200 px-3 py-2"
              />
            </label>

            {/* Simulador ao Vivo */}
            <form onSubmit={handleSimulateBotTraining} className="rounded-xl border border-slate-200 bg-slate-50 p-3.5 space-y-2">
              <p className="text-[11px] font-extrabold uppercase tracking-wide text-slate-700">
                🧪 Simulador: Teste como o Bot Responde Agora
              </p>
              <div className="flex gap-2">
                <input
                  value={simQuestion}
                  onChange={(e) => setSimQuestion(e.target.value)}
                  placeholder="Digite uma dúvida de cliente..."
                  className="flex-1 rounded-xl border border-slate-300 bg-white px-3 py-1.5 text-xs"
                />
                <button
                  type="submit"
                  disabled={simulatingBot}
                  className="rounded-xl bg-slate-900 px-3.5 py-1.5 text-xs font-bold text-white hover:bg-slate-800 disabled:opacity-50"
                >
                  {simulatingBot ? "Testando..." : "Testar Bot"}
                </button>
              </div>
              {simResult && (
                <div className="rounded-xl border border-emerald-200 bg-white p-3 text-xs text-slate-800">
                  <div className="mb-1 flex flex-wrap items-center gap-1.5">
                    <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-extrabold text-emerald-800">
                      Resposta do Bot
                    </span>
                    {simResult.matchedFaqTitle && (
                      <span className="rounded-full bg-violet-100 px-2 py-0.5 text-[10px] font-bold text-violet-800">
                        FAQ ativada: {simResult.matchedFaqTitle}
                      </span>
                    )}
                  </div>
                  <p>{simResult.replyText}</p>
                </div>
              )}
            </form>
          </div>

          <div className="space-y-3 text-xs">
            <div className="rounded-xl border border-slate-200 bg-slate-50 p-3.5 space-y-2">
              <p className="font-extrabold text-slate-900">Adicionar Nova Resposta Treinada (FAQ)</p>
              <div className="grid gap-2 sm:grid-cols-2">
                <input
                  value={newFaqTitle}
                  onChange={(e) => setNewFaqTitle(e.target.value)}
                  placeholder="Assunto (ex: Açaí Zero Açúcar)"
                  className="rounded-xl border border-slate-300 bg-white px-3 py-1.5"
                />
                <input
                  value={newFaqKeywords}
                  onChange={(e) => setNewFaqKeywords(e.target.value)}
                  placeholder="Palavras-chave (ex: zero, diet, acucar)"
                  className="rounded-xl border border-slate-300 bg-white px-3 py-1.5"
                />
              </div>
              <textarea
                rows={2}
                value={newFaqAnswer}
                onChange={(e) => setNewFaqAnswer(e.target.value)}
                placeholder="Resposta que o bot deve dar quando o cliente perguntar sobre isso..."
                className="w-full rounded-xl border border-slate-300 bg-white px-3 py-1.5"
              />
              <button
                type="button"
                onClick={() => {
                  if (!newFaqTitle.trim() || !newFaqAnswer.trim()) {
                    onNotify("Preencha o assunto e a resposta para treinar a regra.");
                    return;
                  }
                  const updated = [
                    ...botFaqs,
                    {
                      id: crypto.randomUUID(),
                      questionTitle: newFaqTitle.trim(),
                      keywordsCsv: newFaqKeywords.trim() || newFaqTitle.trim(),
                      answerText: newFaqAnswer.trim(),
                      isActive: true
                    }
                  ];
                  setBotFaqs(updated);
                  setNewFaqTitle("");
                  setNewFaqKeywords("");
                  setNewFaqAnswer("");
                  void handleSaveBotTraining(updated);
                }}
                className="inline-flex items-center gap-1 rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-extrabold text-white hover:bg-emerald-700"
              >
                <Plus className="h-3.5 w-3.5" /> Adicionar e Salvar Regra
              </button>
            </div>

            <div className="max-h-56 space-y-2 overflow-y-auto">
              {botFaqs.length === 0 ? (
                <p className="rounded-xl border border-dashed border-slate-200 p-4 text-center text-slate-400">
                  Nenhuma FAQ treinada ainda. Adicione regras acima ou clique em &quot;Carregar Exemplos Prontos&quot;.
                </p>
              ) : (
                botFaqs.map((f) => (
                  <div key={f.id} className="rounded-xl border border-slate-200 bg-white p-3">
                    <div className="flex items-start justify-between gap-2">
                      <div>
                        <p className="font-extrabold text-slate-900">{f.questionTitle}</p>
                        <p className="text-[10px] font-semibold text-violet-700">Gatilhos: {f.keywordsCsv}</p>
                        <p className="mt-1 text-slate-700">{f.answerText}</p>
                      </div>
                      <button
                        type="button"
                        onClick={() => {
                          const updated = botFaqs.filter((x) => x.id !== f.id);
                          setBotFaqs(updated);
                          void handleSaveBotTraining(updated);
                        }}
                        aria-label={`Remover regra ${f.questionTitle}`}
                        className="rounded-lg p-1 text-rose-600 hover:bg-rose-50"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
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
      {(() => {
        const allFbs = data?.feedbacks || [];
        const qFb = fbSearch.trim().toLowerCase();
        const filteredFbs = allFbs.filter((fb) => {
          const status = fb.status || "Pending";
          if (fbStatusFilter === "unresolved" && status === "Resolved") return false;
          if (fbStatusFilter === "Analyzing" && status !== "Analyzing") return false;
          if (fbStatusFilter === "Resolved" && status !== "Resolved") return false;
          if (!qFb) return true;
          return (
            fb.customerPhone.toLowerCase().includes(qFb) ||
            (fb.customerName || "").toLowerCase().includes(qFb) ||
            fb.messageText.toLowerCase().includes(qFb)
          );
        });
        const PAGE_SIZE = 5;
        const totalPages = Math.max(1, Math.ceil(filteredFbs.length / PAGE_SIZE));
        const currentPage = Math.min(fbPage, totalPages);
        const pageItems = filteredFbs.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);
        const unresolvedCount = allFbs.filter((f) => (f.status || "Pending") !== "Resolved").length;

        return (
          <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h3 className="text-sm font-extrabold text-slate-900">
                  📋 Ouvidoria WhatsApp & Chat: Sugestões & Reclamações ({filteredFbs.length} exibidas • {unresolvedCount} não resolvidas)
                </h3>
                <p className="text-[11px] text-slate-500">
                  Avise o cliente que está em análise (envia mensagem no chat), marque como resolvida ou exclua o registro.
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <div className="relative">
                  <Search className="pointer-events-none absolute left-2.5 top-2 h-3.5 w-3.5 text-slate-400" />
                  <input
                    value={fbSearch}
                    onChange={(e) => {
                      setFbSearch(e.target.value);
                      setFbPage(1);
                    }}
                    placeholder="Buscar telefone ou texto..."
                    className="rounded-xl border border-slate-200 bg-slate-50 pl-8 pr-3 py-1.5 text-xs outline-none focus:border-slate-400 focus:bg-white"
                  />
                </div>
                <select
                  value={fbStatusFilter}
                  onChange={(e) => {
                    setFbStatusFilter(e.target.value as "all" | "unresolved" | "Analyzing" | "Resolved");
                    setFbPage(1);
                  }}
                  aria-label="Filtrar ocorrências por status"
                  className="rounded-xl border border-slate-200 bg-white px-3 py-1.5 text-xs font-bold text-slate-700"
                >
                  <option value="all">Todos os Status ({allFbs.length})</option>
                  <option value="unresolved">Não Resolvidos ({unresolvedCount})</option>
                  <option value="Analyzing">Em Análise ({allFbs.filter((f) => f.status === "Analyzing").length})</option>
                  <option value="Resolved">Resolvidos ({allFbs.filter((f) => f.status === "Resolved").length})</option>
                </select>
              </div>
            </div>

            {pageItems.length === 0 ? (
              <p className="rounded-xl border border-dashed border-slate-200 p-4 text-center text-xs text-slate-400">
                Nenhuma sugestão ou reclamação encontrada com este filtro.
              </p>
            ) : (
              <div className="space-y-2.5">
                {pageItems.map((fb) => {
                  const st = fb.status || "Pending";
                  const isResolved = st === "Resolved";
                  const isAnalyzing = st === "Analyzing";
                  return (
                    <div
                      key={fb.id}
                      className={`flex flex-wrap items-center justify-between gap-3 rounded-xl border px-3.5 py-2.5 text-xs ${
                        isResolved
                          ? "border-emerald-200 bg-emerald-50/50"
                          : isAnalyzing
                            ? "border-amber-200 bg-amber-50/70"
                            : "border-rose-200 bg-rose-50/70"
                      }`}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-1.5">
                          <span
                            className={`rounded px-2 py-0.5 text-[10px] font-bold text-white ${
                              isResolved ? "bg-emerald-600" : isAnalyzing ? "bg-amber-600" : "bg-rose-600"
                            }`}
                          >
                            {isResolved ? "Resolvido" : isAnalyzing ? "Em Análise" : "Não Resolvido"}
                          </span>
                          <strong className="font-mono text-slate-900">{fb.customerPhone}</strong>
                          {fb.customerName && <span className="text-slate-600">• {fb.customerName}</span>}
                          <span className="text-[10px] text-slate-400">
                            {new Date(fb.createdAtUtc).toLocaleString("pt-BR")}
                          </span>
                        </div>
                        <p className="mt-1 text-slate-800">&quot;{fb.messageText}&quot;</p>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        {!isResolved && (
                          <button
                            type="button"
                            onClick={async () => {
                              const r = await apiFetch(`/api/v1/whatsapp/feedbacks/${fb.id}/analyze`, { method: "POST" }, token);
                              if (r.ok) {
                                onNotify("Cliente avisado no chat que a ocorrência está em análise!");
                                await load();
                              } else onNotify(r.errorMessage || "Erro ao avisar cliente.");
                            }}
                            className="inline-flex items-center gap-1 rounded-lg border border-amber-300 bg-amber-100 px-2.5 py-1.5 font-bold text-amber-900 hover:bg-amber-200"
                          >
                            <Clock3 className="h-3.5 w-3.5" /> Avisar: Em Análise
                          </button>
                        )}
                        {!isResolved && (
                          <button
                            type="button"
                            onClick={async () => {
                              const r = await apiFetch(`/api/v1/whatsapp/feedbacks/${fb.id}/resolve`, { method: "POST" }, token);
                              if (r.ok) {
                                onNotify("Ocorrência marcada como resolvida.");
                                await load();
                              } else onNotify(r.errorMessage || "Erro ao resolver ocorrência.");
                            }}
                            className="inline-flex items-center gap-1 rounded-lg bg-emerald-600 px-2.5 py-1.5 font-bold text-white hover:bg-emerald-700"
                          >
                            <Check className="h-3.5 w-3.5" /> Resolver
                          </button>
                        )}
                        <button
                          type="button"
                          onClick={async () => {
                            const r = await apiFetch(`/api/v1/whatsapp/feedbacks/${fb.id}`, { method: "DELETE" }, token);
                            if (r.ok) {
                              onNotify("Registro da Ouvidoria excluído.");
                              await load();
                            } else onNotify(r.errorMessage || "Erro ao excluir ocorrência.");
                          }}
                          className="inline-flex items-center gap-1 rounded-lg border border-rose-300 bg-white px-2.5 py-1.5 font-bold text-rose-700 hover:bg-rose-100"
                        >
                          <Trash2 className="h-3.5 w-3.5" /> Excluir
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}

            {filteredFbs.length > PAGE_SIZE && (
              <div className="flex items-center justify-between border-t border-slate-100 pt-3 text-xs">
                <span className="text-slate-500">
                  Mostrando {(currentPage - 1) * PAGE_SIZE + 1}–{Math.min(currentPage * PAGE_SIZE, filteredFbs.length)} de {filteredFbs.length} ocorrências
                </span>
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    disabled={currentPage <= 1}
                    onClick={() => setFbPage((p) => Math.max(1, p - 1))}
                    className="rounded-lg border border-slate-200 px-3 py-1 font-bold text-slate-700 hover:bg-slate-50 disabled:opacity-40"
                  >
                    Anterior
                  </button>
                  <span className="font-bold text-slate-700">
                    Página {currentPage} de {totalPages}
                  </span>
                  <button
                    type="button"
                    disabled={currentPage >= totalPages}
                    onClick={() => setFbPage((p) => Math.min(totalPages, p + 1))}
                    className="rounded-lg border border-slate-200 px-3 py-1 font-bold text-slate-700 hover:bg-slate-50 disabled:opacity-40"
                  >
                    Próxima
                  </button>
                </div>
              </div>
            )}
          </div>
        );
      })()}

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
