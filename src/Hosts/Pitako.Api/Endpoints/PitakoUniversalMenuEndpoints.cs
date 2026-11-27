using Pitako.Api.Security;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.Coupons.Application.Services;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Application.Services;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Api.Endpoints;

public static class PitakoUniversalMenuEndpoints
{
    public static void MapUniversalMenuAndAdminExtensions(RouteGroupBuilder v1)
    {
        MapStoreProfileAndPauseEndpoints(v1);
        MapPublicCartQuoteFreightTrackingEndpoints(v1);
        MapCatalogFullCrudEndpoints(v1);
        MapAcaiCrudEndpoints(v1);
        MapInventoryCrudEndpoints(v1);
        MapCouponsAndDeliveryCrudEndpoints(v1);
    }

    private static void MapStoreProfileAndPauseEndpoints(RouteGroupBuilder v1)
    {
        var store = v1.MapGroup("/store");

        store.MapPut("/profile", async (
            UpdateStoreProfileReq req,
            ApiSecurityGuard guard,
            StoreBrandingService brandingSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Tenants, ct);
            if (err is not null) return err;

            return ToHttp(await brandingSvc.UpdateStoreProfileAsync(
                user!,
                user!.TenantId!.Value,
                req.LegalName,
                req.TradeName,
                req.Address,
                req.ContactPhone,
                req.ContactEmail,
                ct));
        });

