namespace Pitako.Modules.Security.Domain;

/// <summary>
/// Nível de atuação da conta de usuário na plataforma PITAKO (Seção 10).
/// </summary>
public enum UserScope
{
    Saas = 1,
    Establishment = 2
}

/// <summary>
/// Perfis administrativos no nível da operadora SaaS (Painel Master).
/// </summary>
public enum SaasRole
{
    MasterAdministrator = 1,
    Financial = 2,
    Commercial = 3,
    Support = 4,
    Technical = 5
}

/// <summary>
/// Perfis operacionais no nível de cada estabelecimento cliente.
/// </summary>
public enum EstablishmentRole
{
    Administrator = 1,
    Manager = 2,
    Cashier = 3,
    Attendant = 4,
    Waiter = 5,
    Kitchen = 6,
    Courier = 7,
    Stockist = 8,
    Financial = 9
}

/// <summary>
/// Ações granulares de autorização configuráveis por módulo (Seção 10).
/// </summary>
[Flags]
public enum PermissionAction
{
    None = 0,
    View = 1 << 0,
    Create = 1 << 1,
    Edit = 1 << 2,
    Delete = 1 << 3,
    Approve = 1 << 4,
    Cancel = 1 << 5,
    Administer = 1 << 6,
    Export = 1 << 7,
    Print = 1 << 8,
    All = View | Create | Edit | Delete | Approve | Cancel | Administer | Export | Print
}

/// <summary>
/// Códigos oficiais dos módulos do PITAKO para controle de acesso e habilitação por plano.
/// </summary>
public static class SystemModules
{
    public const string MasterPanel = "master_panel";
    public const string Tenants = "tenants";
    public const string Catalog = "catalog";
    public const string Acai = "acai";
    public const string Weighing = "weighing";
    public const string Inventory = "inventory";
    public const string Coupons = "coupons";
    public const string Orders = "orders";
    public const string Pdv = "pdv";
    public const string DiningHall = "dining_hall";
    public const string Delivery = "delivery";
    public const string Marketplaces = "marketplaces";
    public const string Printing = "printing";
    public const string WhatsApp = "whatsapp";
    public const string Reports = "reports";
    public const string Finance = "finance";
    public const string Security = "security";
    public const string DataLifecycle = "data_lifecycle";

    public static readonly IReadOnlyList<string> EstablishmentModules =
    [
        Tenants, Catalog, Acai, Weighing, Inventory, Coupons, Orders, Pdv,
        DiningHall, Delivery, Marketplaces, Printing, WhatsApp, Reports,
        Finance, Security, DataLifecycle
    ];

    public static bool IsSaasOnlyModule(string moduleCode) =>
        string.Equals(Normalize(moduleCode), MasterPanel, StringComparison.Ordinal);

    public static string Normalize(string moduleCode) =>
        string.IsNullOrWhiteSpace(moduleCode) ? string.Empty : moduleCode.Trim().ToLowerInvariant();
}

/// <summary>
/// Catálogo padrão de permissões aplicando o princípio do menor privilégio por perfil.
/// </summary>
public static class RolePermissionCatalog
{
    public static PermissionAction GetDefaultPermissions(SaasRole role, string moduleCode)
    {
        var normalized = SystemModules.Normalize(moduleCode);
        return role switch
        {
            SaasRole.MasterAdministrator => PermissionAction.All,
            SaasRole.Financial when normalized is SystemModules.MasterPanel or SystemModules.Finance or SystemModules.Reports
                => PermissionAction.View | PermissionAction.Approve | PermissionAction.Export,
            SaasRole.Commercial when normalized is SystemModules.MasterPanel or SystemModules.Tenants
                => PermissionAction.View | PermissionAction.Create | PermissionAction.Edit,
            SaasRole.Support when normalized is SystemModules.MasterPanel or SystemModules.Tenants
                => PermissionAction.View | PermissionAction.Edit,
            SaasRole.Technical when normalized is SystemModules.MasterPanel or SystemModules.Security or SystemModules.DataLifecycle
                => PermissionAction.View | PermissionAction.Administer | PermissionAction.Export,
            _ => PermissionAction.None
        };
    }

    public static PermissionAction GetDefaultPermissions(EstablishmentRole role, string moduleCode)
    {
        var normalized = SystemModules.Normalize(moduleCode);
        if (SystemModules.IsSaasOnlyModule(normalized))
            return PermissionAction.None;

        return role switch
        {
            EstablishmentRole.Administrator => PermissionAction.All,
            EstablishmentRole.Manager => normalized == SystemModules.Security
                ? PermissionAction.View | PermissionAction.Create | PermissionAction.Edit
                : PermissionAction.View | PermissionAction.Create | PermissionAction.Edit | PermissionAction.Approve | PermissionAction.Cancel | PermissionAction.Export | PermissionAction.Print,
            EstablishmentRole.Cashier => normalized switch
            {
                SystemModules.Pdv => PermissionAction.View | PermissionAction.Create | PermissionAction.Edit | PermissionAction.Cancel | PermissionAction.Print,
                SystemModules.Orders => PermissionAction.View | PermissionAction.Create | PermissionAction.Edit | PermissionAction.Print,
                SystemModules.Catalog or SystemModules.Acai or SystemModules.Coupons or SystemModules.Weighing => PermissionAction.View,
                SystemModules.Printing => PermissionAction.View | PermissionAction.Print,
                _ => PermissionAction.None
            },
            EstablishmentRole.Attendant => normalized switch
            {
                SystemModules.Orders => PermissionAction.View | PermissionAction.Create | PermissionAction.Edit | PermissionAction.Print,
                SystemModules.Catalog or SystemModules.Acai or SystemModules.Coupons or SystemModules.WhatsApp => PermissionAction.View | PermissionAction.Create,
                SystemModules.Printing => PermissionAction.View | PermissionAction.Print,
                _ => PermissionAction.None
            },
            EstablishmentRole.Waiter => normalized switch
            {
                SystemModules.DiningHall => PermissionAction.View | PermissionAction.Create | PermissionAction.Edit | PermissionAction.Print,
                SystemModules.Orders => PermissionAction.View | PermissionAction.Create | PermissionAction.Edit,
                SystemModules.Catalog or SystemModules.Acai => PermissionAction.View,
                _ => PermissionAction.None
            },
            EstablishmentRole.Kitchen => normalized switch
            {
                SystemModules.Orders => PermissionAction.View | PermissionAction.Edit,
                SystemModules.Printing => PermissionAction.View | PermissionAction.Print,
                _ => PermissionAction.None
            },
            EstablishmentRole.Courier => normalized switch
            {
                SystemModules.Delivery => PermissionAction.View | PermissionAction.Edit,
                SystemModules.Orders => PermissionAction.View,
                _ => PermissionAction.None
            },
            EstablishmentRole.Stockist => normalized switch
            {
                SystemModules.Inventory => PermissionAction.View | PermissionAction.Create | PermissionAction.Edit | PermissionAction.Export,
                SystemModules.Catalog or SystemModules.Acai => PermissionAction.View,
                _ => PermissionAction.None
            },
            EstablishmentRole.Financial => normalized switch
            {
                SystemModules.Finance or SystemModules.Reports => PermissionAction.View | PermissionAction.Create | PermissionAction.Edit | PermissionAction.Approve | PermissionAction.Export | PermissionAction.Print,
                SystemModules.Pdv or SystemModules.Orders or SystemModules.Coupons => PermissionAction.View | PermissionAction.Export,
                _ => PermissionAction.None
            },
            _ => PermissionAction.None
        };
    }
}
