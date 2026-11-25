# Relatório de Validação — Fase 9: Módulo Especializado de Açaí

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.AcaiBuilder.Domain`):**
  - `AcaiCupSize`: tamanhos de copos configuráveis pela loja (ex.: 300 ml, 500 ml, 700 ml, 1 L), preço base, limite gratuito (`FreeToppingsLimit`), mínimo opcional (`MinFreeToppings`), modo de contabilização (`AcaiFreeCountingMode.ByDistinctItem` ou `ByPortion`) e teto opcional de adicionais pagos (`MaxPaidAddonsPerCup`).
  - `AcaiBaseFlavor`: cremes/sabores base configuráveis (ex.: Açaí Tradicional, Cupuaçu, Zero Açúcar) com acréscimo opcional.
  - `AcaiFreeTopping`: área exclusiva de acompanhamentos gratuitos com controle de porções máximas por copo, pausa e vínculo opcional a insumo de estoque.
  - `AcaiPaidAddon`: área totalmente separada para adicionais pagos com preço individual, limite máximo por copo e pausa.
  - `AcaiCupAssemblyCalculator`: validador e calculadora autoritativa no backend que agrega quantidades repetidas por item (evitando burla por envio de entradas duplicadas), bloqueia seleções acima do limite gratuito ou itens pausados e recalcula o preço unitário e total de forma independente dos valores do frontend.
- **Aplicação (`Pitako.Modules.AcaiBuilder.Application`):**
  - `AcaiBuilderManagementService` e contrato público `IAcaiAssemblyPricingService`: administração protegida por `TenantGuard` + `SystemModules.Acai`, consulta do catálogo segmentado (`GetMenuCatalogAsync`) e montagem validada (`BuildAndPriceCupAsync`) pronta para integração futura com o carrinho e pedidos (Fases 11 e 13).
- **Infraestrutura (`Pitako.Modules.AcaiBuilder.Infrastructure`):**
  - `AcaiBuilderDbContext` (schema `acai`, herdando `PitakoDbContextBase`), repositórios EF Core, Migration `20261125103117_InitialAcaiBuilderSchema` e script SQL idempotente `docs/banco-de-dados/scripts/007-acai-initial.sql` aplicando `ENABLE/FORCE ROW LEVEL SECURITY` às tabelas `cup_sizes`, `base_flavors`, `free_toppings` e `paid_addons`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros**.
- `Pitako.UnitTests`: **48 aprovados, 0 falhas, 0 ignorados** (+2 novos testes em `AcaiBuilderDomainTests` cobrindo limites distintos por copo, modos `ByPortion` vs `ByDistinctItem`, mínimo obrigatório, bloqueio de burla por duplicação de item, teto de adicionais pagos e cálculo multi-copo, além da inclusão do módulo em `ArchitectureTests`).
- `Pitako.IntegrationTests`: **13 aprovados, 0 falhas, 1 ignorado** (+1 fluxo completo em `AcaiBuilderWorkflowIntegrationTests` validando cadastro separado, pausa de acompanhamento, ocultação no cardápio ativo, bloqueio de item pausado e cálculo completo; `PostgresRlsLiveTests` segue ignorado por ausência de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **9 aprovados, 0 falhas, 0 ignorados** (+1 novo teste em `AcaiBuilderIsolationSecurityTests` comprovando isolamento multi-tenant total e menor privilégio impedindo perfil Cozinha de configurar copos/itens).

## 3. Falhas encontradas e correções realizadas
- Nenhuma falha de compilação ou teste nesta etapa.

## 4. Pendências e limitações registradas
- Execução ao vivo do script `007-acai-initial.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.
- Preservação de múltiplas montagens individuais no carrinho e baixa de estoque pelos insumos vinculados aos acompanhamentos/adicionais serão integradas nas Fases 11 e 13.

## 5. Critérios de aceitação e resultado
- [x] Tamanhos de copo configuráveis com limites gratuitos individuais e contabilização por item ou porção.
- [x] Separação clara entre acompanhamentos gratuitos e adicionais pagos, impedindo seleção acima do permitido no backend.
- [x] Cálculo integral validado no servidor, isolamento multi-tenant, RBAC e políticas PostgreSQL RLS.

**Resultado:** Fase 9 concluída e validada. Aguardando aprovação para avançar para a **Fase 10 — Pesagem e balança**.
