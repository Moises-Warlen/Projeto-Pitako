# Relatório de Validação — Fase 5: Estabelecimentos, Unidades e Personalização

- **Data:** 2026-11-24

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.Tenants.Domain`):**
  - `BusinessSegmentCatalog`: suporte nativo aos 10 segmentos especificados (`pizzaria`, `acaiteria`, `hamburgueria`, `padaria`, `sorveteria`, `restaurante`, `adega`, `lanchonete`, `pastelaria`, `doceria`) e extensibilidade para segmentos futuros sem alterar o núcleo.
  - `StoreVisualIdentity`, `BrandingPalette` e `StoreBanner`: identidade visual completamente independente da marca PITAKO (paleta neutra inicial sem imposição de vermelho ou logo PITAKO), validação hexadecimal `#RRGGBB`, troca de logo, capa, slogan, banners ordenáveis, layout de cardápio e fluxo completo **Rascunho -> Pré-visualização -> Publicação / Descarte**.
  - Evolução de `Tenant` (vínculo `CustomerAccountId`, validação de segmento, endereço/contatos oficiais e CNPJ formatado) e `StoreUnit` (endereço, telefone, fuso horário).
- **Aplicação (`Pitako.Modules.Tenants.Application`):**
  - `TenantProvisioningService` + `MasterPlanQuotaPolicyChecker`: provisionamento de estabelecimentos e criação de unidades com validação real no backend contra as cotas de `Establishments` e `Units` do plano contratado (suportando plano ilimitado sem teto artificial).
  - `StoreBrandingService`: edição de rascunho, pré-visualização antes de publicar, publicação versionada e consulta pública por slug exibindo exclusivamente a versão publicada.
- **Infraestrutura (`Pitako.Modules.Tenants.Infrastructure`):**
  - `TenantsDbContext`, `EfTenantRepository`, `EfStoreUnitRepository`, `EfStoreVisualIdentityRepository`.
  - Migration `20261125012934_AddStoreBrandingAndSegmentFields` com RLS habilitada e forçada (`ENABLE/FORCE ROW LEVEL SECURITY`) nas tabelas `tenants.store_visual_identities` e `tenants.store_banners`, refletida no script SQL idempotente `docs/banco-de-dados/scripts/001-tenants-initial.sql`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros**.
- `Pitako.UnitTests`: **38 aprovados, 0 falhas, 0 ignorados** (+13 novos testes cobrindo catálogo de 10 segmentos + segmento futuro, fluxo rascunho/preview/publicação sem imposição da marca PITAKO, validação de cor `#RRGGBB` e arquitetura limpa).
- `Pitako.IntegrationTests`: **9 aprovados, 0 falhas, 1 ignorado** (+1 novo fluxo completo integrando cota de plano Básico -> bloqueio de 2ª loja -> upgrade para Plano Ilimitado -> criação de 2ª loja -> personalização independente rascunho/preview/publicação; `PostgresRlsLiveTests` segue ignorado por ausência de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **5 aprovados, 0 falhas, 0 ignorados** (+1 novo teste comprovando isolamento estrito de rascunho, preview, edição e publicação da identidade visual entre Loja A e Loja B).

## 3. Falhas encontradas e correções realizadas
- Nenhuma falha de compilação ou teste na etapa.

## 4. Pendências e limitações registradas
- **Upload binário de imagens (S3/Blob Storage):** Nesta etapa a identidade visual persiste e valida URLs de logo/capa/banners; o módulo de armazenamento de mídia será integrado quando o storage definitivo for configurado.
- **PostgreSQL ao vivo:** Aplicação do script atualizado `001-tenants-initial.sql` no PostgreSQL 14 local permanece pendente de credenciais.

## 5. Critérios de aceitação e resultado
- [x] Todos os segmentos comerciais suportados sobre núcleo compartilhado.
- [x] Limite de estabelecimentos e unidades validado no backend pelo plano contratado (e liberado no plano ilimitado).
- [x] Identidade visual da loja independente da marca PITAKO com rascunho, preview e publicação.
- [x] Isolamento cross-tenant comprovado em testes de segurança.

**Resultado:** Fase 5 concluída e validada. Aguardando aprovação para avançar para a **Fase 6 — Produtos e categorias**.
