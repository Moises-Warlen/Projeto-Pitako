using System.Text;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Printing.Domain;

public enum PrintJobStatus
{
    Pending = 1,
    Printed = 2,
    PrintedWithFallback = 3,
    Failed = 4,
    Cancelled = 5
}

public enum PrintDocumentKind
{
    ProductionKitchenTicket = 1,
    CounterPickupConference = 2,
    DiningTabSummarySlip = 3,
    OrderReceiptNonFiscal = 4
}

public sealed record TicketLineInput(
    string ItemTitle,
    int Quantity,
    string CustomizationSummary,
    string? CustomerNotes = null,
    decimal? LineSubtotalAmount = null);

public sealed record ThermalTicketPayloadInput(
    ProductionSectorType SectorType,
    PrintDocumentKind DocumentKind,
    string OrderOrTabReference,
    string ChannelName,
    string CustomerName,
    string? TableOrCallCode,
    string? StaffOperatorName,
    DateTimeOffset EmittedAtUtc,
    IReadOnlyList<TicketLineInput> Lines,
    decimal? TotalAmount = null,
    int CopiesCount = 1);

/// <summary>
/// Renderizador determinístico de comandas internas e cupons operacionais não-fiscais
/// em texto alinhado (32/48 colunas) e quadro binário ESC/POS (Seção 27).
/// </summary>
public static class ThermalTicketRenderer
{
    public static (string PlainText, string EscPosBase64) Render(
        PrintableStoreIdentity store,
        ThermalTicketPayloadInput payload,
        int columnWidth)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(payload);

        var cols = columnWidth >= 48 ? 48 : 32;
        var sep = new string('-', cols);
        var sb = new StringBuilder();

        sb.AppendLine(CenterText(store.TradeName.ToUpperInvariant(), cols));
        sb.AppendLine(CenterText(store.LegalName, cols));
        sb.AppendLine(CenterText($"CNPJ: {store.Cnpj.Formatted}", cols));
        sb.AppendLine(CenterText($"[LOGO LOJA: {Truncate(store.LogoReference, Math.Max(8, cols - 13))}]", cols));
        if (!string.IsNullOrWhiteSpace(store.Address))
            sb.AppendLine(CenterText(Truncate(store.Address, cols), cols));
        if (!string.IsNullOrWhiteSpace(store.Phone))
            sb.AppendLine(CenterText($"Tel: {store.Phone}", cols));

        sb.AppendLine(sep);
        sb.AppendLine(CenterText($"*** SETOR: {SectorTitle(payload.SectorType)} ***", cols));
        sb.AppendLine(CenterText(DocumentTitle(payload.DocumentKind), cols));
        sb.AppendLine(sep);

        sb.AppendLine($"Ref: {payload.OrderOrTabReference} | Canal: {payload.ChannelName}");
        sb.AppendLine($"Cliente: {payload.CustomerName}");
        if (!string.IsNullOrWhiteSpace(payload.TableOrCallCode))
            sb.AppendLine($"Mesa/Senha: {payload.TableOrCallCode}");
        if (!string.IsNullOrWhiteSpace(payload.StaffOperatorName))
            sb.AppendLine($"Atendente: {payload.StaffOperatorName}");
        sb.AppendLine($"Data: {payload.EmittedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine(sep);

        foreach (var line in payload.Lines)
        {
            var priceText = line.LineSubtotalAmount.HasValue ? $" R$ {line.LineSubtotalAmount.Value:N2}" : string.Empty;
            sb.AppendLine($"{line.Quantity}x {line.ItemTitle}{priceText}");
            if (!string.IsNullOrWhiteSpace(line.CustomizationSummary))
                sb.AppendLine($"   > {line.CustomizationSummary}");
            if (!string.IsNullOrWhiteSpace(line.CustomerNotes))
                sb.AppendLine($"   !! OBS: {line.CustomerNotes.ToUpperInvariant()} !!");
        }

        if (payload.TotalAmount.HasValue)
        {
            sb.AppendLine(sep);
            sb.AppendLine($"TOTAL OPERACIONAL: R$ {payload.TotalAmount.Value:N2}");
        }

        sb.AppendLine(sep);
        sb.AppendLine(CenterText("DOCUMENTO OPERACIONAL INTERNO", cols));
        sb.AppendLine(CenterText("NAO SUBSTITUI DOCUMENTO FISCAL", cols));

        var plainText = sb.ToString();
        var escPosBytes = BuildEscPosPacket(plainText);
        return (plainText, Convert.ToBase64String(escPosBytes));
    }

