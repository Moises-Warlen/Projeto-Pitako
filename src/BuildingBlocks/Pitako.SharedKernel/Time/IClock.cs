namespace Pitako.SharedKernel.Time;

/// <summary>
/// Abstração de relógio para garantir previsibilidade e testabilidade em regras temporais (cupons, sessões, auditoria).
/// </summary>
public interface IClock
{
    /// <summary>
    /// Retorna a data e hora atual em UTC.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Implementação padrão de relógio baseada no relógio do sistema em UTC.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