        store.MapPost("/pause", async (
            SetStorePauseReq req,
            ApiSecurityGuard guard,
            StoreBrandingService brandingSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Tenants, ct);
            if (err is not null) return err;

            return ToHttp(await brandingSvc.SetTemporaryPauseAsync(user!, user!.TenantId!.Value, req.IsPaused, req.Reason, ct));
        });
    }

    private static void MapPublicCartQuoteFreightTrackingEndpoints(RouteGroupBuilder v1)
    {
        var pub = v1.MapGroup("/public/store/{slug}");

        pub.MapPost("/cart/quote", async (
            string slug,
            PublicQuoteProductReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            CatalogManagementService catalogSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var quoteRes = await catalogSvc.QuoteProductItemAsync(req.ProductId, req.VariationId, req.Modifiers, ct);
            if (quoteRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(quoteRes.Error);

            var q = quoteRes.Value;
            return Results.Ok(new
            {
                q.ProductId,
                q.CategoryId,
                q.ProductName,
                q.VariationId,
                q.VariationName,
                unitPrice = q.UnitPrice.Amount,
                finalUnitPrice = q.UnitPrice.Amount,
                q.HasNonCombinablePromotion,
                estimatedCostAmount = q.EstimatedCostAmount,
                customizationSummary = q.CustomizationSummaryText,
                selectionsPayloadJson = q.CanonicalSelectionsJson
            });
        });

        pub.MapGet("/cart/{sessionKey}", async (
            string slug,
            string sessionKey,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ShoppingCartService cartSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var res = await cartSvc.GetOrCreateCartAsync(sessionKey, null, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToCartDto(res.Value));
        });

        pub.MapPost("/cart/{sessionKey}/items", async (
            string slug,
            string sessionKey,
            PublicAddCartItemReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ShoppingCartService cartSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var kind = (req.Kind ?? "catalog").Trim().ToLowerInvariant();
            Result<ShoppingCart> res;
            if (kind == "acai" && req.AcaiCup is not null)
            {
                res = await cartSvc.AddCustomAcaiCupAsync(sessionKey, req.AcaiCup, req.ForceSeparateLine, ct);
            }
            else if (kind == "combo" && req.ComboId.HasValue)
            {
                res = await cartSvc.AddComboAsync(sessionKey, req.ComboId.Value, req.Quantity, req.Notes, ct);
            }
            else if (req.ProductId.HasValue)
            {
                res = await cartSvc.AddCatalogProductAsync(sessionKey, req.ProductId.Value, req.VariationId, req.Modifiers, req.Quantity, req.Notes, req.ForceSeparateLine, ct);
            }
            else
            {
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Cart.InvalidItem", "Produto, combo ou copo deve ser informado."));
            }

            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToCartDto(res.Value));
        });

        pub.MapPut("/cart/{sessionKey}/items/{itemId:guid}", async (
            string slug,
            string sessionKey,
            Guid itemId,
            PublicUpdateCartItemReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ShoppingCartService cartSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var res = req.Modifiers is not null || req.VariationId.HasValue || req.CustomerNotes is not null
                ? await cartSvc.UpdateCatalogProductItemAsync(sessionKey, itemId, req.VariationId, req.Modifiers, req.Quantity, req.CustomerNotes, ct)
                : await cartSvc.UpdateItemQuantityAsync(sessionKey, itemId, req.Quantity, ct);

            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToCartDto(res.Value));
        });

        pub.MapDelete("/cart/{sessionKey}/items/{itemId:guid}", async (
            string slug,
            string sessionKey,
            Guid itemId,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ShoppingCartService cartSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var res = await cartSvc.UpdateItemQuantityAsync(sessionKey, itemId, 0, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToCartDto(res.Value));
        });

        pub.MapDelete("/cart/{sessionKey}", async (
            string slug,
            string sessionKey,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ShoppingCartService cartSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var res = await cartSvc.ClearCartAsync(sessionKey, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToCartDto(res.Value));
        });

        pub.MapPost("/cart/{sessionKey}/coupon", async (
            string slug,
            string sessionKey,
            PublicApplyCouponReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ShoppingCartService cartSvc,
            ICouponRepository couponRepo,
            IOrderRepository orderRepo,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);

            var effectiveCustomer = !string.IsNullOrWhiteSpace(req.CustomerIdentifier)
                ? req.CustomerIdentifier
                : (!string.IsNullOrWhiteSpace(req.CustomerPhone) ? req.CustomerPhone : req.CustomerName);

            var normalizedCouponCode = Coupon.NormalizeCode(req.CouponCode);
            var coupon = string.IsNullOrWhiteSpace(normalizedCouponCode)
                ? null
                : await couponRepo.FindByCodeAsync(normalizedCouponCode, ct);

            if (coupon is not null && coupon.IsFirstOrderOnly)
            {
                var customerDigits = new string((req.CustomerPhone ?? req.CustomerIdentifier ?? string.Empty).Where(char.IsDigit).ToArray());
                var customerNameNorm = (req.CustomerName ?? req.CustomerIdentifier ?? string.Empty).Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(customerDigits) || !string.IsNullOrWhiteSpace(customerNameNorm))
                {
                    var allOrders = await orderRepo.ListRecentAllAsync(1000, ct);
                    var hasPrevOrder = allOrders.Any(o =>
                        o.Status != OrderStatus.Cancelled &&
                        ((!string.IsNullOrWhiteSpace(customerDigits) &&
                          new string((o.CustomerPhone ?? string.Empty).Where(char.IsDigit).ToArray()) == customerDigits) ||
                         (!string.IsNullOrWhiteSpace(customerNameNorm) &&
                          (o.CustomerName ?? string.Empty).Trim().ToLowerInvariant() == customerNameNorm)));

                    if (hasPrevOrder)
                    {
                        return ApiSecurityGuard.ProblemFromError(Error.Conflict(
                            "Coupon.FirstOrderOnly",
                            "Este cupom é exclusivo para o 1º pedido do cliente nesta loja."));
                    }
                }
            }

            var previewIdentifier = !string.IsNullOrWhiteSpace(effectiveCustomer)
                ? effectiveCustomer
                : $"cart:{sessionKey}";
            var res = await cartSvc.ApplyCouponAsync(sessionKey, req.CouponCode, previewIdentifier, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToCartDto(res.Value));
        });

        pub.MapDelete("/cart/{sessionKey}/coupon", async (
            string slug,
            string sessionKey,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ShoppingCartService cartSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var res = await cartSvc.RemoveCouponAsync(sessionKey, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToCartDto(res.Value));
        });

        pub.MapPost("/delivery/quote", async (
            string slug,
            PublicDeliveryQuoteReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            IDeliveryFreightQuoteService freightSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            if (req.FulfillmentMode == FulfillmentMode.CounterPickup)
            {
                return Results.Ok(new
                {
                    isConfigured = true,
                    distanceKm = 0m,
                    baseDeliveryFee = 0m,
                    finalDeliveryFee = 0m,
                    isFreeShippingApplied = true,
                    estimatedMinutes = 20,
                    appliedRuleSummary = "Retirada no balcão (sem taxa)"
                });
            }

            var quoteRes = await freightSvc.QuoteFreightAsync(
                null,
                new DeliveryFreightQuoteRequest(
                    Money.Create(Math.Max(0m, req.OrderSubtotal)).Value,
                    req.DestinationLatitude,
                    req.DestinationLongitude,
                    req.Neighborhood,
                    req.ZipCode,
                    req.PrecomputedDistanceKm),
                ct);

            if (quoteRes.IsFailure && quoteRes.Error.Code == "Delivery.PolicyNotConfigured")
            {
                return Results.Ok(new
                {
                    isConfigured = false,
                    distanceKm = 0m,
                    baseDeliveryFee = 0m,
                    finalDeliveryFee = 0m,
                    isFreeShippingApplied = true,
                    estimatedMinutes = 35,
                    appliedRuleSummary = "Taxa padrão gratuita"
                });
            }

            if (quoteRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(quoteRes.Error);

            var q = quoteRes.Value;
            return Results.Ok(new
            {
                isConfigured = true,
                q.DistanceKm,
                baseDeliveryFee = q.BaseDeliveryFee.Amount,
                finalDeliveryFee = q.FinalDeliveryFee.Amount,
                q.IsFreeShippingApplied,
                q.EstimatedMinutes,
                q.AppliedRuleSummary
            });
        });

        pub.MapGet("/orders/{orderNumber}", async (
            string slug,
            string orderNumber,
            string? phone,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            IOrderRepository orderRepo,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var order = await orderRepo.FindByOrderNumberAsync(orderNumber, ct);
            if (order is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Order.NotFound", "Pedido não encontrado."));

            // LGPD: valida dígitos de telefone quando informado ou oculta endereço completo sem confirmação.
            var storedDigits = new string((order.CustomerPhone ?? string.Empty).Where(char.IsDigit).ToArray());
            var inputDigits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
            var phoneVerified = string.IsNullOrWhiteSpace(storedDigits) || (!string.IsNullOrWhiteSpace(inputDigits) && storedDigits.EndsWith(inputDigits, StringComparison.Ordinal));

            return Results.Ok(new
            {
                order.Id,
                order.OrderNumber,
                status = order.Status.ToString(),
                statusCode = (int)order.Status,
                fulfillmentMode = order.FulfillmentMode.ToString(),
                customerName = order.CustomerName,
                deliveryAddressSummary = phoneVerified ? order.DeliveryAddressSummary : null,
                order.PaymentMethod,
                order.IsPaymentConfirmed,
                pixBrCode = order.PaymentMethod != null && order.PaymentMethod.Contains("PIX", StringComparison.OrdinalIgnoreCase)
                    ? PitakoPixPayloadGenerator.BuildBrCode(tenant, order.OrderNumber, order.TotalAmount.Amount)
                    : null,
                order.AppliedCouponCode,
                itemsSubtotal = order.ItemsSubtotal.Amount,
                itemsDiscount = order.ItemsDiscount.Amount,
                netDeliveryFee = order.NetDeliveryFee.Amount,
                totalAmount = order.TotalAmount.Amount,
                order.PlacedAtUtc,
                order.CompletedAtUtc,
                order.CancelledAtUtc,
                order.CancellationReason,
                items = order.Items.Select(i => new
                {
                    i.Id,
                    i.ItemTitle,
                    i.CustomizationSummaryText,
                    i.Quantity,
                    unitPrice = i.UnitPrice.Amount,
                    lineSubtotal = i.LineSubtotal.Amount,
                    i.CustomerNotes
                }),
                statusHistory = order.StatusHistory.OrderBy(h => h.ChangedAtUtc).Select(h => new
                {
                    fromStatus = h.FromStatus?.ToString(),
                    toStatus = h.ToStatus.ToString(),
                    toStatusCode = (int)h.ToStatus,
                    h.Reason,
                    h.ChangedAtUtc
                })
            });
        });
    }

    private static void MapCatalogFullCrudEndpoints(RouteGroupBuilder v1)
    {
        var catalog = v1.MapGroup("/catalog");

        catalog.MapPut("/categories/{id:guid}", async (Guid id, UpdateCategoryReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateCategoryAsync(user!, id, req.Name, req.Description, req.ImageUrl, req.DisplayOrder, req.IsActive, req.SegmentTemplateCode, req.RuleNotes, ct));
        });

        catalog.MapDelete("/categories/{id:guid}", async (Guid id, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeleteCategoryAsync(user!, id, ct));
        });

        catalog.MapPost("/categories/reorder", async (Dictionary<Guid, int> newOrders, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.ReorderCategoriesAsync(user!, newOrders, ct));
        });

        catalog.MapPut("/products/{id:guid}", async (Guid id, UpdateProductReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateProductAsync(user!, id, req.CategoryId, req.Name, req.BasePrice, req.Description, req.Sku, req.IsFeatured, req.DisplayOrder, req.SaleMode, req.IsNew, req.IsRecommended, req.PricingStrategy, req.TagsCsv, req.PreparationMinutes, req.CustomTemplateKey, req.AllowsNotes, ct));
        });

        catalog.MapDelete("/products/{id:guid}", async (Guid id, ApiSecurityGuard guard, CatalogManagementService svc, ITechnicalSheetRepository sheetRepo, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;

            var sheets = await sheetRepo.ListAllAsync(ct);
            var orphanSheets = sheets.Where(s => s.ProductId == id).ToList();
            foreach (var sh in orphanSheets)
            {
                sheetRepo.Remove(sh);
            }
            if (orphanSheets.Count > 0)
                await sheetRepo.SaveChangesAsync(ct);

            var delRes = await svc.DeleteOrArchiveProductAsync(user!, id, ct);
            if (delRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(delRes.Error);

            return Results.Ok(new
            {
                success = true,
                outcome = delRes.Value.ToString()
            });
        });

        catalog.MapPost("/products/{id:guid}/images", async (Guid id, AddProductImageReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.AddProductImageAsync(user!, id, req.ImageUrl, req.DisplayOrder, req.IsPrimary, ct), StatusCodes.Status201Created);
        });

        catalog.MapDelete("/products/{id:guid}/images/{imageId:guid}", async (Guid id, Guid imageId, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.RemoveProductImageAsync(user!, id, imageId, ct));
        });

        catalog.MapPost("/products/{id:guid}/variations", async (Guid id, UpsertProductVariationReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.AddProductVariationAsync(user!, id, req.VariationName, req.Price, req.Sku, req.DisplayOrder, req.Description, req.MeasureAmount, req.MeasureUnit, req.MaxFlavors, ct), StatusCodes.Status201Created);
        });

        catalog.MapPut("/products/{id:guid}/variations/{variationId:guid}", async (Guid id, Guid variationId, UpsertProductVariationReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateProductVariationAsync(user!, id, variationId, req.VariationName, req.Price, req.Sku, req.DisplayOrder, req.Description, req.MeasureAmount, req.MeasureUnit, req.MaxFlavors, req.IsActive, ct));
        });

        catalog.MapDelete("/products/{id:guid}/variations/{variationId:guid}", async (Guid id, Guid variationId, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.RemoveProductVariationAsync(user!, id, variationId, ct));
        });

        catalog.MapPost("/products/{id:guid}/modifier-groups/{groupId:guid}", async (Guid id, Guid groupId, int? displayOrder, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.AttachModifierGroupToProductAsync(user!, id, groupId, displayOrder ?? 0, ct));
        });

        catalog.MapDelete("/products/{id:guid}/modifier-groups/{groupId:guid}", async (Guid id, Guid groupId, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DetachModifierGroupFromProductAsync(user!, id, groupId, ct));
        });

        catalog.MapPost("/products/{id:guid}/variation-rules", async (Guid id, SetVariationRuleReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.SetVariationModifierRuleAsync(user!, id, req.VariationId, req.ModifierGroupId, req.MinSelections, req.MaxSelections, req.FreeAllowance, req.IsRequired, req.IsHidden, ct));
        });

        catalog.MapGet("/modifier-groups", async (ApiSecurityGuard guard, IModifierGroupRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            var groups = await repo.ListAllAsync(ct);
            return Results.Ok(groups.OrderBy(g => g.DisplayOrder).Select(g => new
            {
                g.Id,
                g.Name,
                groupType = (int)g.Type,
                selectionKind = (int)g.SelectionKind,
                g.MinSelections,
                g.MaxSelections,
                g.FreeAllowance,
                g.IsRequired,
                countingMode = (int)g.CountingMode,
                g.CategoryTag,
                g.DisplayOrder,
                g.IsActive,
                options = g.Options.OrderBy(o => o.DisplayOrder).Select(o => new
                {
                    o.Id,
                    o.Name,
                    o.Description,
                    additionalPrice = o.AdditionalPrice.Amount,
                    o.MaxQuantity,
                    o.DisplayOrder,
                    o.IsAvailable,
                    o.SupplyItemId,
                    o.ConsumptionQuantity,
                    o.ConsumptionUnitCode,
                    o.LossPercent,
                    o.ReplacesLabel,
                    o.IncompatibleOptionIdsCsv
                })
            }));
        });

        catalog.MapPost("/modifier-groups", async (UpsertModifierGroupReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.CreateModifierGroupDetailedAsync(
                user!,
                req.Name,
                req.Type,
                req.MinSelections,
                req.MaxSelections,
                req.IsRequired,
                req.Options ?? [],
                req.DisplayOrder,
                req.SelectionKind,
                req.FreeAllowance,
                req.CountingMode,
                req.CategoryTag,
                ct), StatusCodes.Status201Created);
        });

        catalog.MapPut("/modifier-groups/{id:guid}", async (Guid id, UpsertModifierGroupReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateModifierGroupAsync(
                user!,
                id,
                req.Name,
                req.Type,
                req.MinSelections,
                req.MaxSelections,
                req.IsRequired,
                req.DisplayOrder,
                req.SelectionKind ?? ModifierGroupKind.PaidAddon,
                req.FreeAllowance,
                req.CountingMode,
                req.CategoryTag,
                req.IsActive,
                ct));
        });

        catalog.MapDelete("/modifier-groups/{id:guid}", async (Guid id, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeleteModifierGroupAsync(user!, id, ct));
        });

        catalog.MapPost("/modifier-groups/{groupId:guid}/options", async (Guid groupId, ModifierOptionInput req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.AddModifierOptionAsync(user!, groupId, req, ct), StatusCodes.Status201Created);
        });

        catalog.MapPut("/modifier-groups/{groupId:guid}/options/{optionId:guid}", async (Guid groupId, Guid optionId, UpdateModifierOptionReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateModifierOptionAsync(user!, groupId, optionId, req.Input, req.IsAvailable, ct));
        });

        catalog.MapDelete("/modifier-groups/{groupId:guid}/options/{optionId:guid}", async (Guid groupId, Guid optionId, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.RemoveModifierOptionAsync(user!, groupId, optionId, ct));
        });

        catalog.MapGet("/combos", async (ApiSecurityGuard guard, IComboRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            var combos = await repo.ListAllOrderedAsync(ct);
            return Results.Ok(combos.Select(c => new
            {
                c.Id,
                c.Name,
                c.Description,
                comboPrice = c.ComboPrice.Amount,
                c.PrimaryImageUrl,
                c.IsFeatured,
                c.DisplayOrder,
                isActive = c.Status == CatalogItemStatus.Active,
                items = c.Items.Select(i => new { i.ProductId, i.Quantity, i.VariationId, i.Notes })
            }));
        });

        catalog.MapPost("/combos", async (UpsertComboReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            var items = req.Items.Select(i => (i.ProductId, i.Quantity, i.VariationId, i.Notes)).ToList();
            return ToHttp(await svc.CreateComboAsync(user!, req.Name, req.ComboPrice, items, req.Description, req.PrimaryImageUrl, req.IsFeatured, req.DisplayOrder, ct), StatusCodes.Status201Created);
        });

        catalog.MapPut("/combos/{id:guid}", async (Guid id, UpsertComboReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            var items = req.Items.Select(i => (i.ProductId, i.Quantity, i.VariationId, i.Notes)).ToList();
            return ToHttp(await svc.UpdateComboAsync(user!, id, req.Name, req.ComboPrice, items, req.Description, req.PrimaryImageUrl, req.IsFeatured, req.DisplayOrder, req.IsActive, ct));
        });

        catalog.MapDelete("/combos/{id:guid}", async (Guid id, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeleteComboAsync(user!, id, ct));
        });

        catalog.MapGet("/promotions", async (ApiSecurityGuard guard, IPromotionRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            var promos = await repo.ListAllAsync(ct);
            return Results.Ok(promos.Select(p => new
            {
                p.Id,
                p.Name,
                discountType = (int)p.DiscountType,
                p.DiscountValue,
                p.StartsAtUtc,
                p.EndsAtUtc,
                p.CanCombineWithCoupons,
                p.IsActive,
                targets = p.Targets.Select(t => new { targetType = (int)t.TargetType, t.TargetId })
            }));
        });

        catalog.MapPost("/promotions", async (UpsertPromotionReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            var targets = req.Targets.Select(t => (t.TargetType, t.TargetId));
            return ToHttp(await svc.CreatePromotionAsync(user!, req.Name, req.DiscountType, req.DiscountValue, req.StartsAtUtc, req.EndsAtUtc, targets, req.CanCombineWithCoupons, ct), StatusCodes.Status201Created);
        });

        catalog.MapPut("/promotions/{id:guid}", async (Guid id, UpsertPromotionReq req, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            var targets = req.Targets.Select(t => (t.TargetType, t.TargetId));
            return ToHttp(await svc.UpdatePromotionAsync(user!, id, req.Name, req.DiscountType, req.DiscountValue, req.StartsAtUtc, req.EndsAtUtc, targets, req.CanCombineWithCoupons, req.IsActive, ct));
        });

        catalog.MapDelete("/promotions/{id:guid}", async (Guid id, ApiSecurityGuard guard, CatalogManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeletePromotionAsync(user!, id, ct));
        });

        catalog.MapPost("/apply-segment-template", async (ApplySegmentTemplateReq req, ApiSecurityGuard guard, SegmentCatalogTemplateService tplSvc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            return ToHttp(await tplSvc.ApplyTemplateAsync(user!, req.SegmentCode, ct), StatusCodes.Status201Created);
        });

        catalog.MapGet("/analytics/margins", async (
            ApiSecurityGuard guard,
            IProductRepository productRepo,
            ITechnicalSheetRepository sheetRepo,
            IOrderRepository orderRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;

            var products = await productRepo.ListAllOrderedAsync(ct);
            var sheets = await sheetRepo.ListAllAsync(ct);
            var sales = await orderRepo.GetProductSalesCountsAsync(ct);

            var sheetByProduct = sheets.Where(s => s.ProductId.HasValue)
                .GroupBy(s => s.ProductId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var items = products.Select(p =>
            {
                sheetByProduct.TryGetValue(p.Id, out var sheet);
                var cost = sheet?.CostPerPortion ?? 0m;
                var price = p.BasePrice.Amount;
                var profit = price - cost;
                var marginPercent = price > 0m ? decimal.Round((profit / price) * 100m, 2) : 0m;
                var sold = sales.GetValueOrDefault(p.Id, 0);
                return new
                {
                    productId = p.Id,
                    p.Name,
                    status = p.Status.ToString(),
                    sellingPrice = price,
                    unitCost = cost,
                    grossProfit = decimal.Round(profit, 2),
                    grossMarginPercent = marginPercent,
                    hasTechnicalSheet = sheet is not null,
                    salesCount = sold,
                    p.IsFeatured,
                    p.IsNew,
                    p.IsRecommended
                };
            }).OrderByDescending(x => x.grossMarginPercent).ThenByDescending(x => x.salesCount).ToList();

            return Results.Ok(items);
        });
    }

    private static void MapAcaiCrudEndpoints(RouteGroupBuilder v1)
    {
        var acai = v1.MapGroup("/acai");

        acai.MapPut("/cup-sizes/{id:guid}", async (Guid id, UpdateAcaiCupReq req, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateCupSizeAsync(user!, id, req.Name, req.VolumeMl, req.BasePrice, req.FreeToppingsLimit, req.MinFreeToppings, req.CountingMode, req.MaxPaidAddonsPerCup, req.DisplayOrder, ct));
        });

        acai.MapPost("/cup-sizes/{id:guid}/pause", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.SetCupSizeActiveAsync(user!, id, false, ct));
        });

        acai.MapPost("/cup-sizes/{id:guid}/reactivate", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.SetCupSizeActiveAsync(user!, id, true, ct));
        });

        acai.MapDelete("/cup-sizes/{id:guid}", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeleteCupSizeAsync(user!, id, ct));
        });

        acai.MapPut("/flavors/{id:guid}", async (Guid id, UpdateAcaiFlavorReq req, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateBaseFlavorAsync(user!, id, req.Name, req.ExtraPrice, req.DisplayOrder, ct));
        });

        acai.MapDelete("/flavors/{id:guid}", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeleteBaseFlavorAsync(user!, id, ct));
        });

        acai.MapPut("/free-toppings/{id:guid}", async (Guid id, UpdateAcaiFreeReq req, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateFreeToppingAsync(user!, id, req.Name, req.MaxPortionsPerCup, req.DisplayOrder, req.SupplyItemId, req.ConsumptionQuantityPerPortion, req.ConsumptionUnitCode, ct));
        });

        acai.MapPost("/free-toppings/{id:guid}/pause", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.PauseFreeToppingAsync(user!, id, ct));
        });

        acai.MapPost("/free-toppings/{id:guid}/reactivate", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.ReactivateFreeToppingAsync(user!, id, ct));
        });

        acai.MapDelete("/free-toppings/{id:guid}", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeleteFreeToppingAsync(user!, id, ct));
        });

        acai.MapPut("/paid-addons/{id:guid}", async (Guid id, UpdateAcaiPaidReq req, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdatePaidAddonAsync(user!, id, req.Name, req.AdditionalPrice, req.MaxQuantityPerCup, req.DisplayOrder, req.SupplyItemId, req.ConsumptionQuantityPerUnit, req.ConsumptionUnitCode, ct));
        });

        acai.MapPost("/paid-addons/{id:guid}/pause", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.PausePaidAddonAsync(user!, id, ct));
        });

        acai.MapPost("/paid-addons/{id:guid}/reactivate", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.ReactivatePaidAddonAsync(user!, id, ct));
        });

        acai.MapDelete("/paid-addons/{id:guid}", async (Guid id, ApiSecurityGuard guard, AcaiBuilderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Acai, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeletePaidAddonAsync(user!, id, ct));
        });
    }

    private static void MapInventoryCrudEndpoints(RouteGroupBuilder v1)
    {
        var inv = v1.MapGroup("/inventory");

        inv.MapGet("/supplies", async (ApiSecurityGuard guard, ISupplyItemRepository supplyRepo, IStockBalanceRepository stockRepo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;

            var supplies = await supplyRepo.ListAllAsync(ct);
            var stockMap = (await stockRepo.ListAllWithLotsAsync(ct)).ToDictionary(s => s.SupplyItemId);
            return Results.Ok(supplies.OrderBy(s => s.Name).Select(s =>
            {
                stockMap.TryGetValue(s.Id, out var bal);
                return new
                {
                    s.Id,
                    s.Name,
                    s.Sku,
                    s.SupplierName,
                    purchasePrice = s.PurchasePrice.Amount,
                    s.PurchaseQuantity,
                    s.PurchaseUnitCode,
                    s.ContentQuantityPerPackage,
                    s.ContentUnitCode,
                    costPerContentUnit = s.PurchaseQuantity * s.ContentQuantityPerPackage > 0m
                        ? decimal.Round(s.PurchasePrice.Amount / (s.PurchaseQuantity * s.ContentQuantityPerPackage), 6)
                        : 0m,
                    s.DefaultLossPercent,
                    s.IsActive,
                    quantityOnHand = bal?.QuantityOnHand ?? 0m,
                    controlUnitCode = bal?.UnitCode,
                    minimumStockQuantity = bal?.MinimumStockQuantity ?? 0m
                };
            }));
        });

        inv.MapPost("/supplies", async (UpsertSupplyReq req, ApiSecurityGuard guard, SuppliesAndTechnicalSheetService svc, InventoryStockAndProductionService stockSvc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;

            var res = await svc.CreateSupplyItemAsync(user!, req.Name, req.PurchasePrice, req.PurchaseQuantity, req.PurchaseUnitCode, req.SupplierName, req.Sku, req.ContentQuantityPerPackage, req.ContentUnitCode, req.DefaultLossPercent, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);

            if (!string.IsNullOrWhiteSpace(req.ControlUnitCode))
                await stockSvc.ConfigureStockBalanceAsync(user!, res.Value.Id, req.ControlUnitCode, req.MinimumStockQuantity ?? 0m, req.AllowNegativeStock ?? false, true, true, ct);

            return Results.Created($"/api/v1/inventory/supplies/{res.Value.Id}", res.Value);
        });

        inv.MapPut("/supplies/{id:guid}", async (Guid id, UpsertSupplyReq req, ApiSecurityGuard guard, SuppliesAndTechnicalSheetService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateSupplyItemAsync(user!, id, req.Name, req.PurchasePrice, req.PurchaseQuantity, req.PurchaseUnitCode, req.SupplierName, req.Sku, req.ContentQuantityPerPackage, req.ContentUnitCode, req.DefaultLossPercent, req.IsActive, ct));
        });

        inv.MapDelete("/supplies/{id:guid}", async (Guid id, ApiSecurityGuard guard, SuppliesAndTechnicalSheetService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeleteSupplyItemAsync(user!, id, ct));
        });

        inv.MapPost("/stock/balances", async (ConfigureStockBalanceReq req, ApiSecurityGuard guard, InventoryStockAndProductionService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            return ToHttp(await svc.ConfigureStockBalanceAsync(user!, req.SupplyItemId, req.ControlUnitCode, req.MinimumStockQuantity, req.AllowNegativeStock, req.AutoDeductOnOrder, req.BlockExpiredLots, ct));
        });

        inv.MapPost("/purchases", async (ReceivePurchaseReq req, ApiSecurityGuard guard, InventoryStockAndProductionService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            return ToHttp(await svc.ReceivePurchaseAsync(user!, req.SupplierName, req.InvoiceNumber, req.Lines, req.UpdateSupplyPrice, req.Notes, req.IdempotencyKey, ct), StatusCodes.Status201Created);
        });

        inv.MapPost("/waste", async (RegisterWasteReq req, ApiSecurityGuard guard, InventoryStockAndProductionService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            return ToHttp(await svc.RegisterWasteOrLossAsync(user!, req.SupplyItemId, req.QuantityInControlUnit, req.Reason, req.IncludeExpiredLots, ct), StatusCodes.Status201Created);
        });

        inv.MapGet("/technical-sheets", async (ApiSecurityGuard guard, ITechnicalSheetRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            var sheets = await repo.ListAllAsync(ct);
            return Results.Ok(sheets.OrderBy(s => s.Name).Select(s => new
            {
                s.Id,
                s.ProductId,
                s.Name,
                s.YieldQuantity,
                s.YieldUnitCode,
                s.Notes,
                s.IngredientsTotalCost,
                s.PackagingsTotalCost,
                s.TotalCost,
                s.CostPerPortion,
                ingredients = s.Ingredients.Select(i => new
                {
                    i.Id,
                    i.SupplyItemId,
                    i.SupplyItemNameSnapshot,
                    i.Quantity,
                    i.LossPercent,
                    i.GrossQuantity,
                    i.UnitCode,
                    i.CalculatedCost
                }),
                packagings = s.Packagings.Select(pk => new
                {
                    pk.Id,
                    pk.SupplyItemId,
                    pk.Description,
                    pk.Quantity,
                    pk.UnitCode,
                    pk.CalculatedCost
                })
            }));
        });

        inv.MapPost("/technical-sheets", async (UpsertTechnicalSheetReq req, ApiSecurityGuard guard, SuppliesAndTechnicalSheetService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            return ToHttp(await svc.CreateTechnicalSheetAsync(user!, req.Name, req.YieldQuantity, req.YieldUnitCode, req.Ingredients ?? [], req.Packagings ?? [], req.ProductId, req.Notes, ct), StatusCodes.Status201Created);
        });

        inv.MapPut("/technical-sheets/{id:guid}", async (Guid id, UpsertTechnicalSheetReq req, ApiSecurityGuard guard, SuppliesAndTechnicalSheetService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateTechnicalSheetAsync(user!, id, req.Name, req.YieldQuantity, req.YieldUnitCode, req.Ingredients ?? [], req.Packagings ?? [], req.ProductId, req.Notes, ct));
        });

        inv.MapDelete("/technical-sheets/{id:guid}", async (Guid id, ApiSecurityGuard guard, SuppliesAndTechnicalSheetService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeleteTechnicalSheetAsync(user!, id, ct));
        });

        inv.MapPost("/technical-sheets/{id:guid}/simulate-price", async (Guid id, SimulatePriceReq req, ApiSecurityGuard guard, SuppliesAndTechnicalSheetService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;
            return ToHttp(await svc.SimulateSellingPriceAsync(user!, id, req.TaxPercent, req.ChannelFeePercent, req.OperationalOverheadPercent, req.DesiredNetMarginPercent, req.ExplicitMarkupMultiplier, ct));
        });
    }

    private static void MapCouponsAndDeliveryCrudEndpoints(RouteGroupBuilder v1)
    {
        var coupons = v1.MapGroup("/coupons");

        coupons.MapGet("/", async (ApiSecurityGuard guard, ICouponRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Coupons, ct);
            if (err is not null) return err;
            var list = await repo.ListAllAsync(ct);
            return Results.Ok(list.Select(c => new
            {
                c.Id,
                c.Code,
                c.Description,
                discountType = (int)c.DiscountType,
                c.DiscountValue,
                c.ValidFromUtc,
                c.ValidUntilUtc,
                minimumOrderAmount = c.MinimumOrderAmount.Amount,
                maxDiscountAmount = c.MaxDiscountAmount?.Amount,
                c.MaxTotalUses,
                c.MaxUsesPerCustomer,
                currentUses = c.Redemptions.Count(r => !r.IsReversed),
                c.CanCombineWithPromotions,
                c.IsActive,
                firstOrderOnly = c.IsFirstOrderOnly
            }));
        });

        coupons.MapPost("/", async (UpsertCouponReq req, ApiSecurityGuard guard, CouponsManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Coupons, ct);
            if (err is not null) return err;
            return ToHttp(await svc.CreateCouponAsync(user!, req.Code, req.DiscountType, req.DiscountValue, req.ValidFromUtc, req.ValidUntilUtc, req.MinimumOrderAmount, req.MaxDiscountAmount, req.MaxTotalUses, req.MaxUsesPerCustomer, req.CanCombineWithPromotions, null, req.Description, req.FirstOrderOnly, ct), StatusCodes.Status201Created);
        });

        coupons.MapPut("/{id:guid}", async (Guid id, UpsertCouponReq req, ApiSecurityGuard guard, CouponsManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Coupons, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdateCouponAsync(user!, id, req.DiscountType, req.DiscountValue, req.ValidFromUtc, req.ValidUntilUtc, req.MinimumOrderAmount, req.MaxDiscountAmount, req.MaxTotalUses, req.MaxUsesPerCustomer, req.CanCombineWithPromotions, req.Description, req.IsActive, req.FirstOrderOnly, ct));
        });

        coupons.MapPost("/{id:guid}/deactivate", async (Guid id, ApiSecurityGuard guard, CouponsManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Coupons, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeactivateCouponAsync(user!, id, ct));
        });

        coupons.MapPost("/{id:guid}/reactivate", async (Guid id, ApiSecurityGuard guard, CouponsManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Coupons, ct);
            if (err is not null) return err;
            return ToHttp(await svc.ReactivateCouponAsync(user!, id, ct));
        });

        coupons.MapDelete("/{id:guid}", async (Guid id, ApiSecurityGuard guard, CouponsManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Coupons, ct);
            if (err is not null) return err;
            return ToHttp(await svc.DeleteCouponAsync(user!, id, ct));
        });

        var delivery = v1.MapGroup("/delivery");

        delivery.MapGet("/policy", async (ApiSecurityGuard guard, IDeliveryPolicyRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;
            var p = await repo.FindEffectivePolicyAsync(null, ct);
            if (p is null) return Results.Ok<object?>(null);
            return Results.Ok(new
            {
                p.Id,
                p.OriginLatitude,
                p.OriginLongitude,
                p.MaxRadiusKm,
                calculationMode = (int)p.CalculationMode,
                baseFee = p.BaseFee.Amount,
                p.IncludedKm,
                feePerKm = p.FeePerKm.Amount,
                p.BaseEstimatedMinutes,
                minimumOrderAmount = p.MinimumOrderAmount.Amount,
                freeShippingThreshold = p.FreeShippingThreshold?.Amount,
                p.IsDeliveryEnabled,
                p.AllowPickup,
                p.IsTemporarilyPaused,
                p.PauseReason,
                distanceTiers = p.DistanceTiers.Select(t => new { t.Id, t.MinKm, t.MaxKm, fee = t.Fee.Amount, t.EstimatedMinutes }),
                zoneRules = p.ZoneRules.Select(z => new { z.Id, z.ZoneName, z.ZipCodePrefix, fee = z.Fee.Amount, z.EstimatedMinutes, z.IsBlocked })
            });
        });

        delivery.MapPost("/policy", async (UpsertDeliveryPolicyReq req, ApiSecurityGuard guard, DeliveryPolicyAndFreightService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;
            return ToHttp(await svc.CreatePolicyAsync(user!, req.OriginLatitude, req.OriginLongitude, req.MaxRadiusKm, req.CalculationMode, req.BaseFeeAmount, req.IncludedKm, req.FeePerKmAmount, req.BaseEstimatedMinutes, req.MinimumOrderAmount, req.FreeShippingThresholdAmount, req.StoreUnitId, req.IsDeliveryEnabled, req.AllowPickup, ct), StatusCodes.Status201Created);
        });

        delivery.MapPut("/policy/{id:guid}", async (Guid id, UpsertDeliveryPolicyReq req, ApiSecurityGuard guard, DeliveryPolicyAndFreightService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;
            return ToHttp(await svc.UpdatePolicyAsync(user!, id, req.OriginLatitude, req.OriginLongitude, req.MaxRadiusKm, req.CalculationMode, req.BaseFeeAmount, req.IncludedKm, req.FeePerKmAmount, req.BaseEstimatedMinutes, req.MinimumOrderAmount, req.FreeShippingThresholdAmount, req.IsDeliveryEnabled, req.AllowPickup, ct));
        });

        delivery.MapPost("/policy/{id:guid}/pause", async (Guid id, PauseDeliveryReq req, ApiSecurityGuard guard, DeliveryPolicyAndFreightService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;
            return ToHttp(await svc.PauseDeliveryAsync(user!, id, req.Reason, ct));
        });

        delivery.MapPost("/policy/{id:guid}/resume", async (Guid id, ApiSecurityGuard guard, DeliveryPolicyAndFreightService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;
            return ToHttp(await svc.ResumeDeliveryAsync(user!, id, ct));
        });

        delivery.MapPost("/policy/{id:guid}/zones", async (Guid id, AddDeliveryZoneReq req, ApiSecurityGuard guard, DeliveryPolicyAndFreightService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;
            return ToHttp(await svc.AddZoneRuleAsync(user!, id, req.ZoneName, req.ZipCodePrefix, req.FeeAmount, req.EstimatedMinutes, req.IsBlocked, ct));
        });

        delivery.MapDelete("/policy/{id:guid}/zones/{zoneId:guid}", async (Guid id, Guid zoneId, ApiSecurityGuard guard, DeliveryPolicyAndFreightService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;
            return ToHttp(await svc.RemoveZoneRuleAsync(user!, id, zoneId, ct));
        });
    }

    private static object ToCartDto(ShoppingCart c) => new
    {
        c.Id,
        cartSessionKey = c.CartSessionKey,
        c.CustomerIdentifier,
        c.AppliedCouponCode,
        itemsSubtotal = c.ItemsSubtotal.Amount,
        itemsDiscount = c.ItemsDiscount.Amount,
        deliveryFee = c.DeliveryFee.Amount,
        deliveryDiscount = c.DeliveryDiscount.Amount,
        totalAmount = c.TotalAmount.Amount,
        items = c.Items.Select(i => new
        {
            i.Id,
            lineKind = i.ItemKind.ToString(),
            i.ProductId,
            i.CategoryId,
            i.VariationId,
            i.AcaiCupSizeId,
            i.ItemTitle,
            i.CustomizationSummaryText,
            i.SelectionsPayloadJson,
            unitPrice = i.UnitPrice.Amount,
            i.Quantity,
            lineSubtotal = i.LineSubtotal.Amount,
            i.CustomerNotes
        })
    };

    private static IResult ToHttp<T>(Result<T> result, int successCode = StatusCodes.Status200OK) =>
        result.IsFailure
            ? ApiSecurityGuard.ProblemFromError(result.Error)
            : Results.Json(result.Value, statusCode: successCode);

    private static IResult ToHttp(Result result) =>
        result.IsFailure
            ? ApiSecurityGuard.ProblemFromError(result.Error)
            : Results.Ok(new { success = true });
}

