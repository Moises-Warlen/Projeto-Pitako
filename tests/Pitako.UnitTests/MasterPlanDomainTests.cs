using Pitako.Modules.MasterPanel.Domain;
using Pitako.SharedKernel.ValueObjects;
using Xunit;

namespace Pitako.UnitTests;

public sealed class MasterPlanDomainTests
{
    private static PlanQuotaConfiguration CreateLimitedQuotas(int stores, int units, int mkt, int users) =>
        new(
            QuotaLimit.Limited(stores).Value,
            QuotaLimit.Limited(units).Value,
            QuotaLimit.Limited(mkt).Value,
            QuotaLimit.Limited(users).Value,
            QuotaLimit.Limited(5).Value,
            QuotaLimit.Limited(5).Value,
            QuotaLimit.Unlimited());

    [Fact]
    public void UnlimitedPlan_ShouldRemoveAllCommercialLimitsWithoutArtificialCeiling()
    {
        var quotas = new PlanQuotaConfiguration(
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited(),
            QuotaLimit.Unlimited());

        var plan = CommercialPlan.Create(
            "ilimitado",
            "Plano Ilimitado",
            "Operação sem limite comercial",
            Money.Create(999.90m).Value,
            BillingCycle.Monthly,
            14,
            quotas,
            ["pdv", "delivery", "marketplaces", "whatsapp", "acai"]).Value;

        Assert.True(plan.EstablishmentsLimit.IsUnlimited);
        Assert.Null(plan.EstablishmentsLimit.MaxAllowed);
        Assert.True(plan.EstablishmentsLimit.CanAllocate(1_000_000));
        Assert.True(plan.MarketplaceStoresLimit.CanAllocate(500_000));
        Assert.True(plan.UsersLimit.CanAllocate(250_000));
    }

    [Fact]
    public void Subscription_ChangePlan_ShouldBlockDowngradeWhenCurrentUsageExceedsTargetLimits()
    {
        var proPlan = CommercialPlan.Create(
            "profissional", "Profissional", "Até 5 lojas",
            Money.Create(399m).Value, BillingCycle.Monthly, 7,
            CreateLimitedQuotas(5, 10, 15, 30), ["pdv", "delivery"]).Value;

        var basicPlan = CommercialPlan.Create(
            "basico", "Básico", "Até 1 loja",
            Money.Create(149m).Value, BillingCycle.Monthly, 7,
            CreateLimitedQuotas(1, 1, 1, 5), ["pdv"]).Value;

        var subscription = Subscription.Create(Guid.NewGuid(), proPlan, DateTimeOffset.UtcNow);
        var currentUsage = new CurrentUsageSnapshot(
            EstablishmentsCount: 3,
            UnitsCount: 3,
            MarketplaceStoresCount: 4,
            UsersCount: 8,
            DevicesCount: 2,
            ConcurrentSessionsCount: 2);

        var downgrade = subscription.ChangePlan(basicPlan, currentUsage, DateTimeOffset.UtcNow);

        Assert.True(downgrade.IsFailure);
        Assert.Equal("Plan.DowngradeBlockedByCurrentUsage", downgrade.Error.Code);
        Assert.Equal(proPlan.Id, subscription.PlanId);
    }

    [Fact]
    public void Subscription_Reactivate_ShouldFailWhileOverdueInvoicesExist()
    {
        var now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        var plan = CommercialPlan.Create(
            "basico", "Básico", "1 loja", Money.Create(149m).Value,
            BillingCycle.Monthly, 0, CreateLimitedQuotas(1, 1, 1, 5), ["pdv"]).Value;

        var subscription = Subscription.Create(Guid.NewGuid(), plan, now, startWithTrial: false);
        var invoice = subscription.IssueInvoice("2026-11", plan.Price, now.AddDays(-5));
        subscription.RefreshOverdueInvoices(now);
        subscription.Suspend("Inadimplência", now);

        var reactivateBlocked = subscription.Reactivate(now);
        Assert.True(reactivateBlocked.IsFailure);
        Assert.Equal("Subscription.HasOverdueInvoices", reactivateBlocked.Error.Code);

        var paid = subscription.RegisterPayment(invoice.Id, now, "gw-123", "pix");
        Assert.True(paid.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }
}
