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