public sealed record UpdateStoreProfileReq(string LegalName, string TradeName, string? Address = null, string? ContactPhone = null, string? ContactEmail = null);
public sealed record SetStorePauseReq(bool IsPaused, string? Reason = null);
public sealed record PublicQuoteProductReq(Guid ProductId, Guid? VariationId = null, IReadOnlyList<CatalogModifierSelection>? Modifiers = null);
public sealed record PublicAddCartItemReq(string? Kind = "catalog", Guid? ProductId = null, Guid? VariationId = null, IReadOnlyList<CatalogModifierSelection>? Modifiers = null, Guid? ComboId = null, AcaiCupBuildRequest? AcaiCup = null, int Quantity = 1, string? Notes = null, bool ForceSeparateLine = false);
public sealed record PublicUpdateCartItemReq(int Quantity = 1, Guid? VariationId = null, IReadOnlyList<CatalogModifierSelection>? Modifiers = null, string? CustomerNotes = null);
public sealed record PublicApplyCouponReq(string CouponCode, string? CustomerIdentifier = null, string? CustomerPhone = null, string? CustomerName = null);
public sealed record PublicDeliveryQuoteReq(FulfillmentMode FulfillmentMode, decimal OrderSubtotal, double? DestinationLatitude = null, double? DestinationLongitude = null, string? Neighborhood = null, string? ZipCode = null, decimal? PrecomputedDistanceKm = null);

