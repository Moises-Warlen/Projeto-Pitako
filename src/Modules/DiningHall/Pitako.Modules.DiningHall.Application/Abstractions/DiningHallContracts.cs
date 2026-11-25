using Pitako.Modules.DiningHall.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.DiningHall.Application.Abstractions;

public interface IDiningSectorRepository
{
    Task<DiningSector?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiningSector>> ListOrderedAsync(CancellationToken cancellationToken = default);
    Task AddAsync(DiningSector sector, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IDiningTableRepository
{
    Task<DiningTable?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DiningTable?> FindByQrTokenAsync(string qrCodeToken, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiningTable>> ListBySectorAsync(Guid sectorId, CancellationToken cancellationToken = default);
    Task AddAsync(DiningTable table, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITableReservationRepository
{
    Task<TableReservation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TableReservation>> ListForTableAsync(Guid tableId, CancellationToken cancellationToken = default);
    Task AddAsync(TableReservation reservation, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IDiningTabRepository
{
    Task<DiningTab?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> CountAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiningTab>> ListOpenTabsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(DiningTab tab, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Contrato público para identificação de mesas por QR Code na Fase 17.
/// </summary>
public interface IQrTableLookupService
{
    Task<Result<DiningTable>> ResolveTableByQrTokenAsync(string qrCodeToken, CancellationToken cancellationToken = default);
}
