import Link from "next/link";
import { PitakoBrandShell } from "@/components/PitakoBrandShell";
import {
  ShieldCheck,
  Store,
  Monitor,
  Kanban,
  Utensils,
  Wallet,
  Smartphone,
  QrCode,
  Tablet
} from "lucide-react";

const PANELS = [
  {
    href: "/master",
    title: "Painel Master SaaS",
    desc: "Planos comerciais (incluindo Plano Ilimitado), assinaturas, inadimplência e indicadores globais.",
    badge: "Identidade PITAKO",
    icon: ShieldCheck
  },
  {
    href: "/admin",
    title: "Painel Administrativo da Loja",
    desc: "Branding próprio, catálogo, açaíteria, marketplaces multi-loja, WhatsApp, relatórios PDF/XLSX/CSV e LGPD.",
    badge: "Identidade da Loja",
    icon: Store
  },
  {
    href: "/pdv",
    title: "PDV, Caixa & Balança",
    desc: "Abertura/fechamento de caixa, múltiplos pagamentos, leitura Toledo / contingência e cupom com CNPJ da loja.",
    badge: "Operação Loja",
    icon: Monitor
  },
  {
    href: "/pedidos",
    title: "Central de Pedidos & Kanban",
    desc: "Recebimento em tempo real via SignalR e fluxo Recebido → Preparo → Pronto → Entrega.",
    badge: "Tempo Real",
    icon: Kanban
  },
  {
    href: "/salao",
    title: "Salão, Mesas & Comandas",
    desc: "Setores, mesas com QR token, reservas, transferência de mesas e divisão exata em centavos.",
    badge: "Salão",
    icon: Utensils
  },
  {
    href: "/financeiro",
    title: "Financeiro, DRE & Gateways PCI",
    desc: "Contas a pagar/receber, fluxo de caixa realizado/projetado, DRE gerencial e conciliação.",
    badge: "Financeiro",
    icon: Wallet
  },
  {
    href: "/loja/acai-demo",
    title: "Cardápio Digital & Açaíteria",
    desc: "Vitrine whitelabel com cores exclusivas da loja, montador de açaí e checkout de delivery.",
    badge: "Consumidor Final",
    icon: Smartphone
  },
  {
    href: "/mesa/acai-demo/qr-acai-demo-m01",
    title: "Autoatendimento QR Code Mesa",
    desc: "Sessão na mesa sem marca PITAKO, pedidos na cozinha e botão de chamar garçom/conta.",
    badge: "Mesa QR",
    icon: QrCode
  },
  {
    href: "/totem",
    title: "Totem de Autoatendimento",
    desc: "Modo touch full-screen com timeout de ociosidade automático e senha de retirada.",
    badge: "Totem",
    icon: Tablet
  }
];

export default function HomePage() {
  return (
    <PitakoBrandShell
      title="Portal Unificado — PITAKO SaaS"
      subtitle="Monólito Modular multi-tenant em .NET 8 + PostgreSQL RLS + Next.js. Identidade visual PITAKO restrita à plataforma; estabelecimentos clientes operam 100% com marca própria."
    >
      <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
        {PANELS.map((panel) => {
          const Icon = panel.icon;
          return (
            <Link
              key={panel.href}
              href={panel.href}
              className="group rounded-xl border border-zinc-800 bg-zinc-900/80 p-5 transition hover:border-red-500/70 hover:bg-zinc-900"
            >
              <div className="mb-3 flex items-center justify-between">
                <span className="rounded-lg bg-red-600/15 p-2.5 text-red-500">
                  <Icon className="h-5 w-5" />
                </span>
                <span className="rounded-full border border-zinc-700 px-2.5 py-0.5 text-xs text-zinc-300">
                  {panel.badge}
                </span>
              </div>
              <h2 className="text-lg font-semibold text-white group-hover:text-red-400">
                {panel.title}
              </h2>
              <p className="mt-1.5 text-sm leading-relaxed text-zinc-400">{panel.desc}</p>
            </Link>
          );
        })}
      </div>
    </PitakoBrandShell>
  );
}
