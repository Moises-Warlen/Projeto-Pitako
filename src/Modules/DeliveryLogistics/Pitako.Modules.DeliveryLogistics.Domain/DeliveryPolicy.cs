using Pitako.MultiTenancy;
using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DeliveryLogistics.Domain;

public enum FreightCalculationMode
{
    FixedFlat = 1,
    DistanceTiered = 2,
    FixedPlusPerKm = 3,
    ByNeighborhoodOrZone = 4
}

public sealed record DeliveryFreightQuoteRequest(
    Money OrderItemsSubtotal,
    double? DestinationLatitude = null,
    double? DestinationLongitude = null,
    string? Neighborhood = null,
    string? ZipCode = null,
    decimal? PrecomputedDistanceKm = null);

public sealed record DeliveryFreightQuote(
    decimal DistanceKm,
    Money BaseDeliveryFee,
    Money FinalDeliveryFee,
    bool IsFreeShippingApplied,
    int EstimatedMinutes,
    string AppliedRuleSummary);

/// <summary>
/// Faixa configurável de distância em km com tarifa e tempo estimado (Seção 19).
/// </summary>
public sealed class DeliveryDistanceTier : Entity<Guid>, ITenantScopedEntity
{
    private DeliveryDistanceTier()
    {
        Fee = Money.Zero();
    }

    internal DeliveryDistanceTier(
        Guid id,
        Guid tenantId,
        Guid deliveryPolicyId,
        decimal minKm,
        decimal maxKm,
        Money fee,
        int estimatedMinutes) : base(id)
    {
        TenantId = tenantId;
        DeliveryPolicyId = deliveryPolicyId;
        MinKm = decimal.Round(minKm, 2, MidpointRounding.ToEven);
        MaxKm = decimal.Round(maxKm, 2, MidpointRounding.ToEven);
        Fee = Money.Create(fee.Amount, fee.Currency).Value;
        EstimatedMinutes = estimatedMinutes;
    }

    public Guid TenantId { get; private set; }
    public Guid DeliveryPolicyId { get; private set; }
    public decimal MinKm { get; private set; }
    public decimal MaxKm { get; private set; }
    public Money Fee { get; private set; }
    public int EstimatedMinutes { get; private set; }

    public bool Matches(decimal distanceKm) => distanceKm >= MinKm && distanceKm <= MaxKm;

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em DeliveryDistanceTier.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Regra configurável de entrega por bairro ou prefixo de CEP, inclusive áreas bloqueadas (Seção 19).
/// </summary>
public sealed class DeliveryZoneRule : Entity<Guid>, ITenantScopedEntity
{
    private DeliveryZoneRule()
    {
        ZoneName = string.Empty;
        Fee = Money.Zero();
    }

    internal DeliveryZoneRule(
        Guid id,
        Guid tenantId,
        Guid deliveryPolicyId,
        string zoneName,
        string? zipCodePrefix,
        Money fee,
        int estimatedMinutes,
        bool isBlocked) : base(id)
    {
        TenantId = tenantId;
        DeliveryPolicyId = deliveryPolicyId;
        ZoneName = zoneName;
        ZipCodePrefix = zipCodePrefix;
        Fee = Money.Create(fee.Amount, fee.Currency).Value;
        EstimatedMinutes = estimatedMinutes;
        IsBlocked = isBlocked;
    }

    public Guid TenantId { get; private set; }
    public Guid DeliveryPolicyId { get; private set; }
    public string ZoneName { get; private set; }
    public string? ZipCodePrefix { get; private set; }
    public Money Fee { get; private set; }
    public int EstimatedMinutes { get; private set; }
    public bool IsBlocked { get; private set; }

