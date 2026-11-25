using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Time;

namespace Pitako.Persistence;

/// <summary>
/// DbContext base multi-tenant para todos os módulos do PITAKO.
/// Aplica filtros globais automáticos por <see cref="ITenantScopedEntity"/>, valida propriedade
/// na escrita e preenche campos de auditoria temporal.
/// </summary>
public abstract class PitakoDbContextBase : DbContext
{
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    protected PitakoDbContextBase(
        DbContextOptions options,
        ITenantContext tenantContext,
        IClock clock)
        : base(options)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Indica ao filtro global do EF Core se há um estabelecimento autenticado/validado na execução atual.
    /// </summary>
    protected bool HasResolvedTenant => _tenantContext.IsResolved;

    /// <summary>
    /// Identificador do tenant atual ou <see cref="Guid.Empty"/> quando não resolvido (evita acesso a Nullable.Value na parametrização de query).
    /// </summary>
    protected Guid CurrentTenantIdOrEmpty => _tenantContext.IsResolved ? _tenantContext.TenantId!.Value : Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyTenantQueryFilters(modelBuilder);
    }

    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScopedEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantIdProperty = Expression.Property(parameter, nameof(ITenantScopedEntity.TenantId));
            var hasResolvedProp = Expression.Property(Expression.Constant(this), nameof(HasResolvedTenant));
            var currentTenantProp = Expression.Property(Expression.Constant(this), nameof(CurrentTenantIdOrEmpty));

            // Fail-closed: retorna registros apenas quando HasResolvedTenant é verdadeiro e TenantId coincide.
            var equals = Expression.Equal(tenantIdProperty, currentTenantProp);
            var body = Expression.AndAlso(hasResolvedProp, equals);

            var lambda = Expression.Lambda(body, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceMultiTenancyAndAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceMultiTenancyAndAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceMultiTenancyAndAudit()
    {
        var utcNow = _clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
                continue;

            if (entry.Entity is ITenantScopedEntity tenantEntity)
                ValidateTenantScopedEntry(entry, tenantEntity);

            if (entry.Entity is IAuditableEntity auditableEntity)
                ApplyAuditTimestamps(entry.State, auditableEntity, utcNow);
        }
    }

    private void ValidateTenantScopedEntry(EntityEntry entry, ITenantScopedEntity tenantEntity)
    {
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        switch (entry.State)
        {
            case EntityState.Added:
                if (tenantEntity.TenantId == Guid.Empty)
                    tenantEntity.AssignTenant(currentTenantId);
                else if (tenantEntity.TenantId != currentTenantId)
                    throw new CrossTenantAccessException(currentTenantId, tenantEntity.TenantId, "Tentativa de inserir registro pertencente a outro estabelecimento.");
                break;

            case EntityState.Modified:
            case EntityState.Deleted:
                var tenantProperty = entry.Property(nameof(ITenantScopedEntity.TenantId));
                if (tenantProperty.IsModified && !Equals(tenantProperty.OriginalValue, tenantProperty.CurrentValue))
                    throw new CrossTenantAccessException(currentTenantId, tenantEntity.TenantId, "Não é permitido alterar o TenantId de um registro existente.");

                TenantGuard.EnsureOwnership(_tenantContext, tenantEntity.TenantId);
                break;
        }
    }

    private static void ApplyAuditTimestamps(EntityState state, IAuditableEntity auditable, DateTimeOffset utcNow)
    {
        if (state == EntityState.Added)
            auditable.SetCreatedAudit(utcNow);
        else if (state == EntityState.Modified)
            auditable.SetUpdatedAudit(utcNow);
    }
}
