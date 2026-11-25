using System.Security.Cryptography;
using System.Text;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.DataLifecycle.Domain;

public enum LgpdRequestType
{
    DataPortabilityExport = 1,
    DataAnonymizationErasure = 2
}

public enum LgpdRequestStatus
{
    Completed = 1,
    Rejected = 2
}

/// <summary>
/// Utilitário determinístico de mascaramento e pseudonimização LGPD mantendo integridade referencial/fiscal.
/// </summary>
public static class LgpdAnonymizer
{
    public static string GenerateAnonymousToken(Guid tenantId, string subjectIdentifier)
    {
        var input = $"{tenantId:N}:{subjectIdentifier.Trim().ToLowerInvariant()}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return $"ANON-{hash[..12]}";
    }

    public static string MaskIdentifier(string subjectIdentifier)
    {
        var clean = subjectIdentifier.Trim();
        if (clean.Length <= 4)
            return "****";

        return $"{clean[..2]}***{clean[^2..]}";
    }
}

/// <summary>
/// Registro auditável de atendimentos à LGPD por titular dentro de um estabelecimento (portabilidade e anonimização) (Seção 31).
/// </summary>
public sealed class LgpdDataRequest : TenantScopedAggregateRoot
{
    private LgpdDataRequest()
    {
        SubjectHash = string.Empty;
        SubjectMasked = string.Empty;
        ReasonOrNotes = string.Empty;
    }

    private LgpdDataRequest(
        Guid id,
        Guid tenantId,
        LgpdRequestType requestType,
        string subjectHash,
        string subjectMasked,
        string reasonOrNotes,
        string? exportedPortabilityJson,
        int anonymizedRecordsCount,
        Guid processedByUserId,
        DateTimeOffset processedAtUtc) : base(id, tenantId)
    {
        RequestType = requestType;
        SubjectHash = subjectHash;
        SubjectMasked = subjectMasked;
        ReasonOrNotes = reasonOrNotes;
        ExportedPortabilityJson = exportedPortabilityJson;
        AnonymizedRecordsCount = anonymizedRecordsCount;
        ProcessedByUserId = processedByUserId;
        ProcessedAtUtc = processedAtUtc;
        Status = LgpdRequestStatus.Completed;
    }

    public LgpdRequestType RequestType { get; private set; }
    public LgpdRequestStatus Status { get; private set; }
    public string SubjectHash { get; private set; }
    public string SubjectMasked { get; private set; }
    public string ReasonOrNotes { get; private set; }
    public string? ExportedPortabilityJson { get; private set; }
    public int AnonymizedRecordsCount { get; private set; }
    public Guid ProcessedByUserId { get; private set; }
    public DateTimeOffset ProcessedAtUtc { get; private set; }

    public static Result<LgpdDataRequest> RecordCompleted(
        Guid tenantId,
        LgpdRequestType requestType,
        string rawSubjectIdentifier,
        string reasonOrNotes,
        string? exportedPortabilityJson,
        int anonymizedRecordsCount,
        Guid processedByUserId,
        DateTimeOffset processedAtUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<LgpdDataRequest>(Error.Validation("Lgpd.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(rawSubjectIdentifier))
            return Result.Failure<LgpdDataRequest>(Error.Validation("Lgpd.SubjectRequired", "Identificador do titular (e-mail/telefone/CPF) é obrigatório."));
        if (processedByUserId == Guid.Empty)
            return Result.Failure<LgpdDataRequest>(Error.Validation("Lgpd.UserRequired", "Operador responsável é obrigatório."));

        var subjectHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}:{rawSubjectIdentifier.Trim().ToLowerInvariant()}"))).ToLowerInvariant();
        var masked = LgpdAnonymizer.MaskIdentifier(rawSubjectIdentifier);

        return Result.Success(new LgpdDataRequest(
            Guid.NewGuid(),
            tenantId,
            requestType,
            subjectHash,
            masked,
            string.IsNullOrWhiteSpace(reasonOrNotes) ? requestType.ToString() : reasonOrNotes.Trim(),
            exportedPortabilityJson,
            Math.Max(0, anonymizedRecordsCount),
            processedByUserId,
            processedAtUtc));
    }
}
