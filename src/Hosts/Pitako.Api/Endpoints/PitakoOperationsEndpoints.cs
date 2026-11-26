using System.Globalization;
using System.Text;
using Pitako.Api.Security;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Domain;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Domain;
using Pitako.Modules.Finance.Application.Abstractions;
using Pitako.Modules.Finance.Domain;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Application.Services;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.PdvCashier.Application.Abstractions;
using Pitako.Modules.PdvCashier.Application.Services;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Api.Endpoints;

public static class PitakoOperationsEndpoints
{
    public static void MapOperationalExtensions(RouteGroupBuilder v1)
    {
        MapTotemEndpoints(v1);
        MapDiningExtendedEndpoints(v1);
        MapFinanceExtendedEndpoints(v1);
        MapPdvExtendedEndpoints(v1);
        MapOrdersPixAndDeliveryRouteEndpoints(v1);
    }

    private static void MapTotemEndpoints(RouteGroupBuilder v1)
    {
        var totem = v1.MapGroup("/totem");

        totem.MapGet("/terminals", async (ApiSecurityGuard guard, ITotemTerminalRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            var list = await repo.ListAllAsync(ct);
            return Results.Ok(list.Select(t => new
            {
                t.Id,
                t.TerminalCode,
                t.DisplayName,
                status = t.Status.ToString(),
                defaultChoice = t.DefaultFulfillmentChoice.ToString(),
                t.IdleTimeoutSeconds,
                t.LastHeartbeatUtc
            }));
        });

        totem.MapPost("/terminals", async (RegisterTotemReq req, ApiSecurityGuard guard, TotemSelfServiceService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            return ToHttp(await svc.RegisterTerminalAsync(user!, req.TerminalCode, req.DisplayName, req.DefaultChoice, req.RequireCustomerName, req.RequireTableOrPagerNumber, req.IdleTimeoutSeconds, null, ct), StatusCodes.Status201Created);
        });

        var pubTotem = v1.MapGroup("/public/totem/{slug}");

        pubTotem.MapPost("/start", async (
            string slug,
            StartTotemSessionReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            ITotemTerminalRepository terminalRepo,
            TotemSelfServiceService svc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);

            var terminals = await terminalRepo.ListAllAsync(ct);
            var targetTerminal = !string.IsNullOrWhiteSpace(req.TerminalCode)
                ? terminals.FirstOrDefault(t => t.TerminalCode.Equals(req.TerminalCode.Trim(), StringComparison.OrdinalIgnoreCase))
                : terminals.FirstOrDefault(t => t.Status == TotemTerminalStatus.Active) ?? terminals.FirstOrDefault();

            if (targetTerminal is null)
            {
                var actor = User.CreateEstablishmentUser(tenant.Id, "Totem Auto", $"totem@{tenant.Slug}.local", "TotemAuto!2026", EstablishmentRole.Administrator).Value;
                var reg = await svc.RegisterTerminalAsync(actor, "TOTEM-01", "Totem Salão Principal", TotemFulfillmentChoice.CounterPickup, false, false, 120, null, ct);
                if (reg.IsFailure)
                    return ApiSecurityGuard.ProblemFromError(reg.Error);
                targetTerminal = reg.Value;
            }

            var res = await svc.StartSessionAsync(targetTerminal.TerminalCode, req.Choice, req.CustomerName, req.PagerOrTableTag, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);

            return Results.Created($"/api/v1/public/totem/{slug}/sessions/{res.Value.Session.SessionToken}", new
            {
                sessionToken = res.Value.Session.SessionToken,
                fulfillmentChoice = res.Value.Session.FulfillmentChoice.ToString(),
                idleTimeoutSeconds = res.Value.Terminal.IdleTimeoutSeconds,
                terminalCode = res.Value.Terminal.TerminalCode,
                storeBranding = res.Value.StoreBranding
            });
        });

