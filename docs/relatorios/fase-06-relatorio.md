# Relatório de Validação — Fase 6: Produtos e Categorias

- **Data:** 2026-11-24

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.Catalog.Domain`):**
  - `Category`: cadastro, descrição, imagem, ordenação (`DisplayOrder`), pausa, reativação e arquivamento.
  - `Product`: cadastro, preço base (`Money`), modo de venda (`Unit`, `Weight`, `CustomizableAcai`), destaque (`IsFeatured`), galeria de fotos com imagem principal única (`SetPrimaryImage`), variações (`ProductVariation`), vínculo a grupos de adicionais/complementos, pausa/reativação (impedindo novos pedidos quando pausado/arquivado) e regra de exclusão segura (`PrepareDeletion` preserva histórico arquivando quando há referências históricas).
  - `ModifierGroup` e `ModifierOption`: grupos reutilizáveis de adicionais e complementos com seleção mínima/máxima, obrigatoriedade e quantidade máxima por opção.
  - `Combo` e `ComboItem`: combos comerciais que só aceitam pedidos quando ativos e com todos os itens componentes ativos.
  - `Promotion` e `PromotionTarget`: promoções por produto ou categoria com janela temporal, desconto percentual/fixo/preço promocional e piso monetário não negativo.
- **Aplicação (`Pitako.Modules.Catalog.Application`):**
  - `CatalogManagementService`: operações completas de catálogo validadas por `TenantGuard`, permissões granulares (`SystemModules.Catalog`), verificação de histórico (`IProductHistoryReferenceChecker`) e cálculo de preço efetivo considerando promoções vigentes (`GetEffectiveProductPriceAsync`).
- **Infraestrutura (`Pitako.Modules.Catalog.Infrastructure`):**
  - `CatalogDbContext` (schema `catalog`, herda `PitakoDbContextBase` com filtros globais multi-tenant), repositórios EF Core, Migration `20261125013539_InitialCatalogSchema` e script SQL idempotente `docs/banco-de-dados/scripts/004-catalog-initial.sql` aplicando `ENABLE/FORCE ROW LEVEL SECURITY` em todas as 11 tabelas do catálogo.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros**.
- `Pitako.UnitTests`: **41 aprovados, 0 falhas, 0 ignorados** (+3 novos testes cobrindo pausa impedindo pedido, arquivamento preservando histórico vs. exclusão física, imagem principal única, combo com item pausado e janela/piso promocional, além do teste de arquitetura).
- `Pitako.IntegrationTests`: **10 aprovados, 0 falhas, 1 ignorado** (+1 fluxo completo cobrindo categorias, produtos, fotos, variações, adicionais, combos, promoções, pausa/reativação e exclusão com/sem histórico; `PostgresRlsLiveTests` segue ignorado por ausência de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **6 aprovados, 0 falhas, 0 ignorados** (+1 novo teste comprovando isolamento multi-tenant do catálogo e menor privilégio impedindo perfil Cozinha de criar categorias).

## 3. Falhas encontradas e correções realizadas
- Nenhuma falha de compilação ou teste nesta etapa.

## 4. Pendências e limitações registradas
- `IProductHistoryReferenceChecker` será conectado à consulta real de pedidos quando o módulo `Orders` for implementado na Fase 13 (nesta fase foi validado via contrato e implementação stub de integração).
- Aplicação do script SQL `004-catalog-initial.sql` ao vivo no PostgreSQL 14 local permanece pendente de credenciais.

## 5. Critérios de aceitação e resultado
- [x] Categorias, ordenação, fotos com imagem principal, variações, adicionais, complementos, combos, promoções e destaques implementados.
- [x] Produtos pausados bloqueados para novos pedidos e histórico preservado na exclusão.
- [x] Isolamento multi-tenant e políticas PostgreSQL RLS aplicadas a todas as tabelas do catálogo.

**Resultado:** Fase 6 concluída e validada. Aguardando aprovação para avançar para a **Fase 7 — Insumos, fichas técnicas e formação de preços**.
