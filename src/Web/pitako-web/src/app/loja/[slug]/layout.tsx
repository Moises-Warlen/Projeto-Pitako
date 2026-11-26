import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Cardápio Digital Online",
  description: "Faça seu pedido online diretamente pelo cardápio digital."
};

export default function PublicStorefrontLayout({ children }: { children: React.ReactNode }) {
  return children;
}
