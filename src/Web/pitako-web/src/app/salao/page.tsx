"use client";

import { ArrowRightLeft, Bell, CalendarClock, Check, Plus, QrCode, Split, Trash2, UtensilsCrossed } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { QrCodeMatrix } from "@/components/QrCodeMatrix";
import { StoreBrandShell } from "@/components/StoreBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

interface SectorDto {
  id: string;
  name: string;
  displayOrder: number;
}
interface TableDto {
  id: string;
  sectorId: string;
  tableCode: string;
  capacity: number;
  qrCodeToken: string;
  status: string;
  currentTabId?: string | null;
}
interface TabItemDto {
  id: string;
  itemTitle: string;
  customizationSummary: string;
  unitPrice: number;
  quantity: number;
  lineSubtotal: number;
}
interface TabDto {
  id: string;
  tabCode: string;
  diningTableId?: string | null;
  customerName: string;
  guestCount: number;
  serviceChargePercent: number;
  status: string;
  itemsSubtotal: number;
  serviceChargeAmount: number;
  totalAmount: number;
  paidAmount: number;
  remainingBalance: number;
  items: TabItemDto[];
}
interface WaiterCallDto {
  id: string;
  diningTableId: string;
  tableCode: string;
  callType: string;
  status: string;
  customerNotes?: string | null;
  requestedAtUtc: string;
}
interface CatalogSimpleProd {
  id: string;
  name: string;
  basePrice: number;
}

