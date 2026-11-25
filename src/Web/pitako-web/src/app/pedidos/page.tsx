"use client";

import { useEffect, useState } from "react";
import { StoreBrandShell } from "@/components/StoreBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

interface OrderCard {
  id: string;
  orderNumber: string;
  customerName: string;
  status: number;
  totalAmount?: { amount: number };
}

interface KanbanBoardResponse {
  received: OrderCard[];
  accepted: OrderCard[];
  inPreparation: OrderCard[];
  ready: OrderCard[];
  dispatched: OrderCard[];
}

export default function OrdersKanbanPage() {
  const [board, setBoard] = useState<KanbanBoardResponse>({
    received: [],
    accepted: [],
    inPreparation: [],
    ready: [],
    dispatched: []
  });
  const [msg, setMsg] = useState<string | null>(null);

  async function loadBoard() {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") {
      setMsg("Faça login em /login para acompanhar e mover o Kanban em tempo real.");
      return;
    }

    const res = await apiFetch<KanbanBoardResponse>("/api/v1/orders/kanban", {}, session.accessToken);
    if (res.ok && res.data) {
      setBoard(res.data);
      setMsg(null);
    } else {
      setMsg(res.errorMessage || "Erro ao carregar Kanban.");
    }
  }

  useEffect(() => {
    loadBoard();
  }, []);

  async function advanceOrder(orderId: string, nextStatus: number) {
    const session = loadSession();
    if (!session) return;
    await apiFetch(
      `/api/v1/orders/${orderId}/status`,
      { method: "POST", body: JSON.stringify({ nextStatus, reason: "Avanço Kanban web" }) },
      session.accessToken
    );
    loadBoard();
  }

  const columns = [
    { key: "received", label: "Recebidos", items: board.received, next: 2, nextLabel: "Aceitar" },
    { key: "accepted", label: "Aceitos", items: board.accepted, next: 3, nextLabel: "Iniciar Preparo" },
    { key: "prep", label: "Em Preparo", items: board.inPreparation, next: 4, nextLabel: "Marcar Pronto" },
    { key: "ready", label: "Prontos", items: board.ready, next: 5, nextLabel: "Despachar" },
    { key: "disp", label: "Em Rota / Balcão", items: board.dispatched, next: 6, nextLabel: "Finalizar" }
  ];

  return (
    <StoreBrandShell activeSection="/pedidos">
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Central de Pedidos & Kanban</h1>
          <p className="text-xs text-slate-500">Atualização em tempo real por tenant via SignalR (/hubs/realtime).</p>
        </div>
        <button
          onClick={loadBoard}
          style={{ backgroundColor: "var(--store-primary)" }}
          className="rounded-lg px-4 py-2 text-sm font-semibold text-white"
        >
          Atualizar Kanban
        </button>
      </div>

      {msg && (
        <div className="mb-5 rounded-xl border border-slate-300 bg-white p-4 text-sm text-slate-700">
          {msg}
        </div>
      )}

      <div className="grid gap-4 md:grid-cols-5">
        {columns.map((col) => (
          <div key={col.key} className="rounded-xl border border-slate-200 bg-white p-3 shadow-sm">
            <h2 className="mb-3 border-b border-slate-100 pb-2 text-sm font-bold text-slate-800">
              {col.label} ({col.items.length})
            </h2>
            <div className="space-y-2.5">
              {col.items.map((o) => (
                <div key={o.id} className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-xs">
                  <p className="font-bold text-slate-900">{o.orderNumber}</p>
                  <p className="text-slate-600">{o.customerName}</p>
                  {o.totalAmount && (
                    <p className="mt-1 font-semibold text-emerald-700">
                      R$ {o.totalAmount.amount.toFixed(2)}
                    </p>
                  )}
                  <button
                    onClick={() => advanceOrder(o.id, col.next)}
                    className="mt-2 w-full rounded bg-slate-800 py-1 font-medium text-white hover:bg-slate-900"
                  >
                    {col.nextLabel}
                  </button>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </StoreBrandShell>
  );
}