    private static byte[] BuildEscPosPacket(string plainText)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(plainText);
        using var ms = new MemoryStream(bodyBytes.Length + 16);
        ms.Write([0x1B, 0x40]);       // ESC @ (Initialize printer)
        ms.Write([0x1B, 0x61, 0x00]); // ESC a 0 (Left align default)
        ms.Write(bodyBytes);
        ms.Write([0x0A, 0x0A, 0x0A]); // Feed 3 lines
        ms.Write([0x1D, 0x56, 0x01]); // GS V 1 (Partial cut)
        return ms.ToArray();
    }

    private static string CenterText(string text, int cols)
    {
        var clean = text.Trim();
        if (clean.Length >= cols)
            return clean[..cols];

        var pad = (cols - clean.Length) / 2;
        return new string(' ', pad) + clean;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen];

    private static string SectorTitle(ProductionSectorType sector) => sector switch
    {
        ProductionSectorType.Kitchen => "COZINHA",
        ProductionSectorType.Counter => "BALCAO",
        ProductionSectorType.Production => "PRODUCAO / ACAI",
        ProductionSectorType.Bar => "BAR / BEBIDAS",
        ProductionSectorType.Cashier => "CAIXA",
        _ => "PRODUCAO"
    };

    private static string DocumentTitle(PrintDocumentKind kind) => kind switch
    {
        PrintDocumentKind.ProductionKitchenTicket => "COMANDA DE PRODUCAO",
        PrintDocumentKind.CounterPickupConference => "CONFERENCIA DE RETIRADA",
        PrintDocumentKind.DiningTabSummarySlip => "CONFERENCIA DE MESA / COMANDA",
        _ => "CUPOM OPERACIONAL NAO FISCAL"
    };
}

/// <summary>
/// Trabalho de impressão térmica rastreável com suporte a fallback entre impressoras e reimpressão (Seção 27).
/// </summary>
public sealed class PrintJob : TenantScopedAggregateRoot
{
    private PrintJob()
    {
        OrderOrTabReference = string.Empty;
        StoreTradeName = string.Empty;
        StoreCnpj = string.Empty;
        StoreLogoRef = string.Empty;
        RenderedPlainText = string.Empty;
        EscPosPayloadBase64 = string.Empty;
    }

    private PrintJob(
        Guid id,
        Guid tenantId,
        ProductionSectorType sectorType,
        PrintDocumentKind documentKind,
        string orderOrTabReference,
        Guid targetPrinterId,
        string storeTradeName,
        string storeCnpj,
        string storeLogoRef,
        string renderedPlainText,
        string escPosPayloadBase64,
        int copiesCount,
        DateTimeOffset createdAtUtc) : base(id, tenantId)
    {
        SectorType = sectorType;
        DocumentKind = documentKind;
        OrderOrTabReference = orderOrTabReference;
        TargetPrinterId = targetPrinterId;
        StoreTradeName = storeTradeName;
        StoreCnpj = storeCnpj;
        StoreLogoRef = storeLogoRef;
        RenderedPlainText = renderedPlainText;
        EscPosPayloadBase64 = escPosPayloadBase64;
        CopiesCount = copiesCount;
        Status = PrintJobStatus.Pending;
        AttemptCount = 0;
        JobCreatedAtUtc = createdAtUtc;
    }

