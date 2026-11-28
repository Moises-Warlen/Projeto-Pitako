using System.Text.Json;
using Pitako.Api.Security;
using Pitako.Modules.AcaiBuilder.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.DataLifecycle.Application.Abstractions;
using Pitako.Modules.DataLifecycle.Application.Services;
using Pitako.Modules.DataLifecycle.Domain;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Domain;
using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.Security.Infrastructure.Persistence;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;

namespace Pitako.Api.Endpoints;

public static partial class PitakoStoreSuiteEndpoints
{
    private const int MaxImportHistoryItems = 25;

    private static void MapStoreDataSuiteEndpoints(RouteGroupBuilder v1)
    {
        var data = v1.MapGroup("/store-data");

        data.MapGet("/import/template", async (
            string? format,
            ApiSecurityGuard guard,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;

            if (string.Equals(format, "xls", StringComparison.OrdinalIgnoreCase))
            {
                var xlsBytes = PitakoSpreadsheetAndBackupAdapters.BuildTemplateExcelHtmlBytes();
                return Results.File(xlsBytes, "application/vnd.ms-excel; charset=utf-8", "modelo-importacao-pitako.xls");
            }

            var csvBytes = PitakoSpreadsheetAndBackupAdapters.BuildTemplateCsvBytes();
            return Results.File(csvBytes, "text/csv; charset=utf-8", "modelo-importacao-pitako.csv");
        });

        data.MapPost("/import/images-batch", async (
            ImportImagesBatchReq req,
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;

            var tenantId = user!.TenantId!.Value;
            var uploaded = new List<UploadedImportImageDto>();

            if (!string.IsNullOrWhiteSpace(req.ZipBase64))
            {
                var b64Clean = req.ZipBase64.Contains(',') ? req.ZipBase64[(req.ZipBase64.IndexOf(',') + 1)..] : req.ZipBase64;
                byte[] zipBytes;
                try
                {
                    zipBytes = Convert.FromBase64String(b64Clean);
                }
                catch
                {
                    return ApiSecurityGuard.ProblemFromError(Error.Validation("Import.InvalidZip", "Pacote ZIP em Base64 inválido."));
                }

                var zipRes = PitakoSpreadsheetAndBackupAdapters.ExtractValidatedZipImages(zipBytes);
                if (!zipRes.Ok)
                    return ApiSecurityGuard.ProblemFromError(Error.Validation("Import.ZipInvalid", zipRes.Error ?? "Falha ao abrir ZIP."));

                foreach (var img in zipRes.Images)
                {
                    var stored = await StoreUploadedImageAsync(settingRepo, tenantId, img.Base64DataUrl, clock.UtcNow, ct);
                    if (stored.IsSuccess)
                    {
                        uploaded.Add(new UploadedImportImageDto(
                            img.FileName,
                            img.MatchKey,
                            $"/api/v1/public/media/{stored.Value.MediaId}"));
                    }
                }
            }

            if (req.Files is { Count: > 0 })
            {
                foreach (var f in req.Files.Take(80))
                {
                    var stored = await StoreUploadedImageAsync(settingRepo, tenantId, f.Base64DataUrl, clock.UtcNow, ct);
                    if (stored.IsFailure) continue;
                    var key = PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(Path.GetFileNameWithoutExtension(f.FileName));
                    uploaded.Add(new UploadedImportImageDto(
                        f.FileName,
                        key,
                        $"/api/v1/public/media/{stored.Value.MediaId}"));
                }
            }

            if (uploaded.Count == 0)
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Import.NoValidImages", "Nenhuma imagem PNG, JPG ou WebP válida foi enviada."));

            return Results.Ok(new { uploadedCount = uploaded.Count, images = uploaded });
        });

        data.MapPost("/import/spreadsheet/preview", async (
            SpreadsheetPreviewReq req,
            ApiSecurityGuard guard,
            IProductRepository productRepo,
            ICategoryRepository categoryRepo,
            ITechnicalSheetRepository sheetRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(req.FileName) || string.IsNullOrWhiteSpace(req.FileBase64))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Import.FileRequired", "Selecione uma planilha (.csv, .xls ou .xlsx)."));

            byte[] fileBytes;
            try
            {
                var rawB64 = req.FileBase64.Contains(',') ? req.FileBase64[(req.FileBase64.IndexOf(',') + 1)..] : req.FileBase64;
                fileBytes = Convert.FromBase64String(rawB64);
            }
            catch
            {
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Import.InvalidBase64", "Conteúdo de arquivo inválido."));
            }

            var parseRes = PitakoSpreadsheetAndBackupAdapters.ParseSpreadsheetPayload(req.FileName, fileBytes);
            if (!parseRes.Ok)
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Import.ParseFailed", parseRes.Error ?? "Erro ao ler planilha."));

            if (parseRes.Rows.Count == 0)
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Import.EmptyRows", "Nenhuma linha de produto encontrada na planilha."));

            var products = await productRepo.ListAllOrderedAsync(ct);
            var categories = await categoryRepo.ListOrderedAsync(ct);
            var sheets = await sheetRepo.ListAllAsync(ct);

            var previewRows = PitakoSpreadsheetAndBackupAdapters.EvaluateAgainstCatalog(parseRes.Rows, products, categories, sheets);

            return Results.Ok(new
            {
                req.FileName,
                detectedFormat = parseRes.DetectedFormat,
                totalRows = previewRows.Count,
                newCount = previewRows.Count(r => r.MatchStatus == "New"),
                existingCount = previewRows.Count(r => r.MatchStatus == "ExistingMatch"),
                duplicateInFileCount = previewRows.Count(r => r.MatchStatus == "DuplicateInFile"),
                errorCount = previewRows.Count(r => r.MatchStatus == "Error"),
                existingSheetsConflictCount = previewRows.Count(r => r.HasExistingSheet && r.Ingredients.Count > 0),
                rows = previewRows
            });
        });