public sealed record UpdateCategoryReq(string Name, string? Description = null, string? ImageUrl = null, int DisplayOrder = 0, bool IsActive = true, string? SegmentTemplateCode = null, string? RuleNotes = null);
public sealed record UpdateProductReq(Guid CategoryId, string Name, decimal BasePrice, string? Description = null, string? Sku = null, bool IsFeatured = false, int DisplayOrder = 0, ProductSaleMode SaleMode = ProductSaleMode.Unit, bool IsNew = false, bool IsRecommended = false, ProductPricingStrategy PricingStrategy = ProductPricingStrategy.FixedPlusAddons, string? TagsCsv = null, int? PreparationMinutes = null, string? CustomTemplateKey = null, bool AllowsNotes = true);
public sealed record AddProductImageReq(string ImageUrl, int DisplayOrder = 0, bool IsPrimary = true);
public sealed record UpsertProductVariationReq(string VariationName, decimal Price, string? Sku = null, int DisplayOrder = 0, string? Description = null, decimal? MeasureAmount = null, string? MeasureUnit = null, int MaxFlavors = 1, bool IsActive = true);
public sealed record SetVariationRuleReq(Guid? VariationId, Guid ModifierGroupId, int MinSelections, int MaxSelections, int FreeAllowance, bool IsRequired, bool IsHidden = false);
public sealed record UpsertModifierGroupReq(string Name, ModifierGroupType Type, int MinSelections, int MaxSelections, bool IsRequired, IReadOnlyList<ModifierOptionInput>? Options = null, int DisplayOrder = 0, ModifierGroupKind? SelectionKind = null, int FreeAllowance = 0, OptionCountingMode CountingMode = OptionCountingMode.ByTotalQuantity, string? CategoryTag = null, bool IsActive = true);
public sealed record UpdateModifierOptionReq(ModifierOptionInput Input, bool IsAvailable = true);
public sealed record ComboItemDto(Guid ProductId, int Quantity = 1, Guid? VariationId = null, string? Notes = null);
public sealed record UpsertComboReq(string Name, decimal ComboPrice, IReadOnlyList<ComboItemDto> Items, string? Description = null, string? PrimaryImageUrl = null, bool IsFeatured = false, int DisplayOrder = 0, bool IsActive = true);
public sealed record PromotionTargetDto(PromotionTargetType TargetType, Guid TargetId);
public sealed record UpsertPromotionReq(string Name, PromotionDiscountType DiscountType, decimal DiscountValue, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, IReadOnlyList<PromotionTargetDto> Targets, bool CanCombineWithCoupons = false, bool IsActive = true);
public sealed record ApplySegmentTemplateReq(string SegmentCode);

