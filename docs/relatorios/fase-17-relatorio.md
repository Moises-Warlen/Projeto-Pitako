# Relatório de Validação — Fase 17: QR Code na Mesa e Totem de Autoatendimento

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.DiningHall.Domain`):**
  - Extensão de `DiningTab` com `DiningTabOrigin` (`Waiter`, `QrCodeTable`), abertura de autoatendimento `OpenSelfService` vinculada à mesa sem exigir garçom inicial e `RequestClosing` para solicitação de conta.
  - `QrTableSession` e `WaiterCall`: sessão vinculada à leitura de QR Code de mesa ativa com expiração por inatividade (`QrSession.Expired`), registro de chamados (`CallWaiter`, `RequestBill`, `NeedAssistance`, `RequestCutlery`) e ciclo de atendimento pela equipe (`Pending` -> `Acknowledged` -> `Completed`).
  - `TotemTerminal`, `TotemOrderSession` e `TotemOrderLine`: cadastro de terminais de autoatendimento (`CounterPickup` ou `DineIn`, exigência opcional de nome/pager, timeout configurável), preservação de linhas customizadas no carrinho, rejeição de cesta vazia (`Totem.EmptyBasket`), confirmação idempotente e emissão de senha de chamada (`PickupCallCode`).
- **Aplicação (`Pitako.Modules.DiningHall.Application`):**
  - `QrTableSelfServiceService` e `TotemSelfServiceService`: recálculo integral de preços no servidor via `ICatalogItemPricingService` e `IAcaiAssemblyPricingService`, despacho de pedidos via `ISelfServiceOrderDispatcher` (`QrCodeTable` e `SelfServiceTotem`), notificação em tempo real de chamados via `IWaiterCallRealtimeNotifier`, identidade visual exclusiva do estabelecimento via `ISelfServiceBrandingProvider` (`UsesPitakoBranding = false`) e controle RBAC estrito (Garçom atende chamados com `PermissionAction.Edit`; apenas Gerente/Administrador configuram terminais Totem com `PermissionAction.Approve`; perfil Cozinha é bloqueado).
- **Infraestrutura e PostgreSQL RLS (`Pitako.Modules.DiningHall.Infrastructure`):**
  - Extensão do `DiningHallDbContext` e repositórios (`EfQrTableSessionRepository`, `EfWaiterCallRepository`, `EfTotemTerminalRepository`, `EfTotemOrderSessionRepository`), Migration `20261125120004_AddSelfServiceQrAndTotem` e script SQL idempotente [016-dining-self-service.sql](docs/banco-de-dados/scripts/016-dining-self-service.sql) aplicando `ENABLE/FORCE ROW LEVEL SECURITY` às 5 novas tabelas: `qr_table_sessions`, `waiter_calls`, `totem_terminals`, `totem_order_sessions` e `totem_order_lines`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros** (42 projetos).
- `Pitako.UnitTests`: **72 aprovados, 0 falhas, 0 ignorados** (+2 novos testes em `SelfServiceQrAndTotemDomainTests` validando ciclo/expiração de sessão QR e `WaiterCall`, validação de nome obrigatório no Totem, expiração por inatividade, separação de customizações, bloqueio de cesta vazia e confirmação idempotente, além da inclusão de `QrTableSession`, `QrTableSelfServiceService` e `TotemSelfServiceService` em `ArchitectureTests`).
- `Pitako.IntegrationTests`: **21 aprovados, 0 falhas, 1 ignorado** (+1 fluxo completo em `SelfServiceQrAndTotemIntegrationTests` integrando `CatalogDbContext`, `AcaiBuilderDbContext` e `DiningHallDbContext` reais para QR Code na mesa + chamado de garçom/pedido de conta + Totem idempotente com senha `T-0001` e branding exclusivo da loja; `PostgresRlsLiveTests.PostgresRls_ShouldIsolateRowsAtDatabaseEngineLevel` segue ignorado por ausência de credenciais PostgreSQL configuradas).
- `Pitako.SecurityTests`: **17 aprovados, 0 falhas, 0 ignorados** (+1 cenário em `SelfServiceIsolationSecurityTests` comprovando isolamento multi-tenant via Global Query Filters em `qr_table_sessions`, `waiter_calls`, `totem_terminals`, `totem_order_sessions` e `totem_order_lines`, bloqueio cross-tenant em tokens QR/Totem e chamados, além de menor privilégio impedindo Garçom de cadastrar Totem e Cozinha de atender chamados).

## 3. Falhas encontradas e correções realizadas
- Durante a implementação inicial, os contratos de personalização de açaí foram ajustados para o formato exato `AcaiCupBuildRequest(CupSizeId, BaseFlavorIds, FreeToppings, PaidAddons)` do módulo `AcaiBuilder` antes da geração da migration, compilando em seguida sem avisos ou erros.

## 4. Pendências e limitações registradas
- Aplicação ao vivo do script `016-dining-self-service.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.
- Os canais físicos em tempo real (SignalR Hub para chamados de garçom e painel de senhas de Totem) conectarão às portas `IWaiterCallRealtimeNotifier` e `ISelfServiceOrderDispatcher` quando a camada Host/Web for implantada (Fase 26).

## 5. Critérios de aceitação e resultado
- [x] Sessão por QR Code na mesa, lançamentos com recálculo no servidor (Catálogo e Açaí), chamado de garçom e pedido de conta.
- [x] Totem de autoatendimento com timeout de inatividade, opção local/retirada, pagamento/idempotência e senha de chamada (`T-0001`).
- [x] Branding exclusivo da loja (`UsesPitakoBranding = false`), RBAC estrito, Global Query Filters e PostgreSQL RLS.

**Resultado:** Fase 17 concluída e validada. Aguardando aprovação para avançar para a **Fase 18 — Impressoras e setores de produção**.
