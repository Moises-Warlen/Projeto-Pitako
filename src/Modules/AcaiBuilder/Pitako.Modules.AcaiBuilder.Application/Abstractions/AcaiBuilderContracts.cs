using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.AcaiBuilder.Application.Abstractions;

public interface IAcaiCupSizeRepository
{
    Task<AcaiCupSize?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AcaiCupSize>> ListAsync(bool includePaused, CancellationToken cancellationToken = default);
    Task AddAsync(AcaiCupSize cupSize, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAcaiBaseFlavorRepository
{
    Task<IReadOnlyDictionary<Guid, AcaiBaseFlavor>> FindLookupByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AcaiBaseFlavor>> ListAsync(bool includePaused, CancellationToken cancellationToken = default);
    Task AddAsync(AcaiBaseFlavor flavor, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAcaiFreeToppingRepository
{
    Task<AcaiFreeTopping?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, AcaiFreeTopping>> FindLookupByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AcaiFreeTopping>> ListAsync(bool includePaused, CancellationToken cancellationToken = default);
    Task AddAsync(AcaiFreeTopping topping, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAcaiPaidAddonRepository
{
    Task<AcaiPaidAddon?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, AcaiPaidAddon>> FindLookupByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AcaiPaidAddon>> ListAsync(bool includePaused, CancellationToken cancellationToken = default);
    Task AddAsync(AcaiPaidAddon addon, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Contrato público para validação e recálculo autoritativo de copos de açaí no cardápio digital,
/// carrinho e checkout de pedidos (Fases 11 e 13).
/// </summary>
public interface IAcaiAssemblyPricingService
{
    Task<Result<AcaiCupAssemblySummary>> BuildAndPriceCupAsync(
        AcaiCupBuildRequest request,
        CancellationToken cancellationToken = default);
}