public sealed record UpdateAcaiCupReq(string Name, int VolumeMl, decimal BasePrice, int FreeToppingsLimit, int MinFreeToppings = 0, AcaiFreeCountingMode CountingMode = AcaiFreeCountingMode.ByPortion, int? MaxPaidAddonsPerCup = null, int DisplayOrder = 0);
public sealed record UpdateAcaiFlavorReq(string Name, decimal ExtraPrice = 0m, int DisplayOrder = 0);
public sealed record UpdateAcaiFreeReq(string Name, int MaxPortionsPerCup = 1, int DisplayOrder = 0, Guid? SupplyItemId = null, decimal? ConsumptionQuantityPerPortion = null, string? ConsumptionUnitCode = null);
public sealed record UpdateAcaiPaidReq(string Name, decimal AdditionalPrice, int MaxQuantityPerCup = 3, int DisplayOrder = 0, Guid? SupplyItemId = null, decimal? ConsumptionQuantityPerUnit = null, string? ConsumptionUnitCode = null);

public sealed record UpsertSupplyReq(string Name, decimal PurchasePrice, decimal PurchaseQuantity, string PurchaseUnitCode, string? SupplierName = null, string? Sku = null, decimal? ContentQuantityPerPackage = null, string? ContentUnitCode = null, decimal DefaultLossPercent = 0m, bool IsActive = true, string? ControlUnitCode = null, decimal? MinimumStockQuantity = 0m, bool? AllowNegativeStock = false);
public sealed record ConfigureStockBalanceReq(Guid SupplyItemId, string ControlUnitCode, decimal MinimumStockQuantity = 0m, bool AllowNegativeStock = false, bool AutoDeductOnOrder = true, bool BlockExpiredLots = true);
public sealed record ReceivePurchaseReq(string SupplierName, string? InvoiceNumber, IReadOnlyList<PurchaseLineInput> Lines, bool UpdateSupplyPrice = true, string? Notes = null, string? IdempotencyKey = null);
public sealed record RegisterWasteReq(Guid SupplyItemId, decimal QuantityInControlUnit, string Reason, bool IncludeExpiredLots = true);
public sealed record UpsertTechnicalSheetReq(string Name, decimal YieldQuantity, string YieldUnitCode, IReadOnlyList<IngredientInput>? Ingredients = null, IReadOnlyList<PackagingInput>? Packagings = null, Guid? ProductId = null, string? Notes = null);
public sealed record SimulatePriceReq(decimal TaxPercent, decimal ChannelFeePercent, decimal OperationalOverheadPercent, decimal DesiredNetMarginPercent, decimal? ExplicitMarkupMultiplier = null);

public sealed record UpsertCouponReq(string Code, CouponDiscountType DiscountType, decimal DiscountValue, DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc, decimal MinimumOrderAmount = 0m, decimal? MaxDiscountAmount = null, int? MaxTotalUses = null, int? MaxUsesPerCustomer = null, bool CanCombineWithPromotions = false, string? Description = null, bool IsActive = true, bool FirstOrderOnly = false);
public sealed record UpsertDeliveryPolicyReq(double OriginLatitude, double OriginLongitude, decimal MaxRadiusKm, FreightCalculationMode CalculationMode, decimal BaseFeeAmount, decimal IncludedKm = 0m, decimal FeePerKmAmount = 0m, int BaseEstimatedMinutes = 35, decimal MinimumOrderAmount = 0m, decimal? FreeShippingThresholdAmount = null, Guid? StoreUnitId = null, bool IsDeliveryEnabled = true, bool AllowPickup = true);
public sealed record PauseDeliveryReq(string Reason);
public sealed record AddDeliveryZoneReq(string ZoneName, string? ZipCodePrefix, decimal FeeAmount, int EstimatedMinutes, bool IsBlocked = false);
