# Relatório de Validação — Fase 2: Domínio, Banco e Multi-Tenancy

- **Data:** 2026-11-24
- **Solution:** `Pitako.sln` (.NET 8.0.411)

## 1. O que foi implementado
- **Solution e padrões de projeto:** `Pitako.sln` e `Directory.Build.props` com documentação XML habilitada (`GenerateDocumentationFile=true`).
- **Núcleo de Domínio (`Pitako.SharedKernel`):**
  - `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent`, `ITenantScopedEntity`, `IAuditableEntity`.
  - `IClock` / `SystemClock`, `Result` / `Result<T>` / `Error`.
  - Objetos de valor validados: `Cnpj` (módulo 11 + formatação oficial), `Money` (precisão monetária e moeda) e `QuotaLimit` (suporte explícito a `Unlimited` sem números artificiais).
- **Isolamento Multi-Tenant (`Pitako.MultiTenancy`):**
  - `ITenantContext`, `ITenantContextSetter`, `TenantContext` (impede sobrescrita cross-tenant no mesmo escopo e elevação indevida para Master).
  - `TenantGuard` (exige contexto resolvido, valida propriedade e rejeita `TenantId` forjado pelo cliente).
  - `TenantScopedEntity` e `TenantScopedAggregateRoot` com `CrossTenantAccessException`.
- **Persistência e PostgreSQL RLS (`Pitako.Persistence` e `Pitako.Modules.Tenants`):**
  - `PitakoDbContextBase` com Global Query Filter fail-closed em toda `ITenantScopedEntity`, validação de `TenantId` no `SaveChanges`/`SaveChangesAsync` e auditoria UTC automática.
  - `PostgresTenantRlsInterceptor` propagando `app.current_tenant_id` na conexão PostgreSQL e limpando no fechamento.
  - `PostgresRlsSqlGenerator` gerando políticas `ENABLE/FORCE ROW LEVEL SECURITY` fail-closed.
  - Entidades iniciais `Tenant` e `StoreUnit` + `TenantsDbContext` + Migration `20261125005617_InitialTenantsSchema` + script SQL idempotente (`docs/banco-de-dados/scripts/001-tenants-initial.sql`).

## 2. Arquivos criados / alterados
- `Directory.Build.props`, `Pitako.sln`, `README.md`
- `src/BuildingBlocks/Pitako.SharedKernel/*` (9 arquivos)
- `src/BuildingBlocks/Pitako.MultiTenancy/*` (5 arquivos)
- `src/BuildingBlocks/Pitako.Persistence/*` (4 arquivos)
- `src/Modules/Tenants/Pitako.Modules.Tenants.Domain/*` (3 arquivos)
- `src/Modules/Tenants/Pitako.Modules.Tenants.Infrastructure/*` (6 arquivos, incluindo migrations)
- `docs/banco-de-dados/scripts/001-tenants-initial.sql`
- `tests/Pitako.UnitTests/*` (6 arquivos)
- `tests/Pitako.IntegrationTests/*` (3 arquivos)
- `tests/Pitako.SecurityTests/*` (2 arquivos)

## 3. Testes executados
- `Pitako.UnitTests`: **19 aprovados, 0 falhas, 0 ignorados** (CNPJ, Money, QuotaLimit/Ilimitado, TenantContext/Guard, Arquitetura sem acoplamento de domínio em EF/Npgsql).
- `Pitako.IntegrationTests`: **4 aprovados, 0 falhas, 1 ignorado** (isolamento entre instâncias de `TenantsDbContext`, comportamento fail-closed sem tenant, auditoria UTC, geração de DDL RLS; teste ao vivo no PostgreSQL explicitamente ignorado/pendente).
- `Pitako.SecurityTests`: **1 aprovado, 0 falhas, 0 ignorados** (bloqueio de insert/update/delete cross-tenant e bloqueio sem contexto resolvido).

## 4. Falhas encontradas durante a validação e correções realizadas
1. **Assinatura de `DbConnectionInterceptor.ConnectionClosing/Async`:** Corrigida para retornar `InterceptionResult`/`ValueTask<InterceptionResult>` com o parâmetro `result`.
2. **Exceção `Nullable object must have a value` no filtro global quando `TenantContext` não estava resolvido:** Corrigida substituindo acesso a `Nullable<Guid>.Value` por `HasResolvedTenant` (`bool`) e `CurrentTenantIdOrEmpty` (`Guid`).
3. **`MigrationBuilder.IsNpgsql()` ausente sem namespace de extensão:** Substituído por verificação direta de `ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"` na migration.

## 5. Pendências e riscos
- **Validação ao vivo contra PostgreSQL 14 (`postgresql-x64-14`):** Pendente de string de conexão com credenciais (`PITAKO_TEST_POSTGRES_CONNECTION`) para executar `PostgresRlsLiveTests` e aplicar `001-tenants-initial.sql` no servidor real.
- **Logo oficial PITAKO, Docker e Redis:** Continuam pendentes conforme registrado na Fase 1.

## 6. Critérios de aceitação e resultado
- [x] Compilação limpa (`0 avisos`, `0 erros`).
- [x] Separação de camadas comprovada por teste de arquitetura.
- [x] Isolamento multi-tenant comprovado em camada de aplicação/EF Core (leitura, escrita, alteração e exclusão).
- [x] Migration e SQL DDL de PostgreSQL RLS gerados e inspecionados.
- [ ] Execução ao vivo contra PostgreSQL 14 (pendente de credencial).

**Resultado:** Fase 2 concluída com todos os testes locais aprovados após as correções finais e pendência explícita registrada para o teste ao vivo no PostgreSQL 14. Aguardando autorização para avançar para a **Fase 3 — Autenticação, usuários e permissões**.
