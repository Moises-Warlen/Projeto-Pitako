# ADR-003: Planos Configuráveis, Modularidade e Plano Ilimitado Real

- **Status:** Aceito
- **Data:** 2026-11-24

## Contexto
A Seção 7 exige que o administrador Master crie planos configuráveis (lojas, unidades, lojas em marketplaces, usuários, dispositivos, sessões simultâneas, módulos habilitados) e suporte um **Plano Ilimitado** sem limites artificiais escondidos no código.

## Decisão
1. Modelar cada cota de plano como um tipo explícito `QuotaLimit`:
   - `IsUnlimited = true` (`MaxAllowed = null`): a regra de negócio **ignora a verificação numérica de teto** sem substituir por valores como `999999` ou `int.MaxValue`.
   - `IsUnlimited = false` (`MaxAllowed >= 0`): o backend valida a contagem atual antes de criar/ativar o recurso e retorna erro estruturado indicando o limite atingido e elegibilidade para upgrade.
2. Habilitação de módulos é controlada por catálogo dinâmico de `ModuleCode` associado ao plano (e eventuais overrides autorizados pelo Master), avaliado via policy/guard no backend.

## Consequências
- **Positivas:** Conformidade literal com a regra de negócio de planos ilimitados e flexibilidade para lançar novos módulos sem alterar o núcleo.
- **Negativas:** Relatórios e telas precisam tratar `null` / `Ilimitado` explicitamente na apresentação e nos cálculos.
