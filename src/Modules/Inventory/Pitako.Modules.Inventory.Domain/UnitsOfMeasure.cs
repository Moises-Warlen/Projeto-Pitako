using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Inventory.Domain;

/// <summary>
/// Dimensão física ou contável para garantir que conversões ocorram somente entre unidades compatíveis (Seção 15).
/// </summary>
public enum UnitDimension
{
    Mass = 1,
    Volume = 2,
    Count = 3
}

/// <summary>
/// Definição de unidade de medida com fator relativo à unidade base da sua dimensão
/// (Massa -> g, Volume -> ml, Contagem -> unidade).
/// </summary>
public sealed record UnitDefinition(
    string Code,
    string Name,
    UnitDimension Dimension,
    decimal FactorToBaseUnit);

/// <summary>
/// Unidade configurável por estabelecimento para complementar o catálogo padrão
/// (ex.: caixa, pacote, balde, fardo com fator de conversão definido pela loja).
/// </summary>
public sealed class TenantCustomUnit : TenantScopedEntity
{
    private TenantCustomUnit()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private TenantCustomUnit(
        Guid id,
        Guid tenantId,
        string code,
        string name,
        UnitDimension dimension,
        decimal factorToBaseUnit) : base(id, tenantId)
    {
        Code = code;
        Name = name;
        Dimension = dimension;
        FactorToBaseUnit = factorToBaseUnit;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public UnitDimension Dimension { get; private set; }
    public decimal FactorToBaseUnit { get; private set; }

    public static Result<TenantCustomUnit> Create(
        Guid tenantId,
        string code,
        string name,
        UnitDimension dimension,
        decimal factorToBaseUnit)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantCustomUnit>(Error.Validation("Unit.TenantRequired", "TenantId é obrigatório."));

        var normalized = UnitConverter.NormalizeCode(code);
        if (string.IsNullOrWhiteSpace(normalized))
            return Result.Failure<TenantCustomUnit>(Error.Validation("Unit.CodeRequired", "Código da unidade é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<TenantCustomUnit>(Error.Validation("Unit.NameRequired", "Nome da unidade é obrigatório."));

        if (factorToBaseUnit <= 0m)
            return Result.Failure<TenantCustomUnit>(Error.Validation("Unit.InvalidFactor", "Fator de conversão deve ser positivo."));

        return Result.Success(new TenantCustomUnit(Guid.NewGuid(), tenantId, normalized, name.Trim(), dimension, factorToBaseUnit));
    }

    public UnitDefinition ToDefinition() => new(Code, Name, Dimension, FactorToBaseUnit);
}

/// <summary>
/// Conversor de unidades compatíveis para insumos, estoque e fichas técnicas (Seção 15).
/// </summary>
public static class UnitConverter
{
    private static readonly Dictionary<string, UnitDefinition> BuiltInUnits = new(StringComparer.Ordinal)
    {
        ["kg"] = new("kg", "Quilograma", UnitDimension.Mass, 1000m),
        ["g"] = new("g", "Grama", UnitDimension.Mass, 1m),
        ["mg"] = new("mg", "Miligrama", UnitDimension.Mass, 0.001m),
        ["litro"] = new("litro", "Litro", UnitDimension.Volume, 1000m),
        ["l"] = new("litro", "Litro", UnitDimension.Volume, 1000m),
        ["ml"] = new("ml", "Mililitro", UnitDimension.Volume, 1m),
        ["unidade"] = new("unidade", "Unidade", UnitDimension.Count, 1m),
        ["un"] = new("unidade", "Unidade", UnitDimension.Count, 1m),
        ["caixa"] = new("caixa", "Caixa", UnitDimension.Count, 1m),
        ["pacote"] = new("pacote", "Pacote", UnitDimension.Count, 1m)
    };

    public static string NormalizeCode(string? unitCode)
    {
        if (string.IsNullOrWhiteSpace(unitCode))
            return string.Empty;

        var trimmed = unitCode.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "l" or "lt" or "litros" => "litro",
            "un" or "und" or "unidades" => "unidade",
            "cx" => "caixa",
            "pct" => "pacote",
            _ => trimmed
        };
    }

    public static Result<UnitDefinition> Resolve(string unitCode, IReadOnlyDictionary<string, UnitDefinition>? customUnits = null)
    {
        var normalized = NormalizeCode(unitCode);
        if (string.IsNullOrWhiteSpace(normalized))
            return Result.Failure<UnitDefinition>(Error.Validation("Unit.Empty", "Unidade é obrigatória."));

        if (customUnits is not null && customUnits.TryGetValue(normalized, out var custom))
            return Result.Success(custom);

        if (BuiltInUnits.TryGetValue(normalized, out var builtIn))
            return Result.Success(builtIn);

        return Result.Failure<UnitDefinition>(Error.Validation("Unit.Unknown", $"Unidade '{unitCode}' não cadastrada."));
    }

    public static Result<decimal> Convert(
        decimal quantity,
        string fromUnitCode,
        string toUnitCode,
        IReadOnlyDictionary<string, UnitDefinition>? customUnits = null)
    {
        if (quantity < 0m)
            return Result.Failure<decimal>(Error.Validation("Unit.NegativeQuantity", "Quantidade não pode ser negativa."));

        var fromRes = Resolve(fromUnitCode, customUnits);
        if (fromRes.IsFailure)
            return Result.Failure<decimal>(fromRes.Error);

        var toRes = Resolve(toUnitCode, customUnits);
        if (toRes.IsFailure)
            return Result.Failure<decimal>(toRes.Error);

        if (fromRes.Value.Dimension != toRes.Value.Dimension)
        {
            return Result.Failure<decimal>(Error.Validation(
                "Units.Incompatible",
                $"Não é possível converter '{fromRes.Value.Code}' ({fromRes.Value.Dimension}) para '{toRes.Value.Code}' ({toRes.Value.Dimension})."));
        }

        var inBase = quantity * fromRes.Value.FactorToBaseUnit;
        var converted = decimal.Round(inBase / toRes.Value.FactorToBaseUnit, 6, MidpointRounding.ToEven);
        return Result.Success(converted);
    }
}
