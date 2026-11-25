# ADR-001: Monólito Modular em .NET 8 + Next.js + PostgreSQL

- **Status:** Aceito
- **Data:** 2026-11-24

## Contexto
O PITAKO SaaS exige mais de 20 domínios integrados (PDV, estoque, fichas técnicas, açaíteria, balança, marketplaces, salão, financeiro, offline, painel Master), com forte consistência transacional e entrega incremental validada por etapas.

## Decisão
Adotar **Monólito Modular** em **.NET 8 LTS (ASP.NET Core + SignalR)** no backend, **PostgreSQL 14+** como banco transacional principal, **Next.js (React + TypeScript)** no frontend web, e um **Agente Local .NET (`Pitako.HardwareAgent`)** para balanças Toledo, impressoras e operação offline local quando necessário. Para cache/sessões/rate limit distribuído, definir portas/abstrações compatíveis com **Redis** em produção e adaptador local quando Redis não estiver disponível em desenvolvimento.

## Consequências
- **Positivas:** Menor latência entre módulos transacionais (ex.: Pedido -> Estoque -> Caixa), deploys mais simples nas primeiras fases, boundaries rígidos garantidos por testes de arquitetura e capacidade de extrair módulos específicos no futuro sem reescrever o domínio.
- **Negativas / Trade-offs:** Exige disciplina rigorosa nos limites entre módulos para evitar acoplamento indevido; integrações locais com hardware exigem agente local nos terminais de loja.