export default function DiningHallPage() {
  const [token, setToken] = useState("");
  const [slug, setSlug] = useState("acai-demo");
  const [sectors, setSectors] = useState<SectorDto[]>([]);
  const [tables, setTables] = useState<TableDto[]>([]);
  const [openTabs, setOpenTabs] = useState<TabDto[]>([]);
  const [waiterCalls, setWaiterCalls] = useState<WaiterCallDto[]>([]);
  const [catalogProds, setCatalogProds] = useState<CatalogSimpleProd[]>([]);
  const [msg, setMsg] = useState<string | null>(null);

  const [selectedTabId, setSelectedTabId] = useState<string>("");
  const [qrPreviewTable, setQrPreviewTable] = useState<TableDto | null>(null);

  // Forms
  const [newSectorName, setNewSectorName] = useState("");
  const [newTableCode, setNewTableCode] = useState("");
  const [newTableCap, setNewTableCap] = useState("4");
  const [newTableSectorId, setNewTableSectorId] = useState("");

  const [openTabTableId, setOpenTabTableId] = useState("");
  const [openTabCustomer, setOpenTabCustomer] = useState("");
  const [addProdId, setAddProdId] = useState("");
  const [addCustomTitle, setAddCustomTitle] = useState("");
  const [addCustomPrice, setAddCustomPrice] = useState("19.90");
  const [addQty, setAddQty] = useState("1");

  const [transferTargetTableId, setTransferTargetTableId] = useState("");
  const [splitPayers, setSplitPayers] = useState("2");
  const [splitShares, setSplitShares] = useState<Array<{ payerIndex: number; amount: number }>>([]);
  const [payAmount, setPayAmount] = useState("");

  const [resTableId, setResTableId] = useState("");
  const [resName, setResName] = useState("");
  const [resPhone, setResPhone] = useState("");
  const [resSize, setResSize] = useState("4");
  const [resWhen, setResWhen] = useState(() => new Date(Date.now() + 3600000).toISOString().slice(0, 16));

  const reloadAll = useCallback(async () => {
    const session = loadSession();
    if (!session || session.scope !== "Establishment") {
      setMsg("Faça login em /login como estabelecimento para operar o Salão.");
      return;
    }
    setToken(session.accessToken);
    if (session.storeSlug) setSlug(session.storeSlug);

    const [ovRes, catRes] = await Promise.all([
      apiFetch<{
        sectors: SectorDto[];
        tables: TableDto[];
        openTabs: TabDto[];
        waiterCalls: WaiterCallDto[];
      }>("/api/v1/dining/overview", {}, session.accessToken),
      apiFetch<{ products: CatalogSimpleProd[] }>("/api/v1/catalog/products", {}, session.accessToken)
    ]);

    if (ovRes.ok && ovRes.data) {
      setSectors(ovRes.data.sectors);
      setTables(ovRes.data.tables);
      setOpenTabs(ovRes.data.openTabs);
      setWaiterCalls(ovRes.data.waiterCalls);
      if (ovRes.data.sectors[0]) setNewTableSectorId((p) => p || ovRes.data!.sectors[0].id);
      if (ovRes.data.tables[0]) {
        setResTableId((p) => p || ovRes.data!.tables[0].id);
        setOpenTabTableId((p) => p || ovRes.data!.tables[0].id);
      }
      if (ovRes.data.openTabs[0]) {
        setSelectedTabId((prev) => (prev && ovRes.data!.openTabs.some((t) => t.id === prev) ? prev : ovRes.data!.openTabs[0].id));
      } else {
        setSelectedTabId("");
      }
    } else {
      setMsg(ovRes.errorMessage || "Erro ao carregar dados do salão.");
    }

    if (catRes.ok && catRes.data) setCatalogProds(catRes.data.products || []);
  }, []);

  useEffect(() => {
    void reloadAll();
  }, [reloadAll]);

  const activeTab = openTabs.find((t) => t.id === selectedTabId) ?? null;

  async function handleCreateSector(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch("/api/v1/dining/sectors", { method: "POST", body: JSON.stringify({ name: newSectorName, displayOrder: sectors.length + 1 }) }, token);
    if (res.ok) {
      setNewSectorName("");
      setMsg("Setor criado!");
      await reloadAll();
    } else setMsg(res.errorMessage || "Erro ao criar setor.");
  }

  async function handleCreateTable(e: React.FormEvent) {
    e.preventDefault();
    const sid = newTableSectorId || sectors[0]?.id;
    if (!sid) return setMsg("Crie um setor antes de criar mesas.");
    const res = await apiFetch("/api/v1/dining/tables", { method: "POST", body: JSON.stringify({ sectorId: sid, tableCode: newTableCode, capacity: Number(newTableCap) || 4 }) }, token);
    if (res.ok) {
      setNewTableCode("");
      setMsg("Mesa cadastrada com QR Code exclusivo!");
      await reloadAll();
    } else setMsg(res.errorMessage || "Erro ao criar mesa.");
  }

  async function handleOpenTab(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch<TabDto>(
      "/api/v1/dining/tabs",
      { method: "POST", body: JSON.stringify({ customerName: openTabCustomer, guestCount: 2, diningTableId: openTabTableId || null, serviceChargePercent: 10 }) },
      token
    );
    if (res.ok && res.data) {
      setOpenTabCustomer("");
      setSelectedTabId(res.data.id);
      setMsg(`Comanda ${res.data.tabCode} aberta!`);
      await reloadAll();
    } else setMsg(res.errorMessage || "Erro ao abrir comanda.");
  }

  async function handleAddTabItem(e: React.FormEvent) {
    e.preventDefault();
    if (!activeTab) return;
    const chosenProd = catalogProds.find((p) => p.id === addProdId);
    const title = chosenProd ? chosenProd.name : addCustomTitle;
    const price = chosenProd ? chosenProd.basePrice : Number(addCustomPrice);
    if (!title.trim()) return;

    const res = await apiFetch(
      `/api/v1/dining/tabs/${activeTab.id}/items`,
      { method: "POST", body: JSON.stringify({ productId: chosenProd?.id || null, itemTitle: title, unitPrice: price, quantity: Number(addQty) || 1 }) },
      token
    );
    if (res.ok) {
      setAddCustomTitle("");
      await reloadAll();
    } else setMsg(res.errorMessage || "Erro ao lançar item.");
  }

  async function handleCancelItem(itemId: string) {
    if (!activeTab) return;
    await apiFetch(`/api/v1/dining/tabs/${activeTab.id}/items/${itemId}`, { method: "DELETE" }, token);
    await reloadAll();
  }

  async function handleTransferTab() {
    if (!activeTab || !transferTargetTableId) return;
    const res = await apiFetch(`/api/v1/dining/tabs/${activeTab.id}/transfer`, { method: "POST", body: JSON.stringify({ targetTableId: transferTargetTableId }) }, token);
    if (res.ok) {
      setMsg("Comanda transferida de mesa!");
      await reloadAll();
    } else setMsg(res.errorMessage || "Não foi possível transferir.");
  }

  async function handleSplitBill() {
    if (!activeTab) return;
    const res = await apiFetch<Array<{ payerIndex: number; amount: number }>>(
      `/api/v1/dining/tabs/${activeTab.id}/split/${Math.max(1, Number(splitPayers) || 2)}`,
      {},
      token
    );
    if (res.ok && res.data) setSplitShares(res.data);
  }

  async function handlePayTab(e: React.FormEvent) {
    e.preventDefault();
    if (!activeTab) return;
    const amt = Number(payAmount) || activeTab.remainingBalance;
    const res = await apiFetch(`/api/v1/dining/tabs/${activeTab.id}/pay`, { method: "POST", body: JSON.stringify({ amount: amt }) }, token);
    if (res.ok) {
      setPayAmount("");
      setSplitShares([]);
      setMsg("Pagamento lançado na comanda!");
      await reloadAll();
    } else setMsg(res.errorMessage || "Erro ao registrar pagamento.");
  }

  async function handleReservation(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch(
      "/api/v1/dining/reservations",
      {
        method: "POST",
        body: JSON.stringify({
          tableId: resTableId,
          customerName: resName,
          customerPhone: resPhone,
          partySize: Number(resSize) || 2,
          reservedForUtc: new Date(resWhen).toISOString(),
          durationMinutes: 90
        })
      },
      token
    );
    if (res.ok) {
      setResName("");
      setResPhone("");
      setMsg("Reserva confirmada sem conflito de horário!");
      await reloadAll();
    } else setMsg(res.errorMessage || "Conflito de reserva detectado.");
  }

  async function handleWaiterCall(id: string, action: "acknowledge" | "complete") {
    await apiFetch(`/api/v1/dining/waiter-calls/${id}/${action}`, { method: "POST" }, token);
    await reloadAll();
  }

  const originUrl = typeof window !== "undefined" ? window.location.origin : "http://localhost:3000";

  return (
    <StoreBrandShell activeSection="/salao">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-extrabold text-slate-900">Salão, Mesas, Comandas & QR Code</h1>
          <p className="text-xs text-slate-500">
            Reservas sem conflito, transferência de mesas, divisão exata em centavos e autoatendimento por QR Code.
          </p>
        </div>
        <button type="button" onClick={reloadAll} style={{ backgroundColor: "var(--store-primary)" }} className="rounded-xl px-4 py-2 text-xs font-bold text-white">
          Atualizar Salão
        </button>
      </div>

      {msg && (
        <div className="mb-5 flex items-center justify-between rounded-2xl border border-slate-300 bg-white px-4 py-3 text-xs font-semibold text-slate-800">
          <span>{msg}</span>
          <button type="button" onClick={() => setMsg(null)} className="underline">Fechar</button>
        </div>
      )}

      {/* Chamados de Garçom */}
      {waiterCalls.length > 0 && (
        <section className="mb-6 rounded-2xl border border-amber-300 bg-amber-50 p-4">
          <h2 className="mb-2 flex items-center gap-2 text-sm font-extrabold text-amber-900">
            <Bell className="h-4 w-4" /> Chamados de Mesas ({waiterCalls.length})
          </h2>
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3 text-xs">
            {waiterCalls.map((wc) => (
              <div key={wc.id} className="flex items-center justify-between rounded-xl bg-white p-3 shadow-sm">
                <div>
                  <p className="font-extrabold text-slate-900">Mesa {wc.tableCode} — {wc.callType}</p>
                  {wc.customerNotes && <p className="text-slate-500">{wc.customerNotes}</p>}
                </div>
                <div className="flex gap-1">
                  {wc.status === "Pending" && (
                    <button type="button" onClick={() => handleWaiterCall(wc.id, "acknowledge")} className="rounded-lg bg-amber-500 px-2.5 py-1 font-bold text-white">
                      Atender
                    </button>
                  )}
                  <button type="button" onClick={() => handleWaiterCall(wc.id, "complete")} className="rounded-lg bg-emerald-600 px-2.5 py-1 font-bold text-white">
                    <Check className="inline h-3.5 w-3.5" /> Concluir
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        {/* Mesas + Setores */}
        <div className="space-y-6 lg:col-span-2">
          <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-base font-extrabold text-slate-900">Mapa de Mesas ({tables.length})</h2>
              {sectors.length > 0 && (
                <div className="flex flex-wrap gap-1.5 text-[11px]">
                  {sectors.map((sec) => (
                    <span key={sec.id} className="rounded-full border border-slate-200 bg-slate-100 px-2.5 py-0.5 font-semibold text-slate-700">
                      {sec.name}
                    </span>
                  ))}
                </div>
              )}
            </div>
            <div className="grid gap-3 sm:grid-cols-2 md:grid-cols-3">
              {tables.map((t) => {
                const tab = openTabs.find((x) => x.id === t.currentTabId || x.diningTableId === t.id);
                const occupied = Boolean(tab);
                return (
                  <div key={t.id} className={`rounded-2xl border p-4 ${occupied ? "border-amber-300 bg-amber-50/50" : "border-slate-200 bg-slate-50/60"}`}>
                    <div className="flex items-center justify-between">
                      <span className="text-base font-extrabold text-slate-900">Mesa {t.tableCode}</span>
                      <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${occupied ? "bg-amber-200 text-amber-900" : "bg-emerald-100 text-emerald-800"}`}>
                        {occupied ? "Ocupada" : t.status}
                      </span>
                    </div>
                    <p className="mt-1 text-xs text-slate-500">{t.capacity} lugares</p>
                    {tab && (
                      <p className="mt-1 text-xs font-bold text-slate-800">
                        {tab.tabCode} · R$ {tab.remainingBalance.toFixed(2)}
                      </p>
                    )}
                    <div className="mt-3 flex flex-wrap gap-1.5 text-xs">
                      {tab ? (
                        <button type="button" onClick={() => setSelectedTabId(tab.id)} className="rounded-lg bg-slate-900 px-2.5 py-1 font-bold text-white">
                          Gerenciar Comanda
                        </button>
                      ) : (
                        <button
                          type="button"
                          onClick={() => {
                            setOpenTabTableId(t.id);
                            setOpenTabCustomer(`Mesa ${t.tableCode}`);
                          }}
                          className="rounded-lg border border-slate-300 bg-white px-2.5 py-1 font-semibold text-slate-700"
                        >
                          Selecionar p/ Abrir
                        </button>
                      )}
                      <button type="button" onClick={() => setQrPreviewTable(t)} className="inline-flex items-center gap-1 rounded-lg border border-indigo-200 bg-white px-2 py-1 font-semibold text-indigo-700">
                        <QrCode className="h-3.5 w-3.5" /> QR
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          </section>

          {/* Comanda Selecionada */}
          {activeTab && (
            <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b pb-3">
                <div>
                  <h2 className="flex items-center gap-2 text-base font-extrabold text-slate-900">
                    <UtensilsCrossed className="h-4 w-4" /> Comanda {activeTab.tabCode} — {activeTab.customerName}
                  </h2>
                  <p className="text-xs text-slate-500">
                    Subtotal: R$ {activeTab.itemsSubtotal.toFixed(2)} · Serviço ({activeTab.serviceChargePercent}%): R$ {activeTab.serviceChargeAmount.toFixed(2)} · Total:{" "}
                    <strong>R$ {activeTab.totalAmount.toFixed(2)}</strong> · Pago: R$ {activeTab.paidAmount.toFixed(2)} ·{" "}
                    <strong className="text-rose-700">Restante: R$ {activeTab.remainingBalance.toFixed(2)}</strong>
                  </p>
                </div>
                <select value={selectedTabId} onChange={(e) => setSelectedTabId(e.target.value)} className="rounded-xl border px-3 py-1.5 text-xs font-bold">
                  {openTabs.map((t) => <option key={t.id} value={t.id}>{t.tabCode} ({t.customerName})</option>)}
                </select>
              </div>

              <div className="space-y-1.5 text-xs">
                {activeTab.items.map((it) => (
                  <div key={it.id} className="flex items-center justify-between rounded-xl bg-slate-50 px-3 py-2">
                    <span><strong>{it.quantity}x {it.itemTitle}</strong></span>
                    <div className="flex items-center gap-3">
                      <span className="font-mono font-bold">R$ {it.lineSubtotal.toFixed(2)}</span>
                      <button type="button" onClick={() => handleCancelItem(it.id)} className="text-rose-600"><Trash2 className="h-3.5 w-3.5" /></button>
                    </div>
                  </div>
                ))}
              </div>

              {/* Lançar item */}
              <form onSubmit={handleAddTabItem} className="grid gap-2 border-t pt-3 text-xs sm:grid-cols-4">
                <select value={addProdId} onChange={(e) => setAddProdId(e.target.value)} className="rounded-xl border px-2.5 py-2 sm:col-span-2">
                  <option value="">-- Item avulso / digitar abaixo --</option>
                  {catalogProds.map((p) => <option key={p.id} value={p.id}>{p.name} (R$ {p.basePrice.toFixed(2)})</option>)}
                </select>
                {!addProdId && (
                  <>
                    <input value={addCustomTitle} onChange={(e) => setAddCustomTitle(e.target.value)} required placeholder="Nome do item" className="rounded-xl border px-2.5 py-2" />
                    <input type="number" step="0.01" value={addCustomPrice} onChange={(e) => setAddCustomPrice(e.target.value)} className="rounded-xl border px-2.5 py-2" />
                  </>
                )}
                <input type="number" min="1" value={addQty} onChange={(e) => setAddQty(e.target.value)} className="rounded-xl border px-2.5 py-2" />
                <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="rounded-xl px-3 py-2 font-bold text-white">
                  + Lançar
                </button>
              </form>

              {/* Transferência, Divisão em Centavos e Pagamento */}
              <div className="grid gap-3 border-t pt-3 text-xs sm:grid-cols-3">
                <div className="space-y-1.5 rounded-xl bg-slate-50 p-3">
                  <p className="font-bold text-slate-800"><ArrowRightLeft className="mr-1 inline h-3.5 w-3.5" /> Transferir Mesa</p>
                  <select value={transferTargetTableId} onChange={(e) => setTransferTargetTableId(e.target.value)} className="w-full rounded-lg border bg-white px-2 py-1.5">
                    <option value="">Escolher mesa destino...</option>
                    {tables.filter((t) => t.id !== activeTab.diningTableId).map((t) => <option key={t.id} value={t.id}>Mesa {t.tableCode}</option>)}
                  </select>
                  <button type="button" onClick={handleTransferTab} className="w-full rounded-lg bg-slate-900 py-1.5 font-bold text-white">Transferir</button>
                </div>

                <div className="space-y-1.5 rounded-xl bg-slate-50 p-3">
                  <p className="font-bold text-slate-800"><Split className="mr-1 inline h-3.5 w-3.5" /> Dividir em Centavos</p>
                  <div className="flex gap-1.5">
                    <input type="number" min="2" value={splitPayers} onChange={(e) => setSplitPayers(e.target.value)} className="w-16 rounded-lg border bg-white px-2 py-1.5" />
                    <button type="button" onClick={handleSplitBill} className="flex-1 rounded-lg bg-indigo-700 py-1.5 font-bold text-white">Calcular</button>
                  </div>
                  {splitShares.length > 0 && (
                    <div className="space-y-0.5 text-[11px] font-mono">
                      {splitShares.map((s) => <div key={s.payerIndex}>Pessoa {s.payerIndex}: R$ {s.amount.toFixed(2)}</div>)}
                    </div>
                  )}
                </div>

                <form onSubmit={handlePayTab} className="space-y-1.5 rounded-xl bg-emerald-50 p-3">
                  <p className="font-bold text-emerald-900">Receber / Fechar Conta</p>
                  <input type="number" step="0.01" value={payAmount} onChange={(e) => setPayAmount(e.target.value)} placeholder={`Restante R$ ${activeTab.remainingBalance.toFixed(2)}`} className="w-full rounded-lg border bg-white px-2 py-1.5" />
                  <button type="submit" className="w-full rounded-lg bg-emerald-600 py-1.5 font-bold text-white">Registrar Pagamento</button>
                </form>
              </div>
            </section>
          )}
        </div>

        {/* Coluna Lateral: Abrir Comanda, Setor, Mesa, Reserva */}
        <div className="space-y-5 text-xs">
          <form onSubmit={handleOpenTab} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-2.5">
            <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900"><Plus className="h-4 w-4" /> Abrir Nova Comanda</h3>
            <select value={openTabTableId} onChange={(e) => setOpenTabTableId(e.target.value)} className="w-full rounded-xl border px-3 py-2">
              <option value="">Balcão / Sem mesa</option>
              {tables.map((t) => <option key={t.id} value={t.id}>Mesa {t.tableCode}</option>)}
            </select>
            <input value={openTabCustomer} onChange={(e) => setOpenTabCustomer(e.target.value)} required placeholder="Nome do cliente / referência" className="w-full rounded-xl border px-3 py-2 text-sm" />
            <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2.5 text-sm font-bold text-white">Abrir Comanda</button>
          </form>

          <form onSubmit={handleReservation} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-2.5">
            <h3 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900"><CalendarClock className="h-4 w-4" /> Nova Reserva</h3>
            <select value={resTableId} onChange={(e) => setResTableId(e.target.value)} className="w-full rounded-xl border px-3 py-2">
              {tables.map((t) => <option key={t.id} value={t.id}>Mesa {t.tableCode} ({t.capacity} lug.)</option>)}
            </select>
            <input value={resName} onChange={(e) => setResName(e.target.value)} required placeholder="Nome do cliente" className="w-full rounded-xl border px-3 py-2" />
            <input value={resPhone} onChange={(e) => setResPhone(e.target.value)} required placeholder="Telefone" className="w-full rounded-xl border px-3 py-2" />
            <div className="grid grid-cols-2 gap-2">
              <input type="number" min="1" value={resSize} onChange={(e) => setResSize(e.target.value)} placeholder="Pessoas" className="rounded-xl border px-3 py-2" />
              <input type="datetime-local" value={resWhen} onChange={(e) => setResWhen(e.target.value)} className="rounded-xl border px-2 py-2" />
            </div>
            <button type="submit" className="w-full rounded-xl bg-slate-900 py-2 font-bold text-white">Confirmar Reserva</button>
          </form>

          <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
            <h3 className="text-sm font-extrabold text-slate-900">Cadastrar Setor & Mesa</h3>
            <form onSubmit={handleCreateSector} className="flex gap-2">
              <input value={newSectorName} onChange={(e) => setNewSectorName(e.target.value)} required placeholder="Novo setor (ex: Varanda)" className="flex-1 rounded-xl border px-3 py-1.5" />
              <button type="submit" className="rounded-xl bg-slate-800 px-3 py-1.5 font-bold text-white">+</button>
            </form>
            <form onSubmit={handleCreateTable} className="grid grid-cols-3 gap-2">
              <input value={newTableCode} onChange={(e) => setNewTableCode(e.target.value)} required placeholder="M03" className="rounded-xl border px-2.5 py-1.5" />
              <input type="number" min="1" value={newTableCap} onChange={(e) => setNewTableCap(e.target.value)} placeholder="Lug." className="rounded-xl border px-2.5 py-1.5" />
              <button type="submit" style={{ backgroundColor: "var(--store-accent)" }} className="rounded-xl font-bold text-white">+ Mesa</button>
            </form>
          </div>
        </div>
      </div>

      {/* Modal QR Code da Mesa */}
      {qrPreviewTable && (
        <div role="dialog" aria-modal="true" className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4 backdrop-blur-sm">
          <div className="w-full max-w-sm rounded-3xl bg-white p-6 text-center shadow-2xl">
            <h3 className="text-lg font-extrabold text-slate-900">QR Code — Mesa {qrPreviewTable.tableCode}</h3>
            <p className="mt-1 text-xs text-slate-500">O cliente escaneia para ver a conta, pedir itens ou chamar o garçom.</p>
            <div className="my-5 flex justify-center">
              <QrCodeMatrix value={`${originUrl}/mesa/${slug}/${qrPreviewTable.qrCodeToken}`} size={200} alt={`QR Mesa ${qrPreviewTable.tableCode}`} />
            </div>
            <div className="flex gap-2">
              <Link href={`/mesa/${slug}/${qrPreviewTable.qrCodeToken}`} className="flex-1 rounded-xl bg-indigo-700 py-2.5 text-xs font-bold text-white">
                Testar Autoatendimento
              </Link>
              <button type="button" onClick={() => setQrPreviewTable(null)} className="rounded-xl border px-4 py-2.5 text-xs font-bold">Fechar</button>
            </div>
          </div>
        </div>
      )}
    </StoreBrandShell>
  );
}
