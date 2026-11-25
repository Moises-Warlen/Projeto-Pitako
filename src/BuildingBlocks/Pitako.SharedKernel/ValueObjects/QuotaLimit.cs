using Pitako.SharedKernel.Results;

namespace Pitako.SharedKernel.ValueObjects;

/// <summary>
/// Representa um limite operacional ou comercial de um plano SaaS.
/// Quando ilimitado (<see cref="IsUnlimited"/> = true), nenhum teto numérico artificial é aplicado na regra de negócio (Seção 7).
/// </summary>
public sealed class QuotaLimit : ValueObject
{
    private QuotaLimit(bool isUnlimited, int? maxAllowed)
    {
        IsUnlimited = isUnlimited;
        MaxAllowed = maxAllowed;
    }

    /// <summary>
    /// Indica se o recurso possui operação comercial ilimitada neste plano.
    /// </summary>
    public bool IsUnlimited { get; }

    /// <summary>
    /// Limite máximo quando finito; sempre nulo quando <see cref="IsUnlimited"/> for verdadeiro.
    /// </summary>
    public int? MaxAllowed { get; }

    /// <summary>
    /// Cria um limite comercial ilimitado sem utilizar números mágicos ou tetos artificiais.
    /// </summary>
    public static QuotaLimit Unlimited() => new(true, null);

    /// <summary>
    /// Cria um limite finito configurável pelo administrador Master.
    /// </summary>
    public static Result<QuotaLimit> Limited(int maxAllowed)
    {
        if (maxAllowed < 0)
            return Result.Failure<QuotaLimit>(Error.Validation("QuotaLimit.Negative", "O limite do plano não pode ser negativo."));

        return Result.Success(new QuotaLimit(false, maxAllowed));
    }

    /// <summary>
    /// Verifica se uma nova unidade do recurso pode ser alocada dado o consumo atual.
    /// </summary>
    /// <param name="currentUsage">Quantidade atualmente utilizada pelo cliente.</param>
    /// <param name="requestedIncrement">Incremento solicitado (padrão 1).</param>
    public bool CanAllocate(int currentUsage, int requestedIncrement = 1)
    {
        if (currentUsage < 0)
            throw new ArgumentOutOfRangeException(nameof(currentUsage), "Consumo atual não pode ser negativo.");
        if (requestedIncrement <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedIncrement), "Incremento solicitado deve ser positivo.");

        // Regra explícita da Seção 7: plano ilimitado remove o limite comercial sem teto artificial.
        if (IsUnlimited)
            return true;

        return currentUsage + requestedIncrement <= MaxAllowed!.Value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IsUnlimited;
        yield return MaxAllowed;
    }

    public override string ToString() => IsUnlimited ? "Ilimitado" : MaxAllowed!.Value.ToString();
}
