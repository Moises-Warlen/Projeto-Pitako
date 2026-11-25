# Relatório de Validação — Fase 21: WhatsApp e Atendimento com Bot Tradicional e IA

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.WhatsAppSupport.Domain`):**
  - `WhatsAppChannelConfig` e `TraditionalMenuBotRule`: configuração oficial da Meta WhatsApp Business Cloud API por estabelecimento/unidade com tokens cifrados em repouso (`EncryptedAccessTokenBlob`, `EncryptedAppSecretBlob`), hash do `WebhookVerifyToken`, modos de resposta (`TraditionalBotOnly`, `AiGroundedAssistant`, `HybridBotThenAi`, `HumanOnly`) e regras determinísticas (`Exact`/`Contains`, link do cardápio da loja e escalonamento para atendente).
  - `WhatsAppConversation`, `WhatsAppMessage`, `GroundedTenantKnowledgeSnapshot` e `AiGroundingValidator`: inbox com idempotência por `ExternalMessageId`, controle de não lidas, transbordo humano e validação anti-alucinação para IA (`AiGrounding.UnverifiedPriceClaim` e `AiGrounding.UnverifiedOrderReference`), impedindo envio de preços ou pedidos inexistentes na base verificada do estabelecimento.
- **Aplicação (`Pitako.Modules.WhatsAppSupport.Application`):**
  - `WhatsAppSupportService`: configuração segura do canal, cadastro de regras de bot, recepção de webhook com validação HMAC `X-Hub-Signature-256`, fluxo híbrido Bot Tradicional -> IA ancorada nos dados reais do tenant -> bloqueio automático anti-alucinação com envio à fila humana (`WaitingHuman`), resposta manual no inbox (`SendHumanReplyAsync`), resolução e RBAC estrito em `SystemModules.WhatsApp` (Atendente responde e lista inbox com `Create`/`View`; somente Gerente/Admin configuram canal Cloud API e bot com `Approve`).
- **Infraestrutura e PostgreSQL RLS (`Pitako.Modules.WhatsAppSupport.Infrastructure`):**
  - `WhatsAppAesGcmSecretProtector` (AES-GCM 256 bits + SHA-256), `UnavailableWhatsAppCloudGateway` (verificação real HMAC `X-Hub-Signature-256` e falha explícita no envio HTTP sem token Meta ativo), `UnavailableExternalAiGateway` (encaminha para humano sem inventar resposta sem provedor IA), `WhatsAppDbContext` (schema `whatsapp`), Migration `20261125124003_InitialWhatsAppSchema` e script SQL idempotente [020-whatsapp-initial.sql](docs/banco-de-dados/scripts/020-whatsapp-initial.sql) aplicando `ENABLE/FORCE ROW LEVEL SECURITY` em `channel_configs`, `bot_rules`, `conversations` e `messages`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros** (51 projetos).
- `Pitako.UnitTests`: **79 aprovados, 0 falhas, 0 ignorados** (+2 testes em `WhatsAppSupportDomainTests` validando regras `Exact`/`Contains` e bloqueio anti-alucinação de preço/pedido não verificado em `AiGroundingValidator`, além da inclusão de `WhatsAppChannelConfig` e `WhatsAppSupportService` em `ArchitectureTests`).
- `Pitako.IntegrationTests`: **25 aprovados, 0 falhas, 1 ignorado** (+1 fluxo completo em `WhatsAppSupportWorkflowIntegrationTests` validando cifragem AES-GCM, webhook HMAC idempotente, resposta do bot tradicional com link da loja, IA ancorada em preço/pedido reais, bloqueio de alucinação de preço falso com transbordo humano, resposta/resolução do atendente e comportamento offline honesto via gateways indisponíveis; `PostgresRlsLiveTests.PostgresRls_ShouldIsolateRowsAtDatabaseEngineLevel` segue ignorado por ausência de credenciais PostgreSQL configuradas).
- `Pitako.SecurityTests`: **21 aprovados, 0 falhas, 0 ignorados** (+1 cenário em `WhatsAppIsolationSecurityTests` comprovando isolamento multi-tenant via Global Query Filters em todas as 4 tabelas, bloqueio de webhook cross-tenant, rejeição de HMAC forjado e menor privilégio RBAC separando Atendente, Gerente e Cozinha).

## 3. Falhas encontradas e correções realizadas
- Nenhuma falha de compilação ou teste nesta etapa.

## 4. Pendências e limitações registradas
- Credenciais reais da Meta WhatsApp Business Cloud API e chave de provedor LLM externo não configuradas neste ambiente offline; atuam explicitamente `UnavailableWhatsAppCloudGateway` (envio HTTP) e `UnavailableExternalAiGateway` (transbordo humano seguro).
- Aplicação ao vivo do script `020-whatsapp-initial.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.

## 5. Critérios de aceitação e resultado
- [x] Canal oficial WhatsApp Cloud API com proteção AES-GCM, HMAC `X-Hub-Signature-256` e idempotência de webhook.
- [x] Bot tradicional por regras, inbox com transbordo humano e IA restrita a dados reais do tenant com bloqueio anti-alucinação.
- [x] Isolamento multi-tenant, RBAC granular e PostgreSQL RLS.

**Resultado:** Fase 21 concluída e validada. Aguardando aprovação para avançar para a **Fase 22 — Relatórios operacionais/gerenciais e exportações (PDF, Excel, CSV)**.
