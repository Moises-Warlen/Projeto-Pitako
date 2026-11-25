using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Weighing.Domain;

public sealed record WeightPriceCalculation(
    decimal GrossWeightGrams,
    decimal TareWeightGrams,
    decimal NetWeightGrams,
    decimal NetWeightKilograms,
    Money PricePerKilogram,
    Money TotalPrice);

/// <summary>
/// Calculadora determinística de peso líquido e valor financeiro por quilo (Seção 14).
/// </summary>
public static class WeightPriceCalculator
{
    public static Result<WeightPriceCalculation> Calculate(
        decimal grossWeightGrams,
        decimal tareWeightGrams,
        Money pricePerKilogram)
    {
        ArgumentNullException.ThrowIfNull(pricePerKilogram);

        if (grossWeightGrams <= 0m)
            return Result.Failure<WeightPriceCalculation>(Error.Validation("Weighing.NonPositiveGross", "Peso bruto deve ser maior que zero."));

        if (tareWeightGrams < 0m)
            return Result.Failure<WeightPriceCalculation>(Error.Validation("Weighing.NegativeTare", "Tara não pode ser negativa."));

        var roundedGross = decimal.Round(grossWeightGrams, 3, MidpointRounding.ToEven);
        var roundedTare = decimal.Round(tareWeightGrams, 3, MidpointRounding.ToEven);
        var netGrams = decimal.Round(roundedGross - roundedTare, 3, MidpointRounding.ToEven);

        if (netGrams <= 0m)
        {
            return Result.Failure<WeightPriceCalculation>(Error.Validation(
                "Weighing.NetWeightMustBePositive",
                $"Peso líquido inválido ({netGrams:N3} g): peso bruto deve superar a tara."));
        }

        var netKg = decimal.Round(netGrams / 1000m, 4, MidpointRounding.ToEven);
        var totalAmount = (netGrams / 1000m) * pricePerKilogram.Amount;
        var totalPrice = Money.Create(totalAmount, pricePerKilogram.Currency).Value;

        return Result.Success(new WeightPriceCalculation(
            roundedGross,
            roundedTare,
            netGrams,
            netKg,
            pricePerKilogram,
            totalPrice));
    }
}

/// <summary>
/// Registro auditável de pesagem automática (balança) ou manual (contingência) com operador e horário (Seção 14).
/// </summary>
public sealed class WeighingRecord : TenantScopedAggregateRoot
{
    private WeighingRecord()
    {
        ProductNameSnapshot = string.Empty;
        PricePerKilogram = Money.Zero();
        TotalPrice = Money.Zero();
    }

    private WeighingRecord(
        Guid id,
        Guid tenantId,
        Guid? scaleDeviceId,
        Guid? tarePresetId,
        Guid? productId,
        string productNameSnapshot,
        WeighingCaptureMode captureMode,
        decimal grossWeightGrams,
        decimal tareWeightGrams,
        decimal netWeightGrams,
        Money pricePerKilogram,
        Money totalPrice,
        Guid operatorUserId,
        DateTimeOffset capturedAtUtc,
        string? manualFallbackReason,
        string? rawScalePayload) : base(id, tenantId)
    {
        ScaleDeviceId = scaleDeviceId;
        TarePresetId = tarePresetId;
        ProductId = productId;
        ProductNameSnapshot = productNameSnapshot;
        CaptureMode = captureMode;
        GrossWeightGrams = grossWeightGrams;
        TareWeightGrams = tareWeightGrams;
        NetWeightGrams = netWeightGrams;
        PricePerKilogram = Money.Create(pricePerKilogram.Amount, pricePerKilogram.Currency).Value;
        TotalPrice = Money.Create(totalPrice.Amount, totalPrice.Currency).Value;
        OperatorUserId = operatorUserId;
        CapturedAtUtc = capturedAtUtc;
        ManualFallbackReason = manualFallbackReason;
        RawScalePayload = rawScalePayload;
    }

    public Guid? ScaleDeviceId { get; private set; }
    public Guid? TarePresetId { get; private set; }
    public Guid? ProductId { get; private set; }
    public string ProductNameSnapshot { get; private set; }
    public WeighingCaptureMode CaptureMode { get; private set; }
    public decimal GrossWeightGrams { get; private set; }
    public decimal TareWeightGrams { get; private set; }
    public decimal NetWeightGrams { get; private set; }
    public Money PricePerKilogram { get; private set; }
    public Money TotalPrice { get; private set; }
    public Guid OperatorUserId { get; private set; }
    public DateTimeOffset CapturedAtUtc { get; private set; }
    public string? ManualFallbackReason { get; private set; }
    public string? RawScalePayload { get; private set; }
    public Guid? LinkedOrderId { get; private set; }

    public static Result<WeighingRecord> Create(
        Guid tenantId,
        Guid operatorUserId,
        string productNameSnapshot,
        Money pricePerKilogram,
        decimal grossWeightGrams,
        decimal tareWeightGrams,
        WeighingCaptureMode captureMode,
        DateTimeOffset capturedAtUtc,
        Guid? scaleDeviceId = null,
        Guid? tarePresetId = null,
        Guid? productId = null,
        string? manualFallbackReason = null,
        string? rawScalePayload = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<WeighingRecord>(Error.Validation("Weighing.TenantRequired", "TenantId é obrigatório."));
        if (operatorUserId == Guid.Empty)
            return Result.Failure<WeighingRecord>(Error.Validation("Weighing.OperatorRequired", "Operador responsável é obrigatório."));
        if (string.IsNullOrWhiteSpace(productNameSnapshot))
            return Result.Failure<WeighingRecord>(Error.Validation("Weighing.ProductNameRequired", "Descrição do item pesado é obrigatória."));
        if (captureMode == WeighingCaptureMode.ManualFallback && string.IsNullOrWhiteSpace(manualFallbackReason))
            return Result.Failure<WeighingRecord>(Error.Validation("Weighing.ManualReasonRequired", "Motivo do lançamento manual é obrigatório para auditoria."));

        var calcRes = WeightPriceCalculator.Calculate(grossWeightGrams, tareWeightGrams, pricePerKilogram);
        if (calcRes.IsFailure)
            return Result.Failure<WeighingRecord>(calcRes.Error);

        var calc = calcRes.Value;
        return Result.Success(new WeighingRecord(
            Guid.NewGuid(),
            tenantId,
            scaleDeviceId == Guid.Empty ? null : scaleDeviceId,
            tarePresetId == Guid.Empty ? null : tarePresetId,
            productId == Guid.Empty ? null : productId,
            productNameSnapshot.Trim(),
            captureMode,
            calc.GrossWeightGrams,
            calc.TareWeightGrams,
            calc.NetWeightGrams,
            calc.PricePerKilogram,
            calc.TotalPrice,
            operatorUserId,
            capturedAtUtc,
            string.IsNullOrWhiteSpace(manualFallbackReason) ? null : manualFallbackReason.Trim(),
            string.IsNullOrWhiteSpace(rawScalePayload) ? null : rawScalePayload.Trim()));
    }

    public Result AttachToOrder(Guid orderId)
    {
        if (orderId == Guid.Empty)
            return Result.Failure(Error.Validation("Weighing.InvalidOrder", "OrderId é obrigatório."));
        if (LinkedOrderId.HasValue && LinkedOrderId.Value != orderId)
            return Result.Failure(Error.Conflict("Weighing.AlreadyLinked", "Pesagem já vinculada a outro pedido."));

        LinkedOrderId = orderId;
        return Result.Success();
    }
}
