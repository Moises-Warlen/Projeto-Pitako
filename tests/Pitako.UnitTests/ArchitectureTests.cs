using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Coupons.Application.Services;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Orders.Application.Services;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.PdvCashier.Application.Services;
using Pitako.Modules.PdvCashier.Domain;
using Pitako.Modules.Security.Application.Services;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.Weighing.Application.Services;
using Pitako.Modules.Weighing.Domain;
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.UnitTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DomainAndApplicationLayers_ShouldNotDependOnEntityFrameworkOrNpgsql()
    {
        var assemblies = new[]
        {
            typeof(Result).Assembly,
            typeof(Tenant).Assembly,
            typeof(TenantProvisioningService).Assembly,
            typeof(User).Assembly,
            typeof(AuthenticationService).Assembly,
            typeof(CommercialPlan).Assembly,
            typeof(MasterPlanAndSubscriptionService).Assembly,
            typeof(Product).Assembly,
            typeof(CatalogManagementService).Assembly,
            typeof(SupplyItem).Assembly,
            typeof(SuppliesAndTechnicalSheetService).Assembly,
            typeof(AcaiCupSize).Assembly,
            typeof(AcaiBuilderManagementService).Assembly,
            typeof(WeighingRecord).Assembly,
            typeof(WeighingService).Assembly,
            typeof(Coupon).Assembly,
            typeof(CouponsManagementService).Assembly,
            typeof(ShoppingCart).Assembly,
            typeof(ShoppingCartService).Assembly,
            typeof(Order).Assembly,
            typeof(OrderManagementService).Assembly,
            typeof(DeliveryPolicy).Assembly,
            typeof(DeliveryPolicyAndFreightService).Assembly,
            typeof(CashRegisterSession).Assembly,
            typeof(PdvCashierService).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
            Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(referenced, name => name.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase));
        }
    }
}
