# Estrutura dos Módulos — PITAKO SaaS

## 1. BuildingBlocks (Núcleo Compartilhado)

| Bloco | Responsabilidade |
|---|---|
| `Pitako.SharedKernel` | Entidades base, `ValueObject` (CNPJ, Dinheiro, Peso), `Result<T>`, eventos de domínio, interfaces de relógio/IDs e exceções de domínio. |
| `Pitako.MultiTenancy` | Resolução e validação rigorosa de `ITenantContext` no backend (nunca confiar apenas em payload/headers não autenticados), `IUnitContext`, guards cross-tenant. |
| `Pitako.Security` | Autenticação, gestão de sessões, recuperação de senha (token único com expiração), RBAC granular por módulo/ação, hashing de senhas, criptografia de segredos, auditoria e controles LGPD. |
| `Pitako.Messaging` | Barramento interno de eventos entre módulos, padrão Outbox para consistência transacional e chaves de idempotência. |

## 2. Módulos de Negócio Independentes

| Módulo | Responsabilidade Principal | Fase Alvo |
|---|---|---|
| `MasterPanel` | Administração do SaaS, planos configuráveis, limites (inclusive **plano ilimitado** sem teto artificial), assinaturas, inadimplência e suspensão/reativação. | Fase 4 |
| `Tenants` | Estabelecimentos, unidades, segmentos comerciais, branding próprio, horários e configurações por loja. | Fase 5 |
| `Catalog` | Categorias, produtos, pausar/reativar preservando histórico, variações, complementos, combos e promoções. | Fase 6 |
| `Inventory` | Insumos, conversões de unidade, fichas técnicas, formação de custo, estoque, compras, produção e baixa automática configurável. | Fases 7–8 |
| `AcaiBuilder` | Copos/tamanhos configuráveis, acompanhamentos gratuitos (regras por item ou porção + limite por copo) e área separada de adicionais pagos. | Fase 9 |
| `Weighing` | Pesagem automática/manual, tara, peso líquido, cálculo por quilo, registro de operador/horário, contingência e adaptadores para balanças Toledo. | Fase 10 |
| `Coupons` | Cupons isolados por loja com regras de validade, mínimo, limites globais/por cliente, elegibilidade e histórico. | Fase 11 |
| `Orders` | Carrinho preservando personalizações por item, recálculo integral no backend, checkout, central de pedidos e Kanban em tempo real. | Fases 11, 13 |
| `PdvCashier` | Abertura/fechamento de caixa, lançamentos, descontos autorizados, múltiplos pagamentos, cancelamentos autorizados e comprovantes. | Fase 14 |
| `DeliveryLogistics` | Delivery próprio (frete por km/raio/mínimo/isenção), entregadores, agrupamento/rotas, GPS e relatórios de corridas. | Fases 12, 15 |
| `DiningHall` | Salão, setores, mesas, reservas, comandas, transferência, divisão de contas, garçons, QR Code de mesa e Totem. | Fases 16–17 |
| `Printing` | Múltiplas impressoras por setor (Cozinha, Balcão, Produção, Bar, Caixa), fallback principal/reserva e obrigatoriedade de logo + CNPJ da loja. | Fase 18 |
| `Marketplaces` | Hub multi-conector (iFood, Rappi, Aiqfome, Delivery Much, 99Food, Keeta, Food To Save), múltiplas lojas por parceiro e ações em massa com relatório parcial real. | Fases 19–20 |
| `WhatsAppSupport` | Integração oficial WhatsApp Business Platform, inbox, atendimento humano, bot tradicional e IA restrita a dados reais do tenant. | Fase 21 |
| `Reports` | Relatórios operacionais e gerenciais com exportação PDF, Excel e CSV. | Fase 22 |
| `DataLifecycle` | Migração assistida (Consumer, Goomer, Saipos), backup/restauração por estabelecimento e fluxos LGPD. | Fase 23 |
| `Finance` | Contas a pagar/receber, fluxo de caixa, conciliação, DRE e integração com gateways autorizados (sem armazenar dados completos de cartão). | Fase 25 |

## 3. Regras de Fronteira Entre Módulos

1. Módulos **não** acessam tabelas nem `DbContext` de outros módulos diretamente.
2. Comunicação síncrona entre módulos ocorre exclusivamente por contratos/interfaces públicas (`Contracts`) ou consultas via aplicação.
3. Comunicação assíncrona ocorre via eventos de domínio/integração através de `Pitako.Messaging`.
4. Toda funcionalidade modular verifica no backend se o módulo está habilitado no plano ativo do tenant antes de executar.
