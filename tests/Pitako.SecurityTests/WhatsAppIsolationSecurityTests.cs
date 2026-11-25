using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Security.Domain;
using Pitako.Modules.WhatsAppSupport.Application.Abstractions;
using Pitako.Modules.WhatsAppSupport.Application.Services;
using Pitako.Modules.WhatsAppSupport.Domain;
using Pitako.Modules.WhatsAppSupport.Infrastructure.Gateways;
using Pitako.Modules.WhatsAppSupport.Infrastructure.Persistence;
using Pitako.Modules.WhatsAppSupport.Infrastructure.Security;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Time;
using Xunit;

namespace Pitako.SecurityTests;

internal sealed class SecurityStubKnowledgeAndInbox : ITenantKnowledgeProvider, IInboxRealtimeNotifier
{
    public Task<GroundedTenantKnowledgeSnapshot?> GetSnapshotForCustomerAsync(Guid tenantId, string customerPhone, CancellationToken cancellationToken = default) =>
        Task.FromResult<GroundedTenantKnowledgeSnapshot?>(null);

    public Task NotifyAsync(InboxRealtimeNotification notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class WhatsAppIsolationSecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = new SystemClock();
    private readonly WhatsAppAesGcmSecretProtector _protector = WhatsAppAesGcmSecretProtector.FromPassphrase("pitako-wa-sec");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid(), TenantResolutionSource.AuthenticatedSession);
        await using var db = CreateDb(ctx);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private WhatsAppDbContext CreateDb(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<WhatsAppDbContext>().UseSqlite(_connection).Options, ctx, _clock);

    [Fact]
    public async Task TenantA_ShouldNeverReadHijackOrReply_TenantB_WhatsAppInbox_AndRbacMustHold()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var managerA = User.CreateEstablishmentUser(tenantA, "Gerente A", "ger@a.com", "hash", EstablishmentRole.Manager).Value;
        var attendantA = User.CreateEstablishmentUser(tenantA, "Atendente A", "at@a.com", "hash", EstablishmentRole.Attendant).Value;
        var kitchenA = User.CreateEstablishmentUser(tenantA, "Cozinha A", "coz@a.com", "hash", EstablishmentRole.Kitchen).Value;
        var adminB = User.CreateEstablishmentUser(tenantB, "Admin B", "b@b.com", "hash", EstablishmentRole.Administrator).Value;

        var stub = new SecurityStubKnowledgeAndInbox();
        Guid convBId;

        var ctxB = new TenantContext();
        ctxB.SetTenant(tenantB, TenantResolutionSource.AuthenticatedSession);
        await using (var dbB = CreateDb(ctxB))
        {
            var svcB = new WhatsAppSupportService(
                new EfWhatsAppChannelConfigRepository(dbB),
                new EfTraditionalMenuBotRuleRepository(dbB),
                new EfWhatsAppConversationRepository(dbB),
                _protector,
                new UnavailableWhatsAppCloudGateway(),
                stub,
                new UnavailableExternalAiGateway(),
                stub,
                ctxB,
                _clock);

            var chB = (await svcB.ConfigureChannelAsync(adminB, "Loja B", "+5511900000002", "PHONE-B", "WABA-B", "tok-b", "sec-b", "ver-b", WhatsAppAutoReplyMode.HumanOnly)).Value;
            await svcB.AddBotRuleAsync(adminB, chB.Id, "Oi", "oi", BotRuleMatchMode.Exact, "Oi B");
            var convB = (await svcB.ReceiveInboundWebhookAsync("PHONE-B", "wamid.b.1", "5511911112222", "Cli B", "oi")).Value;
            convBId = convB.Id;
        }

        var ctxA = new TenantContext();
        ctxA.SetTenant(tenantA, TenantResolutionSource.AuthenticatedSession);
        await using var dbA = CreateDb(ctxA);
        var svcA = new WhatsAppSupportService(
            new EfWhatsAppChannelConfigRepository(dbA),
            new EfTraditionalMenuBotRuleRepository(dbA),
            new EfWhatsAppConversationRepository(dbA),
            _protector,
            new UnavailableWhatsAppCloudGateway(),
            stub,
            new UnavailableExternalAiGateway(),
            stub,
            ctxA,
            _clock);

        // Global Query Filter oculta canais, regras, conversas e mensagens de B
        Assert.Empty(await dbA.Channels.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.BotRules.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.Conversations.AsNoTracking().ToListAsync());
        Assert.Empty(await dbA.Messages.AsNoTracking().ToListAsync());

        // Cross-tenant webhook targeting PHONE-B e resposta em conversa de B falham
        var crossHook = await svcA.ReceiveInboundWebhookAsync("PHONE-B", "wamid.x", "5511911112222", "Cli B", "oi");
        Assert.True(crossHook.IsFailure);
        Assert.Equal("WhatsApp.ChannelNotFound", crossHook.Error.Code);

        var crossReply = await svcA.SendHumanReplyAsync(managerA, convBId, "Tentativa invasão");
        Assert.True(crossReply.IsFailure);
        Assert.Equal("WhatsApp.ConversationNotFound", crossReply.Error.Code);

        // Assinatura HMAC inválida rejeitada em canal próprio de A
        await svcA.ConfigureChannelAsync(managerA, "Loja A", "+5511900000001", "PHONE-A", "WABA-A", "tok-a", "sec-a", "ver-a", WhatsAppAutoReplyMode.HumanOnly);
        var badSig = await svcA.ReceiveInboundWebhookAsync("PHONE-A", "wamid.a.bad", "5511933334444", "Cli A", "oi", """{"a":1}""", "sha256=fake");
        Assert.True(badSig.IsFailure);
        Assert.Equal("WhatsApp.InvalidWebhookSignature", badSig.Error.Code);

        // RBAC: Atendente NÃO pode reconfigurar tokens Cloud API (exige Approve), mas Cozinha é bloqueada totalmente
        var attendantConfig = await svcA.ConfigureChannelAsync(attendantA, "Hack", "1", "P-X", "W-X", "t", "s", "v");
        Assert.True(attendantConfig.IsFailure);
        Assert.Equal("WhatsApp.Forbidden", attendantConfig.Error.Code);

        var kitchenInbox = await svcA.ListInboxAsync(kitchenA);
        Assert.True(kitchenInbox.IsFailure);
        Assert.Equal("WhatsApp.Forbidden", kitchenInbox.Error.Code);
    }
}
