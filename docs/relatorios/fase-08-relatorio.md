# Relatório de Validação — Fase 8: Estoque, Compras e Produção

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.Inventory.Domain`):**
  - `SupplyItem.ConvertQuantity`: conversão bidirecional entre embalagem comercial (`PurchaseUnitCode`, ex.: `caixa`, `pacote`), unidade interna (`ContentUnitCode`) e unidade de controle de estoque.
  - `StockBalance`, `StockLot` e `StockMovement`: saldo consolidado por insumo com política configurável de estoque mínimo, saldo negativo (`AllowNegativeStock`), baixa automática em vendas (`AutoDeductOnOrder`) e bloqueio de lotes vencidos (`BlockExpiredLots`); consumo em ordem **FEFO** (*First-Expired, First-Out*); histórico imutável de movimentações com chaves de idempotência (`IdempotencyKey`) e isolamento `ITenantScopedEntity` em todas as entidades consultáveis.
  - `PurchaseReceipt` e `PurchaseReceiptItem`: recebimento de compras de fornecedores com conversão automática da unidade comercial para estoque, criação de lotes/validades e cálculo de custo unitário de entrada.
  - `ProductionOrder` e `ProductionIngredientConsumption`: ordens de produção internas baseadas em `TechnicalSheet`, com baixa proporcional dos insumos em FEFO, apuração do custo real produzido e crédito opcional do lote acabado no estoque.
- **Aplicação (`Pitako.Modules.Inventory.Application`):**
  - `InventoryStockAndProductionService` e contrato público `IInventoryOrderDeductionService`: configuração de saldos, entrada idempotente de compras (com reajuste opcional do insumo e recálculo em cascata das fichas técnicas), ajustes manuais, baixas por perda/validade, execução de ordens de produção, baixa automática configurável por pedido (`DeductForOrderSaleAsync`), estorno automático em cancelamentos (`ReverseOrderDeductionAsync`) e consulta de alertas ativos (`GetActiveAlertsAsync`).
- **Infraestrutura (`Pitako.Modules.Inventory.Infrastructure`):**
  - `InventoryDbContext` e repositórios EF Core ampliados com as 7 novas tabelas (`stock_balances`, `stock_lots`, `stock_movements`, `purchase_receipts`, `purchase_receipt_items`, `production_orders`, `production_consumptions`), Migration `20261125101259_AddStockPurchasesAndProduction` e script SQL idempotente `docs/banco-de-dados/scripts/006-inventory-stock-production.sql` com `ENABLE/FORCE ROW LEVEL SECURITY` em todas as tabelas.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros**.
- `Pitako.UnitTests`: **46 aprovados, 0 falhas, 0 ignorados** (+2 novos testes em `InventoryStockAndProductionTests` cobrindo conversão embalagem -> estoque, consumo FEFO de lotes, bloqueio de lotes vencidos com exceção explícita para descarte/perda, saldo negativo configurável, alerta de mínimo e idempotência).
- `Pitako.IntegrationTests`: **12 aprovados, 0 falhas, 1 ignorado** (+1 fluxo completo em `InventoryStockProductionIntegrationTests` cobrindo recebimento idempotente de compra com atualização de ficha técnica, ordem de produção creditando lote acabado, baixa automática configurável por venda de pedido, estorno no cancelamento, descarte por perda e alertas de estoque baixo/vencimento; `PostgresRlsLiveTests` segue ignorado por ausência de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **8 aprovados, 0 falhas, 0 ignorados** (+1 novo teste em `InventoryIsolationSecurityTests` validando isolamento total de saldos, lotes, movimentações, compras e produções entre estabelecimentos e menor privilégio impedindo perfil Cozinha de registrar compras enquanto Estoquista possui acesso autorizado).

## 3. Falhas encontradas e correções realizadas
- **Perda de `0,0001` no arredondamento de custo total multi-lote:** `StockMovement` multiplicava o custo unitário médio arredondado pela quantidade ao consumir lotes de preços distintos (`15,9999` vs `16,0000`); corrigido preservando o custo ponderado exato em `StockMovement`.
- **Filtro global em entidades filhas consultáveis (`StockLot`, `StockMovement`, `PurchaseReceiptItem`, `ProductionIngredientConsumption`):** o teste de segurança detectou que consultas diretas a `db.StockLots` ignoravam o filtro multi-tenant por ausência de `ITenantScopedEntity`; corrigido implementando `ITenantScopedEntity` nessas entidades para ativar o filtro global fail-closed em todos os `DbSet<>`.

## 4. Pendências e limitações registradas
- Execução ao vivo do script `006-inventory-stock-production.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.
- O consumo automático de `IInventoryOrderDeductionService` pelo fluxo de checkout/cancelamento de pedidos será integrado quando o módulo `Orders` for implementado na Fase 13.

## 5. Critérios de aceitação e resultado
- [x] Controle de saldo por insumo, lotes, validades FEFO e bloqueio configurável de saldo negativo/lotes vencidos.
- [x] Entradas de compras com conversão automática de embalagem e atualização opcional de custos/fichas.
- [x] Ordens de produção via ficha técnica, perdas/ajustes, baixa automática e estorno idempotentes por pedido e alertas ativos.
- [x] Isolamento multi-tenant, RBAC (`Stockist` vs `Kitchen`) e políticas PostgreSQL RLS aplicadas a todas as tabelas.

**Resultado:** Fase 8 concluída e validada. Aguardando aprovação para avançar para a **Fase 9 — Módulo especializado de açaí**.