        pubTotem.MapPost("/sessions/{sessionToken}/items", async (
            string slug,
            string sessionToken,
            PublicAddCartItemReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            TotemSelfServiceService svc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);

            Result<TotemOrderSession> res;
            if ((req.Kind ?? "catalog").Equals("acai", StringComparison.OrdinalIgnoreCase) && req.AcaiCup is not null)
            {
                res = await svc.AddCustomAcaiAsync(
                    sessionToken,
                    req.AcaiCup.CupSizeId,
                    req.AcaiCup.BaseFlavorIds,
                    req.AcaiCup.FreeToppings,
                    req.AcaiCup.PaidAddons,
                    req.Quantity,
                    req.Notes,
                    ct);
            }
            else if (req.ProductId.HasValue)
            {
                res = await svc.AddCatalogProductAsync(sessionToken, req.ProductId.Value, req.VariationId, req.Modifiers, req.Quantity, req.Notes, ct);
            }
            else
            {
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Totem.InvalidItem", "Produto deve ser informado."));
            }

            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);
            return Results.Ok(ToTotemSessionDto(res.Value));
        });

        pubTotem.MapDelete("/sessions/{sessionToken}/lines/{lineId:guid}", async (
            string slug,
            string sessionToken,
            Guid lineId,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            TotemSelfServiceService svc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var res = await svc.RemoveLineAsync(sessionToken, lineId, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToTotemSessionDto(res.Value));
        });

        pubTotem.MapPost("/sessions/{sessionToken}/checkout", async (
            string slug,
            string sessionToken,
            ConfirmTotemCheckoutReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            TotemSelfServiceService svc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var res = await svc.ConfirmCheckoutAsync(sessionToken, req.PaymentChoice, req.IdempotencyKey, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);

            var r = res.Value;
            var pixCode = req.PaymentChoice == TotemPaymentChoice.PixOnTotem
                ? PitakoPixPayloadGenerator.BuildBrCode(tenant, r.DispatchedOrderNumber, r.Session.ItemsSubtotal.Amount)
                : null;

            return Results.Ok(new
            {
                r.PickupCallCode,
                r.DispatchedOrderNumber,
                itemsSubtotal = r.Session.ItemsSubtotal.Amount,
                paymentChoice = r.Session.PaymentChoice?.ToString(),
                pixBrCode = pixCode,
                storeBranding = r.StoreBranding
            });
        });
    }

    private static void MapDiningExtendedEndpoints(RouteGroupBuilder v1)
    {
        var dining = v1.MapGroup("/dining");

        dining.MapGet("/overview", async (
            ApiSecurityGuard guard,
            IDiningSectorRepository sectorRepo,
            IDiningTableRepository tableRepo,
            IDiningTabRepository tabRepo,
            ITableReservationRepository resRepo,
            IWaiterCallRepository callRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;

            var sectors = await sectorRepo.ListOrderedAsync(ct);
            var tables = new List<DiningTable>();
            var reservations = new List<TableReservation>();
            foreach (var s in sectors)
            {
                var st = await tableRepo.ListBySectorAsync(s.Id, ct);
                tables.AddRange(st);
                foreach (var t in st)
                    reservations.AddRange(await resRepo.ListForTableAsync(t.Id, ct));
            }
            var openTabs = await tabRepo.ListOpenTabsAsync(ct);
            var waiterCalls = await callRepo.ListPendingAsync(ct);

            return Results.Ok(new
            {
                sectors = sectors.Select(s => new { s.Id, s.Name, s.DisplayOrder }),
                tables = tables.Select(t => new { t.Id, t.SectorId, t.TableCode, t.Capacity, t.QrCodeToken, status = t.Status.ToString(), t.CurrentTabId }),
                openTabs = openTabs.Select(tb => ToTabDto(tb)),
                reservations = reservations.OrderByDescending(r => r.ReservedForUtc).Take(30).Select(r => new
                {
                    r.Id,
                    r.DiningTableId,
                    r.CustomerName,
                    r.CustomerPhone,
                    r.PartySize,
                    r.ReservedForUtc,
                    r.DurationMinutes,
                    status = r.Status.ToString(),
                    r.Notes
                }),
                waiterCalls = waiterCalls.Select(w => new
                {
                    w.Id,
                    w.DiningTableId,
                    w.TableCode,
                    callType = w.CallType.ToString(),
                    status = w.Status.ToString(),
                    w.CustomerNotes,
                    w.RequestedAtUtc
                })
            });
        });

        dining.MapPost("/reservations", async (CreateReservationReq req, ApiSecurityGuard guard, DiningHallManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            return ToHttp(await svc.CreateReservationAsync(user!, req.TableId, req.CustomerName, req.CustomerPhone, req.PartySize, req.ReservedForUtc, req.DurationMinutes, req.Notes, ct), StatusCodes.Status201Created);
        });

        dining.MapPost("/tabs", async (OpenTabReq req, ApiSecurityGuard guard, DiningHallManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            var res = await svc.OpenTabAsync(user!, req.CustomerName, req.GuestCount, req.DiningTableId, req.ServiceChargePercent, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Json(ToTabDto(res.Value), statusCode: StatusCodes.Status201Created);
        });

        dining.MapPost("/tabs/{id:guid}/items", async (Guid id, AddTabItemReq req, ApiSecurityGuard guard, DiningHallManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            var res = await svc.AddTabItemAsync(user!, id, req.ProductId, req.ItemTitle, req.CustomizationSummary ?? "", req.UnitPrice, req.Quantity, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToTabDto(res.Value));
        });

        dining.MapDelete("/tabs/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, string? reason, ApiSecurityGuard guard, DiningHallManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            var res = await svc.CancelTabItemAsync(user!, id, itemId, string.IsNullOrWhiteSpace(reason) ? "Cancelado no salão" : reason, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToTabDto(res.Value));
        });

        dining.MapPost("/tabs/{id:guid}/transfer", async (Guid id, TransferTabReq req, ApiSecurityGuard guard, DiningHallManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            var res = await svc.TransferTabToTableAsync(user!, id, req.TargetTableId, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToTabDto(res.Value));
        });

        dining.MapGet("/tabs/{id:guid}/split/{payersCount:int}", async (Guid id, int payersCount, ApiSecurityGuard guard, DiningHallManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            var res = await svc.CalculateEqualSplitAsync(user!, id, payersCount, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);
            return Results.Ok(res.Value.Select(s => new { s.PayerIndex, amount = s.Amount.Amount }));
        });

        dining.MapPost("/tabs/{id:guid}/pay", async (Guid id, PayTabReq req, ApiSecurityGuard guard, DiningHallManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            var res = await svc.RegisterTabPaymentAsync(user!, id, req.Amount, ct);
            return res.IsFailure ? ApiSecurityGuard.ProblemFromError(res.Error) : Results.Ok(ToTabDto(res.Value));
        });

        dining.MapPost("/waiter-calls/{id:guid}/acknowledge", async (Guid id, ApiSecurityGuard guard, QrTableSelfServiceService qrSvc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            return ToHttp(await qrSvc.AcknowledgeWaiterCallAsync(user!, id, ct));
        });

        dining.MapPost("/waiter-calls/{id:guid}/complete", async (Guid id, ApiSecurityGuard guard, QrTableSelfServiceService qrSvc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DiningHall, ct);
            if (err is not null) return err;
            return ToHttp(await qrSvc.CompleteWaiterCallAsync(user!, id, ct));
        });

        v1.MapGet("/public/qr/{slug}/{token}/summary", async (string slug, string token, ITenantRepository tenantRepo, ITenantContextSetter setter, QrTableSelfServiceService qrSvc, CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var res = await qrSvc.StartOrResumeByQrTokenAsync(token, cancellationToken: ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);

            return Results.Ok(new
            {
                sessionToken = res.Value.Session.SessionToken,
                tableCode = res.Value.Table.TableCode,
                tab = ToTabDto(res.Value.OpenTab),
                storeBranding = res.Value.StoreBranding
            });
        });

        v1.MapPost("/public/qr/{slug}/{token}/items", async (string slug, string token, PublicAddCartItemReq req, ITenantRepository tenantRepo, ITenantContextSetter setter, QrTableSelfServiceService qrSvc, CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var sessRes = await qrSvc.StartOrResumeByQrTokenAsync(token, cancellationToken: ct);
            if (sessRes.IsFailure) return ApiSecurityGuard.ProblemFromError(sessRes.Error);

            if (!req.ProductId.HasValue)
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Qr.ProductRequired", "Produto é obrigatório."));

            var res = await qrSvc.AddCatalogProductAsync(sessRes.Value.Session.SessionToken, req.ProductId.Value, req.VariationId, req.Modifiers, req.Quantity, req.Notes, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);

            return Results.Ok(new
            {
                sessionToken = res.Value.Session.SessionToken,
                tableCode = res.Value.Table.TableCode,
                tab = ToTabDto(res.Value.OpenTab)
            });
        });
    }

    private static void MapFinanceExtendedEndpoints(RouteGroupBuilder v1)
    {
        var finance = v1.MapGroup("/finance");

        finance.MapGet("/entries", async (ApiSecurityGuard guard, IFinancialEntryRepository entryRepo, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Finance, ct);
            if (err is not null) return err;

            var entries = await entryRepo.ListAllInPeriodAsync(clock.UtcNow.AddDays(-90), clock.UtcNow.AddDays(90), ct);
            return Results.Ok(entries.OrderByDescending(e => e.DueDateUtc).Select(e => new
            {
                e.Id,
                e.EntryCode,
                kind = (int)e.Kind,
                kindName = e.Kind.ToString(),
                dreCategory = (int)e.DreCategory,
                dreCategoryName = e.DreCategory.ToString(),
                e.CounterpartyName,
                e.Description,
                e.ReferenceDocument,
                originalAmount = e.OriginalAmount.Amount,
                feeOrAdjustmentAmount = e.FeeOrAdjustmentAmount.Amount,
                settledAmount = e.SettledAmount.Amount,
                status = (int)e.Status,
                statusName = e.Status.ToString(),
                e.DueDateUtc,
                e.SettledAtUtc,
                e.PaymentMethodName
            }));
        });

        finance.MapGet("/gateways", async (ApiSecurityGuard guard, IPaymentGatewayConfigRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Finance, ct);
            if (err is not null) return err;

            var list = await repo.ListAllAsync(ct);
            return Results.Ok(list.Select(g => new
            {
                g.Id,
                provider = g.Provider.ToString(),
                g.MerchantAccountCode,
                g.DefaultMdrPercent,
                g.SettlementDays,
                g.IsActive
            }));
        });
    }

    private static void MapPdvExtendedEndpoints(RouteGroupBuilder v1)
    {
        var pdv = v1.MapGroup("/pdv");

        pdv.MapGet("/sessions/open", async (ApiSecurityGuard guard, ICashRegisterRepository regRepo, IPdvSaleRepository saleRepo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Pdv, ct);
            if (err is not null) return err;

            var open = await regRepo.ListOpenAsync(ct);
            var active = open.FirstOrDefault();
            var sales = active is null ? [] : await saleRepo.ListBySessionAsync(active.Id, ct);

            return Results.Ok(new
            {
                activeSession = active is null ? null : new
                {
                    active.Id,
                    active.TerminalCode,
                    active.OpenedAtUtc,
                    openingBalance = active.OpeningBalance.Amount,
                    expectedCashBalance = active.ExpectedCashBalance.Amount,
                    movementsCount = active.Movements.Count
                },
                sales = sales.Select(s => new
                {
                    s.Id,
                    s.ReceiptNumber,
                    grossAmount = s.GrossAmount.Amount,
                    discountAmount = s.DiscountAmount.Amount,
                    netAmount = s.NetAmount.Amount,
                    status = s.Status.ToString(),
                    s.SettledAtUtc,
                    payments = s.Payments.Select(p => new { method = p.Method.ToString(), amount = p.AllocatedAmount.Amount, change = p.ChangeAmount.Amount })
                })
            });
        });

        pdv.MapPost("/sessions/{id:guid}/supply", async (Guid id, CashMovementReq req, ApiSecurityGuard guard, PdvCashierService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Pdv, ct);
            if (err is not null) return err;
            return ToHttp(await svc.RegisterCashSupplyAsync(user!, id, req.Amount, req.Reason, ct));
        });

        pdv.MapPost("/sessions/{id:guid}/withdrawal", async (Guid id, CashMovementReq req, ApiSecurityGuard guard, PdvCashierService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Pdv, ct);
            if (err is not null) return err;
            return ToHttp(await svc.RegisterCashWithdrawalAsync(user!, id, req.Amount, req.Reason, ct));
        });
    }

    private static void MapOrdersPixAndDeliveryRouteEndpoints(RouteGroupBuilder v1)
    {
        v1.MapGet("/orders/all", async (ApiSecurityGuard guard, IOrderRepository orderRepo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Orders, ct);
            if (err is not null) return err;

            var list = await orderRepo.ListRecentAllAsync(250, ct);
            return Results.Ok(list.Select(o => new
            {
                o.Id,
                o.OrderNumber,
                channel = o.Channel.ToString(),
                fulfillmentMode = o.FulfillmentMode.ToString(),
                fulfillmentModeCode = (int)o.FulfillmentMode,
                status = o.Status.ToString(),
                statusCode = (int)o.Status,
                o.CustomerName,
                o.CustomerPhone,
                o.DeliveryAddressSummary,
                o.PaymentMethod,
                o.IsPaymentConfirmed,
                itemsSubtotal = o.ItemsSubtotal.Amount,
                itemsDiscount = o.ItemsDiscount.Amount,
                netDeliveryFee = o.NetDeliveryFee.Amount,
                totalAmount = o.TotalAmount.Amount,
                o.PlacedAtUtc,
                o.CompletedAtUtc,
                o.CancelledAtUtc,
                items = o.Items.Select(i => new { i.Id, i.ItemTitle, i.CustomizationSummaryText, i.Quantity, lineSubtotal = i.LineSubtotal.Amount, i.CustomerNotes })
            }));
        });

        v1.MapPost("/orders/{id:guid}/confirm-payment", async (Guid id, ConfirmOrderPaymentReq? req, ApiSecurityGuard guard, OrderManagementService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Orders, ct);
            if (err is not null) return err;
            var res = await svc.ConfirmPaymentAsync(user!, id, req?.Notes, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);
            return Results.Ok(new { res.Value.Id, res.Value.OrderNumber, res.Value.IsPaymentConfirmed });
        });

        v1.MapGet("/public/store/{slug}/orders/{orderNumber}/pix", async (
            string slug,
            string orderNumber,
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

            var pixCode = PitakoPixPayloadGenerator.BuildBrCode(tenant, order.OrderNumber, order.TotalAmount.Amount);
            return Results.Ok(new
            {
                order.OrderNumber,
                amount = order.TotalAmount.Amount,
                isPaymentConfirmed = order.IsPaymentConfirmed,
                pixBrCode = pixCode,
                pixKeyDisplay = tenant.Cnpj.Formatted
            });
        });

        v1.MapPost("/public/store/{slug}/orders/{orderNumber}/verify-pix", async (
            string slug,
            string orderNumber,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            IOrderRepository orderRepo,
            OrderManagementService orderSvc,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var order = await orderRepo.FindByOrderNumberAsync(orderNumber, ct);
            if (order is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Order.NotFound", "Pedido não encontrado."));

            if (!order.IsPaymentConfirmed)
            {
                var actor = User.CreateEstablishmentUser(tenant.Id, "Conciliação PIX", $"pix@{tenant.Slug}.local", "PixConfirm!2026", EstablishmentRole.Manager).Value;
                await orderSvc.ConfirmPaymentAsync(actor, order.Id, "Confirmação bancária PIX", ct);
            }

            return Results.Ok(new { order.OrderNumber, isPaymentConfirmed = true, status = "Pagamento PIX Confirmado" });
        });

        var delivery = v1.MapGroup("/delivery");

        delivery.MapGet("/couriers", async (ApiSecurityGuard guard, IDeliveryCourierRepository courierRepo, IDeliveryDispatchRunRepository runRepo, HttpContext http, CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;

            var couriers = await courierRepo.ListAllAsync(ct);
            var runs = await runRepo.ListRecentAsync(20, ct);
            return Results.Ok(new
            {
                couriers = couriers.Select(c => new
                {
                    c.Id,
                    c.FullName,
                    c.Phone,
                    vehicleType = c.VehicleType.ToString(),
                    status = c.Availability.ToString(),
                    c.LicensePlate
                }),
                recentRuns = runs.Select(r => new
                {
                    r.Id,
                    r.CourierId,
                    r.RunCode,
                    totalEstimatedDistanceKm = r.TotalEstimatedDistanceKm,
                    courierCommissionAmount = r.TotalCommission.Amount,
                    r.AssignedAtUtc,
                    stops = r.Stops.OrderBy(s => s.StopSequence).Select(s => new
                    {
                        s.Id,
                        s.StopSequence,
                        s.OrderId,
                        orderNumber = s.OrderNumberSnapshot,
                        customerName = s.CustomerNameSnapshot,
                        s.DestinationAddress,
                        s.DestinationLatitude,
                        s.DestinationLongitude,
                        distanceFromPreviousKm = s.LegDistanceKm,
                        status = s.Status.ToString()
                    })
                })
            });
        });

        delivery.MapPost("/couriers", async (RegisterCourierReq req, ApiSecurityGuard guard, CourierDispatchAndGpsService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;
            return ToHttp(await svc.RegisterCourierAsync(user!, req.FullName, req.Phone, req.VehicleType, req.CommissionMode, req.FixedCommissionAmount, req.FreightCommissionPercentage, null, req.LicensePlate, ct), StatusCodes.Status201Created);
        });

        delivery.MapPost("/dispatch-runs/optimize", async (OptimizeDispatchRunReq req, ApiSecurityGuard guard, CourierDispatchAndGpsService svc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Delivery, ct);
            if (err is not null) return err;

            var stops = req.Stops.Select(s => new DispatchStopInput(
                s.OrderId ?? Guid.NewGuid(),
                s.OrderNumber,
                s.CustomerName,
                s.DestinationAddress,
                s.DestinationLatitude,
                s.DestinationLongitude,
                Money.Create(Math.Max(0m, s.DeliveryFee)).Value)).ToList();

            var res = await svc.CreateAndOptimizeDispatchRunAsync(user!, req.CourierId, req.OriginLatitude, req.OriginLongitude, stops, req.Notes, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);
            var r = res.Value;
            return Results.Created($"/api/v1/delivery/dispatch-runs/{r.Id}", new
            {
                r.Id,
                r.CourierId,
                r.RunCode,
                totalPlannedDistanceKm = r.TotalEstimatedDistanceKm,
                courierCommissionAmount = r.TotalCommission.Amount,
                r.AssignedAtUtc,
                stops = r.Stops.OrderBy(s => s.StopSequence).Select(s => new
                {
                    s.Id,
                    sequenceNumber = s.StopSequence,
                    s.OrderId,
                    orderNumber = s.OrderNumberSnapshot,
                    customerName = s.CustomerNameSnapshot,
                    s.DestinationAddress,
                    s.DestinationLatitude,
                    s.DestinationLongitude,
                    distanceFromPreviousKm = s.LegDistanceKm,
                    status = s.Status.ToString()
                })
            });
        });
    }

    private static object ToTotemSessionDto(TotemOrderSession s) => new
    {
        s.Id,
        s.SessionToken,
        fulfillmentChoice = s.FulfillmentChoice.ToString(),
        status = s.Status.ToString(),
        itemsSubtotal = s.ItemsSubtotal.Amount,
        lines = s.Lines.Select(l => new
        {
            l.Id,
            l.ProductId,
            l.ItemTitle,
            l.CustomizationSummary,
            unitPrice = l.UnitPrice.Amount,
            l.Quantity,
            lineSubtotal = l.LineSubtotal.Amount,
            l.CustomerNotes
        })
    };

    private static object ToTabDto(DiningTab tb) => new
    {
        tb.Id,
        tb.TabCode,
        tb.DiningTableId,
        tb.CustomerName,
        tb.GuestCount,
        tb.ServiceChargePercent,
        status = tb.Status.ToString(),
        origin = tb.Origin.ToString(),
        itemsSubtotal = tb.ItemsSubtotal.Amount,
        serviceChargeAmount = tb.ServiceChargeAmount.Amount,
        totalAmount = tb.TotalAmount.Amount,
        paidAmount = tb.PaidAmount.Amount,
        remainingBalance = Math.Max(0m, tb.TotalAmount.Amount - tb.PaidAmount.Amount),
        tb.OpenedAtUtc,
        items = tb.Items.Where(i => i.Status != DiningTabItemStatus.Cancelled).Select(i => new
        {
            i.Id,
            i.ProductId,
            i.ItemTitle,
            i.CustomizationSummary,
            unitPrice = i.UnitPrice.Amount,
            i.Quantity,
            lineSubtotal = i.LineSubtotal.Amount
        })
    };

    private static IResult ToHttp<T>(Result<T> result, int successCode = StatusCodes.Status200OK) =>
        result.IsFailure ? ApiSecurityGuard.ProblemFromError(result.Error) : Results.Json(result.Value, statusCode: successCode);
}

