"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { StoreBrandShell } from "@/components/StoreBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

interface TableItem {
  id: string;
  tableCode: string;
  capacity: number;
  qrCodeToken: string;
  status: string;
}

export default function DiningHallPage() {
  const [tables, setTables] = useState<TableItem[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [slug, setSlug] = useState("acai-demo");

  useEffect(() => {
    const session = loadSession();
    if (session?.storeSlug) setSlug(session.storeSlug);
    if (!session || session.scope !== "Establishment") {
      setMsg("Faça login em /login para listar e gerenciar mesas e comandas.");
      return;
    }

    apiFetch<TableItem[]>("/api/v1/dining/tables", {}, session.accessToken).then((res) => {
      if (res.ok && res.data) {
        setTables(res.data);
        setMsg(null);
      } else {
        setMsg(res.errorMessage || "Erro ao carregar mesas.");
      }
    });
  }, []);

  return (
    <StoreBrandShell activeSection="/salao">
      <div className="mb-6">
        <h1 className="text-xl font-bold text-slate-900">Salão, Mesas, Comandas & QR Code</h1>
        <p className="text-xs text-slate-500">
          Reservas sem conflito, transferência de mesas, divisão exata em centavos e autoatendimento por QR Code.
        </p>
      </div>

      {msg && (
        <div className="mb-5 rounded-xl border border-slate-300 bg-white p-4 text-sm text-slate-700">
          {msg}
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-4">
        {tables.map((t) => (
          <div key={t.id} className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
            <div className="flex items-center justify-between">
              <span className="text-lg font-bold text-slate-900">Mesa {t.tableCode}</span>
              <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-700">
                {t.status}
              </span>
            </div>
            <p className="mt-1 text-xs text-slate-500">Capacidade: {t.capacity} lugares</p>
            <Link
              href={`/mesa/${slug}/${t.qrCodeToken}`}
              className="mt-3 inline-block text-xs font-semibold text-indigo-700 hover:underline"
            >
              Abrir QR Code da Mesa →
            </Link>
          </div>
        ))}
      </div>
    </StoreBrandShell>
  );
}
