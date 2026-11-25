using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.DiningHall.Domain;

public enum DiningTableStatus
{
    Available = 1,
    Occupied = 2,
    Reserved = 3,
    AwaitingPayment = 4,
    Inactive = 5
}

public enum ReservationStatus
{
    Confirmed = 1,
    Seated = 2,
    Cancelled = 3,
    NoShow = 4
}

/// <summary>
/// Setor físico do salão (ex.: Salão Principal, Varanda, Deck) por estabelecimento/unidade (Seção 24).
/// </summary>
public sealed class DiningSector : TenantScopedAggregateRoot
{
    private DiningSector()
    {
        Name = string.Empty;
    }

    private DiningSector(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        string name,
        int displayOrder) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        Name = name;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public Guid? StoreUnitId { get; private set; }
    public string Name { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<DiningSector> Create(
        Guid tenantId,
        string name,
        Guid? storeUnitId = null,
        int displayOrder = 0)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<DiningSector>(Error.Validation("Dining.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<DiningSector>(Error.Validation("Dining.SectorNameRequired", "Nome do setor é obrigatório."));

        return Result.Success(new DiningSector(
            Guid.NewGuid(),
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            name.Trim(),
            displayOrder));
    }
}

/// <summary>
/// Mesa do salão vinculada a setor, capacidade, status operacional e token de QR Code (Seções 24 e 25).
/// </summary>
public sealed class DiningTable : TenantScopedAggregateRoot
{
    private DiningTable()
    {
        TableCode = string.Empty;
        QrCodeToken = string.Empty;
    }

    private DiningTable(
        Guid id,
        Guid tenantId,
        Guid sectorId,
        string tableCode,
        int capacity,
        string qrCodeToken) : base(id, tenantId)
    {
        SectorId = sectorId;
        TableCode = tableCode;
        Capacity = capacity;
        QrCodeToken = qrCodeToken;
        Status = DiningTableStatus.Available;
    }

    public Guid SectorId { get; private set; }
    public string TableCode { get; private set; }
    public int Capacity { get; private set; }
    public DiningTableStatus Status { get; private set; }
    public Guid? CurrentTabId { get; private set; }
    public string QrCodeToken { get; private set; }

    public static Result<DiningTable> Create(
        Guid tenantId,
        Guid sectorId,
        string tableCode,
        int capacity,
        string? qrCodeToken = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<DiningTable>(Error.Validation("Dining.TenantRequired", "TenantId é obrigatório."));
        if (sectorId == Guid.Empty)
            return Result.Failure<DiningTable>(Error.Validation("Dining.SectorRequired", "Setor da mesa é obrigatório."));
        if (string.IsNullOrWhiteSpace(tableCode))
            return Result.Failure<DiningTable>(Error.Validation("Dining.TableCodeRequired", "Código/número da mesa é obrigatório."));
        if (capacity <= 0)
            return Result.Failure<DiningTable>(Error.Validation("Dining.InvalidCapacity", "Capacidade da mesa deve ser positiva."));

        var token = string.IsNullOrWhiteSpace(qrCodeToken)
            ? $"QR-{Guid.NewGuid():N}"[..20].ToUpperInvariant()
            : qrCodeToken.Trim();

        return Result.Success(new DiningTable(
            Guid.NewGuid(),
            tenantId,
            sectorId,
            tableCode.Trim().ToUpperInvariant(),
            capacity,
            token));
    }

    public Result OccupyWithTab(Guid tabId)
    {
        if (Status == DiningTableStatus.Inactive)
            return Result.Failure(Error.Conflict("Dining.TableInactive", "Mesa desativada."));
        if (Status == DiningTableStatus.Occupied && CurrentTabId.HasValue && CurrentTabId != tabId)
            return Result.Failure(Error.Conflict("Dining.TableAlreadyOccupied", $"Mesa '{TableCode}' já está ocupada por outra comanda."));

        Status = DiningTableStatus.Occupied;
        CurrentTabId = tabId;
        return Result.Success();
    }

    public void MarkAwaitingPayment()
    {
        if (Status == DiningTableStatus.Occupied)
            Status = DiningTableStatus.AwaitingPayment;
    }

    public void ReleaseToAvailable()
    {
        CurrentTabId = null;
        if (Status != DiningTableStatus.Inactive)
            Status = DiningTableStatus.Available;
    }

    public void MarkReserved()
    {
        if (Status == DiningTableStatus.Available)
            Status = DiningTableStatus.Reserved;
    }
}

/// <summary>
/// Reserva de mesa com verificação de capacidade e janela horária sem conflito (Seção 24).
/// </summary>
public sealed class TableReservation : TenantScopedAggregateRoot
{
    private TableReservation()
    {
        CustomerName = string.Empty;
        CustomerPhone = string.Empty;
    }

    private TableReservation(
        Guid id,
        Guid tenantId,
        Guid diningTableId,
        string customerName,
        string customerPhone,
        int partySize,
        DateTimeOffset reservedForUtc,
        int durationMinutes,
        string? notes) : base(id, tenantId)
    {
        DiningTableId = diningTableId;
        CustomerName = customerName;
        CustomerPhone = customerPhone;
        PartySize = partySize;
        ReservedForUtc = reservedForUtc;
        DurationMinutes = durationMinutes;
        Notes = notes;
        Status = ReservationStatus.Confirmed;
    }

    public Guid DiningTableId { get; private set; }
    public string CustomerName { get; private set; }
    public string CustomerPhone { get; private set; }
    public int PartySize { get; private set; }
    public DateTimeOffset ReservedForUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public string? Notes { get; private set; }
    public ReservationStatus Status { get; private set; }

    public DateTimeOffset ReservedUntilUtc => ReservedForUtc.AddMinutes(DurationMinutes);

    public static Result<TableReservation> Create(
        Guid tenantId,
        DiningTable table,
        string customerName,
        string customerPhone,
        int partySize,
        DateTimeOffset reservedForUtc,
        int durationMinutes = 90,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (tenantId == Guid.Empty)
            return Result.Failure<TableReservation>(Error.Validation("Reservation.TenantRequired", "TenantId é obrigatório."));
        if (table.TenantId != tenantId || table.Status == DiningTableStatus.Inactive)
            return Result.Failure<TableReservation>(Error.NotFound("Reservation.TableUnavailable", "Mesa indisponível para reserva."));
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerPhone))
            return Result.Failure<TableReservation>(Error.Validation("Reservation.CustomerRequired", "Nome e telefone são obrigatórios."));
        if (partySize <= 0 || partySize > table.Capacity)
            return Result.Failure<TableReservation>(Error.Validation("Reservation.ExceedsTableCapacity", $"Quantidade de pessoas ({partySize}) excede a capacidade da mesa ({table.Capacity})."));
        if (durationMinutes <= 0)
            return Result.Failure<TableReservation>(Error.Validation("Reservation.InvalidDuration", "Duração da reserva deve ser positiva."));

        return Result.Success(new TableReservation(
            Guid.NewGuid(),
            tenantId,
            table.Id,
            customerName.Trim(),
            customerPhone.Trim(),
            partySize,
            reservedForUtc,
            durationMinutes,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()));
    }

    public bool OverlapsWith(DateTimeOffset windowStartUtc, int durationMinutes)
    {
        if (Status is ReservationStatus.Cancelled or ReservationStatus.NoShow)
            return false;

        var windowEndUtc = windowStartUtc.AddMinutes(durationMinutes);
        return windowStartUtc < ReservedUntilUtc && windowEndUtc > ReservedForUtc;
    }

    public void MarkSeated() => Status = ReservationStatus.Seated;
    public void Cancel() => Status = ReservationStatus.Cancelled;
}
