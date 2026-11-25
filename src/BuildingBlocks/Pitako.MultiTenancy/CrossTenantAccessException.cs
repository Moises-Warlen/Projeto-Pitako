namespace Pitako.MultiTenancy;

/// <summary>
/// Exceção de segurança lançada quando uma operação tenta acessar, criar ou modificar
/// recursos pertencentes a outro estabelecimento ou sem contexto multi-tenant válido.
/// </summary>
public sealed class CrossTenantAccessException : Exception
{
    public CrossTenantAccessException(Guid? currentTenantId, Guid? targetTenantId, string message)
        : base(message)
    {
        CurrentTenantId = currentTenantId;
        TargetTenantId = targetTenantId;
    }

    public Guid? CurrentTenantId { get; }
    public Guid? TargetTenantId { get; }
}
