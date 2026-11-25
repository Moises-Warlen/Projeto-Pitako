using Pitako.SharedKernel.Results;

namespace Pitako.SharedKernel.ValueObjects;

/// <summary>
/// Objeto de valor monetário com precisão fixa de 2 casas decimais e isolamento por moeda.
/// Evita erros de ponto flutuante e somas acidentais entre moedas distintas.
/// </summary>
public sealed class Money : ValueObject
{
    public const string DefaultCurrency = "BRL";

    private Money(decimal amount, string currency)
    {
        Amount = decimal.Round(amount, 2, MidpointRounding.ToEven);
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static Money Zero(string currency = DefaultCurrency) => new(0m, NormalizeCurrency(currency));

    public static Result<Money> Create(decimal amount, string currency = DefaultCurrency, bool allowNegative = false)
    {
        if (!allowNegative && amount < 0m)
            return Result.Failure<Money>(Error.Validation("Money.Negative", "Valor monetário não pode ser negativo."));

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            return Result.Failure<Money>(Error.Validation("Money.InvalidCurrency", "Código de moeda inválido (ISO 4217 esperado)."));

        return Result.Success(new Money(amount, NormalizeCurrency(currency)));
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Result<Money> Subtract(Money other, bool allowNegative = false)
    {
        EnsureSameCurrency(other);
        return Create(Amount - other.Amount, Currency, allowNegative);
    }

    public Money Multiply(decimal factor)
    {
        if (factor < 0m)
            throw new ArgumentOutOfRangeException(nameof(factor), "Fator multiplicador não pode ser negativo.");

        return new Money(Amount * factor, Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException($"Operação inválida entre moedas distintas: {Currency} e {other.Currency}.");
    }

    private static string NormalizeCurrency(string currency) => currency.Trim().ToUpperInvariant();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Currency} {Amount:F2}";
}
