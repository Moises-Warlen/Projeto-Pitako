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
        string legalName,
        string tradeName,
        string slug,
        Cnpj cnpj,
        string segmentCode,
        TenantStatus status) : base(id)
    {
        LegalName = legalName;
        TradeName = tradeName;
        Slug = slug;
        Cnpj = cnpj;
        SegmentCode = segmentCode;
        Status = status;
    }

    public string LegalName { get; private set; }
    public string TradeName { get; private set; }
    public string Slug { get; private set; }
    public Cnpj Cnpj { get; private set; }
    public string SegmentCode { get; private set; }
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Cria um novo estabelecimento garantindo validação de CNPJ e normalização de slug.
    /// </summary>
    public static Result<Tenant> Create(
        string legalName,
        string tradeName,
        string slug,
        string rawCnpj,
        string segmentCode,
        TenantStatus initialStatus = TenantStatus.Trial)
    {
        if (string.IsNullOrWhiteSpace(legalName))
            return Result.Failure<Tenant>(Error.Validation("Tenant.LegalNameRequired", "Razão social é obrigatória."));

        if (string.IsNullOrWhiteSpace(tradeName))
            return Result.Failure<Tenant>(Error.Validation("Tenant.TradeNameRequired", "Nome fantasia é obrigatório."));

        var normalizedSlug = NormalizeSlug(slug);
        if (string.IsNullOrWhiteSpace(normalizedSlug))
            return Result.Failure<Tenant>(Error.Validation("Tenant.SlugRequired", "Slug do estabelecimento é obrigatório."));

        if (string.IsNullOrWhiteSpace(segmentCode))
            return Result.Failure<Tenant>(Error.Validation("Tenant.SegmentRequired", "Segmento comercial é obrigatório."));

        var cnpjResult = Cnpj.Create(rawCnpj);
        if (cnpjResult.IsFailure)
            return Result.Failure<Tenant>(cnpjResult.Error);

        return Result.Success(new Tenant(
            Guid.NewGuid(),
            legalName.Trim(),
            tradeName.Trim(),
            normalizedSlug,
            cnpjResult.Value,
            segmentCode.Trim().ToLowerInvariant(),
            initialStatus));
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
