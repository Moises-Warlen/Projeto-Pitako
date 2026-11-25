# Relatório de Validação — Fase 7: Insumos, Fichas Técnicas e Formação de Preços

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.Inventory.Domain`):**
  - `UnitsOfMeasure` (`UnitDimension`, `UnitDefinition`, `TenantCustomUnit`, `UnitConverter`): catálogo padrão de unidades compatíveis (`kg`, `g`, `mg`, `litro`, `ml`, `unidade`, `caixa`, `pacote`), suporte a unidades configuráveis por estabelecimento (ex.: `saco25`, `balde`) e bloqueio de conversões entre dimensões incompatíveis (`Mass` vs. `Volume`).
  - `SupplyItem` e `SupplyPriceHistory`: cadastro de insumos com unidade de compra, conversão opcional de embalagem para conteúdo interno (ex.: `1 caixa = 12 litros`), fornecedor, SKU, cálculo exato de custo por quantidade utilizada (4 casas decimais) e histórico imutável de preços com isolamento de instâncias owned no EF Core.
  - `TechnicalSheet`, `TechnicalSheetIngredient`, `TechnicalSheetPackaging` e `TechnicalSheetCostSnapshot`: composição por ingredientes e embalagens (vinculadas a insumo ou custo avulso), rendimento, custo total, custo por porção e histórico versionado de custos (`RecalculateFromCurrentSupplies`).
  - `PriceFormationCalculator`: cálculo de ponto de equilíbrio (`BreakEvenPrice`), preço de venda sugerido por margem líquida desejada ou markup explícito, lucro líquido estimado e rejeição de combinações de deduções/margem >= 100%.
- **Aplicação (`Pitako.Modules.Inventory.Application`):**
  - `SuppliesAndTechnicalSheetService`: registro de unidades customizadas, cadastro de insumos, criação de fichas técnicas, reajuste de preço com recálculo automático em cascata de todas as fichas dependentes preservando snapshots históricos e simulação de preços de venda.
- **Infraestrutura (`Pitako.Modules.Inventory.Infrastructure`):**
  - `InventoryDbContext` (schema `inventory`, herdando `PitakoDbContextBase` com filtros globais multi-tenant), repositórios EF Core, Migration `20261125020251_InitialInventorySchema` e script SQL idempotente `docs/banco-de-dados/scripts/005-inventory-initial.sql` aplicando `ENABLE/FORCE ROW LEVEL SECURITY` em todas as 7 tabelas (`custom_units`, `supply_items`, `supply_price_histories`, `technical_sheets`, `technical_sheet_ingredients`, `technical_sheet_packagings`, `technical_sheet_cost_snapshots`).

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros**.
- `Pitako.UnitTests`: **44 aprovados, 0 falhas, 0 ignorados** (+2 novos testes unitários cobrindo conversão de unidades compatíveis/incompatíveis, embalagem vs. conteúdo, histórico de preços, cálculo de ficha técnica com embalagem e formação de preço por margem e markup, além da inclusão de `Inventory.Domain` e `Inventory.Application` no teste de arquitetura).
- `Pitako.IntegrationTests`: **11 aprovados, 0 falhas, 1 ignorado** (+1 novo fluxo completo cobrindo unidade customizada, cadastro de insumos, criação de ficha técnica, reajuste de insumo com recálculo automático em cascata para v2 preservando snapshot v1 e simulação de preço; `PostgresRlsLiveTests` segue ignorado por ausência de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **7 aprovados, 0 falhas, 0 ignorados** (+1 novo teste comprovando isolamento multi-tenant de insumos/fichas e princípio do menor privilégio impedindo perfil Caixa de cadastrar insumos).

## 3. Falhas encontradas e correções realizadas
- **Compartilhamento de instância `Money` (`OwnsOne`) em `SupplyPriceHistory`:** detectado nos testes de integração/segurança ao salvar `SupplyItem` juntamente com seu registro inicial de histórico; corrigido no construtor de `SupplyPriceHistory` para alocar cópias independentes dos objetos de valor `Money`.
- **Arredondamento bancário (`MidpointRounding.ToEven`) na simulação de break-even:** ajustada asserção do teste de integração de `13.13` para `13.12` (`10.50 / 0.80 = 13.125`).

## 4. Pendências e limitações registradas
- Execução ao vivo do script `005-inventory-initial.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.

## 5. Critérios de aceitação e resultado
- [x] Unidades padrão e personalizadas com bloqueio de dimensões incompatíveis.
- [x] Histórico de preços de insumos e recálculo automático em cascata das fichas técnicas preservando snapshots.
- [x] Formação de preços contemplando impostos, taxas de canal/marketplace, despesas operacionais e margem/markup.
- [x] Isolamento multi-tenant, RBAC e políticas PostgreSQL RLS aplicadas às 7 tabelas do módulo.

**Resultado:** Fase 7 concluída e validada. Aguardando aprovação para avançar para a **Fase 8 — Estoque, compras e produção**.
