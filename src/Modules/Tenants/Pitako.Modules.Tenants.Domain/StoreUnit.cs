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
    }

    private StoreUnit(Guid id, Guid tenantId, string name, string code, bool isHeadquarters)
        : base(id, tenantId)
    {
        Name = name;
        Code = code;
        IsHeadquarters = isHeadquarters;
        IsActive = true;
    }

    public string Name { get; private set; }
    public string Code { get; private set; }
    public bool IsHeadquarters { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<StoreUnit> Create(Guid tenantId, string name, string code, bool isHeadquarters = false)
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
            isHeadquarters));
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
