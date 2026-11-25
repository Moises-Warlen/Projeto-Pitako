namespace Pitako.SharedKernel.Abstractions;

/// <summary>
/// Classe base para entidades de domínio com identidade fortemente tipada.
/// </summary>
/// <typeparam name="TId">Tipo do identificador único da entidade.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>
    /// Construtor protegido exigido por ORMs e serializadores.
    /// </summary>
    protected Entity()
    {
        Id = default!;
    }

    /// <summary>
    /// Inicializa a entidade com um identificador explícito.
    /// </summary>
    protected Entity(TId id)
    {
        Id = id;
    }

    /// <summary>
    /// Identificador único da entidade.
    /// </summary>
    public TId Id { get; protected init; }

    /// <inheritdoc />
    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TId> entity && Equals(entity);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}

/// <summary>
/// Classe base para raízes de agregado (Aggregate Roots) capazes de acumular eventos de domínio.
/// </summary>
/// <typeparam name="TId">Tipo do identificador único do agregado.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(TId id) : base(id)
    {
    }

    /// <summary>
    /// Eventos de domínio pendentes de despacho após persistência transacional.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Registra um novo evento de domínio para processamento após confirmação da transação.
    /// </summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Limpa os eventos acumulados após despacho pelo barramento interno/Outbox.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
