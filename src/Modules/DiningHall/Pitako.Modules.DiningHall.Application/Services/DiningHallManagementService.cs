using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DiningHall.Application.Services;

/// <summary>
/// Serviço de aplicação para Salão, Setores, Mesas, Reservas, Comandas,
/// Garçons, Transferência de Mesas e Divisão de Contas (Seção 24).
/// </summary>
public sealed class DiningHallManagementService : IQrTableLookupService
{
    private readonly IDiningSectorRepository _sectorRepo;
    private readonly IDiningTableRepository _tableRepo;
    private readonly ITableReservationRepository _reservationRepo;
    private readonly IDiningTabRepository _tabRepo;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public DiningHallManagementService(
        IDiningSectorRepository sectorRepo,
        IDiningTableRepository tableRepo,
        ITableReservationRepository reservationRepo,
        IDiningTabRepository tabRepo,
        ITenantContext tenantContext,
        IClock clock)
    {
        _sectorRepo = sectorRepo;
        _tableRepo = tableRepo;
        _reservationRepo = reservationRepo;
        _tabRepo = tabRepo;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<DiningSector>> CreateSectorAsync(
        User actor,
        string name,
        Guid? storeUnitId = null,
        int displayOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<DiningSector>(guard.Error);

        var res = DiningSector.Create(guard.Value, name, storeUnitId, displayOrder);
        if (res.IsFailure)
            return res;

        await _sectorRepo.AddAsync(res.Value, cancellationToken);
        await _sectorRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<DiningTable>> CreateTableAsync(
        User actor,
        Guid sectorId,
        string tableCode,
        int capacity,
        string? qrCodeToken = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<DiningTable>(guard.Error);

        var sector = await _sectorRepo.FindByIdAsync(sectorId, cancellationToken);
        if (sector is null)
            return Result.Failure<DiningTable>(Error.NotFound("Dining.SectorNotFound", "Setor não encontrado."));

        TenantGuard.EnsureOwnership(_tenantContext, sector);

        var res = DiningTable.Create(guard.Value, sector.Id, tableCode, capacity, qrCodeToken);
        if (res.IsFailure)
            return res;

        await _tableRepo.AddAsync(res.Value, cancellationToken);
        await _tableRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<TableReservation>> CreateReservationAsync(
        User actor,
        Guid tableId,
        string customerName,
        string customerPhone,
        int partySize,
        DateTimeOffset reservedForUtc,
        int durationMinutes = 90,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<TableReservation>(guard.Error);

        var table = await _tableRepo.FindByIdAsync(tableId, cancellationToken);
        if (table is null)
            return Result.Failure<TableReservation>(Error.NotFound("Dining.TableNotFound", "Mesa não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, table);

        var existingReservations = await _reservationRepo.ListForTableAsync(table.Id, cancellationToken);
        if (existingReservations.Any(r => r.OverlapsWith(reservedForUtc, durationMinutes)))
            return Result.Failure<TableReservation>(Error.Conflict("Reservation.TimeConflict", "Já existe reserva confirmada neste horário para a mesa."));

        var res = TableReservation.Create(guard.Value, table, customerName, customerPhone, partySize, reservedForUtc, durationMinutes, notes);
        if (res.IsFailure)
            return res;

        table.MarkReserved();
        await _reservationRepo.AddAsync(res.Value, cancellationToken);
        await _reservationRepo.SaveChangesAsync(cancellationToken);
        return res;
    }

    public async Task<Result<DiningTab>> OpenTabAsync(
        User actor,
        string customerName,
        int guestCount = 1,
        Guid? diningTableId = null,
        decimal serviceChargePercent = 10m,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Create);
        if (guard.IsFailure)
            return Result.Failure<DiningTab>(guard.Error);

        DiningTable? table = null;
        if (diningTableId.HasValue && diningTableId.Value != Guid.Empty)
        {
            table = await _tableRepo.FindByIdAsync(diningTableId.Value, cancellationToken);
            if (table is null)
                return Result.Failure<DiningTab>(Error.NotFound("Dining.TableNotFound", "Mesa não encontrada."));

            TenantGuard.EnsureOwnership(_tenantContext, table);
        }

        var seq = await _tabRepo.CountAllAsync(cancellationToken) + 1;
        var tabCode = $"COM-{seq:D4}";

        var tabRes = DiningTab.Open(guard.Value, tabCode, actor.Id, customerName, guestCount, serviceChargePercent, table, _clock.UtcNow);
        if (tabRes.IsFailure)
            return tabRes;

        await _tabRepo.AddAsync(tabRes.Value, cancellationToken);
        await _tabRepo.SaveChangesAsync(cancellationToken);
        return tabRes;
    }

    public async Task<Result<DiningTab>> AddTabItemAsync(
        User actor,
        Guid tabId,
        Guid? productId,
        string itemTitle,
        string customizationSummary,
        decimal unitPriceAmount,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<DiningTab>(guard.Error);

        var tab = await _tabRepo.FindByIdAsync(tabId, cancellationToken);
        if (tab is null)
            return Result.Failure<DiningTab>(Error.NotFound("Tab.NotFound", "Comanda não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, tab);

        var priceRes = Money.Create(unitPriceAmount);
        if (priceRes.IsFailure)
            return Result.Failure<DiningTab>(priceRes.Error);

        var addRes = tab.AddItem(productId, itemTitle, customizationSummary, priceRes.Value, quantity, actor.Id, _clock.UtcNow);
        if (addRes.IsFailure)
            return Result.Failure<DiningTab>(addRes.Error);

        await _tabRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(tab);
    }

    public async Task<Result<DiningTab>> CancelTabItemAsync(
        User actor,
        Guid tabId,
        Guid tabItemId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Cancel);
        if (guard.IsFailure)
            return Result.Failure<DiningTab>(guard.Error);

        var tab = await _tabRepo.FindByIdAsync(tabId, cancellationToken);
        if (tab is null)
            return Result.Failure<DiningTab>(Error.NotFound("Tab.NotFound", "Comanda não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, tab);

        var cancelRes = tab.CancelItem(tabItemId, reason, actor.Id, _clock.UtcNow);
        if (cancelRes.IsFailure)
            return Result.Failure<DiningTab>(cancelRes.Error);

        await _tabRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(tab);
    }

    public async Task<Result<DiningTab>> TransferTabToTableAsync(
        User actor,
        Guid tabId,
        Guid targetTableId,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<DiningTab>(guard.Error);

        var tab = await _tabRepo.FindByIdAsync(tabId, cancellationToken);
        if (tab is null)
            return Result.Failure<DiningTab>(Error.NotFound("Tab.NotFound", "Comanda não encontrada."));

        var targetTable = await _tableRepo.FindByIdAsync(targetTableId, cancellationToken);
        if (targetTable is null)
            return Result.Failure<DiningTab>(Error.NotFound("Dining.TargetTableNotFound", "Mesa de destino não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, tab);
        TenantGuard.EnsureOwnership(_tenantContext, targetTable);

        DiningTable? currentTable = tab.DiningTableId.HasValue
            ? await _tableRepo.FindByIdAsync(tab.DiningTableId.Value, cancellationToken)
            : null;

        var transferRes = tab.TransferToTable(currentTable, targetTable, actor.Id, _clock.UtcNow);
        if (transferRes.IsFailure)
            return Result.Failure<DiningTab>(transferRes.Error);

        await _tabRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(tab);
    }

    public async Task<Result<IReadOnlyList<TabSplitShare>>> CalculateEqualSplitAsync(
        User actor,
        Guid tabId,
        int payersCount,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.View);
        if (guard.IsFailure)
            return Result.Failure<IReadOnlyList<TabSplitShare>>(guard.Error);

        var tab = await _tabRepo.FindByIdAsync(tabId, cancellationToken);
        if (tab is null)
            return Result.Failure<IReadOnlyList<TabSplitShare>>(Error.NotFound("Tab.NotFound", "Comanda não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, tab);
        return tab.SplitRemainingEqually(payersCount);
    }

    public async Task<Result<DiningTab>> RegisterTabPaymentAsync(
        User actor,
        Guid tabId,
        decimal paymentAmount,
        CancellationToken cancellationToken = default)
    {
        var guard = ValidateTenantActor(actor, PermissionAction.Edit);
        if (guard.IsFailure)
            return Result.Failure<DiningTab>(guard.Error);

        var tab = await _tabRepo.FindByIdAsync(tabId, cancellationToken);
        if (tab is null)
            return Result.Failure<DiningTab>(Error.NotFound("Tab.NotFound", "Comanda não encontrada."));

        TenantGuard.EnsureOwnership(_tenantContext, tab);

        var moneyRes = Money.Create(paymentAmount);
        if (moneyRes.IsFailure)
            return Result.Failure<DiningTab>(moneyRes.Error);

        DiningTable? linkedTable = tab.DiningTableId.HasValue
            ? await _tableRepo.FindByIdAsync(tab.DiningTableId.Value, cancellationToken)
            : null;

        var payRes = tab.RegisterPartialOrFullPayment(moneyRes.Value, actor.Id, linkedTable, _clock.UtcNow);
        if (payRes.IsFailure)
            return Result.Failure<DiningTab>(payRes.Error);

        await _tabRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(tab);
    }

    public async Task<Result<DiningTable>> ResolveTableByQrTokenAsync(string qrCodeToken, CancellationToken cancellationToken = default)
    {
        TenantGuard.EnsureResolved(_tenantContext);
        if (string.IsNullOrWhiteSpace(qrCodeToken))
            return Result.Failure<DiningTable>(Error.Validation("Dining.EmptyQrToken", "Token QR da mesa é obrigatório."));

        var table = await _tableRepo.FindByQrTokenAsync(qrCodeToken.Trim(), cancellationToken);
        if (table is null || table.Status == DiningTableStatus.Inactive)
            return Result.Failure<DiningTable>(Error.NotFound("Dining.TableNotFound", "Mesa não localizada para este QR Code."));

        TenantGuard.EnsureOwnership(_tenantContext, table);
        return Result.Success(table);
    }

    private Result<Guid> ValidateTenantActor(User actor, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var currentTenantId = TenantGuard.EnsureResolved(_tenantContext);

        if (actor.Scope == UserScope.Establishment && actor.TenantId != currentTenantId)
            throw new CrossTenantAccessException(actor.TenantId, currentTenantId, "Tentativa de operar salão/mesas de outro estabelecimento.");

        if (!actor.HasPermission(SystemModules.DiningHall, action))
            return Result.Failure<Guid>(Error.Forbidden("Dining.Forbidden", "Permissão insuficiente para operar salão e mesas."));

        return Result.Success(currentTenantId);
    }
}
