using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DeliveryLogistics.Domain;

public enum CourierVehicleType
{
    Motorcycle = 1,
    Bicycle = 2,
    Car = 3,
    OnFoot = 4
}

public enum CourierAvailabilityStatus
{
    Offline = 1,
    Available = 2,
    OnRoute = 3,
    Suspended = 4
}

public enum CourierCommissionMode
{
    FixedPerDelivery = 1,
    PercentageOfFreight = 2,
    FixedPlusPercentage = 3
}

/// <summary>
/// Entregador vinculado ao estabelecimento com veículo, regra de comissão,
/// disponibilidade operacional e última posição GPS conhecida (Seção 20).
/// </summary>
public sealed class DeliveryCourier : TenantScopedAggregateRoot
{
    private DeliveryCourier()
    {
        FullName = string.Empty;
        Phone = string.Empty;
        FixedCommission = Money.Zero();
    }

    private DeliveryCourier(
        Guid id,
        Guid tenantId,
        Guid? linkedUserId,
        string fullName,
        string phone,
        CourierVehicleType vehicleType,
        string? licensePlate,
        CourierCommissionMode commissionMode,
        Money fixedCommission,
        decimal freightCommissionPercentage) : base(id, tenantId)
    {
        LinkedUserId = linkedUserId;
        FullName = fullName;
        Phone = phone;
        VehicleType = vehicleType;
        LicensePlate = licensePlate;
        CommissionMode = commissionMode;
        FixedCommission = Money.Create(fixedCommission.Amount, fixedCommission.Currency).Value;
        FreightCommissionPercentage = decimal.Round(freightCommissionPercentage, 2, MidpointRounding.ToEven);
        Availability = CourierAvailabilityStatus.Available;
    }

    public Guid? LinkedUserId { get; private set; }
    public string FullName { get; private set; }
    public string Phone { get; private set; }
    public CourierVehicleType VehicleType { get; private set; }
    public string? LicensePlate { get; private set; }
    public CourierCommissionMode CommissionMode { get; private set; }
    public Money FixedCommission { get; private set; }
    public decimal FreightCommissionPercentage { get; private set; }
    public CourierAvailabilityStatus Availability { get; private set; }
    public double? LastKnownLatitude { get; private set; }
    public double? LastKnownLongitude { get; private set; }
    public DateTimeOffset? LastLocationAtUtc { get; private set; }

    public static Result<DeliveryCourier> Create(
        Guid tenantId,
        string fullName,
        string phone,
        CourierVehicleType vehicleType,
        CourierCommissionMode commissionMode,
        Money fixedCommission,
        decimal freightCommissionPercentage = 0m,
        Guid? linkedUserId = null,
        string? licensePlate = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<DeliveryCourier>(Error.Validation("Courier.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(fullName))
            return Result.Failure<DeliveryCourier>(Error.Validation("Courier.NameRequired", "Nome do entregador é obrigatório."));
        if (string.IsNullOrWhiteSpace(phone))
            return Result.Failure<DeliveryCourier>(Error.Validation("Courier.PhoneRequired", "Telefone do entregador é obrigatório."));
        if (freightCommissionPercentage is < 0m or > 100m)
            return Result.Failure<DeliveryCourier>(Error.Validation("Courier.InvalidCommissionPct", "Percentual de comissão deve estar entre 0 e 100."));

        ArgumentNullException.ThrowIfNull(fixedCommission);

        return Result.Success(new DeliveryCourier(
            Guid.NewGuid(),
            tenantId,
            linkedUserId == Guid.Empty ? null : linkedUserId,
            fullName.Trim(),
            phone.Trim(),
            vehicleType,
            string.IsNullOrWhiteSpace(licensePlate) ? null : licensePlate.Trim().ToUpperInvariant(),
            commissionMode,
            fixedCommission,
            freightCommissionPercentage));
    }

    public Money CalculateCommissionForStop(Money deliveryFee)
    {
        ArgumentNullException.ThrowIfNull(deliveryFee);

        var amount = CommissionMode switch
        {
            CourierCommissionMode.FixedPerDelivery => FixedCommission.Amount,
            CourierCommissionMode.PercentageOfFreight => decimal.Round(deliveryFee.Amount * (FreightCommissionPercentage / 100m), 2, MidpointRounding.ToEven),
            CourierCommissionMode.FixedPlusPercentage => FixedCommission.Amount + decimal.Round(deliveryFee.Amount * (FreightCommissionPercentage / 100m), 2, MidpointRounding.ToEven),
            _ => 0m
        };

        return Money.Create(amount, deliveryFee.Currency).Value;
    }

    public bool CanAcceptDispatch() => Availability == CourierAvailabilityStatus.Available;

    public void MarkOnRoute() => Availability = CourierAvailabilityStatus.OnRoute;
    public void MarkAvailable() => Availability = CourierAvailabilityStatus.Available;
    public void MarkOffline() => Availability = CourierAvailabilityStatus.Offline;
    public void Suspend() => Availability = CourierAvailabilityStatus.Suspended;

    public Result UpdateLocation(double latitude, double longitude, DateTimeOffset recordedAtUtc)
    {
        var coordRes = GeoCoordinate.Create(latitude, longitude);
        if (coordRes.IsFailure)
            return coordRes;

        LastKnownLatitude = latitude;
        LastKnownLongitude = longitude;
        LastLocationAtUtc = recordedAtUtc;
        return Result.Success();
    }
}
