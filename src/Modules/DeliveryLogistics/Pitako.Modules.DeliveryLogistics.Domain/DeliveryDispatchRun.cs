using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DeliveryLogistics.Domain;

public enum DispatchRunStatus
{
    Assigned = 1,
    InTransit = 2,
    Completed = 3,
    Cancelled = 4
}

public enum DeliveryStopStatus
{
    Pending = 1,
    Delivered = 2,
    Failed = 3
}

public sealed record DispatchStopInput(
    Guid OrderId,
    string OrderNumber,
    string CustomerName,
    string DestinationAddress,
    double DestinationLatitude,
    double DestinationLongitude,
    Money DeliveryFee);

/// <summary>
/// Parada de entrega dentro de uma corrida agrupada com sequência otimizada e comissão apurada (Seção 20).
/// </summary>
public sealed class DeliveryRunStop : Entity<Guid>, ITenantScopedEntity
{
    private DeliveryRunStop()
    {
        OrderNumberSnapshot = string.Empty;
        CustomerNameSnapshot = string.Empty;
        DestinationAddress = string.Empty;
        DeliveryFeeSnapshot = Money.Zero();
        CourierCommission = Money.Zero();
    }

    internal DeliveryRunStop(
        Guid id,
        Guid tenantId,
        Guid deliveryDispatchRunId,
        Guid orderId,
        string orderNumberSnapshot,
        string customerNameSnapshot,
        string destinationAddress,
        double destinationLatitude,
        double destinationLongitude,
        int stopSequence,
        decimal legDistanceKm,
        Money deliveryFeeSnapshot,
        Money courierCommission) : base(id)
    {
        TenantId = tenantId;
        DeliveryDispatchRunId = deliveryDispatchRunId;
        OrderId = orderId;
        OrderNumberSnapshot = orderNumberSnapshot;
        CustomerNameSnapshot = customerNameSnapshot;
        DestinationAddress = destinationAddress;
        DestinationLatitude = destinationLatitude;
        DestinationLongitude = destinationLongitude;
        StopSequence = stopSequence;
        LegDistanceKm = decimal.Round(legDistanceKm, 2, MidpointRounding.ToEven);
        DeliveryFeeSnapshot = Money.Create(deliveryFeeSnapshot.Amount, deliveryFeeSnapshot.Currency).Value;
        CourierCommission = Money.Create(courierCommission.Amount, courierCommission.Currency).Value;
        Status = DeliveryStopStatus.Pending;
    }

