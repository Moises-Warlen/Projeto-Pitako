"use client";

import { Edit3, Pause, Play, Plus, Trash2, Users } from "lucide-react";
import { useState } from "react";
import { apiFetch } from "@/lib/api";

export interface AdminAcaiConfigDto {
  cupSizes: Array<{
    id: string;
    name: string;
    volumeMl: number;
    basePrice: { amount: number };
    freeToppingsLimit: number;
    minFreeToppings?: number;
    isActive: boolean;
  }>;
  baseFlavors: Array<{ id: string; name: string; extraPrice: { amount: number }; isActive: boolean }>;
  freeToppings: Array<{ id: string; name: string; maxPortionsPerCup: number; isActive: boolean }>;
  paidAddons: Array<{ id: string; name: string; additionalPrice: { amount: number }; maxQuantityPerCup: number; isActive: boolean }>;
}

export function AdminAcaiTab({
  token,
  acai,
  onNotify,
  onReload
}: {
  token: string;
  acai: AdminAcaiConfigDto;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [editCupId, setEditCupId] = useState<string | null>(null);
  const [cupName, setCupName] = useState("");
  const [cupMl, setCupMl] = useState("500");
  const [cupPrice, setCupPrice] = useState("22.00");
  const [cupFreeLimit, setCupFreeLimit] = useState("5");

  const [editFreeId, setEditFreeId] = useState<string | null>(null);
  const [freeName, setFreeName] = useState("");
  const [freeMax, setFreeMax] = useState("2");

  const [editPaidId, setEditPaidId] = useState<string | null>(null);
  const [paidName, setPaidName] = useState("");
  const [paidPrice, setPaidPrice] = useState("5.00");

  async function saveCup(e: React.FormEvent) {
    e.preventDefault();
    const body = { name: cupName, volumeMl: Number(cupMl), basePrice: Number(cupPrice), freeToppingsLimit: Number(cupFreeLimit), minFreeToppings: 0 };
    const res = editCupId
      ? await apiFetch(`/api/v1/acai/cup-sizes/${editCupId}`, { method: "PUT", body: JSON.stringify(body) }, token)
      : await apiFetch("/api/v1/acai/cup-sizes", { method: "POST", body: JSON.stringify(body) }, token);

    if (res.ok) {
      setEditCupId(null);
      setCupName("");
      onNotify(editCupId ? "Copo atualizado!" : "Copo criado!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao salvar copo.");
  }

  async function saveFree(e: React.FormEvent) {
    e.preventDefault();
    const body = { name: freeName, maxPortionsPerCup: Number(freeMax) || 1 };
    const res = editFreeId
      ? await apiFetch(`/api/v1/acai/free-toppings/${editFreeId}`, { method: "PUT", body: JSON.stringify(body) }, token)
      : await apiFetch("/api/v1/acai/free-toppings", { method: "POST", body: JSON.stringify(body) }, token);

    if (res.ok) {
      setEditFreeId(null);
      setFreeName("");
      onNotify("Acompanhamento gratuito salvo!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro.");
  }

  async function savePaid(e: React.FormEvent) {
    e.preventDefault();
    const body = { name: paidName, additionalPrice: Number(paidPrice), maxQuantityPerCup: 3 };
    const res = editPaidId
      ? await apiFetch(`/api/v1/acai/paid-addons/${editPaidId}`, { method: "PUT", body: JSON.stringify(body) }, token)
      : await apiFetch("/api/v1/acai/paid-addons", { method: "POST", body: JSON.stringify(body) }, token);

    if (res.ok) {
      setEditPaidId(null);
      setPaidName("");
      onNotify("Adicional pago salvo!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro.");
  }

  return (
    <div className="grid gap-6 lg:grid-cols-3 text-xs">
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
        <h3 className="text-sm font-extrabold text-slate-900">Copos / Tamanhos ({acai.cupSizes.length})</h3>
        <div className="space-y-1.5">
          {acai.cupSizes.map((c) => (
            <div key={c.id} className="flex items-center justify-between rounded-xl bg-slate-50 px-3 py-2">
              <div>
                <p className="font-bold text-slate-900">{c.name} ({c.volumeMl}ml)</p>
                <p className="text-slate-500">R$ {c.basePrice.amount.toFixed(2)} · até {c.freeToppingsLimit} grátis</p>
              </div>
              <div className="flex gap-1">
                <button
                  type="button"
                  onClick={() => {
                    setEditCupId(c.id);
                    setCupName(c.name);
                    setCupMl(String(c.volumeMl));
                    setCupPrice(String(c.basePrice.amount));
                    setCupFreeLimit(String(c.freeToppingsLimit));
                  }}
                  className="p-1 text-slate-600"
                >
                  <Edit3 className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  onClick={async () => {
                    await apiFetch(`/api/v1/acai/cup-sizes/${c.id}/${c.isActive ? "pause" : "reactivate"}`, { method: "POST" }, token);
                    await onReload();
                  }}
                  className="p-1 text-slate-600"
                >
                  {c.isActive ? <Pause className="h-3.5 w-3.5" /> : <Play className="h-3.5 w-3.5" />}
                </button>
                <button
                  type="button"
                  onClick={async () => {
                    await apiFetch(`/api/v1/acai/cup-sizes/${c.id}`, { method: "DELETE" }, token);
                    await onReload();
                  }}
                  className="p-1 text-rose-600"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          ))}
        </div>
        <form onSubmit={saveCup} className="space-y-2 border-t pt-3">
          <input value={cupName} onChange={(e) => setCupName(e.target.value)} required placeholder="Nome (ex: Copo 700ml)" className="w-full rounded-xl border px-3 py-2" />
          <div className="grid grid-cols-3 gap-2">
            <input type="number" value={cupMl} onChange={(e) => setCupMl(e.target.value)} placeholder="ml" className="rounded-lg border px-2 py-1.5" />
            <input type="number" step="0.01" value={cupPrice} onChange={(e) => setCupPrice(e.target.value)} placeholder="Preço" className="rounded-lg border px-2 py-1.5" />
            <input type="number" value={cupFreeLimit} onChange={(e) => setCupFreeLimit(e.target.value)} placeholder="Grátis" className="rounded-lg border px-2 py-1.5" />
          </div>
          <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2 font-bold text-white">
            {editCupId ? "Salvar Copo" : "Criar Copo"}
          </button>
        </form>
      </section>

      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
        <h3 className="text-sm font-extrabold text-slate-900">Acompanhamentos Grátis ({acai.freeToppings.length})</h3>
        <div className="space-y-1.5">
          {acai.freeToppings.map((t) => (
            <div key={t.id} className="flex items-center justify-between rounded-xl bg-slate-50 px-3 py-2">
              <span className="font-semibold text-slate-800">{t.name} (máx {t.maxPortionsPerCup})</span>
              <div className="flex gap-1">
                <button
                  type="button"
                  onClick={() => {
                    setEditFreeId(t.id);
                    setFreeName(t.name);
                    setFreeMax(String(t.maxPortionsPerCup));
                  }}
                  className="p-1 text-slate-600"
                >
                  <Edit3 className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  onClick={async () => {
                    await apiFetch(`/api/v1/acai/free-toppings/${t.id}/${t.isActive ? "pause" : "reactivate"}`, { method: "POST" }, token);
                    await onReload();
                  }}
                  className="p-1 text-slate-600"
                >
                  {t.isActive ? <Pause className="h-3.5 w-3.5" /> : <Play className="h-3.5 w-3.5" />}
                </button>
                <button
                  type="button"
                  onClick={async () => {
                    await apiFetch(`/api/v1/acai/free-toppings/${t.id}`, { method: "DELETE" }, token);
                    await onReload();
                  }}
                  className="p-1 text-rose-600"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          ))}
        </div>
        <form onSubmit={saveFree} className="space-y-2 border-t pt-3">
          <input value={freeName} onChange={(e) => setFreeName(e.target.value)} required placeholder="Ex: Paçoca / Leite em Pó" className="w-full rounded-xl border px-3 py-2" />
          <input type="number" value={freeMax} onChange={(e) => setFreeMax(e.target.value)} placeholder="Porções máx por copo" className="w-full rounded-xl border px-3 py-2" />
          <button type="submit" className="w-full rounded-xl bg-emerald-600 py-2 font-bold text-white">
            {editFreeId ? "Salvar Item" : "Adicionar Grátis"}
          </button>
        </form>
      </section>

      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
        <h3 className="text-sm font-extrabold text-slate-900">Adicionais Pagos ({acai.paidAddons.length})</h3>
        <div className="space-y-1.5">
          {acai.paidAddons.map((a) => (
            <div key={a.id} className="flex items-center justify-between rounded-xl bg-slate-50 px-3 py-2">
              <span className="font-semibold text-slate-800">{a.name} (+R$ {a.additionalPrice.amount.toFixed(2)})</span>
              <div className="flex gap-1">
                <button
                  type="button"
                  onClick={() => {
                    setEditPaidId(a.id);
                    setPaidName(a.name);
                    setPaidPrice(String(a.additionalPrice.amount));
                  }}
                  className="p-1 text-slate-600"
                >
                  <Edit3 className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  onClick={async () => {
                    await apiFetch(`/api/v1/acai/paid-addons/${a.id}`, { method: "DELETE" }, token);
                    await onReload();
                  }}
                  className="p-1 text-rose-600"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          ))}
        </div>
        <form onSubmit={savePaid} className="space-y-2 border-t pt-3">
          <input value={paidName} onChange={(e) => setPaidName(e.target.value)} required placeholder="Ex: Creme Ninho / Nutella" className="w-full rounded-xl border px-3 py-2" />
          <input type="number" step="0.01" value={paidPrice} onChange={(e) => setPaidPrice(e.target.value)} required className="w-full rounded-xl border px-3 py-2" />
          <button type="submit" className="w-full rounded-xl bg-amber-600 py-2 font-bold text-white">
            {editPaidId ? "Salvar Adicional" : "Adicionar Pago"}
          </button>
        </form>
      </section>
    </div>
  );
}

export function AdminTeamTab({
  token,
  team,
  onNotify,
  onReload
}: {
  token: string;
  team: Array<{ id: string; fullName: string; normalizedEmail: string; role: string }>;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
}) {
  const [staffName, setStaffName] = useState("");
  const [staffEmail, setStaffEmail] = useState("");
  const [staffPassword, setStaffPassword] = useState("Operador@2026");
  const [staffRole, setStaffRole] = useState("3");

  async function handleCreateStaff(e: React.FormEvent) {
    e.preventDefault();
    const res = await apiFetch(
      "/api/v1/store/users",
      { method: "POST", body: JSON.stringify({ fullName: staffName, email: staffEmail, password: staffPassword, role: Number(staffRole) }) },
      token
    );
    if (res.ok) {
      setStaffName("");
      setStaffEmail("");
      onNotify("Operador cadastrado com sucesso!");
      await onReload();
    } else onNotify(res.errorMessage || "Erro ao criar usuário.");
  }

  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-2">
        <h3 className="mb-3 flex items-center gap-2 text-base font-extrabold text-slate-900">
          <Users className="h-4 w-4" /> Equipe da Loja ({team.length})
        </h3>
        <div className="space-y-2 text-xs">
          {team.map((u) => (
            <div key={u.id} className="flex justify-between rounded-xl border border-slate-100 bg-slate-50 px-3.5 py-2.5">
              <span>
                <strong>{u.fullName}</strong> ({u.normalizedEmail})
              </span>
              <span className="rounded bg-slate-200 px-2 py-0.5 font-bold">{u.role}</span>
            </div>
          ))}
        </div>
      </section>

      <form onSubmit={handleCreateStaff} className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3 text-xs">
        <h4 className="flex items-center gap-1.5 text-sm font-extrabold text-slate-900">
          <Plus className="h-4 w-4" /> Novo Operador
        </h4>
        <input value={staffName} onChange={(e) => setStaffName(e.target.value)} required placeholder="Nome completo" className="w-full rounded-xl border px-3 py-2 text-sm" />
        <input type="email" value={staffEmail} onChange={(e) => setStaffEmail(e.target.value)} required placeholder="E-mail" className="w-full rounded-xl border px-3 py-2" />
        <input value={staffPassword} onChange={(e) => setStaffPassword(e.target.value)} required placeholder="Senha" className="w-full rounded-xl border px-3 py-2" />
        <select value={staffRole} onChange={(e) => setStaffRole(e.target.value)} className="w-full rounded-xl border px-3 py-2">
          <option value="2">Gerente</option>
          <option value="3">Caixa (PDV)</option>
          <option value="5">Garçom</option>
          <option value="6">Cozinha</option>
          <option value="7">Entregador</option>
          <option value="9">Financeiro</option>
        </select>
        <button type="submit" style={{ backgroundColor: "var(--store-primary)" }} className="w-full rounded-xl py-2.5 text-sm font-bold text-white">
          Cadastrar
        </button>
      </form>
    </div>
  );
}