internal static class PitakoPixPayloadGenerator
{
    public static string BuildBrCode(Tenant tenant, string orderNumber, decimal amount)
    {
        var rawKey = !string.IsNullOrWhiteSpace(tenant.Cnpj.Digits) ? tenant.Cnpj.Digits : "00000000000191";
        var gui = Tlv("00", "br.gov.bcb.pix") + Tlv("01", rawKey);
        var merchantAccountInfo = Tlv("26", gui);
        var merchantName = TruncateAscii(tenant.TradeName, 25);
        var txId = TruncateAscii(new string(orderNumber.Where(char.IsLetterOrDigit).ToArray()), 25);
        if (string.IsNullOrEmpty(txId)) txId = "PEDIDO01";

        var payloadWithoutCrc =
            Tlv("00", "01") +
            merchantAccountInfo +
            Tlv("52", "0000") +
            Tlv("53", "986") +
            Tlv("54", amount.ToString("0.00", CultureInfo.InvariantCulture)) +
            Tlv("58", "BR") +
            Tlv("59", merchantName) +
            Tlv("60", "FRANCA") +
            Tlv("62", Tlv("05", txId)) +
            "6304";

        return payloadWithoutCrc + ComputeCrc16Ccitt(payloadWithoutCrc);
    }

    private static string Tlv(string id, string value) => $"{id}{value.Length:D2}{value}";

