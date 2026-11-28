"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { Bell, Check, MessageCircle, Power, Radio, Send, Store, UtensilsCrossed, Volume2, VolumeX, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch, loadSession, resolveMediaUrl } from "@/lib/api";
import {
  getOrderAlarmMuted,
  playContinuousOrderRingPulse,
  playNewMessageSound,
  playNewOrderSound,
  playWaiterCallSound,
  primeAlertAudio,
  setOrderAlarmMuted,
  subscribeOrderAlarmMute
} from "@/lib/soundAlerts";
import { usePitakoRealtime } from "@/lib/usePitakoRealtime";

export interface StoreBrandTheme {
  tradeName: string;
  formattedCnpj?: string;
  logoUrl?: string | null;
  primaryColorHex: string;
  secondaryColorHex: string;
  accentColorHex: string;
}

const FALLBACK_STORE: StoreBrandTheme = {
  tradeName: "Estabelecimento",
  formattedCnpj: "",
  logoUrl: null,
  primaryColorHex: "#1E293B",
  secondaryColorHex: "#F8FAFC",
  accentColorHex: "#0F766E"
};

/**
 * Moldura visual exclusiva do estabelecimento cliente (whitelabel).
 * Aplica exclusivamente cores, logo e CNPJ da loja — zero marca PITAKO.
 */
