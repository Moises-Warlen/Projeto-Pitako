"use client";

import { Download, Printer, X } from "lucide-react";
import { useEffect, useState } from "react";
import { apiFetch, resolveMediaUrl } from "@/lib/api";

export interface PrintedOrderTicketDto {
  orderId: string;
  orderNumber: string;
  paperWidthMm: number;
  columnWidth: number;
  storeLogoUrl?: string | null;
  storeTradeName: string;
  storeCnpj: string;
  storeAddress?: string | null;
  storePhone?: string | null;
  hardwareDispatched: boolean;
  printerName?: string | null;
  statusMessage: string;
  previewTicketText: string;
  escPosBase64: string;
}

export function ThermalComandaModal({
  open,
  orderId,
  initialWidthMm,
  token,
  onClose
}: {
  open: boolean;
  orderId: string | null;
  initialWidthMm: 58 | 80;
  token: string;
  onClose: () => void;
}) {
  const [widthMm, setWidthMm] = useState<58 | 80>(initialWidthMm);
  const [printers, setPrinters] = useState<Array<{ id: string; displayName: string; sector: string; paperWidthMm: number }>>([]);
  const [selectedPrinterId, setSelectedPrinterId] = useState("");
  const [ticket, setTicket] = useState<PrintedOrderTicketDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [errMsg, setErrMsg] = useState<string | null>(null);

  useEffect(() => {
    setWidthMm(initialWidthMm);
  }, [initialWidthMm, orderId]);

  useEffect(() => {
    if (!open || !token) return;
    void apiFetch<{ printers: typeof printers }>("/api/v1/printing/printers", {}, token).then((res) => {
      if (res.ok && res.data?.printers) setPrinters(res.data.printers);
    });
  }, [open, token]);

  useEffect(() => {
    if (!open || !orderId || !token) return;
    let cancelled = false;
    setLoading(true);
    setErrMsg(null);
    const printerQs = selectedPrinterId ? `&printerId=${encodeURIComponent(selectedPrinterId)}` : "";
    void apiFetch<PrintedOrderTicketDto>(
      `/api/v1/orders/${orderId}/print-ticket?paperWidthMm=${widthMm}${printerQs}`,
      { method: "POST" },
      token
    ).then((res) => {
      if (cancelled) return;
      setLoading(false);
      if (res.ok && res.data) {
        setTicket(res.data);
      } else {
        setErrMsg(res.errorMessage || "Não foi possível gerar a comanda térmica.");
      }
    });
    return () => {
      cancelled = true;
    };
  }, [open, orderId, selectedPrinterId, widthMm, token]);

  if (!open || !orderId) return null;

  function handleBrowserPrint() {
    if (typeof window === "undefined") return;
    window.print();
  }

  function handleDownloadEscPos() {
    if (!ticket?.escPosBase64 || typeof window === "undefined") return;
    const bin = atob(ticket.escPosBase64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    const blob = new Blob([bytes], { type: "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `comanda-${ticket.orderNumber}-${widthMm}mm.bin`;
    a.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/75 p-4 backdrop-blur-sm print:static print:bg-white print:p-0">
      <style jsx global>{`
        @media print {
          body * {
            visibility: hidden;
          }
          #thermal-comanda-sheet,
          #thermal-comanda-sheet * {
            visibility: visible;
          }
          #thermal-comanda-sheet {
            position: fixed;
            left: 0;
            top: 0;
            width: ${widthMm}mm !important;
            max-width: ${widthMm}mm !important;
            margin: 0 !important;
            padding: 4px !important;
            box-shadow: none !important;
            border: none !important;
          }
        }
      `}</style>

      <div className="flex max-h-[92vh] w-full max-w-lg flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl print:max-h-none print:border-none print:shadow-none">
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-3.5 print:hidden">
          <div>
            <h3 className="text-sm font-extrabold text-slate-900">
              Comanda Térmica {widthMm}mm ({widthMm === 58 ? "32 colunas" : "48 colunas"})
            </h3>
            {ticket && <p className="text-[11px] text-slate-500">{ticket.statusMessage}</p>}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {printers.length > 0 && (
              <select
                value={selectedPrinterId}
                onChange={(e) => {
                  const id = e.target.value;
                  setSelectedPrinterId(id);
                  const found = printers.find((p) => p.id === id);
                  if (found && (found.paperWidthMm === 58 || found.paperWidthMm === 80)) {
                    setWidthMm(found.paperWidthMm);
                  }
                }}
                aria-label="Selecionar impressora"
                className="max-w-[165px] rounded-lg border border-slate-200 bg-white px-2 py-1 text-[11px] font-bold text-slate-800"
              >
                <option value="">Impressora Automática</option>
                {printers.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.displayName} ({p.paperWidthMm}mm)
                  </option>
                ))}
              </select>
            )}
            <div className="inline-flex rounded-lg border border-slate-200 bg-slate-100 p-0.5 text-[11px] font-bold">
              <button
                type="button"
                onClick={() => setWidthMm(58)}
                className={`rounded-md px-2.5 py-1 ${widthMm === 58 ? "bg-slate-900 text-white" : "text-slate-700"}`}
              >
                58mm
              </button>
              <button
                type="button"
                onClick={() => setWidthMm(80)}
                className={`rounded-md px-2.5 py-1 ${widthMm === 80 ? "bg-slate-900 text-white" : "text-slate-700"}`}
              >
                80mm
              </button>
            </div>
            <button type="button" onClick={onClose} className="rounded-lg p-1 text-slate-400 hover:bg-slate-100">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        <div className="flex-1 overflow-y-auto bg-slate-100 p-5 print:bg-white print:p-0">
          {loading ? (
            <p className="py-10 text-center text-xs text-slate-500">Renderizando comanda ESC/POS...</p>
          ) : errMsg ? (
            <div className="rounded-xl border border-rose-200 bg-rose-50 p-3 text-xs text-rose-800">{errMsg}</div>
          ) : ticket ? (
            <div
              id="thermal-comanda-sheet"
              style={{ maxWidth: widthMm === 58 ? "260px" : "360px" }}
              className="mx-auto rounded-lg border border-amber-200 bg-amber-50/70 p-4 font-mono text-[11px] leading-relaxed text-slate-900 shadow-inner"
            >
              {resolveMediaUrl(ticket.storeLogoUrl) && (
                <div className="mb-2 flex justify-center">
                  <img
                    src={resolveMediaUrl(ticket.storeLogoUrl)!}
                    alt={ticket.storeTradeName}
                    className="h-10 max-w-[120px] object-contain grayscale"
                  />
                </div>
              )}
              <pre className="whitespace-pre-wrap break-words font-mono text-[11px] leading-snug">
                {ticket.previewTicketText}
              </pre>
            </div>
          ) : null}
        </div>

        <div className="flex flex-wrap gap-2 border-t border-slate-200 bg-white px-5 py-3.5 print:hidden">
          <button
            type="button"
            disabled={!ticket}
            onClick={handleBrowserPrint}
            className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-xl bg-slate-900 px-4 py-2 text-xs font-extrabold text-white hover:bg-slate-800 disabled:opacity-50"
          >
            <Printer className="h-3.5 w-3.5" /> Imprimir ({widthMm}mm)
          </button>
          <button
            type="button"
            disabled={!ticket}
            onClick={handleDownloadEscPos}
            className="inline-flex items-center gap-1.5 rounded-xl border border-slate-300 px-3 py-2 text-xs font-bold text-slate-700 hover:bg-slate-50 disabled:opacity-50"
          >
            <Download className="h-3.5 w-3.5" /> ESC/POS .bin
          </button>
          <button type="button" onClick={onClose} className="rounded-xl border border-slate-300 px-4 py-2 text-xs font-bold text-slate-700">
            Fechar
          </button>
        </div>
      </div>
    </div>
  );
}
