namespace Pitako.SharedKernel.Abstractions;

/// <summary>
/// Representa um evento de domínio emitido por um agregado quando ocorre uma transição de estado relevante.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// Identificador único do evento para rastreabilidade e idempotência.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Instante UTC em que o evento ocorreu no domínio.
    /// </summary>
    DateTimeOffset OccurredAtUtc { get; }
}
