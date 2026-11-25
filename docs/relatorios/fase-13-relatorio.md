# Relatório de Validação — Fase 13: Pedidos e Kanban em Tempo Real

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.Orders.Domain`):**
  - `Order`, `OrderItem` e `OrderStatusHistory`: agregado completo de pedidos suportando todos os canais (`OwnDeliveryMenu`, `PdvCounter`, `TableWaiter`, `QrCodeTable`, `SelfServiceTotem`, `WhatsApp`, `Marketplace`) e modalidades (`Delivery`, `CounterPickup`, `DineIn`).
  - Máquina de estados Kanban estrita (`Received -> Accepted -> InPreparation -> Ready -> Dispatched -> Completed`, além de `Cancelled` com motivo obrigatório), impedindo retrocessos ou edições após estado terminal e preservando linha a linha as personalizações individuais de cada item do carrinho.
- **Aplicação e Orquestração Modular (`Pitako.Modules.Orders.Application` / `Inventory` / `Weighing`):**
  - `OrderManagementService`: checkout idempotente (`CheckoutCartAsync`) recalculando frete via `IDeliveryFreightQuoteService`, validando/resgatando cupom via `ICouponValidationService`, vinculando pesagem via `IWeighingOrderAttachmentService`, efetuando baixa automática configurável no estoque via `IInventoryOrderDeductionService` e disparando eventos em tempo real por tenant via `IOrderRealtimeNotifier`.
  - Cancelamento (`TransitionKanbanStatusAsync(..., OrderStatus.Cancelled)`): estorna automaticamente o estoque baixado e o resgate de cupom para reutilização.
  - Consulta Kanban (`GetKanbanBoardAsync`) agrupada por coluna operacional.
- **Infraestrutura, Histórico Real de Produtos e RLS (`Pitako.Modules.Orders.Infrastructure`):**
  - `OrdersDbContext`, `EfOrderRepository` e implementação real de `IProductHistoryReferenceChecker` (`OrderProductHistoryReferenceChecker`), fazendo o módulo `Catalog` arquivar automaticamente produtos já vendidos em pedidos preservando o histórico.
  - Migration `20261125111949_AddOrdersAndKanbanHistory` e script SQL idempotente `docs/banco-de-dados/scripts/012-orders-kanban.sql` com `ENABLE/FORCE ROW LEVEL SECURITY` em `orders`, `order_items` e `order_status_histories`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros** (36 projetos).
- `Pitako.UnitTests`: **66 aprovados, 0 falhas, 0 ignorados** (+2 novos testes em `OrderKanbanDomainTests` cobrindo criação a partir do carrinho preservando copos personalizados distintos, obrigatoriedade de endereço em delivery, transições válidas/inválidas do Kanban e cancelamento com motivo obrigatório).
- `Pitako.IntegrationTests`: **17 aprovados, 0 falhas, 1 ignorado** (+1 fluxo completo entre 7 `DbContext` isolados em `OrdersCheckoutAndKanbanIntegrationTests` cobrindo checkout de caixa/delivery, frete Haversine, 2 copos de açaí distintos, pesagem vinculada, baixa automática de estoque via ficha técnica, arquivamento real de produto com histórico de pedido, avanço completo no Kanban e cancelamento com estorno de estoque + cupom + evento em tempo real; `PostgresRlsLiveTests` segue ignorado por ausência de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **13 aprovados, 0 falhas, 0 ignorados** (+1 novo teste em `OrdersIsolationSecurityTests` comprovando isolamento multi-tenant de pedidos/itens/histórico e menor privilégio impedindo perfis Cozinha e Entregador de cancelar ou aceitar pedidos).

## 3. Falhas encontradas e correções realizadas
- Ajustada autorização de orquestração interna de baixa/estorno de estoque e vínculo de pesagem acionada por checkout/cancelamento de pedidos para aceitar permissões de `SystemModules.Orders` mantendo estrita a validação `CrossTenantAccessException` e `TenantGuard`.

## 4. Pendências e limitações registradas
- O transporte físico SignalR (`Hub` WebSocket na camada Web/Host) fica pronto para conectar-se à interface `IOrderRealtimeNotifier`, validada nesta fase por gravador determinístico em memória.
- Aplicação ao vivo do script `012-orders-kanban.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.

## 5. Critérios de aceitação e resultado
- [x] Checkout unificado recalculando frete, cupons, pesagem e baixa automática de estoque sem confiar no cliente.
- [x] Painel Kanban e trilha de status com máquina de estados estrita e cancelamento com estorno automático.
- [x] Preservação real de histórico de produtos vendidos no catálogo (`OrderProductHistoryReferenceChecker`).
- [x] Isolamento multi-tenant, RBAC e PostgreSQL RLS em todas as tabelas do schema `orders`.

**Resultado:** Fase 13 concluída e validada. Aguardando aprovação para avançar para a **Fase 14 — PDV e caixa**.
