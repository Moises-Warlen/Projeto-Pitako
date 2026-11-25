using System.Security.Cryptography;
using System.Text;
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
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.IntegrationTests;

internal sealed class TestWhatsAppCloudGateway : IWhatsAppCloudGateway
{
    public List<(string ToPhone, string Text)> SentMessages { get; } = [];

    public bool VerifyPayloadSignature(string rawBody, string? xHubSignature256, string decryptedAppSecret) =>
        new UnavailableWhatsAppCloudGateway().VerifyPayloadSignature(rawBody, xHubSignature256, decryptedAppSecret);

    public Task<Result<string>> SendTextMessageAsync(WhatsAppChannelConfig channel, string toPhone, string text, CancellationToken cancellationToken = default)
    {
        SentMessages.Add((toPhone, text));
        return Task.FromResult(Result.Success("wamid.sent.ok"));
    }
}

internal sealed class TestTenantKnowledgeProvider(Guid expectedTenantId) : ITenantKnowledgeProvider
{
    public Task<GroundedTenantKnowledgeSnapshot?> GetSnapshotForCustomerAsync(Guid tenantId, string customerPhone, CancellationToken cancellationToken = default) =>
        Task.FromResult<GroundedTenantKnowledgeSnapshot?>(new GroundedTenantKnowledgeSnapshot(
            expectedTenantId,
            "Açaí Central",
            "09:00 às 22:00",
            "https://acaicentral.exemplo",
            [new GroundedMenuItem("Copo 500ml", 24.90m, true, "Açaí")],
            ["DOCE10"],
            new GroundedOrderFact("#0042", "InPreparation", 24.90m)));
}

internal sealed class SwitchableAiGateway : IAiAssistantGateway
{
    public string NextReply { get; set; } = "O Copo 500ml custa R$ 24,90 e seu pedido #0042 está em preparo.";

    public Task<Result<string>> GenerateGroundedReplyAsync(GroundedTenantKnowledgeSnapshot snapshot, string customerMessage, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(NextReply));
}

internal sealed class RecordingInboxNotifier : IInboxRealtimeNotifier
{
    public List<InboxRealtimeNotification> Notifications { get; } = [];
    public Task NotifyAsync(InboxRealtimeNotification notification, CancellationToken cancellationToken = default)
    {
        Notifications.Add(notification);
        return Task.CompletedTask;
    }
}

public sealed class WhatsAppSupportWorkflowIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 25, 23, 55, 0, TimeSpan.Zero));
    private readonly WhatsAppAesGcmSecretProtector _protector = WhatsAppAesGcmSecretProtector.FromPassphrase("pitako-wa-key");

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
    public async Task WhatsApp_Bot_GroundedAi_AntiHallucinationHandoff_AndHumanInbox_ShouldWorkEndToEnd()
    {
        var tenantId = Guid.NewGuid();
        var manager = User.CreateEstablishmentUser(tenantId, "Gerente", "ger@acai.com", "hash", EstablishmentRole.Manager).Value;
        var attendant = User.CreateEstablishmentUser(tenantId, "Atendente Ana", "ana@acai.com", "hash", EstablishmentRole.Attendant).Value;
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, TenantResolutionSource.AuthenticatedSession);

        var cloud = new TestWhatsAppCloudGateway();
        var knowledge = new TestTenantKnowledgeProvider(tenantId);
        var ai = new SwitchableAiGateway();
        var notifier = new RecordingInboxNotifier();

        const string phoneNumberId = "PHONE-ID-1001";
        const string appSecret = "meta-app-secret-123";

        await using (var db = CreateDb(ctx))
        {
            var svc = new WhatsAppSupportService(
                new EfWhatsAppChannelConfigRepository(db),
                new EfTraditionalMenuBotRuleRepository(db),
                new EfWhatsAppConversationRepository(db),
                _protector,
                cloud,
                knowledge,
                ai,
                notifier,
                ctx,
                _clock);

            var channel = (await svc.ConfigureChannelAsync(
                manager,
                "Açaí Central",
                "+5511999990001",
                phoneNumberId,
                "WABA-1001",
                "meta-permanent-token",
                appSecret,
                "verify-token-secret",
                WhatsAppAutoReplyMode.HybridBotThenAi,
                "https://acaicentral.exemplo")).Value;

            Assert.StartsWith("v1:", channel.EncryptedAccessTokenBlob, StringComparison.Ordinal);

            await svc.AddBotRuleAsync(manager, channel.Id, "Boas Vindas", "oi", BotRuleMatchMode.Exact, "Olá! Bem-vindo ao Açaí Central.", includeMenuLink: true, displayOrder: 1);

            // 1. Webhook com assinatura Meta HMAC válida + idempotência + resposta por Bot Tradicional
            const string raw1 = """{"entry":[{"msg":"oi"}]}""";
            var sig1 = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(raw1))).ToLowerInvariant();

            var conv1 = (await svc.ReceiveInboundWebhookAsync(phoneNumberId, "wamid.1", "5511988887777", "Cliente João", "oi", raw1, sig1)).Value;
            var convDup = (await svc.ReceiveInboundWebhookAsync(phoneNumberId, "wamid.1", "5511988887777", "Cliente João", "oi", raw1, sig1)).Value;
            Assert.Equal(conv1.Id, convDup.Id);
            Assert.Single(cloud.SentMessages);
            Assert.Contains("https://acaicentral.exemplo", cloud.SentMessages[0].Text);

            // 2. Pergunta que aciona IA ancorada em dados reais do tenant
            await svc.ReceiveInboundWebhookAsync(phoneNumberId, "wamid.2", "5511988887777", "Cliente João", "quanto custa o copo 500ml e qual status do pedido?");
            Assert.Equal(2, cloud.SentMessages.Count);
            Assert.Contains("R$ 24,90", cloud.SentMessages[1].Text);

            // 3. Simula tentativa de alucinação da IA citando preço falso R$ 1,99 -> bloqueado e transferido para fila humana!
            ai.NextReply = "Hoje todos os açaís saem por R$ 1,99 no combo falso!";
            var escalated = (await svc.ReceiveInboundWebhookAsync(phoneNumberId, "wamid.3", "5511988887777", "Cliente João", "tem promoção secreta?")).Value;
            Assert.Equal(2, cloud.SentMessages.Count); // Nenhuma resposta alucinada foi enviada ao cliente!
            Assert.Equal(WhatsAppConversationStatus.WaitingHuman, escalated.Status);
            Assert.Contains("anti-alucinação", escalated.HumanHandoffReason);

            // 4. Atendente humano responde no inbox, zera não lidas e encerra conversa
            var replied = (await svc.SendHumanReplyAsync(attendant, escalated.Id, "Olá João! Atendente Ana aqui. No momento não temos combo de R$ 1,99.")).Value;
            Assert.Equal(WhatsAppConversationStatus.AssignedHuman, replied.Status);
            Assert.Equal(0, replied.UnreadCount);

            var resolved = (await svc.ResolveConversationAsync(attendant, escalated.Id)).Value;
            Assert.Equal(WhatsAppConversationStatus.Resolved, resolved.Status);
        }

        // 5. Ambiente offline (UnavailableWhatsAppCloudGateway + UnavailableExternalAiGateway) nunca simula entrega/IA
        await using (var dbOff = CreateDb(ctx))
        {
            var svcOff = new WhatsAppSupportService(
                new EfWhatsAppChannelConfigRepository(dbOff),
                new EfTraditionalMenuBotRuleRepository(dbOff),
                new EfWhatsAppConversationRepository(dbOff),
                _protector,
                new UnavailableWhatsAppCloudGateway(),
                knowledge,
                new UnavailableExternalAiGateway(),
                notifier,
                ctx,
                _clock);

            var offlineConv = (await svcOff.ReceiveInboundWebhookAsync(phoneNumberId, "wamid.4", "5511977776666", "Maria", "dúvida complexa")).Value;
            Assert.Equal(WhatsAppConversationStatus.WaitingHuman, offlineConv.Status);
            Assert.Contains("IA indisponível", offlineConv.HumanHandoffReason);
        }
    }
}
