using Pitako.SharedKernel.Abstractions;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Tenants.Domain;

/// <summary>
/// Situação operacional da assinatura/conta do estabelecimento na plataforma PITAKO.
/// </summary>
public enum TenantStatus
{
    Trial = 1,
    Active = 2,
    Suspended = 3,
    Cancelled = 4
}

/// <summary>
/// Agregado raiz que representa um estabelecimento contratante (cliente SaaS) no PITAKO.
/// </summary>
public sealed class Tenant : AggregateRoot<Guid>, IAuditableEntity
{
    private Tenant()
    {
        LegalName = string.Empty;
        TradeName = string.Empty;
        Slug = string.Empty;
        SegmentCode = string.Empty;
        Cnpj = null!;
    }

    private Tenant(
        Guid id,
        Guid? customerAccountId,
        string legalName,
        string tradeName,
        string slug,
        Cnpj cnpj,
        string segmentCode,
        TenantStatus status,
        string? address,
        string? contactPhone,
        string? contactEmail) : base(id)
    {
        CustomerAccountId = customerAccountId;
        LegalName = legalName;
        TradeName = tradeName;
        Slug = slug;
        Cnpj = cnpj;
        SegmentCode = segmentCode;
        Status = status;
        Address = address;
        ContactPhone = contactPhone;
        ContactEmail = contactEmail;
    }

    public Guid? CustomerAccountId { get; private set; }
    public string LegalName { get; private set; }
    public string TradeName { get; private set; }
    public string Slug { get; private set; }
    public Cnpj Cnpj { get; private set; }
    public string SegmentCode { get; private set; }
    public TenantStatus Status { get; private set; }
    public string? Address { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? ContactEmail { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Cria um novo estabelecimento garantindo validação de CNPJ, segmento comercial e normalização de slug.
    /// </summary>
    public static Result<Tenant> Create(
        string legalName,
        string tradeName,
        string slug,
        string rawCnpj,
        string segmentCode,
        TenantStatus initialStatus = TenantStatus.Trial,
        Guid? customerAccountId = null,
        string? address = null,
        string? contactPhone = null,
        string? contactEmail = null)
    {
        if (string.IsNullOrWhiteSpace(legalName))
            return Result.Failure<Tenant>(Error.Validation("Tenant.LegalNameRequired", "Razão social é obrigatória."));

        if (string.IsNullOrWhiteSpace(tradeName))
            return Result.Failure<Tenant>(Error.Validation("Tenant.TradeNameRequired", "Nome fantasia é obrigatório."));

        var normalizedSlug = NormalizeSlug(slug);
        if (string.IsNullOrWhiteSpace(normalizedSlug))
            return Result.Failure<Tenant>(Error.Validation("Tenant.SlugRequired", "Slug do estabelecimento é obrigatório."));

        var segmentResult = BusinessSegmentCatalog.Resolve(segmentCode);
        if (segmentResult.IsFailure)
            return Result.Failure<Tenant>(segmentResult.Error);

        var cnpjResult = Cnpj.Create(rawCnpj);
        if (cnpjResult.IsFailure)
            return Result.Failure<Tenant>(cnpjResult.Error);

        return Result.Success(new Tenant(
            Guid.NewGuid(),
            customerAccountId == Guid.Empty ? null : customerAccountId,
            legalName.Trim(),
            tradeName.Trim(),
            normalizedSlug,
            cnpjResult.Value,
            segmentResult.Value.Code,
            initialStatus,
            string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim(),
            string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim().ToLowerInvariant()));
    }

    public Result UpdateOfficialDetails(string legalName, string tradeName, string? address, string? contactPhone, string? contactEmail)
    {
        if (string.IsNullOrWhiteSpace(legalName) || string.IsNullOrWhiteSpace(tradeName))
            return Result.Failure(Error.Validation("Tenant.NamesRequired", "Razão social e nome fantasia são obrigatórios."));

        LegalName = legalName.Trim();
        TradeName = tradeName.Trim();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim();
        ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim().ToLowerInvariant();
        return Result.Success();
    }

    public void Suspend() => Status = TenantStatus.Suspended;
    public void Activate() => Status = TenantStatus.Active;

    public void SetCreatedAudit(DateTimeOffset timestampUtc)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = timestampUtc;
    }

    public void SetUpdatedAudit(DateTimeOffset timestampUtc) => UpdatedAtUtc = timestampUtc;

    private static string NormalizeSlug(string slug) =>
        string.IsNullOrWhiteSpace(slug)
            ? string.Empty
            : new string(slug.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
}
