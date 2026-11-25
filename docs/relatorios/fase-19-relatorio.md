# Relatório de Validação — Fase 19: Hub de Marketplaces — Estrutura e Conectores

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.Marketplaces.Domain`):**
  - `MarketplaceProvider`, `MarketplaceCapabilities` e `MarketplaceCapabilityMatrix`: matriz declarativa com as capacidades reais de cada um dos 7 marketplaces (`IFood`, `Rappi`, `Aiqfome`, `DeliveryMuch`, `Food99`, `Keeta` e `FoodToSave`), bloqueando explicitamente operações não suportadas pela API oficial do parceiro (`Marketplace.CapabilityUnsupported`).
  - `MarketplacePartnerAccount` e `MarketplaceStoreBinding`: cadastro de contas parceiras com armazenamento exclusivo de credenciais criptografadas (`EncryptedCredentialsBlob`) e vínculos por `ExternalMerchantId` e unidade da loja.
  - `MarketplaceInboundEvent` e `MarketplaceCatalogMapping`: recepção idempotente de eventos/webhooks por provedor, normalização canônica de pedidos externos (`MarketplaceNormalizedOrderPayload`) e mapeamento de SKU/preço/pausa por loja vinculada.
- **Aplicação (`Pitako.Modules.Marketplaces.Application`):**
  - `MarketplaceConnectorBase`, `MarketplaceConnectorRegistry` e os 7 conectores específicos (`IFoodConnector`, `RappiConnector`, `AiqfomeConnector`, `DeliveryMuchConnector`, `Food99Connector`, `KeetaConnector`, `FoodToSaveConnector`) compartilhando base única de verificação HMAC-SHA256, normalização de schema heterogêneo e guarda de capacidades.
  - `MarketplaceHubService`: conexão segura com proteção prévia de segredos via `ISecretProtector`, vinculação de lojas externas, mapeamento de SKUs, recepção idempotente de webhooks com validação de assinatura HMAC e execução de ações via `IMarketplaceRemoteGateway` com controle RBAC em `SystemModules.Marketplaces`.
- **Infraestrutura e PostgreSQL RLS (`Pitako.Modules.Marketplaces.Infrastructure`):**
  - `AesGcmSecretProtector` (cifragem autenticada AES-GCM 256 bits para ClientSecrets/API Keys em repouso), `UnavailableExternalMarketplaceGateway` (retorna falha explícita `Marketplace.ExternalCredentialsNotConfigured` sem simular chamadas HTTP externas), `MarketplacesDbContext` (schema `marketplaces`), Migration `20261125122047_InitialMarketplacesSchema` e script SQL idempotente [018-marketplaces-initial.sql](docs/banco-de-dados/scripts/018-marketplaces-initial.sql) aplicando `ENABLE/FORCE ROW LEVEL SECURITY` em `partner_accounts`, `store_bindings`, `inbound_events` e `catalog_mappings`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros** (48 projetos).
- `Pitako.UnitTests`: **76 aprovados, 0 falhas, 0 ignorados** (+2 testes em `MarketplacesConnectorsUnitTests` validando registro e normalização de webhooks dos 7 provedores, assinatura HMAC-SHA256 e bloqueio de capacidades não suportadas em `Aiqfome` e `DeliveryMuch`, além de `ArchitectureTests` incluindo `MarketplacePartnerAccount` e `MarketplaceHubService`).
- `Pitako.IntegrationTests`: **23 aprovados, 0 falhas, 1 ignorado** (+1 fluxo completo em `MarketplaceHubIntegrationTests` validando cifragem AES-GCM, vinculação de lojas, mapeamento SKU, recepção idempotente com HMAC no iFood, rejeição de `SyncItemPrice` no Aiqfome e falha explícita no gateway offline; `PostgresRlsLiveTests.PostgresRls_ShouldIsolateRowsAtDatabaseEngineLevel` segue ignorado por ausência de credenciais PostgreSQL configuradas).
- `Pitako.SecurityTests`: **19 aprovados, 0 falhas, 0 ignorados** (+1 cenário em `MarketplaceIsolationSecurityTests` comprovando isolamento multi-tenant via Global Query Filters em todos os 4 `DbSet`s, bloqueio de webhook spoof cross-tenant (`Marketplace.StoreBindingNotFound`), rejeição de HMAC forjado (`Marketplace.InvalidWebhookSignature`) e RBAC bloqueando Cozinha e Garçom).

## 3. Falhas encontradas e correções realizadas
- Nenhuma falha de compilação ou teste nesta etapa.

## 4. Pendências e limitações registradas
- Credenciais OAuth2/API Key reais em nuvem dos 7 parceiros (`IFood`, `Rappi`, `Aiqfome`, `DeliveryMuch`, `Food99`, `Keeta`, `FoodToSave`) permanecem não configuradas neste ambiente offline, atuando explicitamente o `UnavailableExternalMarketplaceGateway`.
- Aplicação ao vivo do script `018-marketplaces-initial.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.

## 5. Critérios de aceitação e resultado
- [x] Estrutura de Hub e conectores reais para os 7 parceiros com matriz explícita de capacidades.
- [x] Credenciais cifradas com AES-GCM 256 bits, webhooks idempotentes com validação HMAC e normalização canônica.
- [x] Isolamento multi-tenant, bloqueio de webhook spoof cross-tenant, RBAC e PostgreSQL RLS.

**Resultado:** Fase 19 concluída e validada. Aguardando aprovação para avançar para a **Fase 20 — Marketplaces — múltiplas lojas, ações em massa e sincronização**.
