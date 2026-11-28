using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pitako.Api.Security;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Api.Endpoints;

public static partial class PitakoStoreSuiteEndpoints
{
    private static void MapMediaAndSegmentArtEndpoints(RouteGroupBuilder v1)
    {
        v1.MapPost("/catalog/upload-image", async (
            UploadMediaReq req,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;

            var stored = await StoreUploadedImageAsync(settingRepo, user!.TenantId!.Value, req.Base64DataUrl, clock.UtcNow, ct);
            if (stored.IsFailure)
                return ApiSecurityGuard.ProblemFromError(stored.Error);

            return Results.Ok(new
            {
                mediaId = stored.Value.MediaId,
                url = $"/api/v1/public/media/{stored.Value.MediaId}",
                contentType = stored.Value.ContentType
            });
        });

        v1.MapPost("/store/branding/upload-logo", async (
            UploadMediaReq req,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            IStoreVisualIdentityRepository identityRepo,
            StoreBrandingService brandingSvc,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Tenants, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var stored = await StoreUploadedImageAsync(settingRepo, tenantId, req.Base64DataUrl, clock.UtcNow, ct);
            if (stored.IsFailure)
                return ApiSecurityGuard.ProblemFromError(stored.Error);

            var mediaUrl = $"/api/v1/public/media/{stored.Value.MediaId}";
            var identity = await identityRepo.FindCurrentTenantIdentityAsync(ct);
            var draft = identity?.GetDraftPreview();
            if (draft is not null)
            {
                await brandingSvc.UpdateDraftAsync(
                    user,
                    tenantId,
                    new UpdateStoreBrandingDraftCommand(
                        mediaUrl,
                        draft.CoverImageUrl,
                        draft.Slogan,
                        draft.PrimaryColorHex,
                        draft.SecondaryColorHex,
                        draft.AccentColorHex,
                        draft.MenuLayoutMode,
                        draft.Banners,
                        draft.OperatingHoursJson,
                        draft.AnnouncementText,
                        draft.WhatsAppNumber,
                        draft.InstagramHandle),
                    ct);
                await brandingSvc.PublishBrandingAsync(user, tenantId, ct);
            }

            return Results.Ok(new { mediaId = stored.Value.MediaId, url = mediaUrl });
        });

        v1.MapGet("/public/media/{mediaId}", async (
            string mediaId,
            IGlobalSettingRepository settingRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var cleanId = Regex.Replace(mediaId ?? string.Empty, "[^a-zA-Z0-9_-]", string.Empty);
            if (string.IsNullOrWhiteSpace(cleanId))
                return Results.NotFound();

            var metaSetting = await settingRepo.FindByKeyAsync($"media:{cleanId}:meta", ct);
            if (metaSetting is null)
                return Results.NotFound();

            var parts = metaSetting.Value.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var chunkCount) || chunkCount <= 0)
                return Results.NotFound();

            var sb = new StringBuilder();
            for (var i = 0; i < chunkCount; i++)
            {
                var c = await settingRepo.FindByKeyAsync($"media:{cleanId}:c{i}", ct);
                if (c is null) return Results.NotFound();
                sb.Append(c.Value);
            }

            try
            {
                var bytes = Convert.FromBase64String(sb.ToString());
                http.Response.Headers.CacheControl = "public, max-age=31536000";
                return Results.Bytes(bytes, parts[0]);
            }
            catch
            {
                return Results.NotFound();
            }
        });

        v1.MapGet("/public/segment-art/{segment}/{key}", (string segment, string key, HttpContext http) =>
        {
            var svg = BuildSegmentVectorArtwork(segment, key);
            http.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.Content(svg, "image/svg+xml", Encoding.UTF8);
        });
    }

    private static void MapProductFullConfigAndQrPayEndpoints(RouteGroupBuilder v1)
    {
        v1.MapPut("/catalog/products/{id:guid}/full-config", async (
            Guid id,
            FullProductConfigReq req,
            ApiSecurityGuard guard,
            IProductRepository productRepo,
            IModifierGroupRepository groupRepo,
            CatalogManagementService catalogSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;

            var prod = await productRepo.FindByIdAsync(id, ct);
            if (prod is null || prod.TenantId != user!.TenantId)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Product.NotFound", "Produto não encontrado."));

            var firstSizePrice = req.Sizes is { Count: > 0 } ? req.Sizes.Min(s => s.Price) : req.BasePrice;
            var tagsList = (prod.TagsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !t.Equals("promo", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (req.IsPromotion) tagsList.Add("promo");

            var updRes = await catalogSvc.UpdateProductAsync(
                user,
                id,
                req.CategoryId != Guid.Empty ? req.CategoryId : prod.CategoryId,
                req.Name.Trim(),
                Math.Max(0.01m, firstSizePrice),
                req.Description,
                req.Sku is not null ? req.Sku : prod.Sku,
                req.IsFeatured ?? prod.IsFeatured,
                req.DisplayOrder ?? prod.DisplayOrder,
                prod.SaleMode,
                req.IsNew,
                prod.IsRecommended,
                req.PricingStrategy ?? prod.PricingStrategy,
                string.Join(",", tagsList),
                req.PreparationMinutes ?? prod.PreparationMinutes,
                prod.CustomTemplateKey,
                req.AllowsNotes ?? prod.AllowsNotes,
                ct,
                req.PromotionalPrice,
                req.TrackInventory,
                req.StockQuantity,
                req.AutoPauseOnZeroStock,
                req.ServesPeopleCount,
                req.DietaryTagsCsv,
                req.AvailabilityScheduleJson,
                req.IsActive);

            if (updRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(updRes.Error);

            var currentPrimary = prod.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl ?? prod.Images.FirstOrDefault()?.ImageUrl;
            if (!string.IsNullOrWhiteSpace(req.ImageUrl) &&
                !req.ImageUrl.Trim().Equals(currentPrimary, StringComparison.OrdinalIgnoreCase))
            {
                await catalogSvc.AddProductImageAsync(user, id, req.ImageUrl.Trim(), 0, true, ct);
            }

            // Sincronizar Grupos de Opções (Disponíveis + Adicionais Extras)
            var allGroups = await groupRepo.ListAllAsync(ct);
            var desiredGroupIds = (req.AttachedGroupIds ?? []).ToHashSet();
            foreach (var currentId in prod.ModifierLinks.Select(l => l.ModifierGroupId).ToList())
            {
                if (!desiredGroupIds.Contains(currentId))
                    await catalogSvc.DetachModifierGroupFromProductAsync(user, id, currentId, ct);
            }
            var idx = 0;
            foreach (var gid in desiredGroupIds)
            {
                if (allGroups.Any(g => g.Id == gid))
                    await catalogSvc.AttachModifierGroupToProductAsync(user, id, gid, idx++, ct);
            }

            // Sincronizar Tamanhos / Variações e Regras por Tamanho
            if (req.Sizes is not null)
            {
                var incomingIds = req.Sizes.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();
                foreach (var oldVar in prod.Variations.ToList())
                {
                    if (!incomingIds.Contains(oldVar.Id))
                        await catalogSvc.RemoveProductVariationAsync(user, id, oldVar.Id, ct);
                }

                var paidGroupIds = allGroups
                    .Where(g => desiredGroupIds.Contains(g.Id) && (g.Type == ModifierGroupType.Addon || g.SelectionKind == ModifierGroupKind.PaidAddon))
                    .Select(g => g.Id)
                    .ToList();
                var freeIncludedGroupIds = allGroups
                    .Where(g => desiredGroupIds.Contains(g.Id) && g.Type != ModifierGroupType.Addon && g.SelectionKind != ModifierGroupKind.PaidAddon)
                    .Select(g => g.Id)
                    .ToList();

                var orderIdx = 1;
                foreach (var sz in req.Sizes)
                {
                    if (string.IsNullOrWhiteSpace(sz.VariationName)) continue;
                    var descNote = string.IsNullOrWhiteSpace(sz.Description) ? null : sz.Description.Trim();
                    var costVal = Math.Max(0m, sz.CostAmount ?? 0m);
                    var mode = (sz.AddonMode ?? string.Empty).Trim().ToLowerInvariant();
                    var allowFree = mode switch
                    {
                        "none" or "paid_only" => false,
                        "free_only" or "both" => true,
                        _ => sz.AllowFree
                    };
                    var allowPaid = mode switch
                    {
                        "none" or "free_only" => false,
                        "paid_only" or "both" => true,
                        _ => sz.AllowPaidExtras
                    };
                    Guid? resolvedVarId = null;

                    if (sz.Id.HasValue && prod.Variations.Any(v => v.Id == sz.Id.Value))
                    {
                        var vRes = await catalogSvc.UpdateProductVariationAsync(
                            user,
                            id,
                            sz.Id.Value,
                            sz.VariationName.Trim(),
                            Math.Max(0.01m, sz.Price),
                            sz.Sku,
                            orderIdx++,
                            descNote,
                            sz.MeasureAmount,
                            sz.MeasureUnit,
                            Math.Max(1, sz.MaxFlavors),
                            sz.IsActive ?? true,
                            ct,
                            costVal,
                            sz.TrackInventory,
                            sz.StockQuantity);
                        if (vRes.IsSuccess) resolvedVarId = vRes.Value.Id;
                    }
                    else
                    {
                        var vRes = await catalogSvc.AddProductVariationAsync(
                            user,
                            id,
                            sz.VariationName.Trim(),
                            Math.Max(0.01m, sz.Price),
                            sz.Sku,
                            orderIdx++,
                            descNote,
                            sz.MeasureAmount,
                            sz.MeasureUnit,
                            Math.Max(1, sz.MaxFlavors),
                            ct,
                            costVal,
                            sz.TrackInventory ?? false,
                            sz.StockQuantity ?? 0);
                        if (vRes.IsSuccess) resolvedVarId = vRes.Value.Id;
                    }

                    if (resolvedVarId.HasValue)
                    {
                        var allowedSet = sz.AllowedGroupIds is { Count: > 0 }
                            ? new HashSet<Guid>(sz.AllowedGroupIds)
                            : (sz.AllowedGroupIds is not null ? new HashSet<Guid>() : null);

                        foreach (var fgId in freeIncludedGroupIds)
                        {
                            var grp = allGroups.First(g => g.Id == fgId);
                            var groupAllowedForSize = allowedSet is null || allowedSet.Contains(fgId);

                            if (grp.IsRequired || grp.SelectionKind == ModifierGroupKind.RequiredChoice)
                            {
                                // Grupos obrigatórios (ex.: Base do Açaí, Massa da Pizza) preservam sua própria regra sem herdar o limite de acompanhamentos grátis
                                var reqHidden = !groupAllowedForSize;
                                await catalogSvc.SetVariationModifierRuleAsync(
                                    user,
                                    id,
                                    resolvedVarId.Value,
                                    fgId,
                                    reqHidden ? 0 : Math.Max(1, grp.MinSelections),
                                    reqHidden ? 0 : Math.Max(1, grp.MaxSelections),
                                    reqHidden ? 0 : grp.FreeAllowance,
                                    !reqHidden && grp.IsRequired,
                                    isHidden: reqHidden,
                                    ct);
                                continue;
                            }

                            if (grp.SelectionKind == ModifierGroupKind.IngredientRemoval)
                            {
                                var prefHidden = !groupAllowedForSize;
                                await catalogSvc.SetVariationModifierRuleAsync(
                                    user,
                                    id,
                                    resolvedVarId.Value,
                                    fgId,
                                    0,
                                    prefHidden ? 0 : grp.MaxSelections,
                                    0,
                                    false,
                                    isHidden: prefHidden,
                                    ct);
                                continue;
                            }

                            var effectiveFree = allowFree ? Math.Max(0, sz.FreeAllowance) : 0;
                            var hideFree = !groupAllowedForSize || !allowFree || (sz.FreeAllowance <= 0 && allowedSet is null);
                            var maxSel = effectiveFree > 0 ? effectiveFree : grp.MaxSelections;
                            await catalogSvc.SetVariationModifierRuleAsync(
                                user,
                                id,
                                resolvedVarId.Value,
                                fgId,
                                0,
                                hideFree ? 0 : Math.Max(1, maxSel),
                                hideFree ? 0 : effectiveFree,
                                false,
                                isHidden: hideFree,
                                ct);
                        }

                        foreach (var pgId in paidGroupIds)
                        {
                            var grp = allGroups.First(g => g.Id == pgId);
                            var groupAllowedForSize = allowedSet is null || allowedSet.Contains(pgId);
                            var hidePaid = !groupAllowedForSize || !allowPaid;
                            await catalogSvc.SetVariationModifierRuleAsync(
                                user,
                                id,
                                resolvedVarId.Value,
                                pgId,
                                0,
                                hidePaid ? 0 : Math.Max(1, grp.MaxSelections),
                                0,
                                false,
                                isHidden: hidePaid,
                                ct);
                        }
                    }
                }
            }

            return Results.Ok(new { saved = true, productId = id });
        });

        v1.MapPut("/inventory/technical-sheets/{id:guid}/quick-ingredients", async (
            Guid id,
            QuickTechSheetReq req,
            ApiSecurityGuard guard,
            ITechnicalSheetRepository sheetRepo,
            ISupplyItemRepository supplyRepo,
            SuppliesAndTechnicalSheetService invSvc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Inventory, ct);
            if (err is not null) return err;

            var sheet = await sheetRepo.FindByIdAsync(id, ct);
            if (sheet is null || sheet.TenantId != user!.TenantId)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("TechnicalSheet.NotFound", "Ficha técnica não encontrada."));

            var ingInputs = new List<IngredientInput>();
            foreach (var row in req.Ingredients)
            {
                Guid supplyId;
                if (row.SupplyItemId.HasValue && row.SupplyItemId.Value != Guid.Empty)
                {
                    supplyId = row.SupplyItemId.Value;
                    if (row.UnitPriceAmount.HasValue && row.UnitPriceAmount.Value > 0m)
                    {
                        var sup = await supplyRepo.FindByIdAsync(supplyId, ct);
                        if (sup is not null)
                        {
                            var pkgPrice = decimal.Round(row.UnitPriceAmount.Value * Math.Max(1m, sup.ContentQuantityPerPackage), 2, MidpointRounding.ToEven);
                            if (Math.Abs(sup.PurchasePrice.Amount - pkgPrice) > 0.01m)
                                await invSvc.UpdateSupplyPriceAsync(user, supplyId, pkgPrice, sup.SupplierName, "Ajuste na ficha técnica", ct);
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(row.NewSupplyName))
                {
                    var uCode = string.IsNullOrWhiteSpace(row.UnitCode) ? "g" : row.UnitCode.Trim().ToLowerInvariant();
                    var unitPrice = Math.Max(0.001m, row.UnitPriceAmount ?? 0.05m);
                    var created = await invSvc.CreateSupplyItemAsync(
                        user,
                        row.NewSupplyName.Trim(),
                        decimal.Round(unitPrice * 1000m, 2, MidpointRounding.ToEven),
                        1m,
                        uCode,
                        supplierName: null,
                        sku: null,
                        contentQuantityPerPackage: 1000m,
                        contentUnitCode: uCode,
                        defaultLossPercent: row.LossPercent,
                        cancellationToken: ct);
                    if (created.IsFailure)
                        return ApiSecurityGuard.ProblemFromError(created.Error);
                    supplyId = created.Value.Id;
                }
                else
                {
                    continue;
                }

                ingInputs.Add(new IngredientInput(supplyId, Math.Max(0.001m, row.Quantity), string.IsNullOrWhiteSpace(row.UnitCode) ? "g" : row.UnitCode.Trim(), row.LossPercent));
            }

            var pkgInputs = req.Packagings.Select(p =>
                new PackagingInput(p.SupplyItemId, p.Description, Math.Max(1m, p.Quantity), p.UnitCode, Math.Max(0m, p.CostAmount))).ToList();

            var updRes = await invSvc.UpdateTechnicalSheetAsync(
                user,
                id,
                string.IsNullOrWhiteSpace(req.Name) ? sheet.Name : req.Name.Trim(),
                Math.Max(0.01m, req.YieldQuantity ?? sheet.YieldQuantity),
                string.IsNullOrWhiteSpace(req.YieldUnitCode) ? sheet.YieldUnitCode : req.YieldUnitCode,
                ingInputs,
                pkgInputs,
                sheet.ProductId,
                req.Notes ?? sheet.Notes,
                ct);

            return updRes.IsFailure
                ? ApiSecurityGuard.ProblemFromError(updRes.Error)
                : Results.Ok(new { saved = true, costPerPortion = updRes.Value.CostPerPortion, totalCost = updRes.Value.TotalCost });
        });

        v1.MapPost("/public/qr/{slug}/{token}/pay", async (
            string slug,
            string token,
            QrPayTableReq req,
            ITenantRepository tenantRepo,
            ITenantContextSetter setter,
            QrTableSelfServiceService qrSvc,
            DiningHallManagementService diningSvc,
            IGlobalSettingRepository settingRepo,
            CancellationToken ct) =>
        {
            var tenant = await tenantRepo.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct);
            if (tenant is null)
                return ApiSecurityGuard.ProblemFromError(Error.NotFound("Store.NotFound", "Loja não encontrada."));

            setter.SetTenant(tenant.Id, TenantResolutionSource.PublicStoreSlug);
            var sessRes = await qrSvc.StartOrResumeByQrTokenAsync(token, cancellationToken: ct);
            if (sessRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(sessRes.Error);

            var tab = sessRes.Value.OpenTab;
            var balance = tab.RemainingBalance.Amount;
            if (balance <= 0m)
            {
                return Results.Ok(new
                {
                    alreadyPaid = true,
                    remainingBalance = 0m,
                    pixBrCode = (string?)null,
                    message = "Comanda já está quitada!"
                });
            }

            var ops = await LoadStoreOpsStateAsync(settingRepo, tenant.Id, tenant, ct);
            var amountToPay = req.SplitCount is > 1
                ? decimal.Round(balance / req.SplitCount.Value, 2, MidpointRounding.ToEven)
                : balance;

            var pixCode = PitakoPixPayloadGenerator.BuildBrCode(tenant, $"MESA{tab.TabCode}", amountToPay, ops.PixKey);
            if (!req.ConfirmImmediatePayment)
            {
                return Results.Ok(new
                {
                    alreadyPaid = false,
                    amountToPay,
                    remainingBalance = balance,
                    pixBrCode = pixCode,
                    pixKeyDisplay = string.IsNullOrWhiteSpace(ops.PixKey) ? tenant.Cnpj.Formatted : ops.PixKey,
                    message = "QR Code PIX da comanda gerado! Escaneie no app do banco e clique em Confirmar Pagamento."
                });
            }

            var actor = User.CreateEstablishmentUser(tenant.Id, "Autoatendimento QR", $"qr@{tenant.Slug}.local", "QrPay!2026", EstablishmentRole.Administrator).Value;
            var payRes = await diningSvc.RegisterTabPaymentAsync(actor, tab.Id, amountToPay, ct);
            if (payRes.IsFailure)
                return ApiSecurityGuard.ProblemFromError(payRes.Error);

            return Results.Ok(new
            {
                alreadyPaid = payRes.Value.RemainingBalance.Amount <= 0m,
                paidAmount = amountToPay,
                remainingBalance = payRes.Value.RemainingBalance.Amount,
                pixBrCode = pixCode,
                message = payRes.Value.RemainingBalance.Amount <= 0m
                    ? "Pagamento confirmado e comanda da mesa quitada!"
                    : $"Parcela de R$ {amountToPay:0.00} registrada. Saldo restante: R$ {payRes.Value.RemainingBalance.Amount:0.00}."
            });
        });

        v1.MapPost("/security/audit-logs/clear", async (
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Security, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var key = $"sec_clear:{tenantId:N}";
            var val = clock.UtcNow.ToString("O");
            var existing = await settingRepo.FindByKeyAsync(key, ct);
            if (existing is null)
            {
                var item = new GlobalPlatformSetting(Guid.NewGuid(), key, val, "Corte visual logs segurança", false);
                item.SetCreatedAudit(clock.UtcNow);
                await settingRepo.AddAsync(item, ct);
            }
            else
            {
                existing.UpdateValue(val);
            }
            await settingRepo.SaveChangesAsync(ct);
            return Results.Ok(new { cleared = true, clearedAtUtc = clock.UtcNow });
        });

        v1.MapGet("/store/live-alerts", async (
            ApiSecurityGuard guard,
            IOrderRepository orderRepo,
            IWhatsAppConversationRepository convRepo,
            IWaiterCallRepository waiterRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, ct: ct);
            if (err is not null) return err;

            var recentOrders = await orderRepo.ListRecentAllAsync(100, ct);
            var activeOrders = recentOrders
                .Where(o => o.Status is OrderStatus.Received or OrderStatus.Accepted or OrderStatus.InPreparation)
                .ToList();
            var newestOrder = recentOrders.OrderByDescending(o => o.PlacedAtUtc).FirstOrDefault();

            var convs = await convRepo.ListInboxAsync(ct);
            var inboundMessages = convs
                .SelectMany(c => c.Messages.Where(m => m.Direction == WhatsAppMessageDirection.Inbound)
                    .Select(m => new { Conv = c, Msg = m }))
                .OrderByDescending(x => x.Msg.SentAtUtc)
                .ToList();
            var newestInbound = inboundMessages.FirstOrDefault();
            var unreadChatCount = convs.Sum(c =>
                c.UnreadCount > 0
                    ? c.UnreadCount
                    : (c.Status == WhatsAppConversationStatus.WaitingHuman ? 1 : 0));
            var unreadConversations = convs
                .OrderByDescending(c => c.UnreadCount > 0 || c.Status == WhatsAppConversationStatus.WaitingHuman ? 1 : 0)
                .ThenByDescending(c => c.LastInteractionAtUtc)
                .Take(8)
                .Select(c => new
                {
                    c.Id,
                    c.CustomerName,
                    c.CustomerPhone,
                    status = c.Status.ToString(),
                    unreadCount = c.UnreadCount > 0 ? c.UnreadCount : (c.Status == WhatsAppConversationStatus.WaitingHuman ? 1 : 0),
                    c.LastMessagePreview,
                    c.LastInteractionAtUtc
                })
                .ToList();

            var pendingWaiterCalls = await waiterRepo.ListPendingAsync(ct);

            return Results.Ok(new
            {
                pendingOrdersCount = activeOrders.Count,
                receivedOrdersCount = recentOrders.Count(o => o.Status == OrderStatus.Received),
                totalOrdersCount = recentOrders.Count,
                latestOrderId = newestOrder?.Id,
                latestOrderNumber = newestOrder?.OrderNumber,
                latestOrderCustomer = newestOrder?.CustomerName,
                latestOrderPlacedAtUtc = newestOrder?.PlacedAtUtc,
                totalInboundChatMessages = inboundMessages.Count,
                unreadChatCount,
                unreadConversations,
                latestInboundMessageId = newestInbound?.Msg.Id,
                latestInboundCustomer = newestInbound?.Conv.CustomerName,
                latestInboundSnippet = newestInbound?.Msg.ContentText,
                latestInboundAtUtc = newestInbound?.Msg.SentAtUtc,
                pendingWaiterCalls = pendingWaiterCalls.Select(w => new
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
    }

    internal static async Task TryNotifyCustomerOrderUpdateAsync(
        Guid tenantId,
        Order order,
        string customMessage,
        ITenantRepository tenantRepo,
        IWhatsAppChannelConfigRepository channelRepo,
        IWhatsAppConversationRepository convRepo,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var phoneDigits = NormalizeDigits(order.CustomerPhone);
        if (string.IsNullOrWhiteSpace(phoneDigits))
            return;

        var channels = await channelRepo.ListAllAsync(ct);
        var channel = channels.FirstOrDefault();
        if (channel is null)
        {
            var tenant = await tenantRepo.FindByIdAsync(tenantId, ct);
            var chRes = WhatsAppChannelConfig.Create(
                tenantId,
                tenant?.TradeName ?? "Loja",
                tenant?.ContactPhone ?? phoneDigits,
                $"phone-{tenant?.Slug ?? tenantId.ToString("N")}",
                $"waba-{tenant?.Slug ?? tenantId.ToString("N")}",
                "sandbox-token",
                "sandbox-secret",
                "pitako-verify",
                WhatsAppAutoReplyMode.HybridBotThenAi,
                tenant is null ? "/loja" : $"/loja/{tenant.Slug}");
            if (chRes.IsFailure) return;
            channel = chRes.Value;
            await channelRepo.AddAsync(channel, ct);
            await channelRepo.SaveChangesAsync(ct);
        }

        var conv = await convRepo.FindByCustomerPhoneAsync(channel.Id, phoneDigits, ct);
        if (conv is null)
        {
            var created = WhatsAppConversation.Start(
                tenantId,
                channel.Id,
                phoneDigits,
                string.IsNullOrWhiteSpace(order.CustomerName) ? "Cliente" : order.CustomerName.Trim(),
                utcNow);
            if (created.IsFailure) return;
            conv = created.Value;
            await convRepo.AddAsync(conv, ct);
        }

        conv.RecordOutbound(WhatsAppMessageDirection.OutboundTraditionalBot, null, customMessage, true, null, true, utcNow);
        await convRepo.SaveChangesAsync(ct);
    }

    internal static async Task<Result<(string MediaId, string ContentType)>> StoreUploadedImageAsync(
        IGlobalSettingRepository settingRepo,
        Guid tenantId,
        string base64DataUrl,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(base64DataUrl))
            return Result.Failure<(string, string)>(Error.Validation("Media.Empty", "Selecione uma imagem válida."));

        var m = DataUrlRegex().Match(base64DataUrl.Trim());
        if (!m.Success)
            return Result.Failure<(string, string)>(Error.Validation("Media.InvalidFormat", "Formato de imagem inválido (use PNG, JPG, WebP ou SVG)."));

        var contentType = m.Groups[1].Value;
        var base64Payload = m.Groups[2].Value;
        if (base64Payload.Length > 900_000)
            return Result.Failure<(string, string)>(Error.Validation("Media.TooLarge", "Imagem excede o limite máximo permitido."));

        var mediaId = $"m{tenantId:N}"[..9] + Guid.NewGuid().ToString("N")[..10];
        const int chunkSize = 1800;
        var chunks = Enumerable.Range(0, (base64Payload.Length + chunkSize - 1) / chunkSize)
            .Select(i => base64Payload.Substring(i * chunkSize, Math.Min(chunkSize, base64Payload.Length - i * chunkSize)))
            .ToList();

        var meta = new GlobalPlatformSetting(Guid.NewGuid(), $"media:{mediaId}:meta", $"{contentType}|{chunks.Count}", $"Midia loja {tenantId:N}", false);
        meta.SetCreatedAudit(utcNow);
        await settingRepo.AddAsync(meta, ct);

        for (var i = 0; i < chunks.Count; i++)
        {
            var cItem = new GlobalPlatformSetting(Guid.NewGuid(), $"media:{mediaId}:c{i}", chunks[i], null, false);
            cItem.SetCreatedAudit(utcNow);
            await settingRepo.AddAsync(cItem, ct);
        }

        await settingRepo.SaveChangesAsync(ct);
        return Result.Success((mediaId, contentType));
    }

    private static string BuildSegmentVectorArtwork(string segment, string key)
    {
        var lowerSeg = (segment ?? "outros").Trim().ToLowerInvariant();
        var lowerKey = (key ?? "item").Trim().ToLowerInvariant();

        var (bg1, bg2, emojiLabel, badge) = lowerSeg switch
        {
            "acaiteria" when lowerKey.Contains("garrafa") => ("#3B0764", "#7E22CE", "🥤", "GARRAFA"),
            "acaiteria" when lowerKey.Contains("mousse") => ("#581C87", "#A855F7", "🍨", "SOBREMESA"),
            "acaiteria" => ("#4C1D95", "#9333EA", "🥣", "AÇAÍ"),
            "pizzaria" => ("#991B1B", "#EA580C", "🍕", "FORNO"),
            "hamburgueria" => ("#78350F", "#D97706", "🍔", "BRASA"),
            "pastelaria" => ("#B45309", "#F59E0B", "🥟", "CROCANTE"),
            "sorveteria" => ("#9D174D", "#EC4899", "🍦", "GELATO"),
            "adega" => ("#1E293B", "#0284C7", "🍻", "GELADA"),
            "padaria" => ("#7C2D12", "#D97706", "🥐", "PADARIA"),
            "doceria" => ("#831843", "#F43F5E", "🧁", "DOCES"),
            _ => ("#064E3B", "#059669", "🍽️", "PRATO")
        };

        var shortTitle = new string((key ?? "item").Replace('-', ' ').Take(18).ToArray()).ToUpperInvariant();
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="320" height="320" viewBox="0 0 320 320">
              <defs>
                <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0%" stop-color="{bg1}"/>
                  <stop offset="100%" stop-color="{bg2}"/>
                </linearGradient>
              </defs>
              <rect width="320" height="320" rx="28" fill="url(#g)"/>
              <circle cx="160" cy="136" r="76" fill="rgba(255,255,255,0.14)"/>
              <text x="160" y="160" font-size="88" text-anchor="middle" dominant-baseline="middle">{emojiLabel}</text>
              <rect x="28" y="24" width="110" height="28" rx="14" fill="rgba(255,255,255,0.2)"/>
              <text x="83" y="42" fill="#FFFFFF" font-family="system-ui,sans-serif" font-size="12" font-weight="800" text-anchor="middle">{badge}</text>
              <text x="160" y="268" fill="#FFFFFF" font-family="system-ui,sans-serif" font-size="18" font-weight="800" text-anchor="middle">{System.Security.SecurityElement.Escape(shortTitle)}</text>
            </svg>
            """;
    }

    [GeneratedRegex("^data:(image/(?:png|jpeg|jpg|webp|svg\\+xml));base64,([A-Za-z0-9+/=]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DataUrlRegex();

    public sealed record UploadMediaReq(string Base64DataUrl, string? FileName = null);
    public sealed record ProductSizeConfigDto(
        Guid? Id,
        string VariationName,
        decimal Price,
        decimal? MeasureAmount,
        string? MeasureUnit,
        int MaxFlavors,
        int FreeAllowance,
        decimal? CostAmount,
        bool AllowPaidExtras,
        string? Description = null,
        bool AllowFree = true,
        string? AddonMode = null,
        string? Sku = null,
        bool? IsActive = null,
        bool? TrackInventory = null,
        int? StockQuantity = null,
        IReadOnlyList<Guid>? AllowedGroupIds = null);
    public sealed record FullProductConfigReq(
        Guid CategoryId,
        string Name,
        string? Description,
        decimal BasePrice,
        bool IsNew,
        bool IsPromotion,
        bool? IsFeatured,
        int? DisplayOrder,
        ProductPricingStrategy? PricingStrategy,
        string? ImageUrl,
        IReadOnlyList<Guid>? AttachedGroupIds,
        IReadOnlyList<ProductSizeConfigDto>? Sizes,
        string? Sku = null,
        int? PreparationMinutes = null,
        bool? AllowsNotes = null,
        bool? IsActive = null,
        decimal? PromotionalPrice = null,
        bool? TrackInventory = null,
        int? StockQuantity = null,
        bool? AutoPauseOnZeroStock = null,
        int? ServesPeopleCount = null,
        string? DietaryTagsCsv = null,
        string? AvailabilityScheduleJson = null);
    public sealed record QuickTechIngDto(Guid? SupplyItemId, string? NewSupplyName, decimal Quantity, string UnitCode, decimal? UnitPriceAmount, decimal LossPercent = 0m);
    public sealed record QuickTechPkgDto(Guid? SupplyItemId, string Description, decimal Quantity, string UnitCode, decimal CostAmount);
    public sealed record QuickTechSheetReq(string? Name, decimal? YieldQuantity, string? YieldUnitCode, string? Notes, IReadOnlyList<QuickTechIngDto> Ingredients, IReadOnlyList<QuickTechPkgDto> Packagings);
    public sealed record QrPayTableReq(string PaymentMethod, int? SplitCount = null, bool ConfirmImmediatePayment = false);
}
