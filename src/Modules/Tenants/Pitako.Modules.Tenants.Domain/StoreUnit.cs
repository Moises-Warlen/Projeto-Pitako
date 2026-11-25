using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Tenants.Domain;

/// <summary>
/// Unidade operacional pertencente a um estabelecimento (<see cref="Tenant"/>).
/// Entidade estritamente isolada por <c>TenantId</c>.
/// </summary>
public sealed class StoreUnit : TenantScopedAggregateRoot
{
    private StoreUnit()
    {
        Name = string.Empty;
        Code = string.Empty;
        TimeZoneId = "America/Sao_Paulo";
    }

    private StoreUnit(Guid id, Guid tenantId, string name, string code, bool isHeadquarters, string? address, string? phone, string timeZoneId)
        : base(id, tenantId)
    {
        Name = name;
        Code = code;
        IsHeadquarters = isHeadquarters;
        Address = address;
        Phone = phone;
        TimeZoneId = timeZoneId;
        IsActive = true;
    }

    public string Name { get; private set; }
    public string Code { get; private set; }
    public bool IsHeadquarters { get; private set; }
    public bool IsActive { get; private set; }
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string TimeZoneId { get; private set; }

    public static Result<StoreUnit> Create(
        Guid tenantId,
        string name,
        string code,
        bool isHeadquarters = false,
        string? address = null,
        string? phone = null,
        string timeZoneId = "America/Sao_Paulo")
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<StoreUnit>(Error.Validation("StoreUnit.TenantRequired", "TenantId é obrigatório."));

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<StoreUnit>(Error.Validation("StoreUnit.NameRequired", "Nome da unidade é obrigatório."));

        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<StoreUnit>(Error.Validation("StoreUnit.CodeRequired", "Código da unidade é obrigatório."));

        return Result.Success(new StoreUnit(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            code.Trim().ToUpperInvariant(),
            isHeadquarters,
            string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            string.IsNullOrWhiteSpace(timeZoneId) ? "America/Sao_Paulo" : timeZoneId.Trim()));
    }

    public Result Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return Result.Failure(Error.Validation("StoreUnit.NameRequired", "Nome da unidade é obrigatório."));

        Name = newName.Trim();
        return Result.Success();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
