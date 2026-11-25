# Relatório de Validação — Fase 14: PDV e Caixa

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.PdvCashier.Domain`):**
  - `CashRegisterSession` e `CashMovement`: abertura por terminal com saldo inicial, suprimentos e sangrias com saldo em espécie validado, bloqueio de duplicidade de caixa aberto no mesmo terminal e de movimentações em caixa fechado, fechamento com apuração automática do saldo esperado por meio de pagamento e diferença (sobra ou quebra).
  - `PdvSale`, `PdvSalePayment` e `PdvReceiptSnapshot`: liquidação com múltiplos meios de pagamento (`Cash`, `Pix`, `DebitCard`, `CreditCard`, `MealVoucher`, `Other`), cálculo exato de troco em espécie, validação da soma das parcelas contra o total líquido, obrigatoriedade de motivo e usuário autorizador para descontos manuais e cancelamento com estorno integral de lançamentos na sessão de caixa.
- **Aplicação (`Pitako.Modules.PdvCashier.Application`):**
  - `PdvCashierService`: orquestração idempotente de abertura, sangria, suprimento, recebimento, cancelamento autorizado (`PermissionAction.Cancel`) e fechamento protegidos por `TenantGuard`, `SystemModules.Pdv` e verificação de aprovação gerencial (`PermissionAction.Approve`) para descontos concedidos por operadores de caixa.
- **Infraestrutura e PostgreSQL RLS (`Pitako.Modules.PdvCashier.Infrastructure`):**
  - `PdvCashierDbContext` (schema `pdv`, herdando `PitakoDbContextBase`), repositórios EF Core, Migration `20261125112738_InitialPdvCashierSchema` e script SQL idempotente `docs/banco-de-dados/scripts/013-pdv-cashier-initial.sql` aplicando `ENABLE/FORCE ROW LEVEL SECURITY` em `cash_register_sessions`, `cash_movements`, `pdv_sales` e `pdv_sale_payments`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros** (39 projetos).
- `Pitako.UnitTests`: **67 aprovados, 0 falhas, 0 ignorados** (+1 fluxo completo de domínio em `PdvCashierDomainTests` cobrindo suprimento, bloqueio de sangria acima do saldo, desconto sem autorizador, venda multi-pagamento Dinheiro + Pix com troco, cancelamento estornando saldo e apuração de quebra negativa no fechamento, além da inclusão de `PdvCashier` em `ArchitectureTests`).
- `Pitako.IntegrationTests`: **18 aprovados, 0 falhas, 1 ignorado** (+1 ciclo completo em `PdvCashierWorkflowIntegrationTests` persistindo abertura, bloqueio de duplicidade, suprimento, sangria, venda multi-pagamento com aprovação de gerente, replay idempotente, cancelamento autorizado, relatório de fechamento e bloqueio após encerramento; `PostgresRlsLiveTests` segue ignorado por ausência de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **14 aprovados, 0 falhas, 0 ignorados** (+1 novo teste em `PdvCashierIsolationSecurityTests` comprovando isolamento multi-tenant de sessões, movimentos, vendas e parcelas, rejeição `CrossTenantAccessException` ao tentar usar aprovador de outro estabelecimento, e menor privilégio impedindo Caixa de conceder desconto sem aprovação e Cozinha de operar o caixa).

## 3. Falhas encontradas e correções realizadas
- Alinhado código de erro esperado no teste unitário de desconto sem autorizador para `PdvSale.DiscountAuthorizationRequired`.

## 4. Pendências e limitações registradas
- Aplicação ao vivo do script `013-pdv-cashier-initial.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.
- Impressão física automática do `PdvReceiptSnapshot` com logo e CNPJ oficiais da loja será conectada na **Fase 18 — Impressoras e setores de produção**.

## 5. Critérios de aceitação e resultado
- [x] Abertura/fechamento de caixa, suprimentos, sangrias e apuração de saldo esperado vs. contado.
- [x] Múltiplos meios de pagamento, cálculo de troco, descontos com aprovação e cancelamentos estornados com idempotência.
- [x] Isolamento multi-tenant, RBAC de menor privilégio e políticas PostgreSQL RLS aplicadas ao schema `pdv`.

**Resultado:** Fase 14 concluída e validada. Aguardando aprovação para avançar para a **Fase 15 — Entregadores, logística e GPS**.
