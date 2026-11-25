using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Pitako.Api.Security;
using Pitako.Modules.AcaiBuilder.Application.Abstractions;
using Pitako.Modules.AcaiBuilder.Application.Services;
using Pitako.Modules.AcaiBuilder.Infrastructure.Persistence;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Catalog.Application.Services;
using Pitako.Modules.Catalog.Infrastructure.Persistence;
using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.Coupons.Application.Services;
using Pitako.Modules.Coupons.Infrastructure.Persistence;
using Pitako.Modules.DataLifecycle.Application.Abstractions;
using Pitako.Modules.DataLifecycle.Application.Services;
using Pitako.Modules.DataLifecycle.Infrastructure.Persistence;
using Pitako.Modules.DataLifecycle.Infrastructure.Security;
using Pitako.Modules.DeliveryLogistics.Application.Abstractions;
using Pitako.Modules.DeliveryLogistics.Application.Services;
using Pitako.Modules.DeliveryLogistics.Infrastructure.Persistence;
using Pitako.Modules.DiningHall.Application.Abstractions;
using Pitako.Modules.DiningHall.Application.Services;
using Pitako.Modules.DiningHall.Infrastructure.Persistence;
using Pitako.Modules.Finance.Application.Abstractions;
using Pitako.Modules.Finance.Application.Services;
using Pitako.Modules.Finance.Infrastructure.Gateways;
using Pitako.Modules.Finance.Infrastructure.Persistence;
using Pitako.Modules.Finance.Infrastructure.Security;
using Pitako.Modules.Inventory.Application.Abstractions;
using Pitako.Modules.Inventory.Application.Services;
using Pitako.Modules.Inventory.Infrastructure.Persistence;
using Pitako.Modules.Marketplaces.Application.Abstractions;
using Pitako.Modules.Marketplaces.Application.Connectors;
using Pitako.Modules.Marketplaces.Application.Services;
using Pitako.Modules.Marketplaces.Infrastructure.Gateways;
using Pitako.Modules.Marketplaces.Infrastructure.Persistence;
using Pitako.Modules.Marketplaces.Infrastructure.Security;
using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Application.Services;
using Pitako.Modules.MasterPanel.Infrastructure.Persistence;
using Pitako.Modules.OfflineSync.Application.Abstractions;
using Pitako.Modules.OfflineSync.Application.Services;
using Pitako.Modules.OfflineSync.Infrastructure.Persistence;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Application.Services;
using Pitako.Modules.Orders.Infrastructure.Persistence;
using Pitako.Modules.PdvCashier.Application.Abstractions;
using Pitako.Modules.PdvCashier.Application.Services;
using Pitako.Modules.PdvCashier.Infrastructure.Persistence;
using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Printing.Application.Services;
using Pitako.Modules.Printing.Infrastructure.Hardware;
using Pitako.Modules.Printing.Infrastructure.Persistence;
using Pitako.Modules.Reports.Application.Abstractions;
using Pitako.Modules.Reports.Application.Services;
using Pitako.Modules.Reports.Infrastructure.Persistence;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Application.Services;
using Pitako.Modules.Security.Infrastructure.Crypto;
using Pitako.Modules.Security.Infrastructure.Persistence;
using Pitako.Modules.Security.Infrastructure.RateLimiting;
using Pitako.Modules.Tenants.Application.Abstractions;
using Pitako.Modules.Tenants.Application.Services;
using Pitako.Modules.Tenants.Infrastructure;
using Pitako.Modules.Weighing.Application.Abstractions;
using Pitako.Modules.Weighing.Application.Services;
using Pitako.Modules.Weighing.Infrastructure.Hardware;
using Pitako.Modules.Weighing.Infrastructure.Persistence;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Application.Services;
using Pitako.Modules.WhatsAppSupport.Infrastructure.Gateways;
using Pitako.Modules.WhatsAppSupport.Infrastructure.Persistence;
using Pitako.Modules.WhatsAppSupport.Infrastructure.Security;
using Pitako.MultiTenancy;
using Pitako.Persistence;
using Pitako.SharedKernel.Time;

namespace Pitako.Api.Composition;

public static class PitakoServiceCollectionExtensions
{
    public static IServiceCollection AddPitakoModularMonolith(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        DbConnection? sharedSqliteConnection = null)
    {
        var provider = configuration["DatabaseProvider"] ?? "PostgreSql";
        var postgresConn = configuration.GetConnectionString("PitakoPostgres");
        var sqliteConn = configuration.GetConnectionString("PitakoSqlite") ?? "Data Source=pitako_local.db";
        var useSqlite = string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase) || sharedSqliteConnection is not null;

