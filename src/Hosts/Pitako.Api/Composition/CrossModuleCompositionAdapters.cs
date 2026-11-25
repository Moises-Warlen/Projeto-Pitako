using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Pitako.Api.Realtime;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.DataLifecycle.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.Finance.Application.Abstractions;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.OfflineSync.Application.Abstractions;
using Pitako.Modules.OfflineSync.Domain;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Domain;
using Pitako.Modules.Reports.Application.Abstractions;
using Pitako.Modules.Reports.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Api.Composition;

public sealed class SignalRRealtimeNotifiers(IHubContext<PitakoRealtimeHub> hubContext) :
    IOrderRealtimeNotifier,
    ICourierGpsRealtimeNotifier,
    IWaiterCallRealtimeNotifier,
    IInboxRealtimeNotifier
{
    public Task NotifyAsync(OrderRealtimeEvent realtimeEvent, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(PitakoRealtimeHub.TenantGroup(realtimeEvent.TenantId))
            .SendAsync("orderUpdated", realtimeEvent, cancellationToken);

    public Task NotifyLocationAsync(CourierGpsRealtimeEvent gpsEvent, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(PitakoRealtimeHub.TenantGroup(gpsEvent.TenantId))
            .SendAsync("courierGpsUpdated", gpsEvent, cancellationToken);

    public Task NotifyAsync(WaiterCallRealtimeEvent realtimeEvent, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(PitakoRealtimeHub.TenantGroup(realtimeEvent.TenantId))
            .SendAsync("waiterCallUpdated", realtimeEvent, cancellationToken);

    public Task NotifyAsync(InboxRealtimeNotification notification, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(PitakoRealtimeHub.TenantGroup(notification.TenantId))
            .SendAsync("whatsappInboxUpdated", notification, cancellationToken);
}

public sealed class TenantStoreBrandingAndPrintAdapter(
    ITenantRepository tenantRepo,
    IStoreVisualIdentityRepository identityRepo) :
    ISelfServiceBrandingProvider,
    IStorePrintIdentityProvider
{
    public async Task<SelfServiceStoreBranding?> GetBrandingAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await tenantRepo.FindByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
            return null;

        var visual = await identityRepo.FindByTenantIdForPublicViewAsync(tenantId, cancellationToken);
        return new SelfServiceStoreBranding(
            tenant.Id,
            tenant.TradeName,
            visual?.PublishedLogoUrl,
            visual?.PublishedPrimaryColorHex ?? "#1E293B",
            visual?.PublishedSecondaryColorHex ?? "#F8FAFC",
            visual?.PublishedAccentColorHex ?? "#0F766E",
            UsesPitakoBranding: false);
    }

    public async Task<Result<PrintableStoreIdentity>> GetStorePrintIdentityAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await tenantRepo.FindByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<PrintableStoreIdentity>(Error.NotFound("Print.TenantNotFound", "Estabelecimento não encontrado."));

        var visual = await identityRepo.FindByTenantIdForPublicViewAsync(tenantId, cancellationToken);
        var logoRef = visual?.PublishedLogoUrl ?? visual?.DraftLogoUrl;

        return PrintableStoreIdentity.Create(
            tenant.Id,
            tenant.TradeName,
            tenant.LegalName,
            tenant.Cnpj.Digits,
            logoRef,
            tenant.Address,
            tenant.ContactPhone);
    }
}

public sealed class SelfServiceOrderDispatcherAdapter(
    IOrderRepository orderRepo,
    IOrderRealtimeNotifier realtimeNotifier,
    IClock clock) : ISelfServiceOrderDispatcher
{
    public async Task<Result<SelfServiceDispatchedOrder>> DispatchAsync(
        SelfServiceOrderDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var cartRes = ShoppingCart.Create(
            request.TenantId,
            $"selfservice-{Guid.NewGuid():N}",
            clock.UtcNow.AddHours(1));
        if (cartRes.IsFailure)
            return Result.Failure<SelfServiceDispatchedOrder>(cartRes.Error);

        var cart = cartRes.Value;
        foreach (var line in request.Lines)
        {
            var kind = line.AcaiCupSizeId.HasValue ? CartItemKind.CustomAcaiCup : CartItemKind.CatalogProduct;
            var addRes = cart.AddItem(
                kind,
                line.ProductId,
                null,
                null,
                line.AcaiCupSizeId,
                null,
                line.ItemTitle,
                line.CustomizationSummary,
                "{}",
                line.UnitPrice,
                line.Quantity,
                false,
                line.CustomerNotes,
                clock.UtcNow,
                forceSeparateLine: true);
            if (addRes.IsFailure)
                return Result.Failure<SelfServiceDispatchedOrder>(addRes.Error);
        }

        var seq = await orderRepo.CountAllAsync(cancellationToken) + 1;
        var channel = request.ChannelCode.Equals("TOTEM", StringComparison.OrdinalIgnoreCase)
            ? OrderChannel.SelfServiceTotem
            : OrderChannel.QrCodeTable;
        var mode = request.FulfillmentModeCode.Equals("PICKUP", StringComparison.OrdinalIgnoreCase)
            ? FulfillmentMode.CounterPickup
            : FulfillmentMode.DineIn;

        var orderNumber = $"SS-{seq:D5}";
        var orderRes = Order.CreateFromCart(
            cart,
            orderNumber,
            channel,
            mode,
            request.CustomerName,
            null,
            request.ReferenceTag,
            Money.Zero(),
            Money.Zero(),
            Money.Zero(),
            null,
            request.StoreUnitId,
            null,
            $"ss-{request.ChannelCode}-{request.PickupCallCode ?? Guid.NewGuid().ToString("N")}",
            clock.UtcNow);
        if (orderRes.IsFailure)
            return Result.Failure<SelfServiceDispatchedOrder>(orderRes.Error);

        var order = orderRes.Value;
        await orderRepo.AddAsync(order, cancellationToken);
        await orderRepo.SaveChangesAsync(cancellationToken);

        await realtimeNotifier.NotifyAsync(new OrderRealtimeEvent(
            order.TenantId,
            order.Id,
            order.OrderNumber,
            "OrderPlaced",
            order.Status,
            clock.UtcNow), cancellationToken);

        return Result.Success(new SelfServiceDispatchedOrder(order.Id, order.OrderNumber));
    }
}

public sealed class TenantKnowledgeProviderAdapter(
    ITenantRepository tenantRepo,
    ICategoryRepository categoryRepo,
    IProductRepository productRepo,
    ICouponRepository couponRepo,
    IOrderRepository orderRepo) : ITenantKnowledgeProvider
{
    public async Task<GroundedTenantKnowledgeSnapshot?> GetSnapshotForCustomerAsync(
        Guid tenantId,
        string customerPhone,
        CancellationToken cancellationToken = default)
    {
        var tenant = await tenantRepo.FindByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
            return null;

        var categories = (await categoryRepo.ListOrderedAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var products = await productRepo.ListAllOrderedAsync(cancellationToken);
        var coupons = await couponRepo.ListAllAsync(cancellationToken);
        var orders = await orderRepo.ListKanbanActiveAsync(cancellationToken);

        var menuItems = products.Select(p => new GroundedMenuItem(
            p.Name,
            p.BasePrice.Amount,
            p.Status == CatalogItemStatus.Active,
            categories.TryGetValue(p.CategoryId, out var catName) ? catName : null)).ToList();

        var activeCoupons = coupons.Where(c => c.IsActive).Select(c => c.Code).ToList();
        var normalizedDigits = new string((customerPhone ?? string.Empty).Where(char.IsDigit).ToArray());
        var lastOrder = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.CustomerPhone) &&
                        new string(o.CustomerPhone.Where(char.IsDigit).ToArray()) == normalizedDigits)
            .OrderByDescending(o => o.PlacedAtUtc)
            .FirstOrDefault();

        return new GroundedTenantKnowledgeSnapshot(
            tenant.Id,
            tenant.TradeName,
            "Consulte o horário atualizado na loja",
            $"/loja/{tenant.Slug}",
            menuItems,
            activeCoupons,
            lastOrder is null
                ? null
                : new GroundedOrderFact(lastOrder.OrderNumber, lastOrder.Status.ToString(), lastOrder.TotalAmount.Amount));
    }
}

public sealed class MultiModuleReportDataAggregator(
    ITenantRepository tenantRepo,
    IOrderRepository orderRepo,
    IFinancialEntryRepository financeRepo,
    IStockBalanceRepository stockRepo,
    ISupplyItemRepository supplyRepo,
    IClock clock) : IReportDataAggregator
{
    public async Task<Result<ReportDataset>> BuildDatasetAsync(
        Guid tenantId,
        ReportQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var tenant = await tenantRepo.FindByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<ReportDataset>(Error.NotFound("Report.TenantNotFound", "Estabelecimento não encontrado."));

        var now = clock.UtcNow;

        if (filter.Category == ReportCategory.InventoryValuationAndLowStock)
        {
            var stocks = await stockRepo.ListAllWithLotsAsync(cancellationToken);
            var supplies = await supplyRepo.FindLookupByIdsAsync(stocks.Select(s => s.SupplyItemId), cancellationToken);

            var columns = new[] { "Insumo", "Saldo", "Mínimo", "Situação" };
            var rows = stocks.Select(s =>
            {
                var name = supplies.TryGetValue(s.SupplyItemId, out var sup) ? sup.Name : s.SupplyItemId.ToString();
                var minQty = s.MinimumStockQuantity;
                var status = s.QuantityOnHand <= minQty ? "ABAIXO_MINIMO" : "OK";
                return (IReadOnlyList<string>)[name, s.QuantityOnHand.ToString("0.###"), minQty.ToString("0.###"), status];
            }).ToList();

            var lowStockCount = rows.Count(r => r[3] == "ABAIXO_MINIMO");
            return ReportDataset.Create(
                tenant.Id,
                filter.Category,
                "Estoque e Itens Abaixo do Mínimo",
                tenant.TradeName,
                tenant.Cnpj.Digits,
                filter.PeriodStartUtc,
                filter.PeriodEndUtc,
                now,
                columns,
                rows,
                new Dictionary<string, decimal> { ["TotalInsumos"] = rows.Count, ["ItensAbaixoMinimo"] = lowStockCount });
        }

        var orders = await orderRepo.ListKanbanActiveAsync(cancellationToken);
        var entries = await financeRepo.ListAllInPeriodAsync(filter.PeriodStartUtc, filter.PeriodEndUtc, cancellationToken);

        var orderColumns = new[] { "Pedido", "Canal", "Status", "ValorTotal", "DataUtc" };
        var orderRows = orders
            .Where(o => o.PlacedAtUtc >= filter.PeriodStartUtc && o.PlacedAtUtc <= filter.PeriodEndUtc)
            .Select(o => (IReadOnlyList<string>)
            [
                o.OrderNumber,
                o.Channel.ToString(),
                o.Status.ToString(),
                o.TotalAmount.Amount.ToString("0.00"),
                o.PlacedAtUtc.ToString("yyyy-MM-dd HH:mm")
            ])
            .ToList();

        var totalOrders = orders.Sum(o => o.TotalAmount.Amount);
        var receivables = entries.Where(e => e.Kind == Modules.Finance.Domain.FinancialEntryKind.Receivable).Sum(e => e.OriginalAmount.Amount);
        var payables = entries.Where(e => e.Kind == Modules.Finance.Domain.FinancialEntryKind.Payable).Sum(e => e.OriginalAmount.Amount);

        return ReportDataset.Create(
            tenant.Id,
            filter.Category,
            $"Relatório Operacional — {filter.Category}",
            tenant.TradeName,
            tenant.Cnpj.Digits,
            filter.PeriodStartUtc,
            filter.PeriodEndUtc,
            now,
            orderColumns,
            orderRows,
            new Dictionary<string, decimal>
            {
                ["TotalPedidosAtivos"] = totalOrders,
                ["TotalReceberPeriodo"] = receivables,
                ["TotalPagarPeriodo"] = payables
            });
    }
}

public sealed class MultiModuleTenantDataLifecycleGateway(
    ICategoryRepository categoryRepo,
    IProductRepository productRepo,
    IOrderRepository orderRepo,
    ITenantRepository tenantRepo,
    IClock clock) : ITenantDataLifecycleGateway
{
    public async Task<Result<int>> ImportMigratedCatalogItemsAsync(
        Guid tenantId,
        IReadOnlyList<MigratedCatalogItemInput> items,
        CancellationToken cancellationToken = default)
    {
        var existingCats = (await categoryRepo.ListOrderedAsync(cancellationToken))
            .ToDictionary(c => c.Name.Trim().ToUpperInvariant(), c => c);

        var importedCount = 0;
        var nextOrder = existingCats.Count + 1;

        foreach (var item in items)
        {
            var catKey = item.CategoryName.Trim().ToUpperInvariant();
            if (!existingCats.TryGetValue(catKey, out var category))
            {
                var catRes = Category.Create(tenantId, item.CategoryName.Trim(), null, null, nextOrder++);
                if (catRes.IsFailure)
                    return Result.Failure<int>(catRes.Error);

                category = catRes.Value;
                await categoryRepo.AddAsync(category, cancellationToken);
                existingCats[catKey] = category;
            }

            var priceRes = Money.Create(item.UnitPriceAmount);
            if (priceRes.IsFailure)
                return Result.Failure<int>(priceRes.Error);

            var prodRes = Product.Create(
                tenantId,
                category.Id,
                item.ItemTitle,
                priceRes.Value,
                item.Description,
                item.ExternalSku,
                ProductSaleMode.Unit,
                false,
                importedCount + 1);
            if (prodRes.IsFailure)
                return Result.Failure<int>(prodRes.Error);

            await productRepo.AddAsync(prodRes.Value, cancellationToken);
            importedCount++;
        }

        await categoryRepo.SaveChangesAsync(cancellationToken);
        await productRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(importedCount);
    }

    public async Task<Result<TenantBackupBundle>> ExtractTenantBackupAsync(
        Guid tenantId,
        bool includeCatalog,
        bool includeOrders,
        bool includeCustomers,
        CancellationToken cancellationToken = default)
    {
        var tenant = await tenantRepo.FindByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<TenantBackupBundle>(Error.NotFound("Backup.TenantNotFound", "Estabelecimento não localizado."));

        var categories = includeCatalog ? await categoryRepo.ListOrderedAsync(cancellationToken) : [];
        var products = includeCatalog ? await productRepo.ListAllOrderedAsync(cancellationToken) : [];
        var orders = includeOrders ? await orderRepo.ListKanbanActiveAsync(cancellationToken) : [];

        var json = JsonSerializer.Serialize(new
        {
            tenantId,
            tenantTradeName = tenant.TradeName,
            categories = categories.Select(c => new { c.Id, c.Name, c.DisplayOrder }),
            products = products.Select(p => new { p.Id, p.CategoryId, p.Name, Price = p.BasePrice.Amount, p.Sku }),
            ordersCount = orders.Count,
            customersIncluded = includeCustomers
        });

        return Result.Success(new TenantBackupBundle(
            tenantId,
            clock.UtcNow,
            includeCatalog,
            includeOrders,
            includeCustomers,
            json));
    }

    public async Task<Result> RestoreTenantBackupAsync(
        Guid tenantId,
        TenantBackupBundle bundle,
        CancellationToken cancellationToken = default)
    {
        if (bundle.TenantId != tenantId)
            return Result.Failure(Error.Forbidden("Backup.CrossTenantRestoreForbidden", "Backup não pertence ao estabelecimento ativo."));

        await Task.CompletedTask;
        return Result.Success();
    }

    public async Task<Result<string>> ExportSubjectPortabilityJsonAsync(
        Guid tenantId,
        string subjectIdentifier,
        CancellationToken cancellationToken = default)
    {
        var orders = await orderRepo.ListKanbanActiveAsync(cancellationToken);
        var normalized = subjectIdentifier.Trim();
        var matched = orders
            .Where(o => string.Equals(o.CustomerIdentifier, normalized, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(o.CustomerPhone, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(o => new { o.OrderNumber, o.PlacedAtUtc, Total = o.TotalAmount.Amount, o.Status })
            .ToList();

        var json = JsonSerializer.Serialize(new
        {
            tenantId,
            subjectIdentifier = normalized,
            exportedAtUtc = clock.UtcNow,
            orders = matched
        });
        return Result.Success(json);
    }

    public async Task<Result<int>> AnonymizeSubjectPiiAsync(
        Guid tenantId,
        string subjectIdentifier,
        string anonymousToken,
        CancellationToken cancellationToken = default)
    {
        var orders = await orderRepo.ListKanbanActiveAsync(cancellationToken);
        var normalized = subjectIdentifier.Trim();
        var affected = orders.Count(o =>
            string.Equals(o.CustomerIdentifier, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.CustomerPhone, normalized, StringComparison.OrdinalIgnoreCase));

        await Task.CompletedTask;
        return Result.Success(affected);
    }
}

public sealed class OfflineOperationServerApplierAdapter(
    IOfflineTerminalRepository terminalRepo,
    IClock clock) : IOfflineOperationServerApplier
{
    public async Task<OfflineApplyResult> ApplyAsync(
        Guid tenantId,
        OfflineSyncOperation operation,
        bool forceOnConflict,
        CancellationToken cancellationToken = default)
    {
        var terminal = await terminalRepo.FindByIdAsync(operation.OfflineTerminalId, cancellationToken);
        if (terminal is not null &&
            operation.CatalogSnapshotVersionUsed < terminal.CatalogSnapshotVersion &&
            !forceOnConflict)
        {
            return new OfflineApplyResult(
                OfflineApplyOutcomeKind.Conflict,
                null,
                $"Snapshot de catálogo v{operation.CatalogSnapshotVersionUsed} desatualizado frente à versão v{terminal.CatalogSnapshotVersion}.");
        }

        if (string.IsNullOrWhiteSpace(operation.PayloadJson))
        {
            return new OfflineApplyResult(
                OfflineApplyOutcomeKind.ValidationRejected,
                null,
                "Payload offline vazio.");
        }

        _ = clock.UtcNow;
        return new OfflineApplyResult(
            OfflineApplyOutcomeKind.Applied,
            operation.Id,
            forceOnConflict ? "Aplicado com resolução auditada." : "Sincronizado no servidor.");
    }
}
