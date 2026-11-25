# PITAKO SaaS

Plataforma SaaS multi-tenant para gestão completa de estabelecimentos comerciais (PDV, delivery próprio, marketplaces, cardápio digital, açaíteria personalizada, balança por peso, fichas técnicas, estoque, salão/mesas, totem, QR Code, impressoras, WhatsApp, financeiro, relatórios e operação offline).

## Documentação

- [Plano e Escopo da Fase 1](docs/fase-01-analise-e-arquitetura.md)
- [Visão Geral da Arquitetura](docs/arquitetura/visao-geral.md)
- [Estrutura dos Módulos](docs/arquitetura/estrutura-modulos.md)
- [ADRs — Decisões Arquiteturais](docs/arquitetura/adrs/)
- [Glossário e Regras Core](docs/dominio/glossario-e-regras-core.md)
- [Estratégia PostgreSQL e Multi-Tenancy](docs/banco-de-dados/estrategia-postgresql-rls.md)
- [Instalação e Ambiente](docs/instalacao-e-ambiente.md)

## Status do Desenvolvimento

- **Fase 1 (Análise do projeto e arquitetura):** Concluída ([Relatório](docs/relatorios/fase-01-relatorio.md)).
- **Fase 2 (Domínio, banco e multi-tenancy):** Concluída com pendência de execução ao vivo no PostgreSQL local por falta de credenciais ([Relatório](docs/relatorios/fase-02-relatorio.md)).
- **Fase 3 (Autenticação, usuários e permissões):** Concluída com testes unitários, integração e segurança aprovados ([Relatório](docs/relatorios/fase-03-relatorio.md)).
- **Fase 4 (Painel Master, planos e assinaturas):** Concluída com testes unitários, integração e segurança aprovados ([Relatório](docs/relatorios/fase-04-relatorio.md)).
- **Fase 5 (Estabelecimentos, unidades e personalização):** Concluída com testes unitários, integração e segurança aprovados ([Relatório](docs/relatorios/fase-05-relatorio.md)).
- **Fase 6 (Produtos e categorias):** Concluída com testes unitários, integração e segurança aprovados ([Relatório](docs/relatorios/fase-06-relatorio.md)).
- **Fase 7 (Insumos, fichas técnicas e formação de preços):** Concluída com testes unitários, integração e segurança aprovados ([Relatório](docs/relatorios/fase-07-relatorio.md)).
- **Fase 8 (Estoque, compras e produção):** Concluída com testes unitários, integração e segurança aprovados ([Relatório](docs/relatorios/fase-08-relatorio.md)).
- **Fase 9 (Módulo especializado de açaí):** Concluída com testes unitários, integração e segurança aprovados ([Relatório](docs/relatorios/fase-09-relatorio.md)).
- **Fase 10 (Pesagem e balança):** Concluída com parser Toledo validado, contingência manual e pendência registrada de balança física ([Relatório](docs/relatorios/fase-10-relatorio.md)).
- **Fase 11 (Carrinho de compras e cupons):** Concluída com preservação por item, recálculo integral no backend e módulo de cupons com RLS ([Relatório](docs/relatorios/fase-11-relatorio.md)).
- **Fase 12 (Delivery próprio e frete):** Concluída com cálculo Haversine, faixas/zonas, raio máximo, pedido mínimo, isenção e RLS ([Relatório](docs/relatorios/fase-12-relatorio.md)).
- **Fase 13 (Pedidos e Kanban em tempo real):** Concluída com checkout multi-módulo, Kanban, estorno automático de estoque/cupons e RLS ([Relatório](docs/relatorios/fase-13-relatorio.md)).
- **Fase 14 (PDV e caixa):** Concluída com abertura/fechamento, sangria, suprimento, múltiplos pagamentos, aprovação de descontos e RLS ([Relatório](docs/relatorios/fase-14-relatorio.md)).
- **Fase 15 (Entregadores, logística e GPS):** Concluída com entregadores, agrupamento e otimização de rotas, telemetria GPS, relatório de corridas e RLS ([Relatório](docs/relatorios/fase-15-relatorio.md)).
- **Fase 16 (Salão, mesas, reservas, comandas e garçons):** Concluída com setores, mesas com QR token, reservas, comandas, transferência, divisão exata em centavos, RBAC e RLS ([Relatório](docs/relatorios/fase-16-relatorio.md)).
- **Fase 17 (QR Code na mesa e Totem de autoatendimento):** Concluída com sessões QR, chamados de garçom em tempo real, terminais Totem com timeout e senha idempotente, branding exclusivo da loja e RLS ([Relatório](docs/relatorios/fase-17-relatorio.md)).
- **Fase 18 (Impressoras e setores de produção):** Concluída com roteamento por setor/categoria, fallback principal/reserva, ESC/POS, obrigatoriedade de logo + CNPJ da loja e RLS ([Relatório](docs/relatorios/fase-18-relatorio.md)).
- **Fase 19 (Hub de marketplaces — estrutura e conectores):** Concluída com conectores e matriz de capacidades dos 7 parceiros, segredos AES-GCM, webhooks idempotentes com HMAC e RLS ([Relatório](docs/relatorios/fase-19-relatorio.md)).
- **Fase 20 (Marketplaces — múltiplas lojas, ações em massa e sincronização):** Concluída com ações em massa multi-loja, respeito às capacidades reais por parceiro, relatório parcial sem falso sucesso global, retry seletivo e RLS ([Relatório](docs/relatorios/fase-20-relatorio.md)).
- **Fase 21 (WhatsApp e atendimento com bot tradicional e IA):** Concluída com canal Cloud API cifrado em AES-GCM, bot tradicional, IA restrita ao snapshot do tenant com validação anti-alucinação, inbox humano e RLS ([Relatório](docs/relatorios/fase-21-relatorio.md)).
- **Fase 22 (Relatórios e exportações PDF, Excel e CSV):** Concluída com exportadores reais PDF 1.4, OpenXML `.xlsx` e CSV anti-injeção de fórmulas, auditoria SHA-256, identidade exclusiva da loja e RLS ([Relatório](docs/relatorios/fase-22-relatorio.md)).
- **Fase 23 (Migração de dados, backup e LGPD):** Concluída com importadores Consumer/Goomer/Saipos, backup por tenant cifrado em AES-GCM + SHA-256, fluxos de portabilidade/anonimização LGPD e RLS ([Relatório](docs/relatorios/fase-23-relatorio.md)).
