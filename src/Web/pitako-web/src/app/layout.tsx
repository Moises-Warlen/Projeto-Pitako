import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "PITAKO SaaS · Plataforma Multi-Tenant",
  description: "Gestão SaaS para food service, PDV, cardápio whitelabel, açaíteria, balança, marketplaces, salão e financeiro."
};

export default function RootLayout({
  children
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="pt-BR">
      <body className="antialiased">{children}</body>
    </html>
  );
}
