using Pitako.SharedKernel.Results;

namespace Pitako.SharedKernel.ValueObjects;

/// <summary>
/// Objeto de valor para CNPJ brasileiro com validação rigorosa dos dígitos verificadores (módulo 11).
/// Requisito obrigatório para identificação oficial de estabelecimentos e impressões (Seção 27).
/// </summary>
public sealed class Cnpj : ValueObject
{
    private static readonly int[] FirstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] SecondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    private Cnpj(string digits)
    {
        Digits = digits;
    }

    /// <summary>
    /// Os 14 dígitos numéricos normalizados do CNPJ (sem máscara).
    /// </summary>
    public string Digits { get; }

    /// <summary>
    /// Retorna o CNPJ formatado no padrão oficial XX.XXX.XXX/XXXX-XX.
    /// </summary>
    public string Formatted =>
        $"{Digits[..2]}.{Digits.Substring(2, 3)}.{Digits.Substring(5, 3)}/{Digits.Substring(8, 4)}-{Digits.Substring(12, 2)}";

    /// <summary>
    /// Valida e cria uma instância de <see cref="Cnpj"/> a partir de entrada com ou sem máscara.
    /// </summary>
    public static Result<Cnpj> Create(string? rawCnpj)
    {
        if (string.IsNullOrWhiteSpace(rawCnpj))
            return Result.Failure<Cnpj>(Error.Validation("CNPJ.Empty", "CNPJ é obrigatório."));

        var digits = new string(rawCnpj.Where(char.IsDigit).ToArray());

        if (digits.Length != 14)
            return Result.Failure<Cnpj>(Error.Validation("CNPJ.InvalidLength", "CNPJ deve conter exatamente 14 dígitos."));

        // Rejeita sequências repetidas como 00000000000000 ou 11111111111111.
        if (digits.Distinct().Count() == 1)
            return Result.Failure<Cnpj>(Error.Validation("CNPJ.RepeatedDigits", "CNPJ inválido."));

        if (!HasValidCheckDigits(digits))
            return Result.Failure<Cnpj>(Error.Validation("CNPJ.InvalidCheckDigits", "Dígitos verificadores do CNPJ são inválidos."));

        return Result.Success(new Cnpj(digits));
    }

    private static bool HasValidCheckDigits(string digits)
    {
        var firstDigit = CalculateDigit(digits, FirstWeights, 12);
        if (digits[12] - '0' != firstDigit)
            return false;

        var secondDigit = CalculateDigit(digits, SecondWeights, 13);
        return digits[13] - '0' == secondDigit;
    }

    private static int CalculateDigit(string digits, int[] weights, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += (digits[i] - '0') * weights[i];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Digits;
    }

    public override string ToString() => Formatted;
}
