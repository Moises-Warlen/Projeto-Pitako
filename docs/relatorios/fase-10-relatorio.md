# Relatório de Validação — Fase 10: Pesagem e Balança

- **Data:** 2026-11-25

## 1. O que foi implementado
- **Domínio (`Pitako.Modules.Weighing.Domain`):**
  - `ScaleDeviceConfig`: cadastro configurável de balanças por estabelecimento (fabricante Toledo/Filizola/Urano/Genérica, tipo de conexão Serial/USB/TCP/Manual, porta/endpoint, perfil de protocolo e flag de contingência manual).
  - `TarePreset`: cadastro configurável de taras de recipientes (ex.: copos, pratos, marmitas em gramas).
  - `WeightPriceCalculator` e `WeighingRecord`: cálculo de peso líquido (`GrossWeightGrams - TareWeightGrams`), validação estrita contra peso líquido não positivo, apuração de valor por quilo (`Money`), obrigatoriedade de motivo em lançamento manual (`ManualFallback`), auditoria completa (operador, data/hora, payload bruto) e vínculo exclusivo por pedido.
  - `ToledoScaleProtocolParser`: decodificador de quadros seriais Toledo (Prt1/Prix 3 P03 com STX/ETX), detectando peso estável, instabilidade (`IIIII`/`U,`), sobrecarga (`SSSSS`), subcarga (`NNNNN`) e quadros corrompidos.
- **Aplicação e Hardware Gateway (`Pitako.Modules.Weighing.Application` / `Infrastructure`):**
  - `WeighingService` e contrato público `IWeighingOrderAttachmentService`: cadastro de balanças/taras, captura automática via `IScaleTransport`, contingência manual auditada e vínculo a pedidos protegidos por `TenantGuard` + `SystemModules.Weighing`.
  - `UnavailablePhysicalScaleTransport`: implementação explícita de transporte quando não há balança Toledo física conectada ao servidor, recusando qualquer simulação falsa e acionando fluxo explícito de contingência manual.
- **Infraestrutura e PostgreSQL RLS (`Pitako.Modules.Weighing.Infrastructure`):**
  - `WeighingDbContext` (schema `weighing`, herdando `PitakoDbContextBase`), repositórios EF Core, Migration `20261125104250_InitialWeighingSchema` e script SQL idempotente `docs/banco-de-dados/scripts/008-weighing-initial.sql` aplicando `ENABLE/FORCE ROW LEVEL SECURITY` em `scale_devices`, `tare_presets` e `weighing_records`.

## 2. Testes executados e resultados
- `dotnet build Pitako.sln --no-incremental`: **0 avisos, 0 erros**.
- `Pitako.UnitTests`: **58 aprovados, 0 falhas, 0 ignorados** (+10 novos casos/teorias em `WeighingDomainAndToledoProtocolTests` cobrindo quadros Toledo válidos, instabilidade, sobrecarga, subcarga, erro de frame, cálculo líquido x preço/kg, bloqueio de peso líquido <= 0, motivo manual obrigatório e vínculo único a pedido, além da inclusão em `ArchitectureTests`).
- `Pitako.IntegrationTests`: **14 aprovados, 0 falhas, 1 ignorado** (+1 fluxo completo em `WeighingWorkflowIntegrationTests` validando leitura via parser de protocolo Toledo em transporte de teste determinístico, retorno honesto de hardware desconectado em `UnavailablePhysicalScaleTransport`, contingência manual auditada e vínculo com pedido; `PostgresRlsLiveTests` segue ignorado por ausência de credenciais PostgreSQL).
- `Pitako.SecurityTests`: **10 aprovados, 0 falhas, 0 ignorados** (+1 novo teste em `WeighingIsolationSecurityTests` comprovando isolamento multi-tenant de balanças, taras e registros de pesagem e menor privilégio impedindo perfil Cozinha de configurar ou operar pesagem).

## 3. Falhas encontradas e correções realizadas
- Ajustado escape hexadecimal de controle ASCII nos literais C# dos casos de teste Toledo de `\x02` para `\u0002` (evitando absorção dos dígitos seguintes do peso na sequência hexadecimal).

## 4. Pendências e limitações registradas
- **Integração física com balança Toledo real:** pendente de validação com hardware Toledo físico e agente serial/USB local (validado nesta fase exclusivamente o decodificador de protocolo Toledo com transporte determinístico em teste e o fallback explícito de hardware indisponível).
- Aplicação ao vivo do script `008-weighing-initial.sql` no PostgreSQL 14 local permanece pendente por ausência de credenciais configuradas.

## 5. Critérios de aceitação e resultado
- [x] Configuração de balanças, taras, peso bruto/líquido e valor por quilo com auditoria de operador e horário.
- [x] Parser de protocolo Toledo testado deterministicamente e modo de contingência manual com registro de motivo.
- [x] Nenhuma integração física simulada como real; limitação de hardware registrada com transparência.
- [x] Isolamento multi-tenant, RBAC e políticas PostgreSQL RLS aplicadas ao schema `weighing`.

**Resultado:** Fase 10 concluída e validada. Aguardando aprovação para avançar para a **Fase 11 — Carrinho de compras e cupons**.
