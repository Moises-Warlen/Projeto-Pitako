using Pitako.Modules.Printing.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Printing.Application.Abstractions;

public interface IPrinterProfileRepository
{
    Task<PrinterProfile?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PrinterProfile?> FindByCodeAsync(string printerCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrinterProfile>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(PrinterProfile printer, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISectorRoutingRuleRepository
{
    Task<SectorRoutingRule?> FindMatchingRuleAsync(ProductionSectorType sectorType, Guid? categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SectorRoutingRule>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task AddAsync(SectorRoutingRule rule, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPrintJobRepository
{
    Task<PrintJob?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrintJob>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task AddAsync(PrintJob job, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Fornece logo, razão social, nome fantasia e CNPJ exclusivos do estabelecimento cliente para cabeçalho de impressão.
/// </summary>
public interface IStorePrintIdentityProvider
{
    Task<Result<PrintableStoreIdentity>> GetStorePrintIdentityAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Porta física de envio ESC/POS para impressoras térmicas TCP/USB/Serial.
/// </summary>
public interface IThermalPrinterTransport
{
    Task<Result> SendEscPosAsync(PrinterProfile printer, byte[] escPosPayload, int copies, CancellationToken cancellationToken = default);
}