        data.MapPost("/import/spreadsheet/confirm", async (
            ConfirmSpreadsheetImportReq req,
            ApiSecurityGuard guard,
            CatalogManagementService catalogSvc,
            SuppliesAndTechnicalSheetService invSvc,
            ICategoryRepository categoryRepo,
            IProductRepository productRepo,
            IModifierGroupRepository modifierRepo,
            ISupplyItemRepository supplyRepo,
            ITechnicalSheetRepository sheetRepo,
            IDataMigrationJobRepository migrationRepo,
            ISecurityAuditRepository auditRepo,
            SecurityDbContext secDb,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;
            if (!user!.HasPermission(SystemModules.Catalog, PermissionAction.Create))
                return ApiSecurityGuard.ProblemFromError(Error.Forbidden("Import.Forbidden", "Permissão insuficiente para importar catálogo."));

            if (req.Rows is null || req.Rows.Count == 0)
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Import.NoRows", "Nenhuma linha selecionada para importação."));

            var result = await ExecuteReviewedCatalogImportAsync(
                user,
                req.SourceLabel ?? "Planilha do Proprietário",
                req.DuplicateMode ?? "skip",
                req.AllowPriceAndSheetOverwrite,
                req.Rows,
                req.ImageMappings ?? [],
                catalogSvc,
                invSvc,
                categoryRepo,
                productRepo,
                modifierRepo,
                supplyRepo,
                sheetRepo,
                migrationRepo,
                auditRepo,
                secDb,
                settingRepo,
                clock,
                http,
                ct);

            return result.IsFailure ? ApiSecurityGuard.ProblemFromError(result.Error) : Results.Ok(result.Value);
        });

