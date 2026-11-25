# Relatório de Validação — Fase 11: Carrinho de Compras e Cupons

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Módulo de Cupons (`Pitako.Modules.Coupons`):**
  - `Coupon`, `CouponEligibilityRule` e `CouponRedemption`: cupons por loja com código normalizado único, descontos percentuais (com teto máximo opcional), valor fixo ou frete grátis, janela temporal, pedido mínimo elegível, limites globais e por cliente, elegibilidade por produto/categoria, controle de cumulatividade com promoções (`CanCombineWithPromotions`) e histórico/estorno de utilizações (`CouponRedemption`).
  - `CouponsManagementService` e contrato público `ICouponValidationService`: avaliação, resgate e estorno protegidos por `TenantGuard` e `SystemModules.Coupons`.
- **Contratos de Cotação Integrados (`Catalog` e `Weighing`):**
  - `ICatalogItemPricingService.QuoteProductItemAsync` em `CatalogManagementService`: cotação no servidor de produto, variação ativa, adicionais/complementos obrigatórios e promoções vigentes (com sinalização de compatibilidade com cupons).
  - `IWeighingOrderAttachmentService.GetValidatedRecordAsync` em `WeighingService`: leitura validada de pesagens concluídas.
- **Núcleo de Carrinho (`Pitako.Modules.Orders`):**
  - `ShoppingCart` e `ShoppingCartItem`: preservação estrita de itens personalizados via fingerprint SHA-256 canônico (`ComputeFingerprint`) garantindo que dois copos de açaí 500 ml com escolhas diferentes (ou pesagens distintas) permaneçam como linhas separadas no carrinho preservando todas as seleções; recálculo integral de subtotal, descontos e entrega no servidor (`ShoppingCartService`).
- **Infraestrutura e PostgreSQL RLS:**
  - `CouponsDbContext` (schema `coupons`, tabelas `coupons`, `coupon_eligibility_rules`, `coupon_redemptions`) e `OrdersDbContext` (schema `orders`, tabelas `shopping_carts`, `shopping_cart_items`), migrations `20261125110025_InitialCouponsSchema` e `20261125110505_InitialShoppingCartSchema` e scripts SQL idempotentes `009-coupons-initial.sql` e `010-orders-cart-initial.sql` com `ENABLE/FORCE ROW LEVEL SECURITY`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros** (33 projetos).
- `Pitako.UnitTests`: **61 aprovados, 0 falhas, 0 ignorados** (+3 novos testes em `CouponsAndShoppingCartTests` cobrindo regras de validade, mínimo, limites global/cliente, estorno liberando uso, frete grátis, incompatibilidade com promoções e preservação de múltiplos copos de açaí personalizados distintos no mesmo carrinho, além da inclusão de `Coupons` e `Orders` em `ArchitectureTests`).
- `Pitako.IntegrationTests`: **15 aprovados, 0 falhas, 1 ignorado** (+1 fluxo completo entre 5 `DbContext` isolados em `CartAndCouponsIntegrationTests` combinando produto com adicional, 2 copos de açaí 500 ml distintos, item pesado, entrega e cupom percentual com recálculo integral no servidor; `PostgresRlsLiveTests` segue ignorado por ausência de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **11 aprovados, 0 falhas, 0 ignorados** (+1 novo teste em `CouponsAndCartIsolationSecurityTests` comprovando isolamento multi-tenant de cupons, carrinhos e itens — inclusive sob mesma `CartSessionKey` — e bloqueio do perfil Cozinha por menor privilégio).

## 3. Falhas encontradas e correções realizadas
- Ajustada propriedade `IsAvailable` de `ModifierOption` durante a implementação de `QuoteProductItemAsync` em `CatalogManagementService`.

## 4. Pendências e limitações registradas
- Execução ao vivo dos scripts `009-coupons-initial.sql` e `010-orders-cart-initial.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.
- Conversão do `ShoppingCart` em pedido definitivo (`Order`), checkout, baixa de estoque e resgate definitivo do cupom (`RedeemCouponForOrderAsync`) serão concluídos na **Fase 13 — Pedidos e Kanban em tempo real**.

## 5. Critérios de aceitação e resultado
- [x] Itens personalizados distintos (inclusive copos de açaí de mesmo tamanho com escolhas diferentes) preservados separadamente no carrinho.
- [x] Recálculo integral no backend de preços, adicionais, promoções, pesagem e cupons sem confiar em valores do cliente.
- [x] Cupons isolados por estabelecimento com todas as restrições comerciais, histórico, isolamento multi-tenant e PostgreSQL RLS.

**Resultado:** Fase 11 concluída e validada. Aguardando aprovação para avançar para a **Fase 12 — Delivery próprio e frete**.
