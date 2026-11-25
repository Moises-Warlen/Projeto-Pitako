# Visão Geral da Arquitetura — PITAKO SaaS

## 1. Princípios Arquiteturais

O **PITAKO** adota uma arquitetura de **Monólito Modular** guiada por:

- **DDD (Domain-Driven Design):** cada domínio de negócio possui suas próprias entidades, objetos de valor, agregados, serviços de domínio e eventos.
- **Clean Architecture:** dependências apontam sempre para dentro (`Presentation/Host -> Application -> Domain`; `Infrastructure -> Application/Domain`). O núcleo não depende de fornecedores externos.
- **SOLID:** interfaces segregadas por módulo, inversão de dependência para gateways de pagamento, marketplaces, WhatsApp, balanças e impressoras.
- **APIs Versionadas:** prefixo explícito `/api/v1/...` com contratos documentados via OpenAPI.
- **Observabilidade e Erros Centralizados:** ProblemDetails (RFC 7807) centralizado, correlation ID e logs estruturados sem vazamento de dados sensíveis ou segredos.

## 2. Stack Tecnológica Definida (Validada com o Ambiente)

| Camada | Tecnologia | Estado no Ambiente Atual |
|---|---|---|
| **Backend** | .NET 8 LTS (C# 12, ASP.NET Core) | SDK `8.0.411` instalado e validado |
| **Tempo Real** | SignalR (WebSockets + fallback) | Integrado ao ASP.NET Core 8 |
| **Banco Relacional** | PostgreSQL 14+ | PostgreSQL `14.18` (`postgresql-x64-14`) rodando localmente |
| **Cache / Rate Limit / Sessões** | Redis (produção) + Abstração local em memória/PG (dev sem Redis) | **Pendente:** `redis-server` não instalado localmente |
| **Frontend Web** | Next.js + React + TypeScript + Tailwind CSS | Node `v22.17.1` e npm `10.9.2` instalados |
| **Agente Local (Hardware/Offline)** | .NET 8 Worker / Windows Service (`Pitako.HardwareAgent`) | A ser implementado nas Fases 10, 18 e 24 |
| **Containerização** | Docker / Docker Compose | **Pendente:** `docker` não instalado localmente |
| **Testes** | xUnit + Playwright | xUnit integrado ao .NET 8; Playwright a configurar para E2E |

## 3. Fluxo de Dependência Entre Camadas

```text
+-------------------------------------------------------------------+
| Hosts (Pitako.Api, Pitako.HardwareAgent, pitako-web)              |
+-------------------------------------------------------------------+
        |                                       |
        v                                       v
+---------------------------+     +---------------------------------+
| Módulos (Application/API) | --> | BuildingBlocks (MultiTenancy,   |
| + Infrastructure Adapters |     | Security, Messaging, SharedKernel)|
+---------------------------+     +---------------------------------+
        |
        v
+---------------------------+
| Módulos (Domain Core)     |  <-- Sem dependência de DB/HTTP/SDKs
+---------------------------+
```

## 4. Separação de Identidades Visuais (Regra Crítica)

- **Plataforma PITAKO:** tema padrão próprio (vermelho, branco, preto e tons escuros) com a logo oficial PITAKO preservada sem redesenho.
- **Estabelecimentos (Tenants):** tema totalmente independente (logo própria, cores principais/secundárias/destaque, banners, capa, slogan e layout de impressão).
- **Impressões e Comprovantes:** utilizam obrigatoriamente a **logo** e o **CNPJ** do estabelecimento cadastrado, nunca a marca PITAKO como emissora do estabelecimento.
