"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { PitakoBrandShell } from "@/components/PitakoBrandShell";
import { apiFetch, loadSession } from "@/lib/api";

const MASTER_TOAST_KEY = "pitako_master_toast";
const MASTER_TAB_KEY = "pitako_master_active_tab";
const TOAST_DURATION_MS = 3000;

interface FloatingToast {
  id: number;
  message: string;
  kind: "success" | "error";
  durationMs: number;
}

interface PlanItem {
  id: string;
  code: string;
  name: string;
  price: number;
  isActive: boolean;
  unlimitedEstablishments: boolean;
  maxEstablishments: number | null;
  modules: string[];
}

interface CustomerRow {
  id: string;
  customerName: string;
  document: string;
  billingEmail: string;
  phone?: string;
  status: string;
  planId?: string;
  planName: string;
  subscriptionStatus: string;
}

interface EstablishmentRow {
  id: string;
  customerAccountId?: string;
  tradeName: string;
  legalName: string;
  slug: string;
  segmentCode: string;
  cnpj: string;
  cnpjDigits: string;
  status: string;
}

interface SegmentOption {
  code: string;
  displayName: string;
  supportsAcaiBuilder: boolean;
  supportsWeightScale: boolean;
  supportsTableService: boolean;
  supportsFractionalItems: boolean;
}

interface MasterOverviewResponse {
  customers: CustomerRow[];
  establishments: EstablishmentRow[];
  segments: SegmentOption[];
}

interface MasterIndicatorsDto {
  totalCustomers: number;
  activeSubscriptions: number;
  trialingSubscriptions: number;
  pastDueSubscriptions: number;
  suspendedSubscriptions: number;
  monthlyRecurringRevenueEstimate: number;
  paidRevenueTotal: number;
  overdueInvoicesCount: number;
  openSupportTicketsCount: number;
}

const ALL_MODULES = [
  { code: "tenants", label: "Identidade & Lojas" },
  { code: "catalog", label: "Cardápio & Produtos" },
  { code: "acai", label: "Montador de Açaí" },
  { code: "weighing", label: "Balança por Quilo" },
  { code: "inventory", label: "Estoque & Insumos" },
  { code: "coupons", label: "Cupons de Desconto" },
  { code: "orders", label: "Pedidos & Kanban" },
  { code: "pdv", label: "PDV & Caixa" },
  { code: "dining_hall", label: "Salão, Mesas, QR & Totem" },
  { code: "delivery", label: "Delivery & Entregadores" },
  { code: "marketplaces", label: "Marketplaces (iFood, Rappi...)" },
  { code: "printing", label: "Impressoras Térmicas" },
  { code: "whatsapp", label: "WhatsApp Bot & IA" },
  { code: "reports", label: "Relatórios PDF/Excel/CSV" },
  { code: "finance", label: "Financeiro & DRE" },
  { code: "security", label: "Usuários & Segurança" },
  { code: "data_lifecycle", label: "Backup, Migração & LGPD" }
];