        data.MapGet("/import/history", async (
            ApiSecurityGuard guard,
            IGlobalSettingRepository settingRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.Catalog, ct);
            if (err is not null) return err;

            var history = await LoadImportHistoryAsync(settingRepo, user!.TenantId!.Value, ct);
            return Results.Ok(new { history });
        });

        data.MapGet("/backup-import/adapters", async (
            ApiSecurityGuard guard,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DataLifecycle, ct);
            if (err is not null) return err;

            return Results.Ok(new { adapters = PitakoSpreadsheetAndBackupAdapters.ListImplementedBackupAdapters() });
        });

        data.MapPost("/backup-import/analyze", async (
            SpreadsheetPreviewReq req,
            ApiSecurityGuard guard,
            IProductRepository productRepo,
            ICategoryRepository categoryRepo,
            ITechnicalSheetRepository sheetRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DataLifecycle, ct);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(req.FileName) || string.IsNullOrWhiteSpace(req.FileBase64))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("BackupImport.FileRequired", "Selecione um arquivo de backup para análise."));

            byte[] fileBytes;
            try
            {
                var rawB64 = req.FileBase64.Contains(',') ? req.FileBase64[(req.FileBase64.IndexOf(',') + 1)..] : req.FileBase64;
                fileBytes = Convert.FromBase64String(rawB64);
            }
            catch
            {
                return ApiSecurityGuard.ProblemFromError(Error.Validation("BackupImport.InvalidFile", "Arquivo em formato inválido."));
            }

            var products = await productRepo.ListAllOrderedAsync(ct);
            var categories = await categoryRepo.ListOrderedAsync(ct);
            var sheets = await sheetRepo.ListAllAsync(ct);

            var analyzeRes = PitakoSpreadsheetAndBackupAdapters.AnalyzeBackupPayload(req.FileName, fileBytes, products, categories, sheets);
            if (!analyzeRes.Ok || analyzeRes.Result is null)
                return ApiSecurityGuard.ProblemFromError(Error.Validation("BackupImport.Unsupported", analyzeRes.Error ?? "Formato incompatível."));

            return Results.Ok(analyzeRes.Result);
        });

        data.MapPost("/backup-import/confirm", async (
            ConfirmBackupImportReq req,
            ApiSecurityGuard guard,
            CatalogManagementService catalogSvc,
            SuppliesAndTechnicalSheetService invSvc,
            ICategoryRepository categoryRepo,
            IProductRepository productRepo,
            IModifierGroupRepository modifierRepo,
            ISupplyItemRepository supplyRepo,
            ITechnicalSheetRepository sheetRepo,
            IDataMigrationJobRepository migrationRepo,
            ISecurityAuditRepository auditRepo,
            SecurityDbContext secDb,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DataLifecycle, ct);
            if (err is not null) return err;
            if (!user!.HasPermission(SystemModules.DataLifecycle, PermissionAction.Create))
                return ApiSecurityGuard.ProblemFromError(Error.Forbidden("BackupImport.Forbidden", "Permissão insuficiente para importar backup."));

            var mappedRows = req.Rows.Select(r =>
            {
                var targetCat = req.CategoryMap is not null && req.CategoryMap.TryGetValue(r.CategoryName, out var remapped) && !string.IsNullOrWhiteSpace(remapped)
                    ? remapped.Trim()
                    : r.CategoryName;
                return r with { CategoryName = targetCat };
            }).ToList();

            var result = await ExecuteReviewedCatalogImportAsync(
                user,
                $"Backup ({req.AdapterId})",
                req.DuplicateMode ?? "skip",
                req.AllowPriceAndSheetOverwrite,
                mappedRows,
                [],
                catalogSvc,
                invSvc,
                categoryRepo,
                productRepo,
                modifierRepo,
                supplyRepo,
                sheetRepo,
                migrationRepo,
                auditRepo,
                secDb,
                settingRepo,
                clock,
                http,
                ct);

            return result.IsFailure ? ApiSecurityGuard.ProblemFromError(result.Error) : Results.Ok(result.Value);
        });

        data.MapGet("/starter-cleanup/preview", async (
            ApiSecurityGuard guard,
            ICategoryRepository categoryRepo,
            IProductRepository productRepo,
            IModifierGroupRepository modifierRepo,
            IComboRepository comboRepo,
            IPromotionRepository promotionRepo,
            ISupplyItemRepository supplyRepo,
            ITechnicalSheetRepository sheetRepo,
            IAcaiCupSizeRepository acaiCupRepo,
            IAcaiBaseFlavorRepository acaiFlavorRepo,
            IAcaiFreeToppingRepository acaiFreeRepo,
            IAcaiPaidAddonRepository acaiPaidRepo,
            IOrderRepository orderRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (_, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DataLifecycle, ct);
            if (err is not null) return err;

            var categories = await categoryRepo.ListOrderedAsync(ct);
            var products = (await productRepo.ListAllOrderedAsync(ct)).Where(p => p.Status != CatalogItemStatus.Archived).ToList();
            var modifierGroups = await modifierRepo.ListAllAsync(ct);
            var combos = await comboRepo.ListAllOrderedAsync(ct);
            var promotions = await promotionRepo.ListAllAsync(ct);
            var supplies = await supplyRepo.ListAllAsync(ct);
            var sheets = await sheetRepo.ListAllAsync(ct);
            var acaiCups = await acaiCupRepo.ListAsync(true, ct);
            var acaiFlavors = await acaiFlavorRepo.ListAsync(true, ct);
            var acaiFrees = await acaiFreeRepo.ListAsync(true, ct);
            var acaiPaids = await acaiPaidRepo.ListAsync(true, ct);
            var orders = await orderRepo.ListRecentAllAsync(500, ct);

            return Results.Ok(new
            {
                protectedOrdersCount = orders.Count,
                categories = categories.Select(c => new { c.Id, c.Name, productsCount = products.Count(p => p.CategoryId == c.Id) }),
                products = products.Select(p => new { p.Id, p.Name, p.CategoryId, p.Sku, price = p.BasePrice.Amount }),
                modifierGroups = modifierGroups.Select(g => new { g.Id, g.Name, optionsCount = g.Options.Count }),
                combos = combos.Select(c => new { c.Id, c.Name, price = c.ComboPrice.Amount }),
                promotions = promotions.Select(pr => new { pr.Id, pr.Name }),
                supplies = supplies.Select(s => new { s.Id, s.Name, s.ContentUnitCode }),
                technicalSheets = sheets.Select(sh => new { sh.Id, sh.Name, sh.ProductId }),
                acaiBuilderCount = acaiCups.Count + acaiFlavors.Count + acaiFrees.Count + acaiPaids.Count
            });
        });

        data.MapPost("/starter-cleanup/execute", async (
            ExecuteStarterCleanupReq req,
            ApiSecurityGuard guard,
            CatalogManagementService catalogSvc,
            SuppliesAndTechnicalSheetService invSvc,
            DataLifecycleService lifecycleSvc,
            ICategoryRepository categoryRepo,
            IProductRepository productRepo,
            IModifierGroupRepository modifierRepo,
            IComboRepository comboRepo,
            IPromotionRepository promotionRepo,
            ISupplyItemRepository supplyRepo,
            ITechnicalSheetRepository sheetRepo,
            IAcaiCupSizeRepository acaiCupRepo,
            IAcaiBaseFlavorRepository acaiFlavorRepo,
            IAcaiFreeToppingRepository acaiFreeRepo,
            IAcaiPaidAddonRepository acaiPaidRepo,
            ISecurityAuditRepository auditRepo,
            SecurityDbContext secDb,
            IGlobalSettingRepository settingRepo,
            IClock clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (user, err) = await guard.RequireAuthenticatedUserAsync(http, UserScope.Establishment, SystemModules.DataLifecycle, ct);
            if (err is not null) return err;

            if (user!.EstablishmentRole != EstablishmentRole.Administrator ||
                !user.HasPermission(SystemModules.DataLifecycle, PermissionAction.Approve))
            {
                return ApiSecurityGuard.ProblemFromError(Error.Forbidden("Cleanup.AdminOnly", "Somente o administrador do estabelecimento pode executar a limpeza de dados iniciais."));
            }

            if (!string.Equals(req.ConfirmPhrase?.Trim(), "LIMPAR", StringComparison.Ordinal))
                return ApiSecurityGuard.ProblemFromError(Error.Validation("Cleanup.ConfirmPhraseRequired", "Digite LIMPAR para confirmar a exclusão dos itens selecionados."));

            var tenantId = user.TenantId!.Value;
            string? safetyBackupCode = null;
            if (req.CreateSafetyBackup)
            {
                var bkpRes = await lifecycleSvc.CreateEncryptedBackupAsync(user, includeCatalog: true, includeOrders: false, includeCustomers: false, ct);
                if (bkpRes.IsSuccess)
                    safetyBackupCode = bkpRes.Value.BackupCode;
            }

            var combosRemoved = 0;
            var promosRemoved = 0;
            var sheetsRemoved = 0;
            var productsRemoved = 0;
            var productsArchivedForOrders = 0;
            var modifiersRemoved = 0;
            var categoriesRemoved = 0;
            var suppliesRemoved = 0;
            var acaiItemsRemoved = 0;

            // 1. Combos & Promoções
            if (req.RemoveCombos)
            {
                var targetSet = req.ComboIds?.ToHashSet();
                foreach (var c in await comboRepo.ListAllOrderedAsync(ct))
                {
                    if (targetSet is null || targetSet.Count == 0 || targetSet.Contains(c.Id))
                    {
                        var r = await catalogSvc.DeleteComboAsync(user, c.Id, ct);
                        if (r.IsSuccess) combosRemoved++;
                    }
                }
            }

            if (req.RemovePromotions)
            {
                foreach (var pr in await promotionRepo.ListAllAsync(ct))
                {
                    var r = await catalogSvc.DeletePromotionAsync(user, pr.Id, ct);
                    if (r.IsSuccess) promosRemoved++;
                }
            }

            // 2. Fichas Técnicas antes de Insumos/Produtos
            if (req.RemoveTechnicalSheets)
            {
                var targetSet = req.TechnicalSheetIds?.ToHashSet();
                foreach (var sh in await sheetRepo.ListAllAsync(ct))
                {
                    if (targetSet is null || targetSet.Count == 0 || targetSet.Contains(sh.Id))
                    {
                        var r = await invSvc.DeleteTechnicalSheetAsync(user, sh.Id, ct);
                        if (r.IsSuccess) sheetsRemoved++;
                    }
                }
            }

            // 3. Produtos (arquiva automaticamente os que possuem histórico de pedidos!)
            if (req.RemoveProducts)
            {
                var targetSet = req.ProductIds?.ToHashSet();
                var allProds = await productRepo.ListAllOrderedAsync(ct);
                foreach (var p in allProds.Where(x => x.Status != CatalogItemStatus.Archived))
                {
                    if (targetSet is null || targetSet.Count == 0 || targetSet.Contains(p.Id))
                    {
                        var r = await catalogSvc.DeleteOrArchiveProductAsync(user, p.Id, ct);
                        if (r.IsSuccess)
                        {
                            if (r.Value == DeletionOutcome.PhysicallyDeleted) productsRemoved++;
                            else productsArchivedForOrders++;
                        }
                    }
                }
            }

            // 4. Grupos de Adicionais e Complementos
            if (req.RemoveModifiers)
            {
                var targetSet = req.ModifierGroupIds?.ToHashSet();
                foreach (var g in await modifierRepo.ListAllAsync(ct))
                {
                    if (targetSet is null || targetSet.Count == 0 || targetSet.Contains(g.Id))
                    {
                        var r = await catalogSvc.DeleteModifierGroupAsync(user, g.Id, ct);
                        if (r.IsSuccess) modifiersRemoved++;
                    }
                }
            }

            // 5. Categorias vazias selecionadas
            if (req.RemoveCategories)
            {
                var targetSet = req.CategoryIds?.ToHashSet();
                foreach (var cat in await categoryRepo.ListOrderedAsync(ct))
                {
                    if (targetSet is null || targetSet.Count == 0 || targetSet.Contains(cat.Id))
                    {
                        var r = await catalogSvc.DeleteCategoryAsync(user, cat.Id, ct);
                        if (r.IsSuccess) categoriesRemoved++;
                    }
                }
            }

            // 6. Ingredientes / Insumos não mais vinculados a fichas ativas
            if (req.RemoveSupplies)
            {
                var targetSet = req.SupplyIds?.ToHashSet();
                foreach (var sup in await supplyRepo.ListAllAsync(ct))
                {
                    if (targetSet is null || targetSet.Count == 0 || targetSet.Contains(sup.Id))
                    {
                        var r = await invSvc.DeleteSupplyItemAsync(user, sup.Id, ct);
                        if (r.IsSuccess) suppliesRemoved++;
                    }
                }
            }

            // 7. Montador Açaí (tamanhos/cremes/coberturas iniciais)
            if (req.RemoveAcaiBuilder)
            {
                foreach (var cup in await acaiCupRepo.ListAsync(true, ct)) { acaiCupRepo.Remove(cup); acaiItemsRemoved++; }
                foreach (var flv in await acaiFlavorRepo.ListAsync(true, ct)) { acaiFlavorRepo.Remove(flv); acaiItemsRemoved++; }
                foreach (var ft in await acaiFreeRepo.ListAsync(true, ct)) { acaiFreeRepo.Remove(ft); acaiItemsRemoved++; }
                foreach (var pa in await acaiPaidRepo.ListAsync(true, ct)) { acaiPaidRepo.Remove(pa); acaiItemsRemoved++; }
                await acaiCupRepo.SaveChangesAsync(ct);
                await acaiFlavorRepo.SaveChangesAsync(ct);
                await acaiFreeRepo.SaveChangesAsync(ct);
                await acaiPaidRepo.SaveChangesAsync(ct);
            }

            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "Admin";
            await auditRepo.AddAsync(new SecurityAuditLog(
                Guid.NewGuid(),
                SecurityEventType.PermissionsUpdated,
                UserScope.Establishment,
                tenantId,
                user.Id,
                null,
                user.NormalizedEmail,
                ip,
                true,
                $"Limpeza inicial executada por {user.FullName}: {productsRemoved} produtos excluídos, {productsArchivedForOrders} arquivados por histórico, {categoriesRemoved} categorias, {sheetsRemoved} fichas, {suppliesRemoved} insumos, {modifiersRemoved} grupos, {combosRemoved} combos. Backup segurança: {safetyBackupCode ?? "não solicitado"}.",
                clock.UtcNow), ct);
            await secDb.SaveChangesAsync(ct);

            await AppendImportHistoryAsync(settingRepo, tenantId, new StoreImportHistoryEntry(
                Guid.NewGuid(),
                "Limpeza Inicial da Loja",
                user.FullName,
                clock.UtcNow,
                productsRemoved + categoriesRemoved + sheetsRemoved + suppliesRemoved + modifiersRemoved + combosRemoved,
                productsArchivedForOrders,
                0,
                $"Backup pré-limpeza: {safetyBackupCode ?? "n/a"} · Pedidos reais preservados."), clock.UtcNow, ct);

            return Results.Ok(new
            {
                safetyBackupCode,
                combosRemoved,
                promosRemoved,
                sheetsRemoved,
                productsRemoved,
                productsArchivedForOrders,
                modifiersRemoved,
                categoriesRemoved,
                suppliesRemoved,
                acaiItemsRemoved,
                ordersPreserved = true
            });
        });
    }

    private static async Task<Result<ConfirmImportSummaryDto>> ExecuteReviewedCatalogImportAsync(
        User user,
        string sourceLabel,
        string duplicateMode,
        bool allowPriceAndSheetOverwrite,
        IReadOnlyList<SpreadsheetPreviewRowDto> rows,
        IReadOnlyList<UploadedImportImageDto> imageMappings,
        CatalogManagementService catalogSvc,
        SuppliesAndTechnicalSheetService invSvc,
        ICategoryRepository categoryRepo,
        IProductRepository productRepo,
        IModifierGroupRepository modifierRepo,
        ISupplyItemRepository supplyRepo,
        ITechnicalSheetRepository sheetRepo,
        IDataMigrationJobRepository migrationRepo,
        ISecurityAuditRepository auditRepo,
        SecurityDbContext secDb,
        IGlobalSettingRepository settingRepo,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = user.TenantId!.Value;
        var existingCats = (await categoryRepo.ListOrderedAsync(ct))
            .GroupBy(c => PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(c.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var existingProducts = (await productRepo.ListAllOrderedAsync(ct))
            .Where(p => p.Status != CatalogItemStatus.Archived)
            .ToList();
        var productBySku = existingProducts
            .Where(p => !string.IsNullOrWhiteSpace(p.Sku))
            .GroupBy(p => p.Sku!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var productByName = existingProducts
            .GroupBy(p => PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(p.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var existingModifiers = (await modifierRepo.ListAllAsync(ct))
            .GroupBy(m => PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(m.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var existingSupplies = (await supplyRepo.ListAllAsync(ct))
            .GroupBy(s => PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(s.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var existingSheets = (await sheetRepo.ListAllAsync(ct))
            .Where(s => s.IsActive && s.ProductId.HasValue)
            .GroupBy(s => s.ProductId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var imgLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var im in imageMappings)
        {
            if (!string.IsNullOrWhiteSpace(im.MatchKey)) imgLookup[im.MatchKey] = im.MediaUrl;
            var fileNorm = PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(Path.GetFileNameWithoutExtension(im.FileName));
            if (!string.IsNullOrWhiteSpace(fileNorm)) imgLookup[fileNorm] = im.MediaUrl;
        }

        var createdCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;
        var sheetsCreatedOrUpdated = 0;
        var imagesAttached = 0;
        var failedRows = new List<string>();
        var nextCatOrder = existingCats.Count + 1;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ProductName) || row.Price <= 0m)
            {
                failedRows.Add($"Linha {row.RowNumber}: nome ou preço inválido.");
                continue;
            }

            var catName = string.IsNullOrWhiteSpace(row.CategoryName) ? "Geral" : row.CategoryName.Trim();
            var catKey = PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(catName);
            if (!existingCats.TryGetValue(catKey, out var category))
            {
                var catRes = await catalogSvc.CreateCategoryAsync(user, catName, null, null, nextCatOrder++, null, null, ct);
                if (catRes.IsFailure)
                {
                    failedRows.Add($"Linha {row.RowNumber}: erro na categoria '{catName}' ({catRes.Error.Message}).");
                    continue;
                }
                category = catRes.Value;
                existingCats[catKey] = category;
            }

            Product? matchedProd = null;
            if (!string.IsNullOrWhiteSpace(row.Sku) && productBySku.TryGetValue(row.Sku.Trim(), out var bySku))
                matchedProd = bySku;
            else if (productByName.TryGetValue(PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(row.ProductName), out var byName))
                matchedProd = byName;

            Product targetProduct;
            if (matchedProd is not null)
            {
                if (duplicateMode == "skip")
                {
                    skippedCount++;
                    continue;
                }

                var priceToUse = allowPriceAndSheetOverwrite ? row.Price : matchedProd.BasePrice.Amount;
                var promoToUse = allowPriceAndSheetOverwrite ? row.PromotionalPrice : matchedProd.PromotionalPrice?.Amount;

                var updRes = await catalogSvc.UpdateProductAsync(
                    user,
                    matchedProd.Id,
                    category.Id,
                    row.ProductName.Trim(),
                    priceToUse,
                    string.IsNullOrWhiteSpace(row.Description) ? matchedProd.Description : row.Description.Trim(),
                    string.IsNullOrWhiteSpace(row.Sku) ? matchedProd.Sku : row.Sku.Trim(),
                    matchedProd.IsFeatured,
                    matchedProd.DisplayOrder,
                    matchedProd.SaleMode,
                    matchedProd.IsNew,
                    matchedProd.IsRecommended,
                    matchedProd.PricingStrategy,
                    string.IsNullOrWhiteSpace(row.TagsCsv) ? matchedProd.TagsCsv : row.TagsCsv.Trim(),
                    matchedProd.PreparationMinutes,
                    matchedProd.CustomTemplateKey,
                    matchedProd.AllowsNotes,
                    ct,
                    promoToUse);

                if (updRes.IsFailure)
                {
                    failedRows.Add($"Linha {row.RowNumber}: {updRes.Error.Message}");
                    continue;
                }
                targetProduct = updRes.Value;
                updatedCount++;
            }
            else
            {
                var createRes = await catalogSvc.CreateProductAsync(
                    user,
                    category.Id,
                    row.ProductName.Trim(),
                    row.Price,
                    string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
                    string.IsNullOrWhiteSpace(row.Sku) ? null : row.Sku.Trim(),
                    ProductSaleMode.Unit,
                    false,
                    createdCount + 1,
                    false,
                    false,
                    ProductPricingStrategy.FixedPlusAddons,
                    string.IsNullOrWhiteSpace(row.TagsCsv) ? null : row.TagsCsv.Trim(),
                    15,
                    null,
                    true,
                    ct,
                    row.PromotionalPrice);

                if (createRes.IsFailure)
                {
                    failedRows.Add($"Linha {row.RowNumber}: {createRes.Error.Message}");
                    continue;
                }
                targetProduct = createRes.Value;
                productByName[PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(targetProduct.Name)] = targetProduct;
                if (!string.IsNullOrWhiteSpace(targetProduct.Sku))
                    productBySku[targetProduct.Sku] = targetProduct;
                createdCount++;
            }

            // Variações
            if (row.Variations.Count > 0)
            {
                var vOrder = targetProduct.Variations.Count + 1;
                foreach (var v in row.Variations)
                {
                    var exVar = targetProduct.Variations.FirstOrDefault(x => string.Equals(x.Name.Trim(), v.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (exVar is null)
                    {
                        await catalogSvc.AddProductVariationAsync(user, targetProduct.Id, v.Name, v.Price, null, vOrder++, null, v.MeasureAmount, v.MeasureUnit, 1, ct);
                    }
                    else if (allowPriceAndSheetOverwrite)
                    {
                        await catalogSvc.UpdateProductVariationAsync(user, targetProduct.Id, exVar.Id, v.Name, v.Price, exVar.Sku, exVar.DisplayOrder, exVar.Description, v.MeasureAmount, v.MeasureUnit, exVar.MaxFlavors, true, ct);
                    }
                }
            }

            // Grupos de Adicionais & Complementos
            foreach (var mg in row.ModifierGroups)
            {
                var mgKey = PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(mg.GroupName);
                if (!existingModifiers.TryGetValue(mgKey, out var group))
                {
                    var optInputs = mg.Options.Select((optName, idx) => new ModifierOptionInput(optName, 0m, 1, idx)).ToList();
                    var gRes = await catalogSvc.CreateModifierGroupDetailedAsync(
                        user,
                        mg.GroupName,
                        ModifierGroupType.Addon,
                        0,
                        Math.Max(1, mg.Options.Count),
                        false,
                        optInputs,
                        existingModifiers.Count + 1,
                        ModifierGroupKind.FreeIncluded,
                        0,
                        OptionCountingMode.ByTotalQuantity,
                        null,
                        ct);
                    if (gRes.IsFailure) continue;
                    group = gRes.Value;
                    existingModifiers[mgKey] = group;
                }

                await catalogSvc.AttachModifierGroupToProductAsync(user, targetProduct.Id, group.Id, 0, ct);
            }

            // Imagem associada por CódigoFoto, Nome do Arquivo, SKU ou Nome do Produto
            string? resolvedPhotoUrl = null;
            if (!string.IsNullOrWhiteSpace(row.PhotoRef))
            {
                var refKey = PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(Path.GetFileNameWithoutExtension(row.PhotoRef));
                imgLookup.TryGetValue(refKey, out resolvedPhotoUrl);
                if (resolvedPhotoUrl is null && row.PhotoRef.StartsWith("/api/v1/public/media/", StringComparison.OrdinalIgnoreCase))
                    resolvedPhotoUrl = row.PhotoRef;
            }
            if (resolvedPhotoUrl is null && !string.IsNullOrWhiteSpace(row.Sku))
                imgLookup.TryGetValue(PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(row.Sku), out resolvedPhotoUrl);
            if (resolvedPhotoUrl is null)
                imgLookup.TryGetValue(PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(row.ProductName), out resolvedPhotoUrl);

            if (!string.IsNullOrWhiteSpace(resolvedPhotoUrl))
            {
                var addImgRes = await catalogSvc.AddProductImageAsync(user, targetProduct.Id, resolvedPhotoUrl, 0, true, ct);
                if (addImgRes.IsSuccess) imagesAttached++;
            }

            // Ingredientes + Insumos + Ficha Técnica
            if (row.Ingredients.Count > 0)
            {
                var hasSheet = existingSheets.TryGetValue(targetProduct.Id, out var currentSheet);
                if (!hasSheet || allowPriceAndSheetOverwrite)
                {
                    var ingInputs = new List<IngredientInput>();
                    foreach (var ing in row.Ingredients)
                    {
                        var ingKey = PitakoSpreadsheetAndBackupAdapters.NormalizeMatchKey(ing.Name);
                        if (!existingSupplies.TryGetValue(ingKey, out var sup))
                        {
                            var unitPkg = ing.UnitCode is "un" ? 1m : 1000m;
                            var pkgPrice = decimal.Round(Math.Max(0.001m, ing.UnitCost) * unitPkg, 2, MidpointRounding.ToEven);
                            var sRes = await invSvc.CreateSupplyItemAsync(
                                user,
                                ing.Name,
                                Math.Max(0.10m, pkgPrice),
                                1m,
                                ing.UnitCode,
                                null,
                                null,
                                unitPkg,
                                ing.UnitCode,
                                0m,
                                ct);
                            if (sRes.IsFailure) continue;
                            sup = sRes.Value;
                            existingSupplies[ingKey] = sup;
                        }
                        ingInputs.Add(new IngredientInput(sup.Id, Math.Max(0.001m, ing.Quantity), ing.UnitCode, 0m));
                    }

                    if (ingInputs.Count > 0)
                    {
                        var yieldQty = 1m;
                        var yieldUnit = "un";
                        if (!string.IsNullOrWhiteSpace(row.YieldText))
                        {
                            var tokens = row.YieldText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length >= 1)
                            {
                                var q = PitakoSpreadsheetAndBackupAdapters.ParseOptionalDecimal(tokens[0]);
                                if (q.HasValue && q.Value > 0m) yieldQty = q.Value;
                            }
                            if (tokens.Length >= 2) yieldUnit = tokens[1].ToLowerInvariant();
                        }

                        if (!hasSheet)
                        {
                            var shRes = await invSvc.CreateTechnicalSheetAsync(
                                user,
                                $"Ficha: {targetProduct.Name}",
                                yieldQty,
                                yieldUnit,
                                ingInputs,
                                [],
                                targetProduct.Id,
                                "Importada via planilha",
                                ct);
                            if (shRes.IsSuccess)
                            {
                                existingSheets[targetProduct.Id] = shRes.Value;
                                sheetsCreatedOrUpdated++;
                            }
                        }
                        else if (currentSheet is not null)
                        {
                            var updSh = await invSvc.UpdateTechnicalSheetAsync(
                                user,
                                currentSheet.Id,
                                currentSheet.Name,
                                yieldQty,
                                yieldUnit,
                                ingInputs,
                                [],
                                targetProduct.Id,
                                "Atualizada via importação autorizada",
                                ct);
                            if (updSh.IsSuccess) sheetsCreatedOrUpdated++;
                        }
                    }
                }
            }
        }

        if (rows.Count > 0)
        {
            var jobRes = DataMigrationJob.Record(
                tenantId,
                DataMigrationSourceSystem.GenericCsv,
                false,
                rows.Count,
                createdCount + updatedCount,
                skippedCount,
                failedRows.Count,
                JsonSerializer.Serialize(new { sourceLabel, createdCount, updatedCount, sheetsCreatedOrUpdated, imagesAttached }, JsonOpts),
                JsonSerializer.Serialize(failedRows, JsonOpts),
                user.Id,
                clock.UtcNow);
            if (jobRes.IsSuccess)
            {
                await migrationRepo.AddAsync(jobRes.Value, ct);
                await migrationRepo.SaveChangesAsync(ct);
            }
        }

        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "Admin";
        await auditRepo.AddAsync(new SecurityAuditLog(
            Guid.NewGuid(),
            SecurityEventType.PermissionsUpdated,
            UserScope.Establishment,
            tenantId,
            user.Id,
            null,
            user.NormalizedEmail,
            ip,
            true,
            $"Importação '{sourceLabel}' executada por {user.FullName}: {createdCount} criados, {updatedCount} atualizados, {skippedCount} ignorados, {sheetsCreatedOrUpdated} fichas técnicas, {imagesAttached} fotos.",
            clock.UtcNow), ct);
        await secDb.SaveChangesAsync(ct);

        await AppendImportHistoryAsync(settingRepo, tenantId, new StoreImportHistoryEntry(
            Guid.NewGuid(),
            sourceLabel,
            user.FullName,
            clock.UtcNow,
            createdCount + updatedCount,
            skippedCount,
            failedRows.Count,
            $"{createdCount} novos · {updatedCount} atualizados · {sheetsCreatedOrUpdated} fichas · {imagesAttached} fotos."), clock.UtcNow, ct);

        return Result.Success(new ConfirmImportSummaryDto(
            sourceLabel,
            createdCount,
            updatedCount,
            skippedCount,
            failedRows.Count,
            sheetsCreatedOrUpdated,
            imagesAttached,
            user.FullName,
            clock.UtcNow,
            failedRows));
    }

    private static async Task<List<StoreImportHistoryEntry>> LoadImportHistoryAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        CancellationToken ct)
    {
        var key = $"store_import_hist:{tenantId:N}";
        var s = await repo.FindByKeyAsync(key, ct);
        if (s is null || string.IsNullOrWhiteSpace(s.Value)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<StoreImportHistoryEntry>>(s.Value, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task AppendImportHistoryAsync(
        IGlobalSettingRepository repo,
        Guid tenantId,
        StoreImportHistoryEntry entry,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var list = await LoadImportHistoryAsync(repo, tenantId, ct);
        list.Insert(0, entry);
        while (list.Count > MaxImportHistoryItems)
            list.RemoveAt(list.Count - 1);

        var json = JsonSerializer.Serialize(list, JsonOpts);
        while (json.Length > 1900 && list.Count > 1)
        {
            list.RemoveAt(list.Count - 1);
            json = JsonSerializer.Serialize(list, JsonOpts);
        }

        var key = $"store_import_hist:{tenantId:N}";
        var existing = await repo.FindByKeyAsync(key, ct);
        if (existing is null)
        {
            var setting = new GlobalPlatformSetting(Guid.NewGuid(), key, json, $"Histórico de importações tenant {tenantId:N}", false);
            setting.SetCreatedAudit(utcNow);
            await repo.AddAsync(setting, ct);
        }
        else
        {
            existing.UpdateValue(json);
        }
        await repo.SaveChangesAsync(ct);
    }

    public sealed record ImportFileReq(string FileName, string Base64DataUrl);
    public sealed record ImportImagesBatchReq(string? ZipBase64, IReadOnlyList<ImportFileReq>? Files);
    public sealed record SpreadsheetPreviewReq(string FileName, string FileBase64);
    public sealed record UploadedImportImageDto(string FileName, string MatchKey, string MediaUrl);
    public sealed record ConfirmSpreadsheetImportReq(
        string? SourceLabel,
        string? DuplicateMode,
        bool AllowPriceAndSheetOverwrite,
        IReadOnlyList<SpreadsheetPreviewRowDto> Rows,
        IReadOnlyList<UploadedImportImageDto>? ImageMappings = null);
    public sealed record ConfirmBackupImportReq(
        string AdapterId,
        string? DuplicateMode,
        bool AllowPriceAndSheetOverwrite,
        Dictionary<string, string>? CategoryMap,
        IReadOnlyList<SpreadsheetPreviewRowDto> Rows);
    public sealed record ExecuteStarterCleanupReq(
        string ConfirmPhrase,
        bool CreateSafetyBackup = true,
        bool RemoveCombos = false,
        bool RemovePromotions = false,
        bool RemoveTechnicalSheets = false,
        bool RemoveProducts = false,
        bool RemoveModifiers = false,
        bool RemoveCategories = false,
        bool RemoveSupplies = false,
        bool RemoveAcaiBuilder = false,
        IReadOnlyList<Guid>? ComboIds = null,
        IReadOnlyList<Guid>? TechnicalSheetIds = null,
        IReadOnlyList<Guid>? ProductIds = null,
        IReadOnlyList<Guid>? ModifierGroupIds = null,
        IReadOnlyList<Guid>? CategoryIds = null,
        IReadOnlyList<Guid>? SupplyIds = null);
    public sealed record StoreImportHistoryEntry(
        Guid Id,
        string SourceLabel,
        string ExecutedBy,
        DateTimeOffset ExecutedAtUtc,
        int ImportedOrAffectedCount,
        int SkippedOrPreservedCount,
        int FailedCount,
        string SummaryText);
    public sealed record ConfirmImportSummaryDto(
        string SourceLabel,
        int CreatedCount,
        int UpdatedCount,
        int SkippedCount,
        int FailedCount,
        int SheetsCreatedOrUpdated,
        int ImagesAttached,
        string ExecutedBy,
        DateTimeOffset ExecutedAtUtc,
        List<string> Errors);
}
