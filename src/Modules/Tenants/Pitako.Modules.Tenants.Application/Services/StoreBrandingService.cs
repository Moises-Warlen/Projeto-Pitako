using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Tenants.Application.Services;

public sealed record UpdateStoreBrandingDraftCommand(
    string? LogoUrl,
    string? CoverImageUrl,
    string? Slogan,
    string PrimaryColorHex,
    string SecondaryColorHex,
    string AccentColorHex,
    string MenuLayoutMode,
    IReadOnlyList<StoreBannerSnapshot>? DraftBanners = null);

public sealed record PublicStorefrontPresentation(
    Guid TenantId,
    string TradeName,
    string Slug,
    string SegmentCode,
    string FormattedCnpj,
    string? Address,
    string? ContactPhone,
    StoreBrandingSnapshot Branding);

/// <summary>
/// Serviço de personalização visual independente dos estabelecimentos com rascunho,
/// pré-visualização e publicação controlada (Seção 3).
/// </summary>
public sealed class StoreBrandingService
{
    private readonly ITenantRepository _tenantRepo;
    private readonly IStoreVisualIdentityRepository _identityRepo;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public StoreBrandingService(
        ITenantRepository tenantRepo,
        IStoreVisualIdentityRepository identityRepo,
        ITenantContext tenantContext,
        IClock clock)
    {
        _tenantRepo = tenantRepo;
        _identityRepo = identityRepo;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<StoreBrandingSnapshot>> UpdateDraftAsync(
        User actor,
        Guid targetTenantId,
        UpdateStoreBrandingDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var auth = ValidateTenantAccess(actor, targetTenantId, PermissionAction.Edit);
        if (auth.IsFailure)
            return Result.Failure<StoreBrandingSnapshot>(auth.Error);

        var identity = await GetOrCreateIdentityAsync(targetTenantId, cancellationToken);
        var update = identity.UpdateDraftBranding(
            command.LogoUrl,
            command.CoverImageUrl,
            command.Slogan,
            command.PrimaryColorHex,
            command.SecondaryColorHex,
            command.AccentColorHex,
            command.MenuLayoutMode,
            command.DraftBanners);

        if (update.IsFailure)
            return Result.Failure<StoreBrandingSnapshot>(update.Error);

        await _identityRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(identity.GetDraftPreview());
    }

    public async Task<Result<StoreBrandingSnapshot>> GetDraftPreviewAsync(
        User actor,
        Guid targetTenantId,
        CancellationToken cancellationToken = default)
    {
        var auth = ValidateTenantAccess(actor, targetTenantId, PermissionAction.View);
        if (auth.IsFailure)
            return Result.Failure<StoreBrandingSnapshot>(auth.Error);

        var identity = await GetOrCreateIdentityAsync(targetTenantId, cancellationToken);
        return Result.Success(identity.GetDraftPreview());
    }

    public async Task<Result<StoreBrandingSnapshot>> PublishBrandingAsync(
        User actor,
        Guid targetTenantId,
        CancellationToken cancellationToken = default)
    {
        var auth = ValidateTenantAccess(actor, targetTenantId, PermissionAction.Edit);
        if (auth.IsFailure)
            return Result.Failure<StoreBrandingSnapshot>(auth.Error);

        var identity = await GetOrCreateIdentityAsync(targetTenantId, cancellationToken);
        identity.Publish(_clock.UtcNow);
        await _identityRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(identity.GetPublishedView());
    }

    public async Task<Result<PublicStorefrontPresentation>> GetPublicStorefrontBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepo.FindBySlugAsync(slug, cancellationToken);
        if (tenant is null || tenant.Status == TenantStatus.Cancelled || tenant.Status == TenantStatus.Suspended)
            return Result.Failure<PublicStorefrontPresentation>(Error.NotFound("Storefront.NotFound", "Estabelecimento não encontrado ou indisponível."));

        var identity = await _identityRepo.FindByTenantIdForPublicViewAsync(tenant.Id, cancellationToken)
            ?? StoreVisualIdentity.CreateDefault(tenant.Id);

        // Sempre expõe somente a versão publicada, nunca alterações em rascunho não publicadas.
        return Result.Success(new PublicStorefrontPresentation(
            tenant.Id,
            tenant.TradeName,
            tenant.Slug,
            tenant.SegmentCode,
            tenant.Cnpj.Formatted,
            tenant.Address,
            tenant.ContactPhone,
            identity.GetPublishedView()));
    }

    private Result ValidateTenantAccess(User actor, Guid targetTenantId, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        TenantGuard.EnsureOwnership(_tenantContext, targetTenantId);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != targetTenantId)
            throw new CrossTenantAccessException(actor.TenantId, targetTenantId, "Tentativa de alterar identidade visual de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.Tenants, action))
            return Result.Failure(Error.Forbidden("Tenants.BrandingForbidden", "Permissão insuficiente para gerenciar identidade visual."));

        return Result.Success();
    }

    private async Task<StoreVisualIdentity> GetOrCreateIdentityAsync(Guid targetTenantId, CancellationToken cancellationToken)
    {
        var existing = await _identityRepo.FindCurrentTenantIdentityAsync(cancellationToken);
        if (existing is not null)
            return existing;

        var created = StoreVisualIdentity.CreateDefault(targetTenantId);
        await _identityRepo.AddAsync(created, cancellationToken);
        return created;
    }
}