export default function MasterPanelPage() {
  const router = useRouter();
  const [plans, setPlans] = useState<PlanItem[]>([]);
  const [overview, setOverview] = useState<MasterOverviewResponse>({
    customers: [],
    establishments: [],
    segments: []
  });
  const [indicators, setIndicators] = useState<MasterIndicatorsDto | null>(null);
  const [sweepingBilling, setSweepingBilling] = useState(false);
  const [submittingForm, setSubmittingForm] = useState(false);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [quickToast, setQuickToast] = useState<FloatingToast | null>(null);
  const [portalCreatedSlug, setPortalCreatedSlug] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"stores" | "segments" | "customers" | "plans" | "emails">("stores");

  // 5. E-mail & SMTP
  const [smtpHost, setSmtpHost] = useState("");
  const [smtpPort, setSmtpPort] = useState("587");
  const [smtpSsl, setSmtpSsl] = useState(true);
  const [smtpUser, setSmtpUser] = useState("");
  const [smtpPass, setSmtpPass] = useState("");
  const [smtpFromEmail, setSmtpFromEmail] = useState("noreply@pitako.com.br");
  const [smtpFromName, setSmtpFromName] = useState("Plataforma PITAKO");
  const [smtpWebBase, setSmtpWebBase] = useState("http://localhost:3000");
  const [emailOutbox, setEmailOutbox] = useState<Array<{ id: string; sentAtUtc: string; toEmail: string; subject: string; bodyText: string; kind: string; deliveryStatus: string }>>([]);

  function notifyAction(message: string, kind: "success" | "error" = "success") {
    setFeedback(message);
    setQuickToast({ id: Date.now(), message, kind, durationMs: TOAST_DURATION_MS });
    if (typeof window !== "undefined") {
      try {
        window.sessionStorage.setItem(
          MASTER_TOAST_KEY,
          JSON.stringify({ message, kind, expiresAt: Date.now() + TOAST_DURATION_MS })
        );
      } catch {
        // Ignora falha de storage
      }
    }
  }

  function changeActiveTab(next: "stores" | "segments" | "customers" | "plans" | "emails") {
    setActiveTab(next);
    if (typeof window !== "undefined") {
      try {
        window.sessionStorage.setItem(MASTER_TAB_KEY, next);
      } catch {
        // Ignora falha de storage
      }
    }
  }

  useEffect(() => {
    if (typeof window === "undefined") return;
    try {
      const savedTab = window.sessionStorage.getItem(MASTER_TAB_KEY);
      if (savedTab === "stores" || savedTab === "segments" || savedTab === "customers" || savedTab === "plans" || savedTab === "emails") {
        setActiveTab(savedTab);
      }
      const rawToast = window.sessionStorage.getItem(MASTER_TOAST_KEY);
      if (rawToast) {
        const parsed = JSON.parse(rawToast) as { message?: string; kind?: "success" | "error"; expiresAt?: number };
        const remaining = (parsed.expiresAt ?? 0) - Date.now();
        if (parsed.message && remaining > 50) {
          const kind = parsed.kind === "error" ? "error" : "success";
          setFeedback(parsed.message);
          setQuickToast({ id: Date.now(), message: parsed.message, kind, durationMs: Math.min(TOAST_DURATION_MS, remaining) });
        } else {
          window.sessionStorage.removeItem(MASTER_TOAST_KEY);
        }
      }
    } catch {
      // Ignora JSON inválido
    }
  }, []);

  useEffect(() => {
    if (!quickToast) return;
    const t = setTimeout(() => {
      setQuickToast(null);
      setFeedback(null);
      if (typeof window !== "undefined") {
        window.sessionStorage.removeItem(MASTER_TOAST_KEY);
      }
    }, quickToast.durationMs);
    return () => clearTimeout(t);
  }, [quickToast]);

  useEffect(() => {
    if (!feedback || quickToast) return;
    const t = setTimeout(() => setFeedback(null), TOAST_DURATION_MS);
    return () => clearTimeout(t);
  }, [feedback, quickToast]);

  function copyPortalLink(slug: string) {
    const url = typeof window !== "undefined" ? `${window.location.origin}/portal/${slug}` : `/portal/${slug}`;
    if (typeof navigator !== "undefined" && navigator.clipboard) {
      navigator.clipboard.writeText(url).then(() => {
        setFeedback(`Link do Portal copiado: ${url}`);
      });
    } else {
      setFeedback(`Link do Portal: ${url}`);
    }
  }

  // 1. Lojas (Criar / Editar)
  const [editingStoreId, setEditingStoreId] = useState<string | null>(null);
  const [storeCustomerId, setStoreCustomerId] = useState("");
  const [storeLegalName, setStoreLegalName] = useState("");
  const [storeTradeName, setStoreTradeName] = useState("");
  const [storeSlug, setStoreSlug] = useState("");
  const [storeCnpj, setStoreCnpj] = useState("");
  const [storeSegment, setStoreSegment] = useState("acaiteria");
  const [storeStatus, setStoreStatus] = useState("2");
  const [storeAdminName, setStoreAdminName] = useState("");
  const [storeAdminEmail, setStoreAdminEmail] = useState("");
  const [storeAdminPassword, setStoreAdminPassword] = useState("Loja@12345");

  // 2. Modalidades (Criar / Editar)
  const [editingSegCode, setEditingSegCode] = useState<string | null>(null);
  const [segCode, setSegCode] = useState("");
  const [segName, setSegName] = useState("");
  const [segAcai, setSegAcai] = useState(false);
  const [segWeight, setSegWeight] = useState(false);
  const [segTables, setSegTables] = useState(true);
  const [segFractional, setSegFractional] = useState(false);

  // 3. Clientes / Donos (Criar / Editar)
  const [editingCustomerId, setEditingCustomerId] = useState<string | null>(null);
  const [customerName, setCustomerName] = useState("");
  const [customerDoc, setCustomerDoc] = useState("");
  const [customerEmail, setCustomerEmail] = useState("");
  const [customerPhone, setCustomerPhone] = useState("11999998888");
  const [customerPlanId, setCustomerPlanId] = useState("");
  const [customerStatus, setCustomerStatus] = useState("1");

  // 4. Planos & Módulos (Criar / Editar)
  const [editingPlanId, setEditingPlanId] = useState<string | null>(null);
  const [planCode, setPlanCode] = useState("");
  const [planName, setPlanName] = useState("");
  const [planPrice, setPlanPrice] = useState("299.90");
  const [planActive, setPlanActive] = useState(true);
  const [isUnlimited, setIsUnlimited] = useState(true);
  const [maxStores, setMaxStores] = useState("1");
  const [selectedModules, setSelectedModules] = useState<string[]>(ALL_MODULES.map((m) => m.code));

  async function refreshAll() {
    const session = loadSession();
    if (!session || session.scope !== "Saas") {
      setFeedback("Entre como Administrador Master SaaS em /login para criar, editar e excluir lojas, modalidades, clientes e planos.");
      return;
    }

    const [plansRes, custRes, indRes, smtpRes, outboxRes] = await Promise.all([
      apiFetch<PlanItem[]>("/api/v1/master/plans", {}, session.accessToken),
      apiFetch<MasterOverviewResponse>("/api/v1/master/customers", {}, session.accessToken),
      apiFetch<MasterIndicatorsDto>("/api/v1/master/overview-indicators", {}, session.accessToken),
      apiFetch<{ host: string; port: number; useSsl: boolean; username: string; fromEmail: string; fromName: string; publicWebBaseUrl: string }>("/api/v1/master/email-settings", {}, session.accessToken),
      apiFetch<Array<{ id: string; sentAtUtc: string; toEmail: string; subject: string; bodyText: string; kind: string; deliveryStatus: string }>>("/api/v1/master/email-outbox", {}, session.accessToken)
    ]);

    if (indRes.ok && indRes.data) {
      setIndicators(indRes.data);
    }

    if (smtpRes.ok && smtpRes.data) {
      setSmtpHost(smtpRes.data.host || "");
      setSmtpPort(String(smtpRes.data.port || 587));
      setSmtpSsl(Boolean(smtpRes.data.useSsl));
      setSmtpUser(smtpRes.data.username || "");
      setSmtpFromEmail(smtpRes.data.fromEmail || "noreply@pitako.com.br");
      setSmtpFromName(smtpRes.data.fromName || "Plataforma PITAKO");
      setSmtpWebBase(smtpRes.data.publicWebBaseUrl || "http://localhost:3000");
    }
    if (outboxRes.ok && Array.isArray(outboxRes.data)) {
      setEmailOutbox(outboxRes.data);
    }

    if (plansRes.ok && plansRes.data) {
      const latestPlans = plansRes.data;
      setPlans(latestPlans);
      setCustomerPlanId((prev) =>
        latestPlans.some((p) => p.id === prev)
          ? prev
          : (latestPlans.find((p) => p.isActive)?.id ?? latestPlans[0]?.id ?? "")
      );
    }
    if (custRes.ok && custRes.data) {
      const latestOverview = custRes.data;
      setOverview(latestOverview);
      setStoreCustomerId((prev) =>
        latestOverview.customers.some((c) => c.id === prev)
          ? prev
          : (latestOverview.customers[0]?.id ?? "")
      );
      setStoreSegment((prev) =>
        latestOverview.segments.some((s) => s.code === prev)
          ? prev
          : (latestOverview.segments[0]?.code ?? "")
      );
    }
    if (!plansRes.ok) {
      setFeedback(plansRes.errorMessage || "Erro ao carregar Painel Master.");
    }
  }

  useEffect(() => {
    refreshAll();
  }, []);

  function toggleModule(code: string) {
    setSelectedModules((prev) => (prev.includes(code) ? prev.filter((c) => c !== code) : [...prev, code]));
  }

  // --- Ações Lojas ---
  function startEditStore(est: EstablishmentRow) {
    setEditingStoreId(est.id);
    setStoreTradeName(est.tradeName);
    setStoreLegalName(est.legalName);
    setStoreSlug(est.slug);
    setStoreCnpj(est.cnpjDigits);
    setStoreSegment(est.segmentCode);
    setStoreStatus(est.status === "Trial" ? "1" : est.status === "Suspended" ? "3" : "2");
    changeActiveTab("stores");
  }

  function resetStoreForm() {
    setEditingStoreId(null);
    setStoreLegalName("");
    setStoreTradeName("");
    setStoreSlug("");
    setStoreCnpj("");
    setStoreStatus("2");
    setStoreAdminName("");
    setStoreAdminEmail("");
  }

  async function withSubmitLock(task: () => Promise<void>) {
    if (submittingForm) return;
    setSubmittingForm(true);
    try {
      await task();
    } finally {
      setSubmittingForm(false);
    }
  }

  async function handleSubmitStore(e: React.FormEvent) {
    e.preventDefault();
    await withSubmitLock(async () => {
      const session = loadSession();
      if (!session) return;

    const cleanSlug = storeSlug.trim().toLowerCase().replace(/[^a-z0-9-]/g, "-");

    if (editingStoreId) {
      const res = await apiFetch(
        `/api/v1/master/establishments/${editingStoreId}`,
        {
          method: "PUT",
          body: JSON.stringify({
            legalName: storeLegalName,
            tradeName: storeTradeName,
            slug: cleanSlug,
            cnpj: storeCnpj,
            segmentCode: storeSegment,
            status: Number(storeStatus)
          })
        },
        session.accessToken
      );
      if (!res.ok) {
        notifyAction(res.errorMessage || "Erro ao atualizar loja.", "error");
        return;
      }

      if (storeAdminEmail.trim() && storeAdminPassword.trim()) {
        const adminRes = await apiFetch(
          `/api/v1/master/establishments/${editingStoreId}/admin-user`,
          {
            method: "POST",
            body: JSON.stringify({
              fullName: storeAdminName || `Admin ${storeTradeName}`,
              email: storeAdminEmail.trim(),
              password: storeAdminPassword,
              role: 1
            })
          },
          session.accessToken
        );
        if (!adminRes.ok) {
          await refreshAll();
          router.refresh();
          notifyAction(`Loja '${storeTradeName}' atualizada, mas erro ao criar usuário: ${adminRes.errorMessage}`, "error");
          return;
        }
        setPortalCreatedSlug(cleanSlug);
        resetStoreForm();
        await refreshAll();
        router.refresh();
        notifyAction(`Loja '${storeTradeName}' atualizada e usuário '${storeAdminEmail}' criado! Portal: /portal/${cleanSlug}`);
        return;
      } else {
        setPortalCreatedSlug(cleanSlug);
        resetStoreForm();
        await refreshAll();
        router.refresh();
        notifyAction(`Loja '${storeTradeName}' atualizada! Portal: /portal/${cleanSlug}`);
        return;
      }
    }

    const effectiveCustomerId = overview.customers.some((c) => c.id === storeCustomerId)
      ? storeCustomerId
      : (overview.customers[0]?.id ?? "");
    const effectiveSegment = overview.segments.some((s) => s.code === storeSegment)
      ? storeSegment
      : (overview.segments[0]?.code ?? "acaiteria");

    if (!effectiveCustomerId) {
      notifyAction("Cadastre um Cliente/Dono primeiro na aba 3. Clientes / Donos.", "error");
      return;
    }

    const estRes = await apiFetch<{ tenant: { id: string } }>(
      "/api/v1/store/establishments",
      {
        method: "POST",
        body: JSON.stringify({
          customerAccountId: effectiveCustomerId,
          legalName: storeLegalName,
          tradeName: storeTradeName,
          slug: cleanSlug,
          cnpj: storeCnpj,
          segmentCode: effectiveSegment
        })
      },
      session.accessToken
    );

    if (!estRes.ok || !estRes.data) {
      notifyAction(estRes.errorMessage || "Erro ao criar loja.", "error");
      return;
    }

    if (storeAdminEmail && storeAdminPassword) {
      const adminRes = await apiFetch(
        `/api/v1/master/establishments/${estRes.data.tenant.id}/admin-user`,
        {
          method: "POST",
          body: JSON.stringify({
            fullName: storeAdminName || `Admin ${storeTradeName}`,
            email: storeAdminEmail,
            password: storeAdminPassword,
            role: 1
          })
        },
        session.accessToken
      );
      if (!adminRes.ok) {
        setPortalCreatedSlug(cleanSlug);
        resetStoreForm();
        await refreshAll();
        router.refresh();
        notifyAction(
          `Loja '${cleanSlug}' criada, mas o login '${storeAdminEmail}' falhou: ${adminRes.errorMessage}. Clique em Editar na loja para criar o usuário.`,
          "error"
        );
        return;
      }
    }

    setPortalCreatedSlug(cleanSlug);
    resetStoreForm();
    await refreshAll();
    router.refresh();
    notifyAction(`Loja '${cleanSlug}' e login '${storeAdminEmail}' criados! Link do Portal do Cliente: /portal/${cleanSlug}`);
    });
  }

  async function handleDeleteStore(est: EstablishmentRow) {
    if (!window.confirm(`Tem certeza que deseja excluir a loja "${est.tradeName}" (${est.slug})?`)) return;
    const session = loadSession();
    if (!session) return;
    const res = await apiFetch(`/api/v1/master/establishments/${est.id}`, { method: "DELETE" }, session.accessToken);
    if (res.ok) {
      if (editingStoreId === est.id) resetStoreForm();
      await refreshAll();
      router.refresh();
      notifyAction(`Loja '${est.tradeName}' excluída com sucesso!`);
    } else {
      notifyAction(res.errorMessage || "Erro ao excluir loja.", "error");
    }
  }

  // --- Ações Modalidades ---
  function startEditSegment(seg: SegmentOption) {
    setEditingSegCode(seg.code);
    setSegCode(seg.code);
    setSegName(seg.displayName);
    setSegAcai(seg.supportsAcaiBuilder);
    setSegWeight(seg.supportsWeightScale);
    setSegTables(seg.supportsTableService);
    setSegFractional(seg.supportsFractionalItems);
    changeActiveTab("segments");
  }

  function resetSegmentForm() {
    setEditingSegCode(null);
    setSegCode("");
    setSegName("");
    setSegAcai(false);
    setSegWeight(false);
    setSegTables(true);
    setSegFractional(false);
  }

  async function handleSubmitSegment(e: React.FormEvent) {
    e.preventDefault();
    await withSubmitLock(async () => {
    const session = loadSession();
    if (!session) return;

    const cleanCode = (editingSegCode || segCode).trim().toLowerCase().replace(/[^a-z0-9_-]/g, "_");
    const method = editingSegCode ? "PUT" : "POST";
    const url = editingSegCode ? `/api/v1/master/segments/${encodeURIComponent(cleanCode)}` : "/api/v1/master/segments";

    const res = await apiFetch(
      url,
      {
        method,
        body: JSON.stringify({
          code: cleanCode,
          displayName: segName,
          supportsAcaiBuilder: segAcai,
          supportsWeightScale: segWeight,
          supportsTableService: segTables,
          supportsFractionalItems: segFractional
        })
      },
      session.accessToken
    );

    if (res.ok) {
      const savedName = segName;
      const wasEdit = Boolean(editingSegCode);
      resetSegmentForm();
      await refreshAll();
      router.refresh();
      notifyAction(wasEdit ? `Modalidade '${savedName}' atualizada!` : `Modalidade '${savedName}' criada!`);
    } else {
      notifyAction(res.errorMessage || "Erro ao salvar modalidade.", "error");
    }
    });
  }

  async function handleDeleteSegment(seg: SegmentOption) {
    if (!window.confirm(`Excluir a modalidade "${seg.displayName}"?`)) return;
    const session = loadSession();
    if (!session) return;
    const res = await apiFetch(`/api/v1/master/segments/${encodeURIComponent(seg.code)}`, { method: "DELETE" }, session.accessToken);
    if (res.ok) {
      if (editingSegCode === seg.code) resetSegmentForm();
      await refreshAll();
      router.refresh();
      notifyAction(`Modalidade '${seg.displayName}' removida!`);
    } else {
      notifyAction(res.errorMessage || "Erro ao excluir modalidade.", "error");
    }
  }

  // --- Ações Clientes / Donos ---
  function startEditCustomer(c: CustomerRow) {
    setEditingCustomerId(c.id);
    setCustomerName(c.customerName);
    setCustomerDoc(c.document);
    setCustomerEmail(c.billingEmail);
    setCustomerPhone(c.phone || "");
    if (c.planId) setCustomerPlanId(c.planId);
    setCustomerStatus(c.status === "Suspended" ? "2" : c.status === "Cancelled" ? "3" : "1");
    changeActiveTab("customers");
  }

  function resetCustomerForm() {
    setEditingCustomerId(null);
    setCustomerName("");
    setCustomerDoc("");
    setCustomerEmail("");
    setCustomerPhone("");
    setCustomerStatus("1");
  }

  async function handleSubmitCustomer(e: React.FormEvent) {
    e.preventDefault();
    await withSubmitLock(async () => {
    const session = loadSession();
    if (!session) return;

    const effectivePlanId = plans.some((p) => p.id === customerPlanId)
      ? customerPlanId
      : (plans.find((p) => p.isActive)?.id ?? plans[0]?.id ?? "");

    if (editingCustomerId) {
      const res = await apiFetch(
        `/api/v1/master/customers/${editingCustomerId}`,
        {
          method: "PUT",
          body: JSON.stringify({
            customerName,
            documentNumber: customerDoc,
            billingEmail: customerEmail,
            phone: customerPhone,
            status: Number(customerStatus),
            planId: effectivePlanId || null
          })
        },
        session.accessToken
      );
      if (res.ok) {
        const savedName = customerName;
        resetCustomerForm();
        await refreshAll();
        router.refresh();
        notifyAction(`Cliente / Dono '${savedName}' atualizado!`);
      } else {
        notifyAction(res.errorMessage || "Erro ao editar cliente.", "error");
      }
      return;
    }

    if (!effectivePlanId) {
      notifyAction("Crie um plano ativo na aba 4. Planos & Módulos antes de cadastrar o cliente.", "error");
      return;
    }

    const res = await apiFetch<{ customerId: string }>(
      "/api/v1/master/customers-subscriptions",
      {
        method: "POST",
        body: JSON.stringify({
          customerName,
          documentNumber: customerDoc,
          billingEmail: customerEmail,
          phone: customerPhone,
          planId: effectivePlanId,
          startWithTrial: false
        })
      },
      session.accessToken
    );

    if (res.ok && res.data) {
      const savedName = customerName;
      setStoreCustomerId(res.data.customerId);
      resetCustomerForm();
      await refreshAll();
      router.refresh();
      notifyAction(`Cliente / Dono '${savedName}' cadastrado! E-mail de boas-vindas enviado.`);
    } else {
      notifyAction(res.errorMessage || "Erro ao cadastrar cliente.", "error");
    }
    });
  }

  async function handleDeleteCustomer(c: CustomerRow) {
    if (!window.confirm(`Excluir o cliente "${c.customerName}"?`)) return;
    const session = loadSession();
    if (!session) return;
    const res = await apiFetch(`/api/v1/master/customers/${c.id}`, { method: "DELETE" }, session.accessToken);
    if (res.ok) {
      if (editingCustomerId === c.id) resetCustomerForm();
      await refreshAll();
      router.refresh();
      notifyAction(`Cliente / Dono '${c.customerName}' excluído!`);
    } else {
      notifyAction(res.errorMessage || "Erro ao excluir cliente.", "error");
    }
  }

  // --- Ações Planos & Módulos ---
  function startEditPlan(p: PlanItem) {
    setEditingPlanId(p.id);
    setPlanCode(p.code);
    setPlanName(p.name);
    setPlanPrice(String(p.price));
    setPlanActive(p.isActive);
    setIsUnlimited(p.unlimitedEstablishments);
    setMaxStores(String(p.maxEstablishments ?? 1));
    setSelectedModules(p.modules);
    changeActiveTab("plans");
  }

  function resetPlanForm() {
    setEditingPlanId(null);
    setPlanCode("");
    setPlanName("");
    setPlanPrice("299.90");
    setPlanActive(true);
    setIsUnlimited(true);
    setMaxStores("1");
    setSelectedModules(ALL_MODULES.map((m) => m.code));
  }

  async function handleSubmitPlan(e: React.FormEvent) {
    e.preventDefault();
    await withSubmitLock(async () => {
    const session = loadSession();
    if (!session) return;

    const payload = {
      code: planCode,
      name: planName,
      priceAmount: Number(planPrice),
      isUnlimited,
      maxEstablishments: isUnlimited ? null : Number(maxStores),
      enabledModules: selectedModules,
      isActive: planActive
    };

    const res = await apiFetch(
      editingPlanId ? `/api/v1/master/plans/${editingPlanId}` : "/api/v1/master/plans",
      { method: editingPlanId ? "PUT" : "POST", body: JSON.stringify(payload) },
      session.accessToken
    );

    if (res.ok) {
      const savedName = planName;
      const wasEdit = Boolean(editingPlanId);
      resetPlanForm();
      await refreshAll();
      router.refresh();
      notifyAction(wasEdit ? `Plano '${savedName}' atualizado!` : `Plano '${savedName}' criado!`);
    } else {
      notifyAction(res.errorMessage || "Erro ao salvar plano.", "error");
    }
    });
  }

  async function handleDeletePlan(p: PlanItem) {
    if (!window.confirm(`Excluir o plano "${p.name}"?`)) return;
    const session = loadSession();
    if (!session) return;
    const res = await apiFetch(`/api/v1/master/plans/${p.id}`, { method: "DELETE" }, session.accessToken);
    if (res.ok) {
      if (editingPlanId === p.id) resetPlanForm();
      await refreshAll();
      router.refresh();
      notifyAction(`Plano '${p.name}' excluído!`);
    } else {
      notifyAction(res.errorMessage || "Erro ao excluir plano.", "error");
    }
  }

  async function handleRunDelinquencySweep() {
    const session = loadSession();
    if (!session || session.scope !== "Saas") return;
    setSweepingBilling(true);
    const res = await apiFetch<{ newlyOverdueInvoices: number; suspendedSubscriptions: number; suspendedStores: number }>(
      "/api/v1/master/billing/sweep-delinquency?gracePeriodDays=3",
      { method: "POST" },
      session.accessToken
    );
    setSweepingBilling(false);
    if (res.ok && res.data) {
      await refreshAll();
      router.refresh();
      notifyAction(
        `Varredura concluída: ${res.data.newlyOverdueInvoices} fatura(s) vencida(s), ${res.data.suspendedSubscriptions} assinatura(s) e ${res.data.suspendedStores} loja(s) bloqueada(s).`
      );
    } else {
      notifyAction(res.errorMessage || "Erro ao verificar inadimplência.", "error");
    }
  }

  return (
    <PitakoBrandShell
      title="Painel Master PITAKO — Lojas, Modalidades, Clientes e Planos"
      subtitle="Gerencie criação, edição e exclusão completa de Lojas, Modalidades, Clientes/Donos e Planos/Módulos."
    >
      {indicators && (
        <div className="mb-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
          <div className="rounded-xl border border-zinc-800 bg-zinc-900 p-4">
            <p className="text-[11px] font-semibold uppercase text-zinc-400">Lojas / Clientes</p>
            <p className="mt-1 text-xl font-extrabold text-white">
              {overview.establishments.length} lojas · {indicators.totalCustomers} clientes
            </p>
          </div>
          <div className="rounded-xl border border-zinc-800 bg-zinc-900 p-4">
            <p className="text-[11px] font-semibold uppercase text-zinc-400">Assinaturas Ativas / Trial</p>
            <p className="mt-1 text-xl font-extrabold text-emerald-400">
              {indicators.activeSubscriptions} ativas · {indicators.trialingSubscriptions} trial
            </p>
          </div>
          <div className="rounded-xl border border-zinc-800 bg-zinc-900 p-4">
            <p className="text-[11px] font-semibold uppercase text-zinc-400">MRR Estimado</p>
            <p className="mt-1 font-mono text-xl font-extrabold text-white">
              R$ {indicators.monthlyRecurringRevenueEstimate.toFixed(2)}
            </p>
          </div>
          <div className="rounded-xl border border-zinc-800 bg-zinc-900 p-4">
            <p className="text-[11px] font-semibold uppercase text-zinc-400">Inadimplência / Suspensas</p>
            <p className="mt-1 text-xl font-extrabold text-rose-400">
              {indicators.pastDueSubscriptions} atraso · {indicators.suspendedSubscriptions} susp.
            </p>
          </div>
          <div className="flex items-center justify-between rounded-xl border border-zinc-800 bg-zinc-900 p-4">
            <button
              type="button"
              disabled={sweepingBilling}
              onClick={() => void handleRunDelinquencySweep()}
              className="w-full rounded-lg bg-amber-500 px-3 py-2 text-xs font-extrabold text-zinc-950 hover:bg-amber-400 disabled:opacity-50"
            >
              {sweepingBilling ? "Verificando..." : "Bloquear Inadimplentes / Trial"}
            </button>
          </div>
        </div>
      )}
      {feedback && (
        <div
          className={`mb-6 flex flex-wrap items-center justify-between gap-3 rounded-xl border p-4 text-sm font-medium ${
            quickToast?.kind === "error"
              ? "border-rose-500/60 bg-rose-950/60 text-rose-100"
              : "border-emerald-500/60 bg-emerald-950/60 text-emerald-100"
          }`}
        >
          <span>{feedback}</span>
          {portalCreatedSlug && (
            <div className="flex flex-wrap gap-2">
              <Link
                href={`/portal/${portalCreatedSlug}`}
                className="rounded-lg bg-amber-500 px-3 py-1.5 text-xs font-semibold text-zinc-950 hover:bg-amber-400"
              >
                Abrir Portal (/portal/{portalCreatedSlug})
              </Link>
              <button
                type="button"
                onClick={() => copyPortalLink(portalCreatedSlug)}
                className="rounded-lg border border-amber-400/60 px-3 py-1.5 text-xs font-semibold text-amber-100 hover:bg-amber-900/60"
              >
                Copiar Link
              </button>
            </div>
          )}
        </div>
      )}

      <div className="mb-6 flex flex-wrap gap-2">
        {[
          { key: "stores", label: `1. Lojas (${overview.establishments.length})` },
          { key: "segments", label: `2. Modalidades (${overview.segments.length})` },
          { key: "customers", label: `3. Clientes / Donos (${overview.customers.length})` },
          { key: "plans", label: `4. Planos & Módulos (${plans.length})` },
          { key: "emails", label: `5. E-mail & Envios (${emailOutbox.length})` }
        ].map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => changeActiveTab(t.key as typeof activeTab)}
            className={`rounded-lg px-4 py-2 text-sm font-semibold ${
              activeTab === t.key ? "bg-red-600 text-white" : "bg-zinc-900 text-zinc-300 hover:bg-zinc-800"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {activeTab === "stores" && (
        <div className="grid gap-8 lg:grid-cols-3">
          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5 lg:col-span-2">
            <h2 className="mb-4 text-lg font-semibold text-white">Lojas Cadastradas</h2>
            <div className="space-y-3">
              {overview.establishments.map((est) => (
                <div key={est.id} className="rounded-xl border border-zinc-800 bg-zinc-950 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <p className="text-base font-bold text-white">{est.tradeName}</p>
                      <p className="text-xs text-zinc-400">
                        Modalidade: <strong className="uppercase text-red-400">{est.segmentCode}</strong> · Slug:{" "}
                        <code>{est.slug}</code> · CNPJ: {est.cnpj} · Status: <strong>{est.status}</strong>
                      </p>
                      <p className="mt-1 text-xs text-zinc-400">
                        Portal do Cliente (Login da Loja):{" "}
                        <Link href={`/portal/${est.slug}`} className="font-mono text-sky-400 hover:underline">
                          /portal/{est.slug}
                        </Link>
                      </p>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      <Link
                        href={`/portal/${est.slug}`}
                        className="rounded-lg bg-sky-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-sky-700"
                      >
                        Abrir Portal
                      </Link>
                      <button
                        type="button"
                        onClick={() => copyPortalLink(est.slug)}
                        className="rounded-lg border border-sky-500/50 px-3 py-1.5 text-xs font-semibold text-sky-300 hover:bg-sky-950"
                      >
                        Copiar Link Portal
                      </button>
                      <Link
                        href={`/loja/${est.slug}`}
                        className="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-emerald-700"
                      >
                        Abrir Cardápio
                      </Link>
                      <button
                        type="button"
                        onClick={() => startEditStore(est)}
                        className="rounded-lg border border-zinc-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-zinc-800"
                      >
                        Editar
                      </button>
                      <button
                        type="button"
                        onClick={() => handleDeleteStore(est)}
                        className="rounded-lg bg-rose-700 px-3 py-1.5 text-xs font-semibold text-white hover:bg-rose-800"
                      >
                        Excluir
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-white">
                {editingStoreId ? "Editar Loja" : "Criar Nova Loja"}
              </h2>
              {editingStoreId && (
                <button type="button" onClick={resetStoreForm} className="text-xs text-zinc-400 underline">
                  Cancelar edição
                </button>
              )}
            </div>
            <form onSubmit={handleSubmitStore} className="space-y-3 text-sm">
              {!editingStoreId && (
                <div>
                  <label className="mb-1 block text-xs text-zinc-400">Cliente Dono da Loja</label>
                  <select
                    value={storeCustomerId}
                    onChange={(e) => setStoreCustomerId(e.target.value)}
                    required
                    className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
                  >
                    {overview.customers.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.customerName} ({c.planName})
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <div>
                <label className="mb-1 block text-xs text-zinc-400">Modalidade / Segmento</label>
                <select
                  value={storeSegment}
                  onChange={(e) => setStoreSegment(e.target.value)}
                  className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white"
                >
                  {overview.segments.map((seg) => (
                    <option key={seg.code} value={seg.code}>
                      {seg.displayName}
                    </option>
                  ))}
                </select>
              </div>
              <input value={storeTradeName} onChange={(e) => setStoreTradeName(e.target.value)} required placeholder="Nome Fantasia" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input value={storeLegalName} onChange={(e) => setStoreLegalName(e.target.value)} required placeholder="Razão Social" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input value={storeSlug} onChange={(e) => setStoreSlug(e.target.value)} required placeholder="Slug da URL (ex: bela-massa)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input value={storeCnpj} onChange={(e) => setStoreCnpj(e.target.value)} required placeholder="CNPJ (14 dígitos)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />

              {editingStoreId && (
                <select value={storeStatus} onChange={(e) => setStoreStatus(e.target.value)} className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white">
                  <option value="2">Ativa</option>
                  <option value="1">Trial</option>
                  <option value="3">Suspensa</option>
                </select>
              )}

              <div className="border-t border-zinc-800 pt-3">
                <p className="mb-2 text-xs font-semibold text-zinc-300">
                  {editingStoreId
                    ? "Adicionar Novo Login do Dono/Admin (opcional)"
                    : "Login Inicial do Dono da Loja (usará no Portal da Loja)"}
                </p>
                <input value={storeAdminName} onChange={(e) => setStoreAdminName(e.target.value)} placeholder="Nome do Administrador" className="mb-2 w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
                <input type="email" value={storeAdminEmail} onChange={(e) => setStoreAdminEmail(e.target.value)} required={!editingStoreId} placeholder="E-mail de acesso" className="mb-2 w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
                <input type="text" value={storeAdminPassword} onChange={(e) => setStoreAdminPassword(e.target.value)} required={!editingStoreId} placeholder="Senha forte (ex: Loja@12345)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              </div>

              <button type="submit" disabled={submittingForm} className="w-full rounded-lg bg-red-600 py-2.5 font-semibold text-white hover:bg-red-700 disabled:opacity-50">
                {submittingForm ? "Processando..." : editingStoreId ? "Salvar Alterações da Loja" : "Criar Loja e Liberar Acesso"}
              </button>
            </form>
          </section>
        </div>
      )}

      {activeTab === "segments" && (
        <div className="grid gap-8 lg:grid-cols-3">
          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5 lg:col-span-2">
            <h2 className="mb-4 text-lg font-semibold text-white">Modalidades / Segmentos Disponíveis</h2>
            <div className="space-y-3">
              {overview.segments.map((seg) => (
                <div key={seg.code} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-zinc-800 bg-zinc-950 p-4">
                  <div>
                    <p className="font-bold text-white">{seg.displayName} <span className="text-xs font-normal text-zinc-400">({seg.code})</span></p>
                    <p className="text-xs text-zinc-400">
                      {seg.supportsAcaiBuilder ? "Açaí · " : ""}
                      {seg.supportsWeightScale ? "Balança · " : ""}
                      {seg.supportsTableService ? "Salão/Mesas · " : ""}
                      {seg.supportsFractionalItems ? "Meia-a-Meia" : ""}
                    </p>
                  </div>
                  <div className="flex gap-2">
                    <button type="button" onClick={() => startEditSegment(seg)} className="rounded-lg border border-zinc-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-zinc-800">Editar</button>
                    <button type="button" onClick={() => handleDeleteSegment(seg)} className="rounded-lg bg-rose-700 px-3 py-1.5 text-xs font-semibold text-white hover:bg-rose-800">Excluir</button>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-white">{editingSegCode ? "Editar Modalidade" : "Nova Modalidade"}</h2>
              {editingSegCode && <button type="button" onClick={resetSegmentForm} className="text-xs text-zinc-400 underline">Cancelar</button>}
            </div>
            <form onSubmit={handleSubmitSegment} className="space-y-3 text-sm">
              <input value={segCode} onChange={(e) => setSegCode(e.target.value)} disabled={!!editingSegCode} required placeholder="Código (ex: sushi)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white disabled:opacity-50" />
              <input value={segName} onChange={(e) => setSegName(e.target.value)} required placeholder="Nome (ex: Sushi Bar)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <label className="flex items-center gap-2 text-xs text-zinc-200"><input type="checkbox" checked={segAcai} onChange={(e) => setSegAcai(e.target.checked)} /> Usa Montador de Açaí</label>
              <label className="flex items-center gap-2 text-xs text-zinc-200"><input type="checkbox" checked={segWeight} onChange={(e) => setSegWeight(e.target.checked)} /> Usa Balança por Quilo</label>
              <label className="flex items-center gap-2 text-xs text-zinc-200"><input type="checkbox" checked={segTables} onChange={(e) => setSegTables(e.target.checked)} /> Usa Salão / Mesas</label>
              <label className="flex items-center gap-2 text-xs text-zinc-200"><input type="checkbox" checked={segFractional} onChange={(e) => setSegFractional(e.target.checked)} /> Permite Fracionado (Meia-a-Meia)</label>
              <button type="submit" disabled={submittingForm} className="w-full rounded-lg bg-red-600 py-2 font-semibold text-white hover:bg-red-700 disabled:opacity-50">
                {submittingForm ? "Processando..." : editingSegCode ? "Salvar Modalidade" : "Criar Modalidade"}
              </button>
            </form>
          </section>
        </div>
      )}

      {activeTab === "customers" && (
        <div className="grid gap-8 lg:grid-cols-3">
          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5 lg:col-span-2">
            <h2 className="mb-4 text-lg font-semibold text-white">Clientes / Donos Contratantes</h2>
            <div className="space-y-3">
              {overview.customers.map((c) => (
                <div key={c.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-zinc-800 bg-zinc-950 p-4 text-sm">
                  <div>
                    <p className="font-bold text-white">{c.customerName} <span className="text-xs font-normal text-zinc-400">({c.status})</span></p>
                    <p className="text-xs text-zinc-400">
                      Doc: {c.document} · E-mail: {c.billingEmail} · Plano: <strong className="text-red-400">{c.planName}</strong>
                    </p>
                  </div>
                  <div className="flex gap-2">
                    <button type="button" onClick={() => startEditCustomer(c)} className="rounded-lg border border-zinc-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-zinc-800">Editar</button>
                    <button type="button" onClick={() => handleDeleteCustomer(c)} className="rounded-lg bg-rose-700 px-3 py-1.5 text-xs font-semibold text-white hover:bg-rose-800">Excluir</button>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-white">{editingCustomerId ? "Editar Cliente / Dono" : "Novo Cliente + Assinatura"}</h2>
              {editingCustomerId && <button type="button" onClick={resetCustomerForm} className="text-xs text-zinc-400 underline">Cancelar</button>}
            </div>
            <form onSubmit={handleSubmitCustomer} className="space-y-3 text-sm">
              <input value={customerName} onChange={(e) => setCustomerName(e.target.value)} required placeholder="Nome / Empresa Cliente" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input value={customerDoc} onChange={(e) => setCustomerDoc(e.target.value)} required placeholder="CPF ou CNPJ" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input type="email" value={customerEmail} onChange={(e) => setCustomerEmail(e.target.value)} required placeholder="E-mail" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input value={customerPhone} onChange={(e) => setCustomerPhone(e.target.value)} placeholder="Telefone" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              {editingCustomerId && (
                <select value={customerStatus} onChange={(e) => setCustomerStatus(e.target.value)} className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white">
                  <option value="1">Ativo</option>
                  <option value="2">Suspenso</option>
                  <option value="3">Cancelado</option>
                </select>
              )}
              <div>
                <label className="mb-1 block text-xs text-zinc-400">Plano Contratado</label>
                <select value={customerPlanId} onChange={(e) => setCustomerPlanId(e.target.value)} required className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white">
                  {plans.map((p) => (
                    <option key={p.id} value={p.id}>{p.name} (R$ {p.price.toFixed(2)})</option>
                  ))}
                </select>
              </div>
              <button type="submit" disabled={submittingForm} className="w-full rounded-lg bg-red-600 py-2.5 font-semibold text-white hover:bg-red-700 disabled:opacity-50">
                {submittingForm ? "Processando..." : editingCustomerId ? "Salvar Cliente" : "Cadastrar Cliente"}
              </button>
            </form>
          </section>
        </div>
      )}

      {activeTab === "plans" && (
        <div className="grid gap-8 lg:grid-cols-3">
          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5 lg:col-span-2">
            <h2 className="mb-4 text-lg font-semibold text-white">Planos & Módulos</h2>
            <div className="space-y-3">
              {plans.map((p) => (
                <div key={p.id} className="rounded-lg border border-zinc-800 bg-zinc-950 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <span className="text-base font-bold text-white">{p.name}</span>{" "}
                      <span className="text-xs text-zinc-400">({p.code}) · {p.isActive ? "Ativo" : "Inativo"}</span>
                    </div>
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="rounded-full bg-red-600/20 px-3 py-1 text-xs font-semibold text-red-400">R$ {p.price.toFixed(2)}</span>
                      <button type="button" onClick={() => startEditPlan(p)} className="rounded-lg border border-zinc-600 px-3 py-1 text-xs font-semibold text-white hover:bg-zinc-800">Editar</button>
                      <button type="button" onClick={() => handleDeletePlan(p)} className="rounded-lg bg-rose-700 px-3 py-1 text-xs font-semibold text-white hover:bg-rose-800">Excluir</button>
                    </div>
                  </div>
                  <p className="mt-2 text-xs text-zinc-300">
                    Limite Lojas: <strong>{p.unlimitedEstablishments ? "Ilimitado" : `${p.maxEstablishments}`}</strong> · Módulos: {p.modules.join(", ")}
                  </p>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-white">{editingPlanId ? "Editar Plano" : "Criar Plano & Módulos"}</h2>
              {editingPlanId && <button type="button" onClick={resetPlanForm} className="text-xs text-zinc-400 underline">Cancelar</button>}
            </div>
            <form onSubmit={handleSubmitPlan} className="space-y-3 text-sm">
              <input value={planCode} onChange={(e) => setPlanCode(e.target.value)} disabled={!!editingPlanId} required placeholder="Código (ex: pizzaria-plus)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white disabled:opacity-50" />
              <input value={planName} onChange={(e) => setPlanName(e.target.value)} required placeholder="Nome comercial" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input type="number" step="0.01" value={planPrice} onChange={(e) => setPlanPrice(e.target.value)} required className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              {editingPlanId && (
                <label className="flex items-center gap-2 text-xs text-zinc-200"><input type="checkbox" checked={planActive} onChange={(e) => setPlanActive(e.target.checked)} /> Plano Ativo</label>
              )}
              <label className="flex items-center gap-2 text-xs text-zinc-200"><input type="checkbox" checked={isUnlimited} onChange={(e) => setIsUnlimited(e.target.checked)} /> Plano Ilimitado Real</label>
              {!isUnlimited && (
                <input type="number" min={1} value={maxStores} onChange={(e) => setMaxStores(e.target.value)} placeholder="Máximo de lojas" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              )}
              <div className="max-h-52 space-y-1.5 overflow-y-auto rounded-lg border border-zinc-800 bg-zinc-950 p-3">
                <p className="text-xs font-semibold text-zinc-300">Módulos Liberados neste Plano:</p>
                {ALL_MODULES.map((m) => (
                  <label key={m.code} className="flex items-center gap-2 text-xs text-zinc-300">
                    <input type="checkbox" checked={selectedModules.includes(m.code)} onChange={() => toggleModule(m.code)} />
                    {m.label}
                  </label>
                ))}
              </div>
              <button type="submit" disabled={submittingForm} className="w-full rounded-lg bg-red-600 py-2 font-semibold text-white hover:bg-red-700 disabled:opacity-50">
                {submittingForm ? "Processando..." : editingPlanId ? "Salvar Alterações do Plano" : "Salvar Plano"}
              </button>
            </form>
          </section>
        </div>
      )}
      {activeTab === "emails" && (
        <div className="grid gap-8 lg:grid-cols-3">
          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5 lg:col-span-2">
            <h2 className="mb-4 text-lg font-semibold text-white">Caixa de Saída (Boas-vindas, Acessos & Recuperação de Senha)</h2>
            <div className="space-y-3">
              {emailOutbox.length === 0 ? (
                <p className="text-xs text-zinc-400">Nenhum e-mail registrado ainda.</p>
              ) : (
                emailOutbox.map((mail) => (
                  <div key={mail.id} className="rounded-lg border border-zinc-800 bg-zinc-950 p-4 text-xs">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-bold text-white">{mail.subject}</span>
                      <span className="rounded-full bg-emerald-500/20 px-2.5 py-0.5 text-[10px] font-bold text-emerald-300">
                        {mail.deliveryStatus}
                      </span>
                    </div>
                    <p className="mt-1 text-zinc-400">
                      Para: <strong className="text-zinc-200">{mail.toEmail}</strong> · {new Date(mail.sentAtUtc).toLocaleString("pt-BR")}
                    </p>
                    <pre className="mt-2 whitespace-pre-wrap rounded bg-zinc-900 p-2.5 font-sans text-[11px] text-zinc-300">
                      {mail.bodyText}
                    </pre>
                  </div>
                ))
              )}
            </div>
          </section>

          <section className="rounded-xl border border-zinc-800 bg-zinc-900 p-5">
            <h2 className="mb-3 text-lg font-semibold text-white">Configurar Servidor SMTP Real</h2>
            <form
              onSubmit={async (e) => {
                e.preventDefault();
                await withSubmitLock(async () => {
                const session = loadSession();
                if (!session) return;
                const res = await apiFetch(
                  "/api/v1/master/email-settings",
                  {
                    method: "PUT",
                    body: JSON.stringify({
                      host: smtpHost,
                      port: Number(smtpPort) || 587,
                      useSsl: smtpSsl,
                      username: smtpUser,
                      password: smtpPass,
                      fromEmail: smtpFromEmail,
                      fromName: smtpFromName,
                      publicWebBaseUrl: smtpWebBase
                    })
                  },
                  session.accessToken
                );
                if (res.ok) {
                  await refreshAll();
                  router.refresh();
                  notifyAction("Configuração SMTP salva e atualizada!");
                } else {
                  notifyAction(res.errorMessage || "Erro ao salvar SMTP.", "error");
                }
                });
              }}
              className="space-y-3 text-xs"
            >
              <input value={smtpHost} onChange={(e) => setSmtpHost(e.target.value)} placeholder="SMTP Host (ex: smtp.gmail.com)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <div className="grid grid-cols-2 gap-2">
                <input value={smtpPort} onChange={(e) => setSmtpPort(e.target.value)} placeholder="Porta (587)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
                <label className="flex items-center gap-2 text-zinc-200"><input type="checkbox" checked={smtpSsl} onChange={(e) => setSmtpSsl(e.target.checked)} /> SSL/TLS</label>
              </div>
              <input value={smtpUser} onChange={(e) => setSmtpUser(e.target.value)} placeholder="Usuário SMTP" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input type="password" autoComplete="new-password" value={smtpPass} onChange={(e) => setSmtpPass(e.target.value)} placeholder="Senha / App Password (opcional manter)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input value={smtpFromEmail} onChange={(e) => setSmtpFromEmail(e.target.value)} placeholder="E-mail remetente" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input value={smtpFromName} onChange={(e) => setSmtpFromName(e.target.value)} placeholder="Nome remetente" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <input value={smtpWebBase} onChange={(e) => setSmtpWebBase(e.target.value)} placeholder="URL base do painel (ex: https://app.seudominio.com)" className="w-full rounded-lg border border-zinc-700 bg-zinc-950 px-3 py-2 text-white" />
              <button type="submit" disabled={submittingForm} className="w-full rounded-lg bg-red-600 py-2.5 font-semibold text-white hover:bg-red-700 disabled:opacity-50">
                {submittingForm ? "Processando..." : "Salvar SMTP"}
              </button>
            </form>
          </section>
        </div>
      )}

      {quickToast && (
        <div
          role="status"
          aria-live="polite"
          className={`fixed bottom-5 right-5 z-50 flex max-w-md items-center gap-3 rounded-2xl border px-5 py-3.5 text-sm font-bold shadow-2xl transition-all ${
            quickToast.kind === "error"
              ? "border-rose-500/70 bg-rose-950/95 text-rose-100"
              : "border-emerald-500/70 bg-emerald-950/95 text-emerald-100"
          }`}
        >
          <span>{quickToast.message}</span>
          <button
            type="button"
            onClick={() => {
              setQuickToast(null);
              if (typeof window !== "undefined") window.sessionStorage.removeItem(MASTER_TOAST_KEY);
            }}
            className="rounded px-1.5 py-0.5 text-xs opacity-75 hover:opacity-100"
            aria-label="Fechar notificação"
          >
            ✕
          </button>
        </div>
      )}
    </PitakoBrandShell>
  );
}