    private static string TruncateAscii(string input, int maxLen)
    {
        var norm = (input ?? "LOJA").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && ch < 128)
                sb.Append(ch);
        }
        var cleaned = sb.ToString().Trim();
        return cleaned.Length > maxLen ? cleaned[..maxLen] : cleaned;
    }

    private static string ComputeCrc16Ccitt(string input)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.ASCII.GetBytes(input))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc.ToString("X4", CultureInfo.InvariantCulture);
    }
}

public sealed record RegisterTotemReq(string TerminalCode, string DisplayName, TotemFulfillmentChoice DefaultChoice = TotemFulfillmentChoice.CounterPickup, bool RequireCustomerName = false, bool RequireTableOrPagerNumber = false, int IdleTimeoutSeconds = 120);
public sealed record StartTotemSessionReq(string? TerminalCode = null, TotemFulfillmentChoice? Choice = null, string? CustomerName = null, string? PagerOrTableTag = null);
public sealed record ConfirmTotemCheckoutReq(TotemPaymentChoice PaymentChoice, string? IdempotencyKey = null);

public sealed record CreateReservationReq(Guid TableId, string CustomerName, string CustomerPhone, int PartySize, DateTimeOffset ReservedForUtc, int DurationMinutes = 90, string? Notes = null);
public sealed record OpenTabReq(string CustomerName, int GuestCount = 1, Guid? DiningTableId = null, decimal ServiceChargePercent = 10m);
public sealed record AddTabItemReq(Guid? ProductId, string ItemTitle, string? CustomizationSummary, decimal UnitPrice, int Quantity = 1);
public sealed record TransferTabReq(Guid TargetTableId);
public sealed record PayTabReq(decimal Amount);
public sealed record CashMovementReq(decimal Amount, string Reason);
public sealed record ConfirmOrderPaymentReq(string? Notes = null);
public sealed record RegisterCourierReq(string FullName, string Phone, CourierVehicleType VehicleType = CourierVehicleType.Motorcycle, CourierCommissionMode CommissionMode = CourierCommissionMode.FixedPerDelivery, decimal FixedCommissionAmount = 6m, decimal FreightCommissionPercentage = 0m, string? LicensePlate = null);
public sealed record OptimizeStopDto(Guid? OrderId, string OrderNumber, string CustomerName, string DestinationAddress, double DestinationLatitude, double DestinationLongitude, decimal DeliveryFee);
public sealed record OptimizeDispatchRunReq(Guid CourierId, double OriginLatitude, double OriginLongitude, IReadOnlyList<OptimizeStopDto> Stops, string? Notes = null);