    public Guid TenantId { get; private set; }
    public Guid DeliveryDispatchRunId { get; private set; }
    public Guid OrderId { get; private set; }
    public string OrderNumberSnapshot { get; private set; }
    public string CustomerNameSnapshot { get; private set; }
    public string DestinationAddress { get; private set; }
    public double DestinationLatitude { get; private set; }
    public double DestinationLongitude { get; private set; }
    public int StopSequence { get; private set; }
    public decimal LegDistanceKm { get; private set; }
    public Money DeliveryFeeSnapshot { get; private set; }
    public Money CourierCommission { get; private set; }
    public DeliveryStopStatus Status { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? ResolutionNotes { get; private set; }

    internal void MarkDelivered(string? notes, DateTimeOffset utcNow)
    {
        Status = DeliveryStopStatus.Delivered;
        CompletedAtUtc = utcNow;
        ResolutionNotes = string.IsNullOrWhiteSpace(notes) ? "Entrega confirmada" : notes.Trim();
    }

    internal void MarkFailed(string reason, DateTimeOffset utcNow)
    {
        Status = DeliveryStopStatus.Failed;
        CompletedAtUtc = utcNow;
        ResolutionNotes = reason.Trim();
    }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em DeliveryRunStop.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Registro histórico de posição GPS enviada pelo aplicativo do entregador (Seção 20).
/// </summary>
public sealed class CourierGpsPing : TenantScopedEntity
{
    private CourierGpsPing()
    {
    }

    private CourierGpsPing(
        Guid id,
        Guid tenantId,
        Guid courierId,
        Guid? activeDispatchRunId,
        double latitude,
        double longitude,
        decimal? speedKmh,
        DateTimeOffset recordedAtUtc) : base(id, tenantId)
    {
        CourierId = courierId;
        ActiveDispatchRunId = activeDispatchRunId;
        Latitude = latitude;
        Longitude = longitude;
        SpeedKmh = speedKmh;
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid CourierId { get; private set; }
    public Guid? ActiveDispatchRunId { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public decimal? SpeedKmh { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public static Result<CourierGpsPing> Create(
        Guid tenantId,
        Guid courierId,
        double latitude,
        double longitude,
        DateTimeOffset recordedAtUtc,
        Guid? activeDispatchRunId = null,
        decimal? speedKmh = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CourierGpsPing>(Error.Validation("Gps.TenantRequired", "TenantId é obrigatório."));
        if (courierId == Guid.Empty)
            return Result.Failure<CourierGpsPing>(Error.Validation("Gps.CourierRequired", "CourierId é obrigatório."));

        var coordRes = GeoCoordinate.Create(latitude, longitude);
        if (coordRes.IsFailure)
            return Result.Failure<CourierGpsPing>(coordRes.Error);

        if (speedKmh.HasValue && speedKmh.Value < 0m)
            return Result.Failure<CourierGpsPing>(Error.Validation("Gps.NegativeSpeed", "Velocidade não pode ser negativa."));

        return Result.Success(new CourierGpsPing(
            Guid.NewGuid(),
            tenantId,
            courierId,
            activeDispatchRunId == Guid.Empty ? null : activeDispatchRunId,
            latitude,
            longitude,
            speedKmh,
            recordedAtUtc));
    }
}

/// <summary>
/// Corrida/saída logística agrupando 1 ou mais pedidos para um entregador,
/// com sequenciamento geodésico de rota (Nearest Neighbor) e apuração de comissão (Seção 20).
/// </summary>
public sealed class DeliveryDispatchRun : TenantScopedAggregateRoot
{
    private readonly List<DeliveryRunStop> _stops = new();

    private DeliveryDispatchRun()
    {
        RunCode = string.Empty;
        TotalCommission = Money.Zero();
    }

    private DeliveryDispatchRun(
        Guid id,
        Guid tenantId,
        Guid courierId,
        string runCode,
        decimal totalEstimatedDistanceKm,
        Money totalCommission,
        DateTimeOffset createdAtRunUtc,
        string? notes) : base(id, tenantId)
    {
        CourierId = courierId;
        RunCode = runCode;
        TotalEstimatedDistanceKm = decimal.Round(totalEstimatedDistanceKm, 2, MidpointRounding.ToEven);
        TotalCommission = Money.Create(totalCommission.Amount, totalCommission.Currency).Value;
        Status = DispatchRunStatus.Assigned;
        AssignedAtUtc = createdAtRunUtc;
        Notes = notes;
    }

    public Guid CourierId { get; private set; }
    public string RunCode { get; private set; }
    public DispatchRunStatus Status { get; private set; }
    public decimal TotalEstimatedDistanceKm { get; private set; }
    public Money TotalCommission { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyCollection<DeliveryRunStop> Stops => _stops.AsReadOnly();

    /// <summary>
    /// Cria corrida agrupando pedidos próximos ordenados automaticamente pela heurística
    /// do vizinho mais próximo (Haversine) a partir do ponto de origem do estabelecimento.
    /// </summary>
    public static Result<DeliveryDispatchRun> CreateOptimized(
        Guid tenantId,
        DeliveryCourier courier,
        string runCode,
        GeoCoordinate storeOrigin,
        IEnumerable<DispatchStopInput> stops,
        DateTimeOffset utcNow,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(courier);
        ArgumentNullException.ThrowIfNull(storeOrigin);

        if (tenantId == Guid.Empty)
            return Result.Failure<DeliveryDispatchRun>(Error.Validation("Dispatch.TenantRequired", "TenantId é obrigatório."));
        if (courier.TenantId != tenantId)
            return Result.Failure<DeliveryDispatchRun>(Error.Forbidden("Dispatch.CrossTenantCourier", "Entregador pertence a outro estabelecimento."));
        if (!courier.CanAcceptDispatch())
            return Result.Failure<DeliveryDispatchRun>(Error.Conflict("Courier.Unavailable", $"Entregador '{courier.FullName}' não está disponível (status: {courier.Availability})."));
        if (string.IsNullOrWhiteSpace(runCode))
            return Result.Failure<DeliveryDispatchRun>(Error.Validation("Dispatch.RunCodeRequired", "Código da corrida é obrigatório."));

        var pending = stops.ToList();
        if (pending.Count == 0)
            return Result.Failure<DeliveryDispatchRun>(Error.Validation("Dispatch.EmptyStops", "Corrida exige ao menos 1 entrega."));

        foreach (var s in pending)
        {
            if (s.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(s.OrderNumber) || string.IsNullOrWhiteSpace(s.DestinationAddress))
                return Result.Failure<DeliveryDispatchRun>(Error.Validation("Dispatch.InvalidStopData", "Dados do pedido e endereço são obrigatórios na parada."));

            var coordRes = GeoCoordinate.Create(s.DestinationLatitude, s.DestinationLongitude);
            if (coordRes.IsFailure)
                return Result.Failure<DeliveryDispatchRun>(coordRes.Error);
        }

        // Ordenação gulosa pelo vizinho mais próximo (Nearest Neighbor) partindo da loja
        var runId = Guid.NewGuid();
        var current = storeOrigin;
        var orderedStops = new List<DeliveryRunStop>();
        var remaining = new List<DispatchStopInput>(pending);
        decimal totalDistanceKm = 0m;
        var totalCommission = Money.Zero(pending[0].DeliveryFee.Currency);
        var sequence = 1;

        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestKm = decimal.MaxValue;

            for (var i = 0; i < remaining.Count; i++)
            {
                var candidateCoord = new GeoCoordinate(remaining[i].DestinationLatitude, remaining[i].DestinationLongitude);
                var km = GeoDistanceCalculator.CalculateKilometers(current, candidateCoord);
                if (km < bestKm)
                {
                    bestKm = km;
                    bestIndex = i;
                }
            }

            var chosen = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);

            totalDistanceKm += bestKm;
            var stopCommission = courier.CalculateCommissionForStop(chosen.DeliveryFee);
            totalCommission = totalCommission.Add(stopCommission);

            orderedStops.Add(new DeliveryRunStop(
                Guid.NewGuid(),
                tenantId,
                runId,
                chosen.OrderId,
                chosen.OrderNumber.Trim().ToUpperInvariant(),
                chosen.CustomerName.Trim(),
                chosen.DestinationAddress.Trim(),
                chosen.DestinationLatitude,
                chosen.DestinationLongitude,
                sequence++,
                bestKm,
                chosen.DeliveryFee,
                stopCommission));

            current = new GeoCoordinate(chosen.DestinationLatitude, chosen.DestinationLongitude);
        }

        var run = new DeliveryDispatchRun(
            runId,
            tenantId,
            courier.Id,
            runCode.Trim().ToUpperInvariant(),
            totalDistanceKm,
            totalCommission,
            utcNow,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());

        run._stops.AddRange(orderedStops);
        courier.MarkOnRoute();
        return Result.Success(run);
    }

    public Result StartRoute(DateTimeOffset utcNow)
    {
        if (Status != DispatchRunStatus.Assigned)
            return Result.Failure(Error.Conflict("Dispatch.CannotStart", $"Corrida em status '{Status}' não pode ser iniciada."));

        Status = DispatchRunStatus.InTransit;
        DispatchedAtUtc = utcNow;
        return Result.Success();
    }

    public Result ResolveStop(Guid stopId, bool delivered, string? notesOrReason, DeliveryCourier courier, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(courier);

        if (Status is DispatchRunStatus.Completed or DispatchRunStatus.Cancelled)
            return Result.Failure(Error.Conflict("Dispatch.TerminalStatus", "Corrida já encerrada."));

        var stop = _stops.FirstOrDefault(s => s.Id == stopId);
        if (stop is null)
            return Result.Failure(Error.NotFound("Dispatch.StopNotFound", "Parada não encontrada nesta corrida."));
        if (stop.Status != DeliveryStopStatus.Pending)
            return Result.Failure(Error.Conflict("Dispatch.StopAlreadyResolved", "Parada já foi finalizada."));

        if (delivered)
        {
            stop.MarkDelivered(notesOrReason, utcNow);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(notesOrReason))
                return Result.Failure(Error.Validation("Dispatch.FailureReasonRequired", "Motivo da ocorrência/falha na entrega é obrigatório."));
            stop.MarkFailed(notesOrReason, utcNow);
        }

        if (_stops.All(s => s.Status != DeliveryStopStatus.Pending))
        {
            Status = DispatchRunStatus.Completed;
            CompletedAtUtc = utcNow;
            if (courier.Availability == CourierAvailabilityStatus.OnRoute)
                courier.MarkAvailable();
        }

        return Result.Success();
    }
}
