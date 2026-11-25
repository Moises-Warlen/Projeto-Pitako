using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Printing.Domain;

public enum ProductionSectorType
{
    Kitchen = 1,
    Counter = 2,
    Production = 3,
    Bar = 4,
    Cashier = 5
}

public enum PrinterConnectionKind
{
    NetworkTcpEscPos = 1,
    UsbSpooler = 2,
    SerialEscPos = 3
}

public enum PrinterStatus
{
    Online = 1,
    Offline = 2,
    Paused = 3
}

public enum PaperWidthMm
{
    Mm58 = 58,
    Mm80 = 80
}

/// <summary>
/// Cabeçalho visual e fiscal obrigatório do estabelecimento cliente para impressões operacionais.
/// Garante que logo e CNPJ pertençam exclusivamente à loja contratante, nunca à operadora PITAKO (Seção 27).
/// </summary>
public sealed record PrintableStoreIdentity
{
    private PrintableStoreIdentity(
        Guid tenantId,
        string tradeName,
        string legalName,
        Cnpj cnpj,
        string logoReference,
        string? address,
        string? phone)
    {
        TenantId = tenantId;
        TradeName = tradeName;
        LegalName = legalName;
        Cnpj = cnpj;
        LogoReference = logoReference;
        Address = address;
        Phone = phone;
    }

    public Guid TenantId { get; }
    public string TradeName { get; }
    public string LegalName { get; }
    public Cnpj Cnpj { get; }
    public string LogoReference { get; }
    public string? Address { get; }
    public string? Phone { get; }

    public static Result<PrintableStoreIdentity> Create(
        Guid tenantId,
        string tradeName,
        string legalName,
        string rawCnpj,
        string? logoReference,
        string? address = null,
        string? phone = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PrintableStoreIdentity>(Error.Validation("Print.TenantRequired", "TenantId é obrigatório na impressão."));
        if (string.IsNullOrWhiteSpace(tradeName) || string.IsNullOrWhiteSpace(legalName))
            return Result.Failure<PrintableStoreIdentity>(Error.Validation("Print.StoreNameRequired", "Nome fantasia e razão social da loja são obrigatórios na impressão."));
        if (string.IsNullOrWhiteSpace(logoReference))
            return Result.Failure<PrintableStoreIdentity>(Error.Validation("Print.StoreLogoRequired", "Logo da loja é obrigatória para cabeçalho de impressão."));

        var cnpjRes = Cnpj.Create(rawCnpj);
        if (cnpjRes.IsFailure)
            return Result.Failure<PrintableStoreIdentity>(cnpjRes.Error);

        if (ContainsPitakoBranding(tradeName) || ContainsPitakoBranding(legalName) || ContainsPitakoBranding(logoReference))
            return Result.Failure<PrintableStoreIdentity>(Error.Validation(
                "Print.PitakoBrandingForbidden",
                "Impressões da loja devem usar exclusivamente a logo, marca e CNPJ do estabelecimento cliente, nunca da PITAKO."));

        return Result.Success(new PrintableStoreIdentity(
            tenantId,
            tradeName.Trim(),
            legalName.Trim(),
            cnpjRes.Value,
            logoReference.Trim(),
            string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            string.IsNullOrWhiteSpace(phone) ? null : phone.Trim()));
    }

    private static bool ContainsPitakoBranding(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return normalized is "PITAKO" or "PITAKO SAAS" ||
               normalized.Contains("PITAKO-LOGO", StringComparison.Ordinal) ||
               normalized.Contains("PITAKO_LOGO", StringComparison.Ordinal) ||
               normalized.Contains("PITAKO.PNG", StringComparison.Ordinal) ||
               normalized.Contains("PITAKO.SVG", StringComparison.Ordinal);
    }
}

/// <summary>
/// Perfil de impressora térmica cadastrada para o estabelecimento/unidade (Seção 27).
/// </summary>
public sealed class PrinterProfile : TenantScopedAggregateRoot
{
    private PrinterProfile()
    {
        PrinterCode = string.Empty;
        DisplayName = string.Empty;
        EndpointOrPort = string.Empty;
    }

