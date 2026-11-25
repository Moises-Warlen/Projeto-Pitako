# Estratégia de Banco de Dados — PostgreSQL e Multi-Tenancy

## 1. Organização Lógica

- Banco principal: **PostgreSQL 14+**.
- Separação por **schemas** correspondentes aos módulos (ex.: `master`, `tenants`, `security`, `catalog`, `acai`, `inventory`, `orders`, `pdv`, `logistics`, `printing`, `finance`, `audit`), evitando acoplamento direto de tabelas entre módulos.

## 2. Isolamento Multi-Tenant

- Todas as tabelas de escopo do cliente possuem coluna `tenant_id uuid NOT NULL`.
- Índices compostos iniciando por `(tenant_id, ...)` garantem performance e particionamento lógico das consultas.
- **Row-Level Security (RLS):**
  - Habilitado em todas as tabelas multi-tenant.
  - Política padrão baseada em `tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid`.
  - Contextos administrativos globais (Painel Master) operam apenas em tabelas de escopo SaaS ou mediante função/papel auditado e específico.

## 3. Integridade e Auditoria

- Chaves primárias em `uuid` (UUIDv7/v4 gerado de forma previsível/segura na aplicação).
- Campos padrão de auditoria (`created_at_utc`, `updated_at_utc`, `created_by`, `updated_by`).
- Exclusão lógica / inativação / pausa para preservar histórico de pedidos, cupons, custos e movimentações financeiras.
- Valores monetários em `numeric(18,2)` (ou `numeric(18,4)` para custos unitários de ficha técnica) e pesos em `numeric(18,3)` (kg/g).
