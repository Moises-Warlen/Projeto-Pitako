using Pitako.Modules.DiningHall.Domain;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.DiningHall.Application.Abstractions;

public interface IQrTableSessionRepository
{
    Task<QrTableSession?> FindBySessionTokenAsync(string sessionToken, CancellationToken cancellationToken = default);
    Task<QrTableSession?> FindActiveByTableAsync(Guid diningTableId, CancellationToken cancellationToken = default);
    Task AddAsync(QrTableSession session, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IWaiterCallRepository
{
    Task<WaiterCall?> FindByIdAsync(Guid callId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WaiterCall>> ListPendingAsync(CancellationToken cancellationToken = default);
    Task AddAsync(WaiterCall waiterCall, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITotemTerminalRepository
{
    Task<TotemTerminal?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TotemTerminal?> FindByCodeAsync(string terminalCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TotemTerminal>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TotemTerminal terminal, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITotemOrderSessionRepository
{
    Task<TotemOrderSession?> FindBySessionTokenAsync(string sessionToken, CancellationToken cancellationToken = default);
    Task<TotemOrderSession?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<int> CountConfirmedAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TotemOrderSession session, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Identidade visual exclusiva da loja cliente exibida no QR Code de mesa e no Totem (nunca marca PITAKO).
/// </summary>
public sealed record SelfServiceStoreBranding(
    Guid TenantId,
    string StoreTradeName,
    string? LogoUrl,
    string PrimaryColorHex,
    string SecondaryColorHex,
    string AccentColorHex,
    bool UsesPitakoBranding = false);

public interface ISelfServiceBrandingProvider
{
    Task<SelfServiceStoreBranding?> GetBrandingAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed record WaiterCallRealtimeEvent(
    Guid TenantId,
    Guid WaiterCallId,
    Guid DiningTableId,
    string TableCode,
    WaiterCallType CallType,
    WaiterCallStatus Status,
    string? Notes,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Porta de notificação em tempo real para painel/aplicativo dos garçons (chamado na mesa ou solicitação de conta).
/// </summary>
public interface IWaiterCallRealtimeNotifier
{
    Task NotifyAsync(WaiterCallRealtimeEvent realtimeEvent, CancellationToken cancellationToken = default);
}

public sealed record SelfServiceDispatchedLine(
    Guid? ProductId,
    Guid? AcaiCupSizeId,
    string ItemTitle,
    string CustomizationSummary,
    Money UnitPrice,
    int Quantity,
    string? CustomerNotes);

public sealed record SelfServiceOrderDispatchRequest(
    Guid TenantId,
    string ChannelCode,
    string FulfillmentModeCode,
    string CustomerName,
    string? ReferenceTag,
    string? PickupCallCode,
    Guid? StoreUnitId,
    IReadOnlyList<SelfServiceDispatchedLine> Lines,
    Money ItemsSubtotal);

public sealed record SelfServiceDispatchedOrder(Guid OrderId, string OrderNumber);

/// <summary>
/// Porta de envio de pedidos de autoatendimento (QR Code / Totem) para a cozinha e painel Kanban.
/// </summary>
public interface ISelfServiceOrderDispatcher
{
    Task<Result<SelfServiceDispatchedOrder>> DispatchAsync(
        SelfServiceOrderDispatchRequest request,
        CancellationToken cancellationToken = default);
}