    public ProductionSectorType SectorType { get; private set; }
    public PrintDocumentKind DocumentKind { get; private set; }
    public string OrderOrTabReference { get; private set; }
    public Guid TargetPrinterId { get; private set; }
    public Guid? ActualPrinterId { get; private set; }
    public bool UsedFallback { get; private set; }
    public string? FallbackReason { get; private set; }
    public string StoreTradeName { get; private set; }
    public string StoreCnpj { get; private set; }
    public string StoreLogoRef { get; private set; }
    public string RenderedPlainText { get; private set; }
    public string EscPosPayloadBase64 { get; private set; }
    public int CopiesCount { get; private set; }
    public PrintJobStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset JobCreatedAtUtc { get; private set; }
    public DateTimeOffset? PrintedAtUtc { get; private set; }

    public static Result<PrintJob> Create(
        Guid tenantId,
        PrintableStoreIdentity storeIdentity,
        PrinterProfile targetPrinter,
        ThermalTicketPayloadInput payload,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(storeIdentity);
        ArgumentNullException.ThrowIfNull(targetPrinter);
        ArgumentNullException.ThrowIfNull(payload);

        if (tenantId == Guid.Empty)
            return Result.Failure<PrintJob>(Error.Validation("PrintJob.TenantRequired", "TenantId é obrigatório."));
        if (storeIdentity.TenantId != tenantId || targetPrinter.TenantId != tenantId)
            return Result.Failure<PrintJob>(Error.Forbidden("PrintJob.CrossTenant", "Dados de impressão pertencem a outro estabelecimento."));
        if (string.IsNullOrWhiteSpace(payload.OrderOrTabReference))
            return Result.Failure<PrintJob>(Error.Validation("PrintJob.RefRequired", "Referência do pedido ou comanda é obrigatória."));
        if (payload.Lines.Count == 0)
            return Result.Failure<PrintJob>(Error.Validation("PrintJob.EmptyLines", "Não é possível imprimir comanda sem itens."));

        var copies = Math.Clamp(payload.CopiesCount, 1, 5);
        var (plainText, escPosBase64) = ThermalTicketRenderer.Render(storeIdentity, payload, targetPrinter.ColumnWidth);

        return Result.Success(new PrintJob(
            Guid.NewGuid(),
            tenantId,
            payload.SectorType,
            payload.DocumentKind,
            payload.OrderOrTabReference.Trim(),
            targetPrinter.Id,
            storeIdentity.TradeName,
            storeIdentity.Cnpj.Formatted,
            storeIdentity.LogoReference,
            plainText,
            escPosBase64,
            copies,
            utcNow));
    }

    public void MarkPrinted(Guid actualPrinterId, bool usedFallback, string? fallbackReason, DateTimeOffset utcNow)
    {
        AttemptCount++;
        ActualPrinterId = actualPrinterId;
        UsedFallback = usedFallback;
        FallbackReason = string.IsNullOrWhiteSpace(fallbackReason) ? null : fallbackReason.Trim();
        LastError = null;
        Status = usedFallback ? PrintJobStatus.PrintedWithFallback : PrintJobStatus.Printed;
        PrintedAtUtc = utcNow;
    }

    public void MarkFailed(string errorMessage)
    {
        AttemptCount++;
        Status = PrintJobStatus.Failed;
        LastError = string.IsNullOrWhiteSpace(errorMessage) ? "Falha desconhecida de impressão." : errorMessage.Trim();
    }

    public Result Cancel()
    {
        if (Status is PrintJobStatus.Printed or PrintJobStatus.PrintedWithFallback)
            return Result.Failure(Error.Conflict("PrintJob.AlreadyPrinted", "Trabalho já impresso não pode ser cancelado."));

        Status = PrintJobStatus.Cancelled;
        return Result.Success();
    }
}
