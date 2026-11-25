namespace Pitako.MultiTenancy;

/// <summary>
/// Origem confiável utilizada pelo backend para resolver o estabelecimento ativo na requisição.
/// </summary>
public enum TenantResolutionSource
{
    Unresolved = 0,
    AuthenticatedSession = 1,
    PublicStoreSlug = 2,
    MasterAdministrativeScope = 3,
    BackgroundJob = 4
}

/// <summary>
/// Contexto de leitura do estabelecimento atual para a execução em andamento.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Identificador do estabelecimento atual, ou nulo quando não resolvido ou em escopo exclusivo Master.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// Identificador opcional da unidade/filial selecionada dentro do estabelecimento.
    /// </summary>
    Guid? UnitId { get; }

    /// <summary>
    /// Indica se existe um estabelecimento válido e autenticado/verificado associado ao contexto.
    /// </summary>
    bool IsResolved => TenantId.HasValue && TenantId.Value != Guid.Empty;

    /// <summary>
    /// Indica se a execução atual pertence explicitamente ao Painel Master do SaaS.
    /// </summary>
    bool IsMasterScope { get; }

    /// <summary>
    /// Origem da resolução do contexto multi-tenant para fins de auditoria e segurança.
    /// </summary>
    TenantResolutionSource Source { get; }
}

/// <summary>
/// Contrato de escrita do contexto multi-tenant, restrito a middlewares de autenticação,
/// resolvedores públicos de cardápio e processadores de jobs em background.
/// </summary>
public interface ITenantContextSetter
{
    void SetTenant(Guid tenantId, TenantResolutionSource source, Guid? unitId = null);
    void SetMasterScope();
    void Clear();
}