    private PrinterProfile(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        string printerCode,
        string displayName,
        PrinterConnectionKind connectionKind,
        string endpointOrPort,
        PaperWidthMm paperWidthMm,
        Guid? backupPrinterId,
        bool supportsLogoRaster) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        PrinterCode = printerCode;
        DisplayName = displayName;
        ConnectionKind = connectionKind;
        EndpointOrPort = endpointOrPort;
        PaperWidthMm = paperWidthMm;
        BackupPrinterId = backupPrinterId;
        SupportsLogoRaster = supportsLogoRaster;
        Status = PrinterStatus.Online;
    }

    public Guid? StoreUnitId { get; private set; }
    public string PrinterCode { get; private set; }
    public string DisplayName { get; private set; }
    public PrinterConnectionKind ConnectionKind { get; private set; }
    public string EndpointOrPort { get; private set; }
    public PaperWidthMm PaperWidthMm { get; private set; }
    public PrinterStatus Status { get; private set; }
    public Guid? BackupPrinterId { get; private set; }
    public bool SupportsLogoRaster { get; private set; }

    public int ColumnWidth => PaperWidthMm == PaperWidthMm.Mm58 ? 32 : 48;

    public static Result<PrinterProfile> Create(
        Guid tenantId,
        string printerCode,
        string displayName,
        PrinterConnectionKind connectionKind,
        string endpointOrPort,
        PaperWidthMm paperWidthMm = PaperWidthMm.Mm80,
        Guid? backupPrinterId = null,
        bool supportsLogoRaster = true,
        Guid? storeUnitId = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PrinterProfile>(Error.Validation("Printer.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(printerCode))
            return Result.Failure<PrinterProfile>(Error.Validation("Printer.CodeRequired", "Código da impressora é obrigatório."));
        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<PrinterProfile>(Error.Validation("Printer.NameRequired", "Nome da impressora é obrigatório."));
        if (string.IsNullOrWhiteSpace(endpointOrPort))
            return Result.Failure<PrinterProfile>(Error.Validation("Printer.EndpointRequired", "IP/porta ou fila da impressora é obrigatória."));

        var id = Guid.NewGuid();
        if (backupPrinterId == id)
            return Result.Failure<PrinterProfile>(Error.Validation("Printer.InvalidBackup", "Impressora reserva não pode apontar para ela mesma."));

        return Result.Success(new PrinterProfile(
            id,
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            printerCode.Trim().ToUpperInvariant(),
            displayName.Trim(),
            connectionKind,
            endpointOrPort.Trim(),
            paperWidthMm,
            backupPrinterId == Guid.Empty ? null : backupPrinterId,
            supportsLogoRaster));
    }

    public Result SetBackupPrinter(Guid? backupPrinterId)
    {
        if (backupPrinterId == Id)
            return Result.Failure(Error.Validation("Printer.InvalidBackup", "Impressora reserva não pode ser a própria impressora."));

        BackupPrinterId = backupPrinterId == Guid.Empty ? null : backupPrinterId;
        return Result.Success();
    }

    public void SetStatus(PrinterStatus status) => Status = status;
}

/// <summary>
/// Regra de roteamento de impressão por setor de produção (e opcionalmente por categoria de produto) (Seção 27).
/// </summary>
public sealed class SectorRoutingRule : TenantScopedAggregateRoot
{
    private SectorRoutingRule()
    {
    }

    private SectorRoutingRule(
        Guid id,
        Guid tenantId,
        Guid? storeUnitId,
        ProductionSectorType sectorType,
        Guid? categoryId,
        Guid primaryPrinterId,
        Guid? fallbackPrinterId,
        int copiesCount,
        bool autoPrintOnOrderAccepted) : base(id, tenantId)
    {
        StoreUnitId = storeUnitId;
        SectorType = sectorType;
        CategoryId = categoryId;
        PrimaryPrinterId = primaryPrinterId;
        FallbackPrinterId = fallbackPrinterId;
        CopiesCount = copiesCount;
        AutoPrintOnOrderAccepted = autoPrintOnOrderAccepted;
        IsActive = true;
    }

    public Guid? StoreUnitId { get; private set; }
    public ProductionSectorType SectorType { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid PrimaryPrinterId { get; private set; }
    public Guid? FallbackPrinterId { get; private set; }
    public int CopiesCount { get; private set; }
    public bool AutoPrintOnOrderAccepted { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<SectorRoutingRule> Create(
        Guid tenantId,
        ProductionSectorType sectorType,
        Guid primaryPrinterId,
        Guid? fallbackPrinterId = null,
        Guid? categoryId = null,
        int copiesCount = 1,
        bool autoPrintOnOrderAccepted = true,
        Guid? storeUnitId = null)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SectorRoutingRule>(Error.Validation("Routing.TenantRequired", "TenantId é obrigatório."));
        if (primaryPrinterId == Guid.Empty)
            return Result.Failure<SectorRoutingRule>(Error.Validation("Routing.PrimaryRequired", "Impressora principal é obrigatória."));
        if (fallbackPrinterId == primaryPrinterId)
            return Result.Failure<SectorRoutingRule>(Error.Validation("Routing.SameFallback", "Impressora reserva deve ser diferente da principal."));
        if (copiesCount is < 1 or > 5)
            return Result.Failure<SectorRoutingRule>(Error.Validation("Routing.InvalidCopies", "Quantidade de vias deve estar entre 1 e 5."));

        return Result.Success(new SectorRoutingRule(
            Guid.NewGuid(),
            tenantId,
            storeUnitId == Guid.Empty ? null : storeUnitId,
            sectorType,
            categoryId == Guid.Empty ? null : categoryId,
            primaryPrinterId,
            fallbackPrinterId == Guid.Empty ? null : fallbackPrinterId,
            copiesCount,
            autoPrintOnOrderAccepted));
    }

    public Result UpdatePrinters(Guid primaryPrinterId, Guid? fallbackPrinterId, int copiesCount)
    {
        if (primaryPrinterId == Guid.Empty)
            return Result.Failure(Error.Validation("Routing.PrimaryRequired", "Impressora principal é obrigatória."));
        if (fallbackPrinterId == primaryPrinterId)
            return Result.Failure(Error.Validation("Routing.SameFallback", "Impressora reserva deve ser diferente da principal."));
        if (copiesCount is < 1 or > 5)
            return Result.Failure(Error.Validation("Routing.InvalidCopies", "Quantidade de vias deve estar entre 1 e 5."));

        PrimaryPrinterId = primaryPrinterId;
        FallbackPrinterId = fallbackPrinterId == Guid.Empty ? null : fallbackPrinterId;
        CopiesCount = copiesCount;
        return Result.Success();
    }
}