        if (!useSqlite && string.IsNullOrWhiteSpace(postgresConn))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:PitakoPostgres não configurada e DatabaseProvider não definido como Sqlite.");
        }

        var jwtSigningKey = configuration["Security:JwtSigningKey"];
        if (string.IsNullOrWhiteSpace(jwtSigningKey) || jwtSigningKey.Length < 32)
        {
            if (environment.IsDevelopment() || sharedSqliteConnection is not null)
                jwtSigningKey = "pitako-dev-only-jwt-signing-key-32bytes!";
            else
                throw new InvalidOperationException("Security:JwtSigningKey obrigatória (>= 32 caracteres) em produção.");
        }

        var encPassphrase = configuration["Security:DataEncryptionPassphrase"];
        if (string.IsNullOrWhiteSpace(encPassphrase))
        {
            if (environment.IsDevelopment() || sharedSqliteConnection is not null)
                encPassphrase = "pitako-dev-only-aes-gcm-passphrase";
            else
                throw new InvalidOperationException("Security:DataEncryptionPassphrase obrigatória em produção.");
        }

        // BuildingBlocks
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<PostgresTenantRlsInterceptor>();
        services.AddScoped<ApiSecurityGuard>();

        void ConfigureDb<TDbContext>() where TDbContext : PitakoDbContextBase
        {
            services.AddDbContext<TDbContext>((sp, options) =>
            {
                if (useSqlite)
                {
                    if (sharedSqliteConnection is not null)
                        options.UseSqlite(sharedSqliteConnection);
                    else
                        options.UseSqlite(sqliteConn);
                }
                else
                {
                    options.UseNpgsql(postgresConn);
                    options.AddInterceptors(sp.GetRequiredService<PostgresTenantRlsInterceptor>());
                }
            });
        }

        void ConfigurePlainDb<TDbContext>() where TDbContext : DbContext
        {
            services.AddDbContext<TDbContext>((_, options) =>
            {
                if (useSqlite)
                {
                    if (sharedSqliteConnection is not null)
                        options.UseSqlite(sharedSqliteConnection);
                    else
                        options.UseSqlite(sqliteConn);
                }
                else
                {
                    options.UseNpgsql(postgresConn);
                }
            });
        }

        // 19 DbContexts dos módulos
        ConfigurePlainDb<SecurityDbContext>();
        ConfigurePlainDb<MasterPanelDbContext>();
        ConfigurePlainDb<TenantsDbContext>();
        ConfigureDb<CatalogDbContext>();
        ConfigureDb<InventoryDbContext>();
        ConfigureDb<AcaiBuilderDbContext>();
        ConfigureDb<WeighingDbContext>();
        ConfigureDb<CouponsDbContext>();
        ConfigureDb<OrdersDbContext>();
        ConfigureDb<DeliveryLogisticsDbContext>();
        ConfigureDb<PdvCashierDbContext>();
        ConfigureDb<DiningHallDbContext>();
        ConfigureDb<PrintingDbContext>();
        ConfigureDb<MarketplacesDbContext>();
        ConfigureDb<WhatsAppDbContext>();
        ConfigureDb<ReportsDbContext>();
        ConfigureDb<DataLifecycleDbContext>();
        ConfigureDb<OfflineSyncDbContext>();
        ConfigureDb<FinanceDbContext>();

        // Security
        services.AddSingleton(new JwtSettings { SigningKey = jwtSigningKey });
        services.AddSingleton<SecurityPolicyOptions>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IPasswordStrengthValidator, DefaultPasswordStrengthValidator>();
        services.AddSingleton<ITokenGenerator, CryptoTokenGenerator>();
        services.AddSingleton<IAccessTokenService, HmacJwtTokenService>();
        services.AddSingleton<ILoginRateLimiter, InMemoryLoginRateLimiter>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<ISecurityAuditRepository, EfSecurityAuditRepository>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<PasswordRecoveryService>();
        services.AddScoped<UserAuthorizationService>();

        // MasterPanel & Tenants
        services.AddScoped<ICommercialPlanRepository, EfCommercialPlanRepository>();
        services.AddScoped<IMasterCustomerRepository, EfMasterCustomerRepository>();
        services.AddScoped<ISubscriptionRepository, EfSubscriptionRepository>();
        services.AddScoped<ISupportTicketRepository, EfSupportTicketRepository>();
        services.AddScoped<IGlobalSettingRepository, EfGlobalSettingRepository>();
        services.AddScoped<PlanEntitlementEnforcementService>();
        services.AddScoped<MasterPlanAndSubscriptionService>();
        services.AddScoped<MasterOperationsAndDashboardService>();
        services.AddScoped<IPlanQuotaPolicyChecker, MasterPlanQuotaPolicyChecker>();

        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<IStoreUnitRepository, EfStoreUnitRepository>();
        services.AddScoped<IStoreVisualIdentityRepository, EfStoreVisualIdentityRepository>();
        services.AddScoped<TenantProvisioningService>();
        services.AddScoped<StoreBrandingService>();

        // Catalog & AcaiBuilder & Coupons & Inventory & Weighing
        services.AddScoped<ICategoryRepository, EfCategoryRepository>();
        services.AddScoped<IProductRepository, EfProductRepository>();
        services.AddScoped<IModifierGroupRepository, EfModifierGroupRepository>();
        services.AddScoped<IComboRepository, EfComboRepository>();
        services.AddScoped<IPromotionRepository, EfPromotionRepository>();
        services.AddScoped<IProductHistoryReferenceChecker, OrderProductHistoryReferenceChecker>();
        services.AddScoped<CatalogManagementService>();
        services.AddScoped<ICatalogItemPricingService>(sp => sp.GetRequiredService<CatalogManagementService>());

        services.AddScoped<IAcaiCupSizeRepository, EfAcaiCupSizeRepository>();
        services.AddScoped<IAcaiBaseFlavorRepository, EfAcaiBaseFlavorRepository>();
        services.AddScoped<IAcaiFreeToppingRepository, EfAcaiFreeToppingRepository>();
        services.AddScoped<IAcaiPaidAddonRepository, EfAcaiPaidAddonRepository>();
        services.AddScoped<AcaiBuilderManagementService>();
        services.AddScoped<IAcaiAssemblyPricingService>(sp => sp.GetRequiredService<AcaiBuilderManagementService>());

        services.AddScoped<ICouponRepository, EfCouponRepository>();
        services.AddScoped<CouponsManagementService>();
        services.AddScoped<ICouponValidationService>(sp => sp.GetRequiredService<CouponsManagementService>());

        services.AddScoped<ITenantCustomUnitRepository, EfTenantCustomUnitRepository>();
        services.AddScoped<ISupplyItemRepository, EfSupplyItemRepository>();
        services.AddScoped<ITechnicalSheetRepository, EfTechnicalSheetRepository>();
        services.AddScoped<IStockBalanceRepository, EfStockBalanceRepository>();
        services.AddScoped<IPurchaseReceiptRepository, EfPurchaseReceiptRepository>();
        services.AddScoped<IProductionOrderRepository, EfProductionOrderRepository>();
        services.AddScoped<SuppliesAndTechnicalSheetService>();
        services.AddScoped<InventoryStockAndProductionService>();
        services.AddScoped<IInventoryOrderDeductionService>(sp => sp.GetRequiredService<InventoryStockAndProductionService>());

        services.AddScoped<IScaleDeviceRepository, EfScaleDeviceRepository>();
        services.AddScoped<ITarePresetRepository, EfTarePresetRepository>();
        services.AddScoped<IWeighingRecordRepository, EfWeighingRecordRepository>();
        services.AddSingleton<IScaleTransport, UnavailablePhysicalScaleTransport>();
        services.AddScoped<WeighingService>();
        services.AddScoped<IWeighingOrderAttachmentService>(sp => sp.GetRequiredService<WeighingService>());

        // DeliveryLogistics, Orders, PdvCashier, DiningHall, Printing
        services.AddScoped<IDeliveryPolicyRepository, EfDeliveryPolicyRepository>();
        services.AddScoped<IDeliveryCourierRepository, EfDeliveryCourierRepository>();
        services.AddScoped<IDeliveryDispatchRunRepository, EfDeliveryDispatchRunRepository>();
        services.AddScoped<ICourierGpsPingRepository, EfCourierGpsPingRepository>();
        services.AddScoped<DeliveryPolicyAndFreightService>();
        services.AddScoped<IDeliveryFreightQuoteService>(sp => sp.GetRequiredService<DeliveryPolicyAndFreightService>());
        services.AddScoped<CourierDispatchAndGpsService>();

        services.AddScoped<IShoppingCartRepository, EfShoppingCartRepository>();
        services.AddScoped<IOrderRepository, EfOrderRepository>();
        services.AddScoped<ShoppingCartService>();
        services.AddScoped<OrderManagementService>();

        services.AddScoped<ICashRegisterRepository, EfCashRegisterRepository>();
        services.AddScoped<IPdvSaleRepository, EfPdvSaleRepository>();
        services.AddScoped<PdvCashierService>();

        services.AddScoped<IDiningSectorRepository, EfDiningSectorRepository>();
        services.AddScoped<IDiningTableRepository, EfDiningTableRepository>();
        services.AddScoped<ITableReservationRepository, EfTableReservationRepository>();
        services.AddScoped<IDiningTabRepository, EfDiningTabRepository>();
        services.AddScoped<IQrTableSessionRepository, EfQrTableSessionRepository>();
        services.AddScoped<IWaiterCallRepository, EfWaiterCallRepository>();
        services.AddScoped<ITotemTerminalRepository, EfTotemTerminalRepository>();
        services.AddScoped<ITotemOrderSessionRepository, EfTotemOrderSessionRepository>();
        services.AddScoped<DiningHallManagementService>();
        services.AddScoped<QrTableSelfServiceService>();
        services.AddScoped<TotemSelfServiceService>();

        services.AddScoped<IPrinterProfileRepository, EfPrinterProfileRepository>();
        services.AddScoped<ISectorRoutingRuleRepository, EfSectorRoutingRuleRepository>();
        services.AddScoped<IPrintJobRepository, EfPrintJobRepository>();
        services.AddSingleton<IThermalPrinterTransport, UnavailablePhysicalPrinterTransport>();
        services.AddScoped<PrintingManagementService>();

        // Marketplaces & WhatsAppSupport
        services.AddScoped<IMarketplacePartnerAccountRepository, EfMarketplacePartnerAccountRepository>();
        services.AddScoped<IMarketplaceStoreBindingRepository, EfMarketplaceStoreBindingRepository>();
        services.AddScoped<IMarketplaceInboundEventRepository, EfMarketplaceInboundEventRepository>();
        services.AddScoped<IMarketplaceCatalogMappingRepository, EfMarketplaceCatalogMappingRepository>();
        services.AddScoped<IMarketplaceBulkJobRepository, EfMarketplaceBulkJobRepository>();
        services.AddSingleton<ISecretProtector>(_ => AesGcmSecretProtector.FromPassphrase(encPassphrase));
        services.AddSingleton<IMarketplaceRemoteGateway, UnavailableExternalMarketplaceGateway>();
        services.AddSingleton<IMarketplaceConnector, IFoodConnector>();
        services.AddSingleton<IMarketplaceConnector, RappiConnector>();
        services.AddSingleton<IMarketplaceConnector, AiqfomeConnector>();
        services.AddSingleton<IMarketplaceConnector, DeliveryMuchConnector>();
        services.AddSingleton<IMarketplaceConnector, Food99Connector>();
        services.AddSingleton<IMarketplaceConnector, KeetaConnector>();
        services.AddSingleton<IMarketplaceConnector, FoodToSaveConnector>();
        services.AddSingleton<MarketplaceConnectorRegistry>();
        services.AddScoped<MarketplaceHubService>();
        services.AddScoped<MarketplaceBulkSyncService>();

        services.AddScoped<IWhatsAppChannelConfigRepository, EfWhatsAppChannelConfigRepository>();
        services.AddScoped<ITraditionalMenuBotRuleRepository, EfTraditionalMenuBotRuleRepository>();
        services.AddScoped<IWhatsAppConversationRepository, EfWhatsAppConversationRepository>();
        services.AddSingleton<IWhatsAppSecretProtector>(_ => WhatsAppAesGcmSecretProtector.FromPassphrase(encPassphrase));
        services.AddSingleton<IWhatsAppCloudGateway, UnavailableWhatsAppCloudGateway>();
        services.AddSingleton<IAiAssistantGateway, UnavailableExternalAiGateway>();
        services.AddScoped<WhatsAppSupportService>();

        // Reports, DataLifecycle, OfflineSync, Finance
        services.AddScoped<IReportExportAuditRepository, EfReportExportAuditRepository>();
        services.AddScoped<ReportingManagementService>();

        services.AddScoped<IDataMigrationJobRepository, EfDataMigrationJobRepository>();
        services.AddScoped<ITenantBackupRepository, EfTenantBackupRepository>();
        services.AddScoped<ILgpdDataRequestRepository, EfLgpdDataRequestRepository>();
        services.AddSingleton<IBackupEncryptionProvider>(_ => BackupAesGcmEncryptionProvider.FromPassphrase(encPassphrase));
        services.AddScoped<DataLifecycleService>();

        services.AddScoped<IOfflineTerminalRepository, EfOfflineTerminalRepository>();
        services.AddScoped<IOfflineSyncOperationRepository, EfOfflineSyncOperationRepository>();
        services.AddScoped<OfflineSynchronizationService>();

        services.AddScoped<IFinancialEntryRepository, EfFinancialEntryRepository>();
        services.AddScoped<ICashFlowLedgerRepository, EfCashFlowLedgerRepository>();
        services.AddScoped<IPaymentGatewayConfigRepository, EfPaymentGatewayConfigRepository>();
        services.AddScoped<IPaymentTransactionRepository, EfPaymentTransactionRepository>();
        services.AddScoped<IReconciliationBatchRepository, EfReconciliationBatchRepository>();
        services.AddSingleton<IFinanceSecretProtector>(_ => FinanceAesGcmSecretProtector.FromPassphrase(encPassphrase));
        services.AddSingleton<IPaymentGatewayClient, UnavailablePaymentGatewayClient>();
        services.AddScoped<FinanceManagementService>();

        // Cross-Module Composition Adapters
        services.AddScoped<SignalRRealtimeNotifiers>();
        services.AddScoped<IOrderRealtimeNotifier>(sp => sp.GetRequiredService<SignalRRealtimeNotifiers>());
        services.AddScoped<ICourierGpsRealtimeNotifier>(sp => sp.GetRequiredService<SignalRRealtimeNotifiers>());
        services.AddScoped<IWaiterCallRealtimeNotifier>(sp => sp.GetRequiredService<SignalRRealtimeNotifiers>());
        services.AddScoped<IInboxRealtimeNotifier>(sp => sp.GetRequiredService<SignalRRealtimeNotifiers>());

        services.AddScoped<TenantStoreBrandingAndPrintAdapter>();
        services.AddScoped<ISelfServiceBrandingProvider>(sp => sp.GetRequiredService<TenantStoreBrandingAndPrintAdapter>());
        services.AddScoped<IStorePrintIdentityProvider>(sp => sp.GetRequiredService<TenantStoreBrandingAndPrintAdapter>());

        services.AddScoped<ISelfServiceOrderDispatcher, SelfServiceOrderDispatcherAdapter>();
        services.AddScoped<ITenantKnowledgeProvider, TenantKnowledgeProviderAdapter>();
        services.AddScoped<IReportDataAggregator, MultiModuleReportDataAggregator>();
        services.AddScoped<ITenantDataLifecycleGateway, MultiModuleTenantDataLifecycleGateway>();
        services.AddScoped<IOfflineOperationServerApplier, OfflineOperationServerApplierAdapter>();

        return services;
    }

    /// <summary>
    /// Inicializa tabelas em desenvolvimento/testes SQLite para todos os 19 DbContexts isolados.
    /// Em PostgreSQL utiliza Migrations versionadas oficiais com RLS.
    /// </summary>
    public static async Task EnsureDatabaseInitializedAsync(this IServiceProvider services, bool useSqlite, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var setter = sp.GetRequiredService<ITenantContextSetter>();
        setter.SetMasterScope();

        var dbTypes = new[]
        {
            typeof(SecurityDbContext),
            typeof(MasterPanelDbContext),
            typeof(TenantsDbContext),
            typeof(CatalogDbContext),
            typeof(InventoryDbContext),
            typeof(AcaiBuilderDbContext),
            typeof(WeighingDbContext),
            typeof(CouponsDbContext),
            typeof(OrdersDbContext),
            typeof(DeliveryLogisticsDbContext),
            typeof(PdvCashierDbContext),
            typeof(DiningHallDbContext),
            typeof(PrintingDbContext),
            typeof(MarketplacesDbContext),
            typeof(WhatsAppDbContext),
            typeof(ReportsDbContext),
            typeof(DataLifecycleDbContext),
            typeof(OfflineSyncDbContext),
            typeof(FinanceDbContext)
        };

        foreach (var dbType in dbTypes)
        {
            var db = (DbContext)sp.GetRequiredService(dbType);
            if (!useSqlite)
            {
                await db.Database.MigrateAsync(ct);
                continue;
            }

            var creator = db.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync(ct))
                await creator.CreateAsync(ct);

            var firstTable = db.Model.GetEntityTypes().Select(e => e.GetTableName()).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (firstTable is null)
                continue;

            var conn = (SqliteConnection)db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
            cmd.Parameters.AddWithValue("$name", firstTable);
            var exists = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
            if (!exists)
                await creator.CreateTablesAsync(ct);
        }
    }
}
