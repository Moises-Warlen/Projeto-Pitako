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

public sealed class EfQrTableSessionRepository(DiningHallDbContext db) : IQrTableSessionRepository
{
    public Task<QrTableSession?> FindBySessionTokenAsync(string sessionToken, CancellationToken cancellationToken = default) =>
        db.QrTableSessions.FirstOrDefaultAsync(s => s.SessionToken == sessionToken, cancellationToken);

    public async Task<QrTableSession?> FindActiveByTableAsync(Guid diningTableId, CancellationToken cancellationToken = default)
    {
        var query = db.QrTableSessions
            .Where(s => s.DiningTableId == diningTableId &&
                        (s.Status == QrSessionStatus.Active || s.Status == QrSessionStatus.BillRequested));

        if (db.Database.IsNpgsql())
            return await query.OrderByDescending(s => s.OpenedAtUtc).FirstOrDefaultAsync(cancellationToken);

        var list = await query.ToListAsync(cancellationToken);
        return list.OrderByDescending(s => s.OpenedAtUtc).FirstOrDefault();
    }

    public async Task AddAsync(QrTableSession session, CancellationToken cancellationToken = default) =>
        await db.QrTableSessions.AddAsync(session, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfWaiterCallRepository(DiningHallDbContext db) : IWaiterCallRepository
{
    public Task<WaiterCall?> FindByIdAsync(Guid callId, CancellationToken cancellationToken = default) =>
        db.WaiterCalls.FirstOrDefaultAsync(c => c.Id == callId, cancellationToken);

    public async Task<IReadOnlyList<WaiterCall>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var query = db.WaiterCalls
            .AsNoTracking()
            .Where(c => c.Status == WaiterCallStatus.Pending || c.Status == WaiterCallStatus.Acknowledged);

        if (db.Database.IsNpgsql())
            return await query.OrderBy(c => c.RequestedAtUtc).ToListAsync(cancellationToken);

        var list = await query.ToListAsync(cancellationToken);
        return list.OrderBy(c => c.RequestedAtUtc).ToList();
    }

    public async Task AddAsync(WaiterCall waiterCall, CancellationToken cancellationToken = default) =>
        await db.WaiterCalls.AddAsync(waiterCall, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfTotemTerminalRepository(DiningHallDbContext db) : ITotemTerminalRepository
{
    public Task<TotemTerminal?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.TotemTerminals.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<TotemTerminal?> FindByCodeAsync(string terminalCode, CancellationToken cancellationToken = default) =>
        db.TotemTerminals.FirstOrDefaultAsync(t => t.TerminalCode == terminalCode, cancellationToken);

    public async Task<IReadOnlyList<TotemTerminal>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.TotemTerminals.AsNoTracking().OrderBy(t => t.TerminalCode).ToListAsync(cancellationToken);

    public async Task AddAsync(TotemTerminal terminal, CancellationToken cancellationToken = default) =>
        await db.TotemTerminals.AddAsync(terminal, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfTotemOrderSessionRepository(DiningHallDbContext db) : ITotemOrderSessionRepository
{
    public Task<TotemOrderSession?> FindBySessionTokenAsync(string sessionToken, CancellationToken cancellationToken = default) =>
        db.TotemOrderSessions.Include(s => s.Lines).FirstOrDefaultAsync(s => s.SessionToken == sessionToken, cancellationToken);

    public Task<TotemOrderSession?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
        db.TotemOrderSessions.Include(s => s.Lines).FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<int> CountConfirmedAsync(CancellationToken cancellationToken = default) =>
        db.TotemOrderSessions.CountAsync(s => s.Status == TotemSessionStatus.Confirmed, cancellationToken);

    public async Task AddAsync(TotemOrderSession session, CancellationToken cancellationToken = default) =>
        await db.TotemOrderSessions.AddAsync(session, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
