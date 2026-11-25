# ADR-002: Isolamento Multi-Tenant Rigoroso no Backend

- **Status:** Aceito
- **Data:** 2026-11-24

## Contexto
Cada estabelecimento cliente do PITAKO possui dados comerciais, fiscais, financeiros, clientes finais, cardápios e integrações independentes. Vazamento de dados entre tenants é falha crítica de segurança e LGPD.

## Decisão
1. Todo dado pertencente a um estabelecimento carregará `TenantId` obrigatório (e `UnitId` quando aplicável à unidade/filial).
2. A resolução do `TenantId` ocorrerá exclusivamente no servidor a partir do token/sessão autenticada (ou slug/domínio validado em rotas públicas de cardápio), **nunca** confiando em `tenantId` enviado em corpo JSON/query string para bypass de autorização.
3. Aplicar defesa em profundidade no PostgreSQL:
   - Query Filters globais no EF Core vinculados ao `ITenantContext` atual.
   - Row-Level Security (RLS) no PostgreSQL utilizando variável de sessão `app.current_tenant_id` definida por conexão/transação autenticada.
4. Operações administrativas do Painel Master exigem perfil SaaS explícito e nunca se misturam com credenciais de loja.

## Consequências
- **Positivas:** Proteção dupla (ORM + Banco de Dados) contra acesso indevido entre estabelecimentos.
- **Negativas:** Migrações e testes de integração precisam rodar contra PostgreSQL real para validar efetivamente políticas RLS.
