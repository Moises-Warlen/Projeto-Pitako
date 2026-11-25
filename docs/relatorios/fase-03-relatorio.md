# Relatório de Validação — Fase 3: Autenticação, Usuários e Permissões

- **Data:** 2026-11-24

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.Security.Domain`):**
  - Dois níveis de contas isolados (`UserScope.Saas` e `UserScope.Establishment`) com perfis SaaS (`MasterAdministrator`, `Financial`, `Commercial`, `Support`, `Technical`) e perfis de Estabelecimento (`Administrator`, `Manager`, `Cashier`, `Attendant`, `Waiter`, `Kitchen`, `Courier`, `Stockist`, `Financial`).
  - Permissões granulares por módulo (`View`, `Create`, `Edit`, `Delete`, `Approve`, `Cancel`, `Administer`, `Export`, `Print`), matriz padrão pelo princípio do menor privilégio (`RolePermissionCatalog`) e customização por usuário (`UserModulePermission`).
  - Bloqueio estrutural impedindo que usuários de estabelecimento recebam permissões do módulo `master_panel` ou administrem outros tenants.
  - Gestão de sessões (`UserSession`), tokens de recuperação de senha de uso único e validade limitada (`PasswordResetToken`) e log de auditoria imutável (`SecurityAuditLog`).
- **Aplicação (`Pitako.Modules.Security.Application`):**
  - `AuthenticationService`: login individual SaaS/Loja, proteção contra força bruta (lockout configurável), rate limiting, validação de sessão ativa com `SecurityStamp` e revogação individual/global.
  - `PasswordRecoveryService`: recuperação de senha para Master e Estabelecimentos com resposta anti-enumeração, link seguro, token único expirável e revogação automática de todas as sessões ativas ao concluir redefinição.
  - `UserAuthorizationService`: criação de usuários e alteração granular de permissões respeitando `TenantGuard` e fronteira SaaS/Loja.
- **Infraestrutura (`Pitako.Modules.Security.Infrastructure`):**
  - `Pbkdf2PasswordHasher` (PBKDF2-SHA256, salt 16 bytes, 210.000 iterações, comparação em tempo constante).
  - `HmacJwtTokenService` (JWT HMAC-SHA256 com `iss`, `aud`, `sid`, `scope`, `tenant_id`, `sstamp`, `exp`).
  - `CryptoTokenGenerator`, `DefaultPasswordStrengthValidator`, `InMemoryLoginRateLimiter`.
  - `SecurityDbContext` (schema `security`), repositórios EF Core, Migration `20261125010416_InitialSecuritySchema` e script SQL idempotente `docs/banco-de-dados/scripts/002-security-initial.sql`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln`: **0 avisos, 0 erros**.
- `Pitako.UnitTests`: **22 aprovados, 0 falhas, 0 ignorados** (+3 novos testes cobrindo catálogo de menor privilégio, bloqueio de módulo SaaS em usuário de loja e ciclo de uso único/expiração/revogação de token de recuperação, além da atualização do teste de arquitetura).
- `Pitako.IntegrationTests`: **7 aprovados, 0 falhas, 1 ignorado** (+3 novos testes cobrindo login/lockout/revogação, recuperação de senha Master e Loja com invalidação de sessões e bloqueio de reuso, e persistência de permissões granulares; `PostgresRlsLiveTests` segue ignorado por falta de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **3 aprovados, 0 falhas, 0 ignorados** (+2 novos testes comprovando bloqueio de administração cross-tenant, bloqueio de acesso/criação SaaS por admin de loja, rejeição de permissão `master_panel` e bloqueio/auditoria por rate limiting).

## 3. Falhas encontradas e correções realizadas
1. Erro `CS1674` em `SecurityWorkflowIntegrationTests.cs` ao usar `using` em tupla de retorno: corrigido com wrapper `ServiceScope : IDisposable`.
2. `NotSupportedException` do provedor SQLite ao executar `OrderByDescending(DateTimeOffset)` em `EfSecurityAuditRepository`: corrigido para executar `ORDER BY` no banco quando `Database.IsNpgsql()` e ordenar em memória apenas em provedores sem suporte nativo (preservando uso de índice no PostgreSQL).

## 4. Pendências e limitações registradas
- **Rate limiter distribuído (Redis):** Implementação atual usa `InMemoryLoginRateLimiter` (instância única); integração com Redis real permanece pendente até Docker/Redis estarem disponíveis no ambiente.
- **Envio de e-mail SMTP/provedor:** O `PasswordRecoveryService` gera o link seguro e retorna o payload de despacho para o canal de notificação (sem envio SMTP real nesta fase).
- **PostgreSQL ao vivo:** Aplicação da migration `002-security-initial.sql` no servidor PostgreSQL 14 local pendente de credenciais.

## 5. Critérios de aceitação e resultado
- [x] Autenticação individual SaaS e Estabelecimentos validada.
- [x] Recuperação de senha com link seguro, token único, expiração e invalidação de sessões validada para Master e Loja.
- [x] Permissões granulares por módulo e princípio do menor privilégio validados.
- [x] Isolamento de administração entre lojas e bloqueio ao Painel Master comprovados em testes de segurança.

**Resultado:** Fase 3 concluída e validada. Aguardando aprovação para avançar para a **Fase 4 — Painel Master, planos e assinaturas**.