    public bool Matches(string? neighborhood, string? zipCode)
    {
        var cleanZip = string.IsNullOrWhiteSpace(zipCode) ? string.Empty : new string(zipCode.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(ZipCodePrefix) && !string.IsNullOrWhiteSpace(cleanZip) &&
            cleanZip.StartsWith(ZipCodePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(neighborhood) &&
               string.Equals(ZoneName, neighborhood.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId não pode ser vazio.", nameof(tenantId));
        if (TenantId != Guid.Empty && TenantId != tenantId)
            throw new CrossTenantAccessException(TenantId, tenantId, "Reatribuição de tenant não permitida em DeliveryZoneRule.");
        TenantId = tenantId;
    }
}

/// <summary>
/// Política comercial e operacional de delivery próprio por estabelecimento/unidade,
/// abrangendo cálculo por km, faixas, zonas, raio máximo, pedido mínimo e isenção de frete (Seção 19).
/// </summary>
public sealed class DeliveryPolicy : TenantScopedAggregateRoot
{
    private readonly List<DeliveryDistanceTier> _distanceTiers = new();
    private readonly List<DeliveryZoneRule> _zoneRules = new();

    private DeliveryPolicy()
    {
        BaseFee = Money.Zero();
        FeePerKm = Money.Zero();
        MinimumOrderAmount = Money.Zero();
    }

    private DeliveryPolicy(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        double originLatitude,
        double originLongitude,
        decimal maxRadiusKm,
        FreightCalculationMode calculationMode,
        Money baseFee,
        decimal includedKm,
        Money feePerKm,
        int baseEstimatedMinutes,
        Money minimumOrderAmount,
        Money? freeShippingThreshold,
        bool isDeliveryEnabled,
        bool allowPickup) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        OriginLatitude = originLatitude;
        OriginLongitude = originLongitude;
        MaxRadiusKm = decimal.Round(maxRadiusKm, 2, MidpointRounding.ToEven);
        CalculationMode = calculationMode;
        BaseFee = baseFee;
        IncludedKm = decimal.Round(includedKm, 2, MidpointRounding.ToEven);
        FeePerKm = feePerKm;
        BaseEstimatedMinutes = baseEstimatedMinutes;
        MinimumOrderAmount = minimumOrderAmount;
        FreeShippingThreshold = freeShippingThreshold;
        IsDeliveryEnabled = isDeliveryEnabled;
        AllowPickup = allowPickup;
    }

    public Guid? StoreUnitId { get; private set; }
    public double OriginLatitude { get; private set; }
    public double OriginLongitude { get; private set; }
    public decimal MaxRadiusKm { get; private set; }
    public FreightCalculationMode CalculationMode { get; private set; }
    public Money BaseFee { get; private set; }
    public decimal IncludedKm { get; private set; }
    public Money FeePerKm { get; private set; }
    public int BaseEstimatedMinutes { get; private set; }
    public Money MinimumOrderAmount { get; private set; }
    public Money? FreeShippingThreshold { get; private set; }
    public bool IsDeliveryEnabled { get; private set; }
    public bool AllowPickup { get; private set; }
    public bool IsTemporarilyPaused { get; private set; }
    public string? PauseReason { get; private set; }

    public IReadOnlyCollection<DeliveryDistanceTier> DistanceTiers => _distanceTiers.AsReadOnly();
    public IReadOnlyCollection<DeliveryZoneRule> ZoneRules => _zoneRules.AsReadOnly();

    public static Result<DeliveryPolicy> Create(
        Guid tenantId,
        double originLatitude,
        double originLongitude,
        decimal maxRadiusKm,
        FreightCalculationMode calculationMode,
        Money baseFee,
        decimal includedKm,
        Money feePerKm,
        int baseEstimatedMinutes,
        Money minimumOrderAmount,
        Money? freeShippingThreshold = null,
        Guid? storeUnitId = null,
        bool isDeliveryEnabled = true,
        bool allowPickup = true)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<DeliveryPolicy>(Error.Validation("Delivery.TenantRequired", "TenantId é obrigatório."));

        var originRes = GeoCoordinate.Create(originLatitude, originLongitude);
        if (originRes.IsFailure)
            return Result.Failure<DeliveryPolicy>(originRes.Error);

        if (maxRadiusKm <= 0m)
            return Result.Failure<DeliveryPolicy>(Error.Validation("Delivery.InvalidRadius", "Raio máximo de entrega deve ser positivo."));

        if (includedKm < 0m)
            return Result.Failure<DeliveryPolicy>(Error.Validation("Delivery.NegativeIncludedKm", "Quilometragem incluída não pode ser negativa."));

        if (baseEstimatedMinutes <= 0)
            return Result.Failure<DeliveryPolicy>(Error.Validation("Delivery.InvalidEstimatedTime", "Tempo estimado deve ser positivo."));

        ArgumentNullException.ThrowIfNull(baseFee);
        ArgumentNullException.ThrowIfNull(feePerKm);
        ArgumentNullException.ThrowIfNull(minimumOrderAmount);

        return Result.Success(new DeliveryPolicy(
            Guid.NewGuid(),
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            originLatitude,
            originLongitude,
            maxRadiusKm,
            calculationMode,
            baseFee,
            includedKm,
            feePerKm,
            baseEstimatedMinutes,
            minimumOrderAmount,
            freeShippingThreshold,
            isDeliveryEnabled,
            allowPickup));
    }

    public Result AddDistanceTier(decimal minKm, decimal maxKm, Money fee, int estimatedMinutes)
    {
        if (minKm < 0m || maxKm <= minKm)
            return Result.Failure(Error.Validation("Delivery.InvalidTierRange", "Faixa de km inválida."));
        if (estimatedMinutes <= 0)
            return Result.Failure(Error.Validation("Delivery.InvalidEstimatedTime", "Tempo estimado da faixa deve ser positivo."));
        ArgumentNullException.ThrowIfNull(fee);

        var overlaps = _distanceTiers.Any(t => minKm < t.MaxKm && maxKm > t.MinKm);
        if (overlaps)
            return Result.Failure(Error.Conflict("Delivery.OverlappingTier", "A faixa informada sobrepõe outra faixa existente."));

        _distanceTiers.Add(new DeliveryDistanceTier(Guid.NewGuid(), TenantId, Id, minKm, maxKm, fee, estimatedMinutes));
        return Result.Success();
    }

    public Result AddZoneRule(string zoneName, string? zipCodePrefix, Money fee, int estimatedMinutes, bool isBlocked = false)
    {
        if (string.IsNullOrWhiteSpace(zoneName))
            return Result.Failure(Error.Validation("Delivery.ZoneNameRequired", "Nome do bairro/zona é obrigatório."));
        if (estimatedMinutes <= 0)
            return Result.Failure(Error.Validation("Delivery.InvalidEstimatedTime", "Tempo estimado da zona deve ser positivo."));
        ArgumentNullException.ThrowIfNull(fee);

        var cleanPrefix = string.IsNullOrWhiteSpace(zipCodePrefix)
            ? null
            : new string(zipCodePrefix.Where(char.IsDigit).ToArray());

        _zoneRules.Add(new DeliveryZoneRule(Guid.NewGuid(), TenantId, Id, zoneName.Trim(), cleanPrefix, fee, estimatedMinutes, isBlocked));
        return Result.Success();
    }

    public void PauseDelivery(string reason)
    {
        IsTemporarilyPaused = true;
        PauseReason = string.IsNullOrWhiteSpace(reason) ? "Delivery temporariamente pausado." : reason.Trim();
    }

    public void ResumeDelivery()
    {
        IsTemporarilyPaused = false;
        PauseReason = null;
    }

    /// <summary>
    /// Calcula o frete no backend validando pausa, pedido mínimo, raio máximo, região bloqueada e isenção por valor.
    /// </summary>
    public Result<DeliveryFreightQuote> QuoteFreight(DeliveryFreightQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsDeliveryEnabled)
            return Result.Failure<DeliveryFreightQuote>(Error.Conflict("Delivery.Disabled", "Entrega própria não habilitada para esta unidade."));

        if (IsTemporarilyPaused)
            return Result.Failure<DeliveryFreightQuote>(Error.Conflict("Delivery.Paused", PauseReason ?? "Delivery temporariamente pausado."));

        if (request.OrderItemsSubtotal.Amount < MinimumOrderAmount.Amount)
        {
            return Result.Failure<DeliveryFreightQuote>(Error.Validation(
                "Delivery.MinimumOrderNotReached",
                $"Pedido mínimo de {MinimumOrderAmount} não atingido para delivery."));
        }

        // Verifica explicitamente regiões/CEPs bloqueados independentemente do modo de cálculo.
        var matchingZone = _zoneRules.FirstOrDefault(z => z.Matches(request.Neighborhood, request.ZipCode));
        if (matchingZone is not null && matchingZone.IsBlocked)
        {
            return Result.Failure<DeliveryFreightQuote>(Error.Validation(
                "Delivery.ZoneBlocked",
                $"Entrega bloqueada para a região '{matchingZone.ZoneName}'."));
        }

        decimal distanceKm = 0m;
        if (request.PrecomputedDistanceKm.HasValue)
        {
            if (request.PrecomputedDistanceKm.Value < 0m)
                return Result.Failure<DeliveryFreightQuote>(Error.Validation("Delivery.NegativeDistance", "Distância não pode ser negativa."));
            distanceKm = decimal.Round(request.PrecomputedDistanceKm.Value, 2, MidpointRounding.ToEven);
        }
        else if (request.DestinationLatitude.HasValue && request.DestinationLongitude.HasValue)
        {
            var destCoordRes = GeoCoordinate.Create(request.DestinationLatitude.Value, request.DestinationLongitude.Value);
            if (destCoordRes.IsFailure)
                return Result.Failure<DeliveryFreightQuote>(destCoordRes.Error);

            var originCoord = new GeoCoordinate(OriginLatitude, OriginLongitude);
            distanceKm = GeoDistanceCalculator.CalculateKilometers(originCoord, destCoordRes.Value);
        }
        else if (CalculationMode is FreightCalculationMode.DistanceTiered or FreightCalculationMode.FixedPlusPerKm)
        {
            return Result.Failure<DeliveryFreightQuote>(Error.Validation(
                "Delivery.DestinationCoordinatesRequired",
                "Coordenadas ou distância válida são obrigatórias para cálculo de frete por km."));
        }

        if (distanceKm > MaxRadiusKm)
        {
            return Result.Failure<DeliveryFreightQuote>(Error.Validation(
                "Delivery.OutOfCoverageRadius",
                $"Endereço a {distanceKm:N2} km excede o raio máximo de {MaxRadiusKm:N2} km."));
        }

        Money baseFee;
        int estimatedMinutes;
        string summary;
        var currency = BaseFee.Currency;

        switch (CalculationMode)
        {
            case FreightCalculationMode.FixedFlat:
                baseFee = BaseFee;
                estimatedMinutes = BaseEstimatedMinutes;
                summary = $"Taxa fixa ({distanceKm:N2} km)";
                break;

            case FreightCalculationMode.DistanceTiered:
                var tier = _distanceTiers
                    .OrderBy(t => t.MinKm)
                    .FirstOrDefault(t => t.Matches(distanceKm));
                if (tier is null)
                {
                    return Result.Failure<DeliveryFreightQuote>(Error.Validation(
                        "Delivery.NoMatchingDistanceTier",
                        $"Nenhuma faixa de frete configurada para {distanceKm:N2} km."));
                }
                baseFee = tier.Fee;
                estimatedMinutes = tier.EstimatedMinutes;
                summary = $"Faixa {tier.MinKm:N1}–{tier.MaxKm:N1} km";
                break;

            case FreightCalculationMode.FixedPlusPerKm:
                var billableKm = Math.Max(0m, distanceKm - IncludedKm);
                var amount = BaseFee.Amount + (billableKm * FeePerKm.Amount);
                baseFee = Money.Create(amount, currency).Value;
                estimatedMinutes = BaseEstimatedMinutes + (int)Math.Ceiling(distanceKm * 2.5m);
                summary = $"Base {BaseFee} + {billableKm:N2} km × {FeePerKm}";
                break;

            case FreightCalculationMode.ByNeighborhoodOrZone:
                if (matchingZone is null)
                {
                    return Result.Failure<DeliveryFreightQuote>(Error.Validation(
                        "Delivery.ZoneNotCovered",
                        "Bairro/CEP fora das zonas de atendimento configuradas."));
                }
                baseFee = matchingZone.Fee;
                estimatedMinutes = matchingZone.EstimatedMinutes;
                summary = $"Zona '{matchingZone.ZoneName}'";
                break;

            default:
                return Result.Failure<DeliveryFreightQuote>(Error.Validation("Delivery.UnsupportedMode", "Modo de cálculo de frete inválido."));
        }

        var isFree = FreeShippingThreshold is not null &&
                     FreeShippingThreshold.Amount > 0m &&
                     request.OrderItemsSubtotal.Amount >= FreeShippingThreshold.Amount;

        var finalFee = isFree ? Money.Zero(currency) : baseFee;
        if (isFree)
            summary += $" (Isenção automática a partir de {FreeShippingThreshold})";

        return Result.Success(new DeliveryFreightQuote(
            distanceKm,
            baseFee,
            finalFee,
            isFree,
            estimatedMinutes,
            summary));
    }
}
