using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Pitako.Modules.Marketplaces.Application.Abstractions;
using Pitako.Modules.Marketplaces.Application.Services;
using Pitako.Modules.Marketplaces.Domain;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Application.Services;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.PdvCashier.Application.Abstractions;
using Pitako.Modules.PdvCashier.Application.Services;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Domain;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
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
        MapMarketplacesExtendedEndpoints(v1);
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

            var list = await orderRepo.ListRecentAllAsync(500, ct);
            static string CustomerOrderGroupKey(Order ord)
            {
                var digits = new string((ord.CustomerPhone ?? string.Empty).Where(char.IsDigit).ToArray());
                if (digits.Length >= 8) return $"p:{digits}";
                return $"n:{(ord.CustomerName ?? "cliente").Trim().ToLowerInvariant()}";
            }

            var countsByCustomer = list
                .Where(o => o.Status != OrderStatus.Cancelled)
                .GroupBy(CustomerOrderGroupKey)
                .ToDictionary(g => g.Key, g => g.Count());

            return Results.Ok(list.Take(250).Select(o =>
            {
                var key = CustomerOrderGroupKey(o);
                var totalCustomerOrders = countsByCustomer.TryGetValue(key, out var cnt) ? cnt : 1;
                return new
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
                    customerOrderCount = totalCustomerOrders,
                    customerPreviousOrdersCount = Math.Max(0, totalCustomerOrders - 1),
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
                };
            }));
        });

        v1.MapPost("/orders/{id:guid}/confirm-payment", async (
            Guid id,
            ConfirmOrderPaymentReq? req,
            ApiSecurityGuard guard,
            OrderManagementService svc,
            ITenantRepository tenantRepo,
            IWhatsAppChannelConfigRepository channelRepo,
            IWhatsAppConversationRepository convRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Orders, ct);
            if (err is not null) return err;
            var res = await svc.ConfirmPaymentAsync(user!, id, req?.Notes, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);

            await PitakoStoreSuiteEndpoints.TryNotifyCustomerOrderUpdateAsync(
                user!.TenantId!.Value,
                res.Value,
                $"Pagamento confirmado para seu pedido #{res.Value.OrderNumber}! Já estamos cuidando dele para você.",
                tenantRepo,
                channelRepo,
                convRepo,
                clock.UtcNow,
                ct);

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

    private static void MapMarketplacesExtendedEndpoints(RouteGroupBuilder v1)
    {
        var mp = v1.MapGroup("/marketplaces");

        mp.MapGet("/overview", async (
            ApiSecurityGuard guard,
            ITenantRepository tenantRepo,
            MarketplaceHubService hubSvc,
            IMarketplacePartnerAccountRepository accRepo,
            IMarketplaceStoreBindingRepository bindRepo,
            IMarketplaceInboundEventRepository eventRepo,
            IMarketplaceCatalogMappingRepository mapRepo,
            IMarketplaceBulkJobRepository jobRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Marketplaces, ct);
            if (err is not null) return err;

            var tenant = user!.TenantId.HasValue ? await tenantRepo.FindByIdAsync(user.TenantId.Value, ct) : null;
            var capabilities = hubSvc.ListCapabilitiesMatrix();
            var accounts = await accRepo.ListAllAsync(ct);
            var bindings = await bindRepo.ListAllAsync(ct);
            var events = await eventRepo.ListRecentAsync(40, ct);
            var mappings = await mapRepo.ListAllAsync(ct);
            var jobs = await jobRepo.ListRecentAsync(20, ct);

            return Results.Ok(new
            {
                storeSlug = tenant?.Slug ?? "",
                storeTradeName = tenant?.TradeName ?? "",
                capabilities = capabilities.Select(c => new
                {
                    provider = (int)c.Provider,
                    providerKey = c.Provider.ToString(),
                    c.DisplayName,
                    authKind = c.AuthKind.ToString(),
                    c.SupportsOrderConfirmation,
                    c.SupportsOrderDispatch,
                    c.SupportsOrderCancellation,
                    c.SupportsItemPauseToggle,
                    c.SupportsCatalogPriceSync,
                    c.SupportsStoreOpenCloseToggle,
                    c.SupportsWebhookHmacSignature,
                    c.SupportsBulkMenuActions
                }),
                accounts = accounts.Select(a => new
                {
                    a.Id,
                    provider = (int)a.Provider,
                    providerName = a.Provider.ToString(),
                    a.AccountLabel,
                    a.ClientOrMerchantIdentifier,
                    a.IsActive,
                    a.LastHealthCheckUtc
                }),
                bindings = bindings.Select(b => new
                {
                    b.Id,
                    b.PartnerAccountId,
                    provider = (int)b.Provider,
                    providerName = b.Provider.ToString(),
                    b.ExternalMerchantId,
                    b.ExternalStoreName,
                    status = b.Status.ToString(),
                    statusCode = (int)b.Status,
                    b.AutoAcceptOrders,
                    b.LastSyncUtc,
                    b.LastError
                }),
                inboundEvents = events.Select(e => new
                {
                    e.Id,
                    provider = e.Provider.ToString(),
                    e.ExternalEventId,
                    e.ExternalOrderId,
                    e.ExternalMerchantId,
                    e.EventType,
                    syncStatus = e.SyncStatus.ToString(),
                    e.SignatureVerified,
                    e.NormalizedCustomerName,
                    normalizedTotal = e.NormalizedTotal.Amount,
                    e.InternalOrderId,
                    e.FailureReason,
                    e.ReceivedAtUtc
                }),
                mappings = mappings.Select(m => new
                {
                    m.Id,
                    m.StoreBindingId,
                    m.InternalProductId,
                    m.InternalItemTitle,
                    m.ExternalSkuCode,
                    lastSyncedPrice = m.LastSyncedPrice.Amount,
                    isPausedExternally = m.IsPausedAtPartner
                }),
                bulkJobs = jobs.Select(j => new
                {
                    j.Id,
                    operationType = j.OperationType.ToString(),
                    j.OperationSummary,
                    j.TotalTargets,
                    j.SucceededCount,
                    j.FailedCount,
                    j.CreatedAtUtc
                })
            });
        });

        mp.MapPost("/accounts", async (ConnectMarketplaceAccountReq req, ApiSecurityGuard guard, MarketplaceHubService hubSvc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Marketplaces, ct);
            if (err is not null) return err;
            var res = await hubSvc.ConnectPartnerAccountAsync(user!, req.Provider, req.AccountLabel, req.ClientOrMerchantIdentifier, req.RawSecretPayload, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);
            var a = res.Value;
            return Results.Created($"/api/v1/marketplaces/accounts/{a.Id}", new
            {
                a.Id,
                provider = (int)a.Provider,
                providerName = a.Provider.ToString(),
                a.AccountLabel,
                a.ClientOrMerchantIdentifier,
                a.IsActive
            });
        });

        mp.MapPost("/bindings", async (BindMarketplaceStoreReq req, ApiSecurityGuard guard, MarketplaceHubService hubSvc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Marketplaces, ct);
            if (err is not null) return err;
            var res = await hubSvc.BindExternalStoreAsync(user!, req.PartnerAccountId, req.ExternalMerchantId, req.ExternalStoreName, null, req.AutoAcceptOrders, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);
            var b = res.Value;
            return Results.Created($"/api/v1/marketplaces/bindings/{b.Id}", new
            {
                b.Id,
                b.PartnerAccountId,
                provider = (int)b.Provider,
                providerName = b.Provider.ToString(),
                b.ExternalMerchantId,
                b.ExternalStoreName,
                status = b.Status.ToString(),
                b.AutoAcceptOrders
            });
        });

        mp.MapPost("/catalog-mappings", async (MapMarketplaceCatalogReq req, ApiSecurityGuard guard, MarketplaceHubService hubSvc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Marketplaces, ct);
            if (err is not null) return err;
            var res = await hubSvc.MapCatalogProductAsync(user!, req.StoreBindingId, req.InternalProductId, req.InternalItemTitle, req.ExternalSkuCode, req.CurrentPrice, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);
            var m = res.Value;
            return Results.Created($"/api/v1/marketplaces/catalog-mappings/{m.Id}", new
            {
                m.Id,
                m.StoreBindingId,
                m.InternalProductId,
                m.InternalItemTitle,
                m.ExternalSkuCode,
                lastSyncedPrice = m.LastSyncedPrice.Amount
            });
        });

        mp.MapPost("/bindings/{id:guid}/toggle-store", async (Guid id, ToggleMarketplaceStoreReq req, ApiSecurityGuard guard, MarketplaceBulkSyncService bulkSvc, HttpContext http, CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Marketplaces, ct);
            if (err is not null) return err;
            return ToHttp(await bulkSvc.BulkToggleStoresOpenCloseAsync(user!, [id], req.PauseStore, req.Reason, ct));
        });

        mp.MapPost("/bindings/{id:guid}/simulate-order", async (
            Guid id,
            SimulateMarketplaceOrderReq? req,
            ApiSecurityGuard guard,
            IMarketplaceStoreBindingRepository bindRepo,
            IMarketplacePartnerAccountRepository accRepo,
            ISecretProtector secretProtector,
            MarketplaceHubService hubSvc,
            IOrderRepository orderRepo,
            IOrderRealtimeNotifier realtimeNotifier,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Marketplaces, ct);
            if (err is not null) return err;

            var binding = await bindRepo.FindByIdAsync(id, ct);
            if (binding is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Marketplace.StoreBindingNotFound", "Loja externa vinculada não encontrada."));

            var account = await accRepo.FindByIdAsync(binding.PartnerAccountId, ct);
            if (account is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Marketplace.AccountNotFound", "Conta parceira não encontrada."));

            var extOrderCode = req?.ExternalOrderCode ?? $"MK-{Random.Shared.Next(1000, 9999)}";
            var customer = string.IsNullOrWhiteSpace(req?.CustomerName) ? $"Cliente {binding.Provider}" : req.CustomerName.Trim();
            var itemTitle = string.IsNullOrWhiteSpace(req?.ItemTitle) ? "Açaí Especial 500ml" : req.ItemTitle.Trim();
            var unitPrice = req?.UnitPrice > 0 ? req.UnitPrice : 26.90m;
            var deliveryFee = req?.DeliveryFee >= 0 ? req.DeliveryFee : 5.00m;
            var total = unitPrice + deliveryFee;

            var rawPayload = BuildProviderSampleOrderJson(binding.Provider, extOrderCode, customer, itemTitle, unitPrice, deliveryFee, total);
            var rawSecret = secretProtector.Unprotect(account.EncryptedCredentialsBlob);
            var hmacHex = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(rawSecret.Trim()), Encoding.UTF8.GetBytes(rawPayload))).ToLowerInvariant();
            var eventId = $"evt-{Guid.NewGuid():N}";

            var inboundRes = await hubSvc.ReceiveAndNormalizeWebhookAsync(
                binding.Provider,
                eventId,
                binding.ExternalMerchantId,
                "ORDER_CREATED",
                rawPayload,
                $"sha256={hmacHex}",
                ct);

            if (inboundRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(inboundRes.Error);

            var order = await DispatchNormalizedMarketplaceOrderAsync(
                binding.TenantId,
                binding,
                inboundRes.Value,
                customer,
                itemTitle,
                unitPrice,
                deliveryFee,
                orderRepo,
                realtimeNotifier,
                clock,
                ct);

            return Results.Created($"/api/v1/orders/{order.Id}", new
            {
                inboundEventId = inboundRes.Value.Id,
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                provider = binding.Provider.ToString(),
                status = order.Status.ToString(),
                totalAmount = order.TotalAmount.Amount
            });
        });

        v1.MapPost("/public/webhooks/marketplaces/{provider}/{slug}", async (
            string provider,
            string slug,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            MarketplaceHubService hubSvc,
            IMarketplaceStoreBindingRepository bindRepo,
            IOrderRepository orderRepo,
            IOrderRealtimeNotifier realtimeNotifier,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<MarketplaceProvider>(provider, true, out var parsedProvider))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Marketplace.UnknownProvider", $"Provedor '{provider}' desconhecido."));

            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);

            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync(ct);

            var merchantId = http.Request.Headers["X-Merchant-Id"].FirstOrDefault();
            var eventId = http.Request.Headers["X-Event-Id"].FirstOrDefault() ?? $"wh-{Guid.NewGuid():N}";
            var signature = http.Request.Headers["X-Hub-Signature-256"].FirstOrDefault()
                ?? http.Request.Headers["X-IFood-Signature"].FirstOrDefault()
                ?? http.Request.Headers["X-Signature"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(merchantId) && !string.IsNullOrWhiteSpace(rawBody))
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawBody);
                    if (doc.RootElement.TryGetProperty("merchantId", out var mpProp))
                        merchantId = mpProp.GetString();
                }
                catch { /* parsed below by connector */ }
            }

            if (string.IsNullOrWhiteSpace(merchantId))
                merchantId = (await bindRepo.ListAllAsync(ct)).FirstOrDefault(b => b.Provider == parsedProvider)?.ExternalMerchantId ?? string.Empty;

            var res = await hubSvc.ReceiveAndNormalizeWebhookAsync(parsedProvider, eventId, merchantId, "WEBHOOK_ORDER", rawBody, signature, ct);
            if (res.IsFailure) return ApiSecurityGuard.ProblemFromError(res.Error);

            var binding = res.Value.StoreBindingId.HasValue ? await bindRepo.FindByIdAsync(res.Value.StoreBindingId.Value, ct) : null;
            var order = await DispatchNormalizedMarketplaceOrderAsync(
                tenant.Id,
                binding,
                res.Value,
                res.Value.NormalizedCustomerName ?? $"Cliente {parsedProvider}",
                "Pedido Marketplace",
                Math.Max(0m, res.Value.NormalizedTotal.Amount),
                0m,
                orderRepo,
                realtimeNotifier,
                clock,
                ct);

            return Results.Ok(new
            {
                inboundEventId = res.Value.Id,
                orderNumber = order.OrderNumber,
                provider = parsedProvider.ToString(),
                status = order.Status.ToString()
            });
        });
    }

    private static async Task<Order> DispatchNormalizedMarketplaceOrderAsync(
        Guid tenantId,
        MarketplaceStoreBinding? binding,
        MarketplaceInboundEvent inbound,
        string customerName,
        string fallbackItemTitle,
        decimal fallbackUnitPrice,
        decimal deliveryFeeAmount,
        IOrderRepository orderRepo,
        IOrderRealtimeNotifier realtimeNotifier,
        IClock clock,
        CancellationToken ct)
    {
        var existingByKey = await orderRepo.FindByIdempotencyKeyAsync($"mp-{inbound.Provider}-{inbound.ExternalEventId}", ct);
        if (existingByKey is not null)
            return existingByKey;

        var utcNow = clock.UtcNow;
        var cart = ShoppingCart.Create(tenantId, $"mp-{Guid.NewGuid():N}", utcNow.AddHours(1)).Value;
        var items = new List<MarketplaceNormalizedOrderItem>();
        try
        {
            items = JsonSerializer.Deserialize<List<MarketplaceNormalizedOrderItem>>(inbound.NormalizedItemsJson) ?? [];
        }
        catch { /* fallback item used below */ }

        if (items.Count == 0)
        {
            items.Add(new MarketplaceNormalizedOrderItem("MK-SKU-01", fallbackItemTitle, 1, fallbackUnitPrice, "Padrão Marketplace"));
        }

        foreach (var item in items)
        {
            cart.AddItem(
                CartItemKind.CatalogProduct,
                null,
                null,
                null,
                null,
                null,
                item.Title,
                item.CustomizationSummary,
                "{}",
                Money.Create(Math.Max(0m, item.UnitPriceAmount)).Value,
                Math.Max(1, item.Quantity),
                false,
                item.Notes,
                utcNow,
                forceSeparateLine: true);
        }

        var seq = await orderRepo.CountAllAsync(ct) + 1;
        var prefix = inbound.Provider switch
        {
            MarketplaceProvider.IFood => "IF",
            MarketplaceProvider.Food99 => "99",
            MarketplaceProvider.Rappi => "RP",
            MarketplaceProvider.Aiqfome => "AQ",
            _ => "MK"
        };
        var orderNumber = $"{prefix}-{seq:D4}";
        var fulfillment = inbound.NormalizedFulfillment == MarketplaceFulfillmentKind.CounterPickup
            ? FulfillmentMode.CounterPickup
            : FulfillmentMode.Delivery;

        var order = Order.CreateFromCart(
            cart,
            orderNumber,
            OrderChannel.Marketplace,
            fulfillment,
            customerName,
            null,
            $"{inbound.Provider} · Pedido {inbound.ExternalOrderId}",
            Money.Create(Math.Max(0m, deliveryFeeAmount)).Value,
            Money.Zero(),
            Money.Zero(),
            null,
            binding?.StoreUnitId,
            null,
            $"mp-{inbound.Provider}-{inbound.ExternalEventId}",
            utcNow,
            $"{inbound.Provider} (Online)").Value;

        order.ConfirmPayment(Guid.Empty, $"Pago online via {inbound.Provider}", utcNow);
        if (binding?.AutoAcceptOrders == true)
        {
            order.TransitionTo(OrderStatus.Accepted, Guid.Empty, $"Aceite automático {inbound.Provider}", utcNow);
        }

        await orderRepo.AddAsync(order, ct);
        await orderRepo.SaveChangesAsync(ct);

        await realtimeNotifier.NotifyAsync(new OrderRealtimeEvent(
            order.TenantId,
            order.Id,
            order.OrderNumber,
            "OrderPlaced",
            order.Status,
            utcNow), ct);

        return order;
    }

    private static string BuildProviderSampleOrderJson(
        MarketplaceProvider provider,
        string extOrderCode,
        string customer,
        string itemTitle,
        decimal unitPrice,
        decimal deliveryFee,
        decimal total) => provider switch
        {
            MarketplaceProvider.Food99 => JsonSerializer.Serialize(new
            {
                orderNo = extOrderCode,
                pickupCode = extOrderCode[^4..],
                buyerName = customer,
                freightAmount = deliveryFee,
                promoAmount = 0m,
                payAmount = total,
                deliverType = "DELIVERY",
                dishes = new[] { new { dishCode = "SKU-99-01", dishName = itemTitle, count = 1, price = unitPrice, customization = "Padrão" } }
            }),
            MarketplaceProvider.Rappi => JsonSerializer.Serialize(new
            {
                order_id = extOrderCode,
                short_code = extOrderCode[^4..],
                client_name = customer,
                delivery_fee = deliveryFee,
                discount_value = 0m,
                total_value = total,
                delivery_method = "DELIVERY",
                items = new[] { new { sku = "SKU-RP-01", name = itemTitle, units = 1, price = unitPrice, customization = "Padrão" } }
            }),
            MarketplaceProvider.Aiqfome => JsonSerializer.Serialize(new
            {
                pedido_id = extOrderCode,
                codigo = extOrderCode[^4..],
                cliente_nome = customer,
                taxa_entrega = deliveryFee,
                desconto = 0m,
                valor_total = total,
                modalidade = "DELIVERY",
                itens = new[] { new { codigo_pdv = "SKU-AQ-01", nome = itemTitle, quantidade = 1, valor_unitario = unitPrice, customization = "Padrão" } }
            }),
            MarketplaceProvider.DeliveryMuch => JsonSerializer.Serialize(new
            {
                orderCode = extOrderCode,
                shortCode = extOrderCode[^4..],
                customer,
                shipping = deliveryFee,
                voucher = 0m,
                totalValue = total,
                fulfillment = "DELIVERY",
                products = new[] { new { code = "SKU-DM-01", title = itemTitle, qty = 1, unitValue = unitPrice } }
            }),
            MarketplaceProvider.Keeta => JsonSerializer.Serialize(new
            {
                orderSn = extOrderCode,
                daySeq = extOrderCode[^4..],
                recipientName = customer,
                deliveryAmount = deliveryFee,
                discountAmount = 0m,
                orderAmount = total,
                deliveryMode = "DELIVERY",
                skuList = new[] { new { outerSkuId = "SKU-KT-01", skuName = itemTitle, quantity = 1, price = unitPrice } }
            }),
            MarketplaceProvider.FoodToSave => JsonSerializer.Serialize(new
            {
                bagOrderCode = extOrderCode,
                pickupPin = extOrderCode[^4..],
                consumerName = customer,
                deliveryFee,
                discount = 0m,
                totalAmount = total,
                pickupType = "TAKEOUT",
                bags = new[] { new { bagSku = "BAG-FTS-01", bagTitle = itemTitle, quantity = 1, bagPrice = unitPrice } }
            }),
            _ => JsonSerializer.Serialize(new
            {
                orderId = extOrderCode,
                displayId = extOrderCode[^4..],
                customerName = customer,
                deliveryFee,
                discounts = 0m,
                totalAmount = total,
                orderType = "DELIVERY",
                items = new[] { new { externalCode = "SKU-IF-01", name = itemTitle, quantity = 1, unitPrice, customization = "Padrão" } }
            })
        };

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
    public static string BuildBrCode(Tenant tenant, string orderNumber, decimal amount, string? overridePixKey = null)
    {
        var rawKey = !string.IsNullOrWhiteSpace(overridePixKey)
            ? overridePixKey.Trim()
            : (!string.IsNullOrWhiteSpace(tenant.Cnpj.Digits) ? tenant.Cnpj.Digits : "00000000000191");
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
public sealed record ConnectMarketplaceAccountReq(MarketplaceProvider Provider, string AccountLabel, string ClientOrMerchantIdentifier, string RawSecretPayload);
public sealed record BindMarketplaceStoreReq(Guid PartnerAccountId, string ExternalMerchantId, string ExternalStoreName, bool AutoAcceptOrders = false);
public sealed record MapMarketplaceCatalogReq(Guid StoreBindingId, Guid InternalProductId, string InternalItemTitle, string ExternalSkuCode, decimal CurrentPrice);
public sealed record ToggleMarketplaceStoreReq(bool PauseStore, string? Reason = null);
public sealed record SimulateMarketplaceOrderReq(string? ExternalOrderCode = null, string? CustomerName = null, string? ItemTitle = null, decimal UnitPrice = 26.90m, decimal DeliveryFee = 5.00m);
