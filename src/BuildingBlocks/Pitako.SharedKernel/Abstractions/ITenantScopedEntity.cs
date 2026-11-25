namespace Pitako.SharedKernel.Abstractions;

/// <summary>
/// Contrato obrigatório para toda entidade de negócio isolada por estabelecimento (tenant).
/// Impede alteração indevida de propriedade entre estabelecimentos após a vinculação inicial.
/// </summary>
public interface ITenantScopedEntity
{
    /// <summary>
    /// Identificador do estabelecimento proprietário do registro.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    /// Vincula o registro ao estabelecimento no momento da persistência inicial.
    /// Deve rejeitar reatribuição para um tenant distinto.
    /// </summary>
    /// <param name="tenantId">Identificador válido do estabelecimento.</param>
    void AssignTenant(Guid tenantId);
}

/// <summary>
/// Contrato de auditoria temporal padrão para entidades persistidas.
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAtUtc { get; }
    DateTimeOffset? UpdatedAtUtc { get; }

    void SetCreatedAudit(DateTimeOffset timestampUtc);
    void SetUpdatedAudit(DateTimeOffset timestampUtc);
}
