namespace Pitako.MultiTenancy;

/// <summary>
/// Implementação com escopo por requisição/operação do contexto multi-tenant no backend.
/// Protege contra troca acidental ou maliciosa de tenant durante a mesma execução.
/// </summary>
public sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    public Guid? TenantId { get; private set; }
    public Guid? UnitId { get; private set; }
    public bool IsMasterScope { get; private set; }
    public TenantResolutionSource Source { get; private set; } = TenantResolutionSource.Unresolved;

    /// <summary>
    /// Define o estabelecimento ativo para a operação atual.
    /// Rejeita sobrescrita para um tenant diferente dentro do mesmo escopo sem limpeza explícita.
    /// </summary>
    public void SetTenant(Guid tenantId, TenantResolutionSource source, Guid? unitId = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser Guid.Empty.", nameof(tenantId));

        if (source == TenantResolutionSource.Unresolved || source == TenantResolutionSource.MasterAdministrativeScope)
            throw new ArgumentException("Origem inválida para definição de contexto de estabelecimento.", nameof(source));

        if (TenantId.HasValue && TenantId.Value != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Tentativa de alterar o TenantId já resolvido no mesmo escopo de execução.");

        TenantId = tenantId;
        UnitId = unitId == Guid.Empty ? null : unitId;
        IsMasterScope = false;
        Source = source;
    }

    /// <summary>
    /// Ativa o escopo administrativo global do Painel Master do SaaS.
    /// </summary>
    public void SetMasterScope()
    {
        if (TenantId.HasValue)
            throw new CrossTenantAccessException(TenantId, null, "Não é permitido elevar um contexto já vinculado a uma loja para o escopo Master.");

        TenantId = null;
        UnitId = null;
        IsMasterScope = true;
        Source = TenantResolutionSource.MasterAdministrativeScope;
    }

    /// <summary>
    /// Limpa o contexto atual (uso exclusivo em reutilização controlada de escopos de worker/teste).
    /// </summary>
    public void Clear()
    {
        TenantId = null;
        UnitId = null;
        IsMasterScope = false;
        Source = TenantResolutionSource.Unresolved;
    }
}
