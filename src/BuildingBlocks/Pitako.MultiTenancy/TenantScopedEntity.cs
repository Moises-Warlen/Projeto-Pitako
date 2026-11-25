using Pitako.SharedKernel.Abstractions;

namespace Pitako.MultiTenancy;

internal static class TenantOwnershipHelper
{
    internal static Guid ValidateAndAssignTenant(Guid currentTenantId, Guid newTenantId, string entityDescription)
    {
        if (newTenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(newTenantId));

        if (currentTenantId != Guid.Empty && currentTenantId != newTenantId)
            throw new CrossTenantAccessException(currentTenantId, newTenantId, $"Não é permitido transferir {entityDescription} para outro estabelecimento.");

        return newTenantId;
    }
}

/// <summary>
/// Classe base para entidades de domínio pertencentes a um estabelecimento específico.
/// Centraliza proteção contra troca de propriedade de tenant e campos de auditoria temporal.
/// </summary>
public abstract class TenantScopedEntity : Entity<Guid>, ITenantScopedEntity, IAuditableEntity
{
    protected TenantScopedEntity()
    {
    }

    protected TenantScopedEntity(Guid id, Guid tenantId) : base(id)
    {
        AssignTenant(tenantId);
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public void AssignTenant(Guid tenantId) =>
        TenantId = TenantOwnershipHelper.ValidateAndAssignTenant(TenantId, tenantId, "uma entidade persistida");

    public void SetCreatedAudit(DateTimeOffset timestampUtc)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = timestampUtc;
    }

    public void SetUpdatedAudit(DateTimeOffset timestampUtc) => UpdatedAtUtc = timestampUtc;
}

/// <summary>
/// Raiz de agregado com isolamento obrigatório por estabelecimento (tenant).
/// </summary>
public abstract class TenantScopedAggregateRoot : AggregateRoot<Guid>, ITenantScopedEntity, IAuditableEntity
{
    protected TenantScopedAggregateRoot()
    {
    }

    protected TenantScopedAggregateRoot(Guid id, Guid tenantId) : base(id)
    {
        AssignTenant(tenantId);
    }

    public Guid TenantId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public void AssignTenant(Guid tenantId) =>
        TenantId = TenantOwnershipHelper.ValidateAndAssignTenant(TenantId, tenantId, "um agregado persistido");

    public void SetCreatedAudit(DateTimeOffset timestampUtc)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = timestampUtc;
    }

    public void SetUpdatedAudit(DateTimeOffset timestampUtc) => UpdatedAtUtc = timestampUtc;
}
