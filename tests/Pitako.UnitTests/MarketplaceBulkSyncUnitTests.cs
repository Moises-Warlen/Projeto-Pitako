using Pitako.Modules.Marketplaces.Domain;
using Xunit;

namespace Pitako.UnitTests;

public sealed class MarketplaceBulkSyncUnitTests
{
    [Fact]
    public void MarketplaceBulkJob_ShouldReportPartialSuccessWhenAnyStoreFailsOrIsUnsupported_NeverFakeGlobalSuccess()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var utc0 = new DateTimeOffset(2026, 11, 25, 22, 0, 0, TimeSpan.Zero);

        var accIfood = MarketplacePartnerAccount.Create(tenantId, MarketplaceProvider.IFood, "iFood", "c1", "v1:cipher").Value;
        var accAiq = MarketplacePartnerAccount.Create(tenantId, MarketplaceProvider.Aiqfome, "Aiqfome", "c2", "v1:cipher").Value;

        var b1 = MarketplaceStoreBinding.Create(tenantId, accIfood, "IF-01", "Loja Centro").Value;
        var b2 = MarketplaceStoreBinding.Create(tenantId, accIfood, "IF-02", "Loja Shopping").Value;
        var b3 = MarketplaceStoreBinding.Create(tenantId, accAiq, "AQ-01", "Loja Aiqfome").Value;

        var job = MarketplaceBulkJob.Start(
            tenantId,
            MarketplaceBulkOperationType.SyncProductPrice,
            "Sync Preço",
            [b1, b2, b3],
            userId,
            utc0,
            Guid.NewGuid(),
            29.90m).Value;

        job.RecordItemResult(b1.Id, MarketplaceBulkItemOutcome.Succeeded, null, null, utc0.AddSeconds(1));
        job.RecordItemResult(b2.Id, MarketplaceBulkItemOutcome.Failed, "Timeout", "HTTP 504", utc0.AddSeconds(2));
        job.RecordItemResult(b3.Id, MarketplaceBulkItemOutcome.UnsupportedCapability, "Marketplace.CapabilityUnsupported", "Aiqfome não suporta", utc0.AddSeconds(3));

        Assert.Equal(3, job.TotalTargets);
        Assert.Equal(1, job.SucceededCount);
        Assert.Equal(2, job.FailedCount);
        Assert.Equal(MarketplaceBulkJobStatus.PartialSuccess, job.Status);
        Assert.False(job.IsGlobalSuccess);

        // Reprocessa falha de b2 com sucesso -> Aiqfome ainda UnsupportedCapability -> continua PartialSuccess
        job.RecordItemResult(b2.Id, MarketplaceBulkItemOutcome.Succeeded, null, null, utc0.AddSeconds(10));
        Assert.Equal(2, job.SucceededCount);
        Assert.Equal(1, job.FailedCount);
        Assert.Equal(MarketplaceBulkJobStatus.PartialSuccess, job.Status);
        Assert.False(job.IsGlobalSuccess);

        // Job exclusivamente com alvos bem-sucedidos resulta em Completed + IsGlobalSuccess = true
        var cleanJob = MarketplaceBulkJob.Start(
            tenantId,
            MarketplaceBulkOperationType.ToggleProductAvailability,
            "Pausar item",
            [b1, b2],
            userId,
            utc0).Value;

        cleanJob.RecordItemResult(b1.Id, MarketplaceBulkItemOutcome.Succeeded, null, null, utc0.AddSeconds(1));
        cleanJob.RecordItemResult(b2.Id, MarketplaceBulkItemOutcome.Succeeded, null, null, utc0.AddSeconds(2));

        Assert.Equal(MarketplaceBulkJobStatus.Completed, cleanJob.Status);
        Assert.True(cleanJob.IsGlobalSuccess);
    }
}
