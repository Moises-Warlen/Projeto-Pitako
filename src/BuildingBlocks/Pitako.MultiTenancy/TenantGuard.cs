using Pitako.SharedKernel.Abstractions;

namespace Pitako.MultiTenancy;

/// <summary>
/// Guardas de segurança multi-tenant executadas obrigatoriamente no backend (Seção 6).
/// </summary>
public static class TenantGuard
{
    /// <summary>
    /// Garante que existe um estabelecimento resolvido e autenticado/validado no contexto atual.
    /// </summary>
    public static Guid EnsureResolved(ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);

        if (!tenantContext.IsResolved || tenantContext.TenantId is null)
            throw new CrossTenantAccessException(null, null, "Contexto de estabelecimento não resolvido para operação isolada.");

        return tenantContext.TenantId.Value;
    }

    /// <summary>
    /// Valida que uma entidade pertence estritamente ao estabelecimento do contexto atual.
    /// </summary>
    public static void EnsureOwnership(ITenantContext tenantContext, ITenantScopedEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsureOwnership(tenantContext, entity.TenantId);
    }

    /// <summary>
    /// Valida que o identificador do recurso corresponde ao estabelecimento do contexto atual.
    /// </summary>
    public static void EnsureOwnership(ITenantContext tenantContext, Guid resourceTenantId)
    {
        var currentTenantId = EnsureResolved(tenantContext);

        if (resourceTenantId == Guid.Empty || resourceTenantId != currentTenantId)
        {
            throw new CrossTenantAccessException(
                currentTenantId,
                resourceTenantId,
                $"Acesso bloqueado entre estabelecimentos: contexto '{currentTenantId}' tentou operar recurso de '{resourceTenantId}'.");
        }
    }

    /// <summary>
    /// Bloqueia tentativas do cliente/navegador de forjar outro TenantId em payloads, query strings ou headers.
    /// </summary>
    public static void RejectClientSuppliedMismatch(ITenantContext tenantContext, Guid? clientSuppliedTenantId)
    {
        var currentTenantId = EnsureResolved(tenantContext);

        if (clientSuppliedTenantId.HasValue && clientSuppliedTenantId.Value != currentTenantId)
        {
            throw new CrossTenantAccessException(
                currentTenantId,
                clientSuppliedTenantId,
                "Identificador de estabelecimento enviado pelo cliente diverge da sessão autenticada.");
        }
    }
}
