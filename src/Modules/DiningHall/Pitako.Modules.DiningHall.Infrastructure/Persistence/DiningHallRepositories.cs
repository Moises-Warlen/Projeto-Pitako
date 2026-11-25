using Microsoft.EntityFrameworkCore;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Domain;

namespace Pitako.Modules.DiningHall.Infrastructure.Persistence;

public sealed class EfDiningSectorRepository(DiningHallDbContext db) : IDiningSectorRepository
{
    public Task<DiningSector?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Sectors.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DiningSector>> ListOrderedAsync(CancellationToken cancellationToken = default) =>
        await db.Sectors.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ToListAsync(cancellationToken);

    public async Task AddAsync(DiningSector sector, CancellationToken cancellationToken = default) =>
        await db.Sectors.AddAsync(sector, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfDiningTableRepository(DiningHallDbContext db) : IDiningTableRepository
{
    public Task<DiningTable?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Tables.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<DiningTable?> FindByQrTokenAsync(string qrCodeToken, CancellationToken cancellationToken = default) =>
        db.Tables.FirstOrDefaultAsync(t => t.QrCodeToken == qrCodeToken, cancellationToken);

    public async Task<IReadOnlyList<DiningTable>> ListBySectorAsync(Guid sectorId, CancellationToken cancellationToken = default) =>
        await db.Tables.AsNoTracking().Where(t => t.SectorId == sectorId).OrderBy(t => t.TableCode).ToListAsync(cancellationToken);

    public async Task AddAsync(DiningTable table, CancellationToken cancellationToken = default) =>
        await db.Tables.AddAsync(table, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfTableReservationRepository(DiningHallDbContext db) : ITableReservationRepository
{
    public Task<TableReservation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Reservations.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TableReservation>> ListForTableAsync(Guid tableId, CancellationToken cancellationToken = default) =>
        await db.Reservations.AsNoTracking().Where(r => r.DiningTableId == tableId).ToListAsync(cancellationToken);

    public async Task AddAsync(TableReservation reservation, CancellationToken cancellationToken = default) =>
        await db.Reservations.AddAsync(reservation, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfDiningTabRepository(DiningHallDbContext db) : IDiningTabRepository
{
    private IQueryable<DiningTab> FullQuery() =>
        db.Tabs
            .Include(t => t.Items)
            .Include(t => t.AuditEvents);

    public Task<DiningTab?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<int> CountAllAsync(CancellationToken cancellationToken = default) =>
        db.Tabs.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<DiningTab>> ListOpenTabsAsync(CancellationToken cancellationToken = default) =>
        await FullQuery()
            .AsNoTracking()
            .Where(t => t.Status == DiningTabStatus.Open || t.Status == DiningTabStatus.ClosingRequested)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(DiningTab tab, CancellationToken cancellationToken = default) =>
        await db.Tabs.AddAsync(tab, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
