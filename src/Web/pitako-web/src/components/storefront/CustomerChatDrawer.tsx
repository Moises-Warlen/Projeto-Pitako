"use client";

import { MessageCircle, Send, X } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "@/lib/api";

interface ChatMsgDto {
  id: string;
  direction: string;
  contentText: string;
  sentAtUtc: string;
}

export function CustomerChatDrawer({
  slug,
  storeTradeName
}: {
  slug: string;
  storeTradeName: string;
}) {
  const [open, setOpen] = useState(false);
  const [customerName, setCustomerName] = useState("");
  const [customerPhone, setCustomerPhone] = useState("");
  const [messageInput, setMessageInput] = useState("");
  const [agentName, setAgentName] = useState(storeTradeName);
  const [messages, setMessages] = useState<ChatMsgDto[]>([]);
  const [sending, setSending] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const savedName = window.localStorage.getItem(`pitako_chat_name_${slug}`) || "";
    let savedPhone = window.localStorage.getItem(`pitako_chat_phone_${slug}`) || "";
    if (!savedPhone) {
      savedPhone = `169${String(Math.floor(10000000 + Math.random() * 89999999))}`;
      window.localStorage.setItem(`pitako_chat_phone_${slug}`, savedPhone);
    }
    if (savedName) setCustomerName(savedName);
    setCustomerPhone(savedPhone);
  }, [slug]);

  const loadMessages = useCallback(async () => {
    const cleanPhone = customerPhone.replace(/\D/g, "");
    const qs = cleanPhone ? `?phone=${encodeURIComponent(cleanPhone)}` : "";
    const res = await apiFetch<{
      agentName?: string;
      messages?: ChatMsgDto[];
    }>(`/api/v1/public/store/${slug}/chat${qs}`);

    if (res.ok && res.data) {
      if (res.data.agentName) setAgentName(res.data.agentName);
      setMessages(res.data.messages || []);
    }
  }, [customerPhone, slug]);

  useEffect(() => {
    if (!open) return;
    void loadMessages();
    const timer = setInterval(() => {
      void loadMessages();
    }, 4500);
    return () => clearInterval(timer);
  }, [loadMessages, open]);

  async function handleSend(e: React.FormEvent) {
    e.preventDefault();
    if (!messageInput.trim()) return;
    setErrorMsg(null);
    const phoneDigits = customerPhone.replace(/\D/g, "") || "16999990001";
    const nameFinal = customerName.trim() || "Cliente";

    if (typeof window !== "undefined") {
      window.localStorage.setItem(`pitako_chat_name_${slug}`, nameFinal);
      window.localStorage.setItem(`pitako_chat_phone_${slug}`, phoneDigits);
      if (!customerPhone.trim()) setCustomerPhone(phoneDigits);
      if (!customerName.trim()) setCustomerName(nameFinal);
    }

    setSending(true);
    const res = await apiFetch<{
      agentName?: string;
      messages?: ChatMsgDto[];
    }>(`/api/v1/public/store/${slug}/chat`, {
      method: "POST",
      body: JSON.stringify({
        customerName: nameFinal,
        customerPhone: phoneDigits,
        message: messageInput.trim()
      })
    });
    setSending(false);

    if (!res.ok) {
      setErrorMsg(res.errorMessage || "Não foi possível enviar sua mensagem.");
      return;
    }

    setMessageInput("");
    if (res.data?.agentName) setAgentName(res.data.agentName);
    if (res.data?.messages) setMessages(res.data.messages);
  }

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        style={{ backgroundColor: "var(--store-accent)" }}
        className="fixed bottom-20 right-4 z-30 inline-flex items-center gap-2 rounded-full px-4 py-3 text-xs font-extrabold text-white shadow-xl transition hover:opacity-95 sm:bottom-6 sm:right-6"
      >
        <MessageCircle className="h-4 w-4" />
        Chat com {storeTradeName}
      </button>

      {open && (
        <div className="fixed bottom-4 right-4 z-50 flex h-[470px] w-[calc(100vw-2rem)] max-w-sm flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl">
          <div
            style={{ backgroundColor: "var(--store-primary)" }}
            className="flex items-center justify-between px-4 py-3 text-white"
          >
            <div>
              <p className="text-sm font-extrabold">{agentName}</p>
              <p className="text-[11px] opacity-85">Atendimento direto • {storeTradeName}</p>
            </div>
            <button
              type="button"
              onClick={() => setOpen(false)}
              className="rounded-lg bg-white/15 p-1 text-white hover:bg-white/25"
              aria-label="Fechar chat"
            >
              <X className="h-4 w-4" />
            </button>
          </div>

          <div className="grid grid-cols-2 gap-2 border-b border-slate-100 bg-slate-50 px-3 py-2">
            <input
              value={customerName}
              onChange={(e) => setCustomerName(e.target.value)}
              placeholder="Seu nome"
              className="rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs outline-none focus:border-slate-400"
            />
            <input
              value={customerPhone}
              onChange={(e) => setCustomerPhone(e.target.value)}
              placeholder="WhatsApp (DDD+Nº)"
              className="rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs outline-none focus:border-slate-400"
            />
          </div>

          <div className="flex-1 space-y-2.5 overflow-y-auto bg-slate-50/70 p-3 text-xs">
            {messages.length === 0 ? (
              <div className="rounded-xl border border-slate-200 bg-white p-3 text-slate-600">
                Olá! Tire suas dúvidas sobre o cardápio, ingredientes ou status do pedido com a equipe da{" "}
                <strong>{storeTradeName}</strong>.
              </div>
            ) : (
              messages.map((m) => {
                const isCustomer = m.direction === "Inbound";
                return (
                  <div key={m.id} className={`flex ${isCustomer ? "justify-end" : "justify-start"}`}>
                    <div
                      className={`max-w-[82%] rounded-2xl px-3 py-2 shadow-sm ${
                        isCustomer
                          ? "bg-slate-900 text-white"
                          : "border border-slate-200 bg-white text-slate-800"
                      }`}
                    >
                      <p className="whitespace-pre-wrap leading-relaxed">{m.contentText}</p>
                      <p className={`mt-1 text-[10px] ${isCustomer ? "text-slate-300" : "text-slate-400"}`}>
                        {new Date(m.sentAtUtc).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}
                      </p>
                    </div>
                  </div>
                );
              })
            )}
            {errorMsg && <p className="rounded-lg bg-rose-50 p-2 text-[11px] font-semibold text-rose-700">{errorMsg}</p>}
          </div>

          <form onSubmit={handleSend} className="flex items-center gap-2 border-t border-slate-200 bg-white p-2.5">
            <input
              value={messageInput}
              onChange={(e) => setMessageInput(e.target.value)}
              placeholder="Escreva sua dúvida ou pedido..."
              className="flex-1 rounded-xl border border-slate-200 px-3 py-2 text-xs outline-none focus:border-slate-400"
            />
            <button
              type="submit"
              disabled={sending}
              style={{ backgroundColor: "var(--store-primary)" }}
              className="inline-flex h-9 items-center gap-1 rounded-xl px-3 text-xs font-bold text-white transition hover:opacity-90 disabled:opacity-60"
            >
              <Send className="h-3.5 w-3.5" /> Enviar
            </button>
          </form>
        </div>
      )}
    </>
  );
}