export function StoreBrandShell({
  theme,
  activeSection,
  hideOpsNav = false,
  onStoreStatusChanged,
  children
}: {
  theme?: StoreBrandTheme;
  activeSection?: string;
  hideOpsNav?: boolean;
  onStoreStatusChanged?: (isPaused: boolean) => void;
  children: React.ReactNode;
}) {
  const router = useRouter();
  const [loadedTheme, setLoadedTheme] = useState<StoreBrandTheme | null>(null);
  const [isPaused, setIsPaused] = useState<boolean>(false);
  const [statusKnown, setStatusKnown] = useState<boolean>(false);
  const [pendingOrdersCount, setPendingOrdersCount] = useState<number>(0);
  const [receivedOrdersCount, setReceivedOrdersCount] = useState<number>(0);
  const [alarmMuted, setAlarmMuted] = useState<boolean>(false);
  const [opsToken, setOpsToken] = useState<string>("");
  const [unreadChatCount, setUnreadChatCount] = useState<number>(0);
  const [unreadConversations, setUnreadConversations] = useState<
    Array<{
      id: string;
      customerName: string;
      customerPhone: string;
      status: string;
      unreadCount: number;
      lastMessagePreview: string;
      lastInteractionAtUtc: string;
    }>
  >([]);
  const [chatInboxOpen, setChatInboxOpen] = useState(false);
  const [selectedConvId, setSelectedConvId] = useState<string | null>(null);
  const [chatThread, setChatThread] = useState<{
    id: string;
    customerName: string;
    customerPhone: string;
    status: string;
    messages: Array<{ id: string; direction: string; contentText: string; sentAtUtc: string }>;
  } | null>(null);
  const [chatLoading, setChatLoading] = useState(false);
  const [quickReply, setQuickReply] = useState("");
  const [sendingReply, setSendingReply] = useState(false);
  const [chatFeedback, setChatFeedback] = useState<string | null>(null);
  const chatBoxRef = useRef<HTMLDivElement | null>(null);
  const [pendingWaiterCalls, setPendingWaiterCalls] = useState<
    Array<{ id: string; tableCode: string; callType: string; customerNotes?: string | null; requestedAtUtc: string }>
  >([]);
  const [liveToast, setLiveToast] = useState<{ kind: "order" | "chat" | "waiter"; title: string; detail: string; href: string } | null>(null);

  const baselineReadyRef = useRef(false);
  const lastOrderCountRef = useRef<number>(0);
  const lastOrderIdRef = useRef<string | null>(null);
  const lastInboundCountRef = useRef<number>(0);
  const lastInboundIdRef = useRef<string | null>(null);
  const lastWaiterIdsRef = useRef<string>("");

  useEffect(() => {
    if (hideOpsNav) return;
    primeAlertAudio();
    setAlarmMuted(getOrderAlarmMuted());
    return subscribeOrderAlarmMute(setAlarmMuted);
  }, [hideOpsNav]);

  useEffect(() => {
    if (hideOpsNav) return;
    const s = loadSession();
    if (s && s.scope === "Establishment") setOpsToken(s.accessToken);
  }, [hideOpsNav]);

  useEffect(() => {
    if (!liveToast) return;
    const t = setTimeout(() => setLiveToast(null), 2800);
    return () => clearTimeout(t);
  }, [liveToast]);

  const pollLiveAlerts = useCallback(async () => {
    if (hideOpsNav) return;
    const session = loadSession();
    if (!session || session.scope !== "Establishment") return;

    const res = await apiFetch<{
      pendingOrdersCount: number;
      receivedOrdersCount?: number;
      totalOrdersCount: number;
      latestOrderId?: string | null;
      latestOrderNumber?: string | null;
      latestOrderCustomer?: string | null;
      totalInboundChatMessages: number;
      unreadChatCount: number;
      unreadConversations?: Array<{
        id: string;
        customerName: string;
        customerPhone: string;
        status: string;
        unreadCount: number;
        lastMessagePreview: string;
        lastInteractionAtUtc: string;
      }>;
      latestInboundMessageId?: string | null;
      latestInboundCustomer?: string | null;
      latestInboundSnippet?: string | null;
      pendingWaiterCalls: Array<{
        id: string;
        tableCode: string;
        callType: string;
        customerNotes?: string | null;
        requestedAtUtc: string;
      }>;
    }>("/api/v1/store/live-alerts", {}, session.accessToken);

    if (!res.ok || !res.data) return;
    const d = res.data;
    setPendingOrdersCount(d.pendingOrdersCount);
    setReceivedOrdersCount(d.receivedOrdersCount ?? 0);
    setUnreadChatCount(d.unreadChatCount);
    if (Array.isArray(d.unreadConversations)) setUnreadConversations(d.unreadConversations);
    setPendingWaiterCalls(d.pendingWaiterCalls || []);

    const waiterIdsStr = (d.pendingWaiterCalls || []).map((w) => w.id).join(",");

    if (!baselineReadyRef.current) {
      baselineReadyRef.current = true;
      lastOrderCountRef.current = d.totalOrdersCount;
      lastOrderIdRef.current = d.latestOrderId ?? null;
      lastInboundCountRef.current = d.totalInboundChatMessages;
      lastInboundIdRef.current = d.latestInboundMessageId ?? null;
      lastWaiterIdsRef.current = waiterIdsStr;
      return;
    }

    const hasNewWaiter = (d.pendingWaiterCalls || []).some((w) => !lastWaiterIdsRef.current.includes(w.id));
    if (hasNewWaiter && d.pendingWaiterCalls.length > 0) {
      const latestW = d.pendingWaiterCalls[0];
      playWaiterCallSound();
      setLiveToast({
        kind: "waiter",
        title: latestW.callType === "RequestBill" ? `Mesa ${latestW.tableCode} pediu a conta!` : `Mesa ${latestW.tableCode} chamou o garçom!`,
        detail: latestW.customerNotes || "Atendimento solicitado no QR Code da mesa.",
        href: "/salao"
      });
    } else if (
      d.totalOrdersCount > lastOrderCountRef.current ||
      (d.latestOrderId && d.latestOrderId !== lastOrderIdRef.current)
    ) {
      playNewOrderSound();
      setLiveToast({
        kind: "order",
        title: `Novo pedido ${d.latestOrderNumber || "recebido"}!`,
        detail: d.latestOrderCustomer ? `Cliente: ${d.latestOrderCustomer}` : "Confira no Kanban de Pedidos.",
        href: "/pedidos"
      });
    } else if (
      d.totalInboundChatMessages > lastInboundCountRef.current ||
      (d.latestInboundMessageId && d.latestInboundMessageId !== lastInboundIdRef.current)
    ) {
      playNewMessageSound();
      setLiveToast({
        kind: "chat",
        title: `Nova mensagem de ${d.latestInboundCustomer || "cliente"}!`,
        detail: d.latestInboundSnippet || "Responda no Chat de Clientes.",
        href: "/admin"
      });
    }

    lastOrderCountRef.current = d.totalOrdersCount;
    lastOrderIdRef.current = d.latestOrderId ?? null;
    lastInboundCountRef.current = d.totalInboundChatMessages;
    lastInboundIdRef.current = d.latestInboundMessageId ?? null;
    lastWaiterIdsRef.current = waiterIdsStr;
  }, [hideOpsNav]);

  async function resolveWaiterCallFromBanner(id: string) {
    const session = loadSession();
    if (!session) return;
    await apiFetch(`/api/v1/dining/waiter-calls/${id}/complete`, { method: "POST" }, session.accessToken);
    void pollLiveAlerts();
  }

  const openConversationThread = useCallback(async (convId: string, clearFeedback = false) => {
    const session = loadSession();
    if (!session) return;
    setSelectedConvId(convId);
    setChatLoading(true);
    if (clearFeedback) setChatFeedback(null);
    try {
      const res = await apiFetch<{
        conversations?: Array<{
          id: string;
          customerName: string;
          customerPhone: string;
          status: string;
          messages: Array<{ id: string; direction: string; contentText: string; sentAtUtc: string }>;
        }>;
      }>("/api/v1/whatsapp/dashboard", {}, session.accessToken);
      if (res.ok && res.data?.conversations) {
        const match = res.data.conversations.find((c) => c.id === convId);
        if (match) setChatThread(match);
      }
    } finally {
      setChatLoading(false);
    }
  }, []);

  async function handleQuickReplySubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedConvId || !quickReply.trim() || sendingReply) return;
    const session = loadSession();
    if (!session) return;
    setSendingReply(true);
    setChatFeedback(null);
    try {
      const res = await apiFetch(
        `/api/v1/whatsapp/conversations/${selectedConvId}/reply`,
        { method: "POST", body: JSON.stringify({ messageText: quickReply.trim() }) },
        session.accessToken
      );
      if (res.ok) {
        setQuickReply("");
        setChatFeedback("Resposta enviada!");
        await Promise.all([openConversationThread(selectedConvId), pollLiveAlerts()]);
      } else {
        setChatFeedback(res.errorMessage || "Erro ao enviar resposta.");
      }
    } finally {
      setSendingReply(false);
    }
  }

  async function handleQuickResolve(convId: string) {
    const session = loadSession();
    if (!session) return;
    const res = await apiFetch(`/api/v1/whatsapp/conversations/${convId}/resolve`, { method: "POST" }, session.accessToken);
    if (res.ok) {
      setChatFeedback("Conversa resolvida!");
      await Promise.all([openConversationThread(convId), pollLiveAlerts()]);
    }
  }

  function openFullChatTab(convId?: string) {
    setChatInboxOpen(false);
    if (typeof window !== "undefined") {
      window.dispatchEvent(new CustomEvent("pitako:open-chat", { detail: { conversationId: convId } }));
      if (!window.location.pathname.startsWith("/admin")) {
        router.push(convId ? `/admin?tab=chat&conv=${convId}` : "/admin?tab=chat");
      }
    }
  }

  useEffect(() => {
    if (!chatInboxOpen) return;
    const handleOutside = (ev: MouseEvent) => {
      if (chatBoxRef.current && !chatBoxRef.current.contains(ev.target as Node)) {
        setChatInboxOpen(false);
      }
    };
    document.addEventListener("mousedown", handleOutside);
    return () => document.removeEventListener("mousedown", handleOutside);
  }, [chatInboxOpen]);

  const loadHeaderStatus = useCallback(async () => {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") return;

    const [brandRes, ordersRes] = await Promise.all([
      apiFetch<{
        tradeName?: string;
        legalName?: string;
        cnpj?: string;
        draft?: {
          logoUrl?: string | null;
          primaryColorHex?: string;
          secondaryColorHex?: string;
          accentColorHex?: string;
          isTemporarilyPaused?: boolean;
        };
      }>("/api/v1/store/branding", {}, session.accessToken),
      !hideOpsNav
        ? apiFetch<Array<{ status?: string }>>("/api/v1/orders/kanban", {}, session.accessToken)
        : Promise.resolve({ ok: false, data: null })
    ]);

    if (brandRes.ok && brandRes.data) {
      const d = brandRes.data;
      if (!theme) {
        setLoadedTheme({
          tradeName: d.tradeName || d.legalName || "Estabelecimento",
          formattedCnpj: d.cnpj || "",
          logoUrl: d.draft?.logoUrl ?? null,
          primaryColorHex: d.draft?.primaryColorHex || "#1E293B",
          secondaryColorHex: d.draft?.secondaryColorHex || "#F8FAFC",
          accentColorHex: d.draft?.accentColorHex || "#0F766E"
        });
      }
      setIsPaused(Boolean(d.draft?.isTemporarilyPaused));
      setStatusKnown(true);
    }

    if (ordersRes.ok && Array.isArray(ordersRes.data)) {
      const pending = ordersRes.data.filter(
        (o) => o.status === "Received" || o.status === "Accepted" || o.status === "InPreparation"
      ).length;
      setPendingOrdersCount(pending);
      setReceivedOrdersCount(ordersRes.data.filter((o) => o.status === "Received").length);
    }
  }, [hideOpsNav, theme]);

  useEffect(() => {
    void loadHeaderStatus();
  }, [loadHeaderStatus]);

  const realtimeState = usePitakoRealtime(!hideOpsNav ? opsToken : null, {
    onOrderUpdated: () => {
      void pollLiveAlerts();
    },
    onWaiterCallUpdated: () => {
      void pollLiveAlerts();
    },
    onWhatsappInboxUpdated: () => {
      void pollLiveAlerts();
    }
  });

  useEffect(() => {
    if (hideOpsNav) return;
    void pollLiveAlerts();
    const id = setInterval(() => void pollLiveAlerts(), 4000);
    return () => clearInterval(id);
  }, [hideOpsNav, pollLiveAlerts]);

  useEffect(() => {
    if (hideOpsNav || receivedOrdersCount <= 0 || alarmMuted) return;
    playContinuousOrderRingPulse();
    const ringId = setInterval(() => {
      playContinuousOrderRingPulse();
    }, 3600);
    return () => clearInterval(ringId);
  }, [hideOpsNav, receivedOrdersCount, alarmMuted]);

  async function toggleStoreOpenFromHeader() {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") return;
    const nextPaused = !isPaused;
    const res = await apiFetch(
      "/api/v1/store/pause",
      { method: "POST", body: JSON.stringify({ isPaused: nextPaused, reason: nextPaused ? "Pausada no topo do painel" : null }) },
      session.accessToken
    );
    if (res.ok) {
      setIsPaused(nextPaused);
      setStatusKnown(true);
      onStoreStatusChanged?.(nextPaused);
    }
  }

  const resolvedTheme = theme ?? loadedTheme ?? FALLBACK_STORE;

  useEffect(() => {
    if (typeof document !== "undefined" && resolvedTheme.tradeName && resolvedTheme.tradeName !== "Estabelecimento") {
      document.title = resolvedTheme.tradeName;
    }
  }, [resolvedTheme.tradeName]);

  const styleVars = {
    "--store-primary": resolvedTheme.primaryColorHex,
    "--store-secondary": resolvedTheme.secondaryColorHex,
    "--store-accent": resolvedTheme.accentColorHex
  } as React.CSSProperties;

  const navItems = [
    { href: "/admin", label: "Painel Loja" },
    { href: "/pdv", label: "PDV & Balança" },
    { href: "/pedidos", label: "Kanban Pedidos" },
    { href: "/salao", label: "Salão & Mesas" },
    { href: "/integracoes", label: "Integrações" }
  ];

  return (
    <div style={styleVars} className="min-h-screen bg-slate-50 text-slate-900">
      <header style={{ backgroundColor: "var(--store-primary)" }} className="text-white shadow">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-4 px-6 py-4">
          <div className="flex items-center gap-3">
            {resolveMediaUrl(resolvedTheme.logoUrl) ? (
              <img
                src={resolveMediaUrl(resolvedTheme.logoUrl)!}
                alt={`Logo ${resolvedTheme.tradeName}`}
                className="h-10 w-10 rounded-lg bg-white/10 object-contain p-1"
              />
            ) : (
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-white/15">
                <Store className="h-5 w-5" />
              </div>
            )}
            <div>
              <div className="flex items-center gap-2">
                <p className="text-base font-bold leading-tight">{resolvedTheme.tradeName}</p>
                {!hideOpsNav && (
                  <span className="rounded-md bg-white/15 px-2 py-0.5 text-[10px] font-extrabold tracking-wider text-white/95">
                    GESTOR PRO
                  </span>
                )}
              </div>
              {resolvedTheme.formattedCnpj && (
                <p className="text-xs opacity-80">CNPJ {resolvedTheme.formattedCnpj}</p>
              )}
            </div>
          </div>

          {!hideOpsNav && (
            <div className="flex flex-wrap items-center gap-3">
              {statusKnown && (
                <button
                  type="button"
                  onClick={() => void toggleStoreOpenFromHeader()}
                  title="Clique para abrir ou fechar o cardápio digital agora"
                  className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-extrabold shadow-sm transition ${
                    !isPaused
                      ? "bg-emerald-500 text-white hover:bg-emerald-400"
                      : "bg-rose-500 text-white hover:bg-rose-400"
                  }`}
                >
                  <Power className="h-3.5 w-3.5" />
                  {!isPaused ? "LOJA ABERTA" : "LOJA FECHADA"}
                </button>
              )}

              <Link
                href="/pedidos"
                title="Pedidos pendentes no Kanban"
                className={`relative inline-flex h-8 w-8 items-center justify-center rounded-lg text-white transition ${
                  receivedOrdersCount > 0 ? "animate-pulse bg-amber-500 text-slate-950" : "bg-white/15 hover:bg-white/25"
                }`}
              >
                <Bell className="h-4 w-4" />
                {pendingOrdersCount > 0 && (
                  <span className="absolute -right-1 -top-1 flex h-4 min-w-[16px] items-center justify-center rounded-full bg-amber-400 px-1 text-[10px] font-extrabold text-slate-900">
                    {pendingOrdersCount}
                  </span>
                )}
              </Link>

              {receivedOrdersCount > 0 && (
                <button
                  type="button"
                  onClick={() => setOrderAlarmMuted(!alarmMuted)}
                  title={alarmMuted ? "Reativar alarme sonoro de novos pedidos" : "Silenciar campainha de novos pedidos"}
                  className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[11px] font-extrabold transition ${
                    alarmMuted ? "bg-slate-800 text-slate-300" : "bg-rose-600 text-white"
                  }`}
                >
                  {alarmMuted ? <VolumeX className="h-3.5 w-3.5" /> : <Volume2 className="h-3.5 w-3.5" />}
                  {receivedOrdersCount} {receivedOrdersCount === 1 ? "Novo" : "Novos"}
                </button>
              )}

              {realtimeState === "connected" && (
                <span
                  title="Conectado em tempo real via SignalR"
                  className="hidden items-center gap-1 rounded-full bg-emerald-500/20 px-2 py-0.5 text-[10px] font-bold text-emerald-100 sm:inline-flex"
                >
                  <Radio className="h-3 w-3" /> Ao Vivo
                </span>
              )}

              <div ref={chatBoxRef} className="relative">
                <button
                  type="button"
                  onClick={() => {
                    const nextOpen = !chatInboxOpen;
                    setChatInboxOpen(nextOpen);
                    if (nextOpen) {
                      void pollLiveAlerts();
                      if (selectedConvId) void openConversationThread(selectedConvId);
                    }
                  }}
                  title="Notificações e mensagens de clientes"
                  aria-label="Abrir notificações de mensagens de clientes"
                  className={`relative inline-flex h-8 w-8 items-center justify-center rounded-lg text-white transition ${
                    unreadChatCount > 0 ? "animate-pulse bg-emerald-500 text-white hover:bg-emerald-400" : "bg-white/15 hover:bg-white/25"
                  }`}
                >
                  <MessageCircle className="h-4 w-4" />
                  {unreadChatCount > 0 && (
                    <span className="absolute -right-1.5 -top-1.5 flex h-4 min-w-[16px] items-center justify-center rounded-full bg-rose-500 px-1 text-[10px] font-extrabold text-white shadow">
                      {unreadChatCount}
                    </span>
                  )}
                </button>

                {chatInboxOpen && (
                  <div className="fixed right-4 top-16 z-50 w-96 max-w-[calc(100vw-2rem)] overflow-hidden rounded-2xl border border-slate-200 bg-white text-slate-900 shadow-2xl">
                    <div className="flex items-center justify-between border-b border-slate-100 bg-slate-50 px-4 py-3">
                      <div className="flex items-center gap-2">
                        <MessageCircle className="h-4 w-4 text-emerald-600" />
                        <span className="text-xs font-extrabold text-slate-900">Mensagens de Clientes</span>
                        <span
                          className={`rounded-full px-2 py-0.5 text-[10px] font-extrabold ${
                            unreadChatCount > 0 ? "bg-rose-100 text-rose-800" : "bg-slate-200 text-slate-700"
                          }`}
                        >
                          {unreadChatCount > 0 ? `${unreadChatCount} nova(s)` : "Em dia"}
                        </span>
                      </div>
                      <div className="flex items-center gap-2">
                        <button
                          type="button"
                          onClick={() => openFullChatTab(selectedConvId || unreadConversations[0]?.id)}
                          className="text-[11px] font-bold text-indigo-700 hover:underline"
                        >
                          Abrir Chat
                        </button>
                        <button
                          type="button"
                          onClick={() => setChatInboxOpen(false)}
                          aria-label="Fechar mensagens"
                          className="rounded p-0.5 text-slate-400 hover:bg-slate-200 hover:text-slate-700"
                        >
                          <X className="h-4 w-4" />
                        </button>
                      </div>
                    </div>

                    {!selectedConvId ? (
                      <div className="max-h-96 divide-y divide-slate-100 overflow-y-auto">
                        {unreadConversations.length === 0 ? (
                          <div className="p-6 text-center text-xs text-slate-500">
                            Nenhuma mensagem de cliente recebida ainda. Quando algum cliente mandar mensagem no cardápio ou WhatsApp, ela aparece aqui para você responder!
                          </div>
                        ) : (
                          unreadConversations.map((c) => (
                            <button
                              key={c.id}
                              type="button"
                              onClick={() => void openConversationThread(c.id, true)}
                              className="flex w-full items-start justify-between gap-3 px-4 py-3 text-left transition hover:bg-slate-50"
                            >
                              <div className="min-w-0 flex-1">
                                <div className="flex items-center gap-2">
                                  <span className="truncate text-xs font-extrabold text-slate-900">{c.customerName}</span>
                                  {c.unreadCount > 0 && (
                                    <span className="rounded-full bg-emerald-600 px-1.5 py-0.5 text-[10px] font-extrabold text-white">
                                      {c.unreadCount}
                                    </span>
                                  )}
                                </div>
                                <p className="text-[10px] text-slate-500">{c.customerPhone}</p>
                                <p className="mt-1 line-clamp-2 text-xs text-slate-700">{c.lastMessagePreview}</p>
                              </div>
                              <span className="shrink-0 rounded-lg bg-slate-900 px-2.5 py-1 text-[10px] font-bold text-white">
                                Responder
                              </span>
                            </button>
                          ))
                        )}
                      </div>
                    ) : (
                      <div className="flex flex-col">
                        <div className="flex items-center justify-between border-b border-slate-100 px-3 py-2 text-xs">
                          <button
                            type="button"
                            onClick={() => {
                              setSelectedConvId(null);
                              setChatThread(null);
                              setChatFeedback(null);
                            }}
                            className="font-bold text-slate-600 hover:text-slate-900"
                          >
                            ← Conversas
                          </button>
                          <div className="min-w-0 truncate px-2 text-center">
                            <span className="font-extrabold text-slate-900">
                              {chatThread?.customerName || unreadConversations.find((u) => u.id === selectedConvId)?.customerName || "Cliente"}
                            </span>
                          </div>
                          {chatThread && chatThread.status !== "Resolved" ? (
                            <button
                              type="button"
                              onClick={() => void handleQuickResolve(chatThread.id)}
                              className="rounded bg-emerald-50 px-2 py-0.5 text-[10px] font-bold text-emerald-700 hover:bg-emerald-100"
                            >
                              Resolver
                            </button>
                          ) : (
                            <span className="text-[10px] text-slate-400">{chatThread?.status || ""}</span>
                          )}
                        </div>

                        <div className="max-h-60 space-y-2 overflow-y-auto bg-slate-50 p-3 text-xs">
                          {chatLoading ? (
                            <p className="py-4 text-center text-slate-500">Carregando mensagens...</p>
                          ) : !chatThread || chatThread.messages.length === 0 ? (
                            <p className="py-4 text-center text-slate-500">Sem mensagens nesta conversa.</p>
                          ) : (
                            chatThread.messages.map((m) => {
                              const isInbound = m.direction === "Inbound";
                              return (
                                <div
                                  key={m.id}
                                  className={`max-w-[85%] rounded-xl px-3 py-2 ${
                                    isInbound
                                      ? "mr-auto border border-slate-200 bg-white text-slate-900"
                                      : "ml-auto bg-emerald-600 text-white"
                                  }`}
                                >
                                  <p>{m.contentText}</p>
                                  <p className={`mt-1 text-[9px] ${isInbound ? "text-slate-400" : "text-emerald-100"}`}>
                                    {new Date(m.sentAtUtc).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}
                                  </p>
                                </div>
                              );
                            })
                          )}
                        </div>

                        {chatFeedback && (
                          <div className="border-t border-emerald-100 bg-emerald-50 px-3 py-1.5 text-[11px] font-bold text-emerald-800">
                            {chatFeedback}
                          </div>
                        )}

                        <form onSubmit={handleQuickReplySubmit} className="flex gap-2 border-t border-slate-200 bg-white p-2.5">
                          <input
                            value={quickReply}
                            onChange={(e) => setQuickReply(e.target.value)}
                            placeholder="Responder para o cliente..."
                            className="min-w-0 flex-1 rounded-xl border border-slate-300 px-3 py-1.5 text-xs focus:border-emerald-500 focus:outline-none"
                          />
                          <button
                            type="submit"
                            disabled={sendingReply || !quickReply.trim()}
                            className="inline-flex items-center gap-1 rounded-xl bg-emerald-600 px-3 py-1.5 text-xs font-bold text-white hover:bg-emerald-700 disabled:opacity-50"
                          >
                            <Send className="h-3.5 w-3.5" /> Enviar
                          </button>
                        </form>
                      </div>
                    )}
                  </div>
                )}
              </div>

              <nav className="flex flex-wrap items-center gap-1.5 text-sm">
                {navItems.map((item) => (
                  <Link
                    key={item.href}
                    href={item.href}
                    className={`rounded-lg px-3 py-1.5 transition ${
                      activeSection === item.href
                        ? "bg-white/25 font-semibold text-white"
                        : "text-white/85 hover:bg-white/15 hover:text-white"
                    }`}
                  >
                    {item.label}
                  </Link>
                ))}
              </nav>
            </div>
          )}
        </div>
      </header>

      {!hideOpsNav && pendingWaiterCalls.length > 0 && (
        <div className="border-b border-amber-300 bg-amber-50 px-6 py-2.5 text-xs text-amber-950 shadow-sm">
          <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap items-center gap-2 font-extrabold">
              <UtensilsCrossed className="h-4 w-4 text-amber-700" />
              <span>Chamados de Mesa Pendentes ({pendingWaiterCalls.length}):</span>
              {pendingWaiterCalls.map((wc) => (
                <span key={wc.id} className="inline-flex items-center gap-1.5 rounded-full border border-amber-300 bg-white px-2.5 py-1 text-[11px]">
                  <strong>Mesa {wc.tableCode}</strong> · {wc.callType === "RequestBill" ? "Pedir Conta" : "Chamar Garçom"}
                  <button
                    type="button"
                    onClick={() => void resolveWaiterCallFromBanner(wc.id)}
                    className="ml-1 inline-flex items-center gap-0.5 rounded bg-emerald-600 px-1.5 py-0.5 text-[10px] font-bold text-white hover:bg-emerald-700"
                  >
                    <Check className="h-3 w-3" /> Atender
                  </button>
                </span>
              ))}
            </div>
          </div>
        </div>
      )}

      {liveToast && (
        <div
          role="alert"
          className="fixed bottom-5 right-5 z-50 flex max-w-sm items-start gap-3 rounded-2xl border border-slate-700 bg-slate-900 px-4 py-3 text-xs text-white shadow-2xl"
        >
          <Volume2 className="mt-0.5 h-4 w-4 shrink-0 text-amber-400" />
          <div className="min-w-0 flex-1">
            <p className="font-extrabold">{liveToast.title}</p>
            <p className="mt-0.5 text-slate-300">{liveToast.detail}</p>
            {liveToast.kind === "chat" ? (
              <button
                type="button"
                onClick={() => {
                  setLiveToast(null);
                  setChatInboxOpen(true);
                  if (unreadConversations[0]?.id) {
                    void openConversationThread(unreadConversations[0].id);
                  } else {
                    openFullChatTab();
                  }
                }}
                className="mt-1.5 inline-block font-bold text-amber-300 underline"
              >
                Responder agora
              </button>
            ) : (
              <Link href={liveToast.href} className="mt-1.5 inline-block font-bold text-amber-300 underline">
                Visualizar agora
              </Link>
            )}
          </div>
          <button type="button" onClick={() => setLiveToast(null)} className="text-slate-400 hover:text-white">
            ×
          </button>
        </div>
      )}

      <main className="mx-auto max-w-7xl px-6 py-8">{children}</main>
    </div>
  );
}
