using Microsoft.EntityFrameworkCore;
using Pitako.Modules.MasterPanel.Application.Abstractions;
using Pitako.Modules.MasterPanel.Domain;

namespace Pitako.Modules.MasterPanel.Infrastructure.Persistence;

public sealed class EfCommercialPlanRepository(MasterPanelDbContext db) : ICommercialPlanRepository
{
    private IQueryable<CommercialPlan> FullQuery() =>
        db.CommercialPlans.Include(p => p.Modules).Include(p => p.Features);

    public Task<CommercialPlan?> FindByIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

    public Task<CommercialPlan?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(p => p.Code == code.Trim().ToLowerInvariant(), cancellationToken);

    public async Task<IReadOnlyList<CommercialPlan>> ListAllAsync(bool onlyActive = false, CancellationToken cancellationToken = default)
    {
        var query = FullQuery();
        if (onlyActive)
            query = query.Where(p => p.IsActive);

        return await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(CommercialPlan plan, CancellationToken cancellationToken = default) =>
        await db.CommercialPlans.AddAsync(plan, cancellationToken);

    public void Remove(CommercialPlan plan) =>
        db.CommercialPlans.Remove(plan);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfMasterCustomerRepository(MasterPanelDbContext db) : IMasterCustomerRepository
{
    public Task<MasterCustomerAccount?> FindByIdAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

    public async Task<IReadOnlyList<MasterCustomerAccount>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.Customers.OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(MasterCustomerAccount customer, CancellationToken cancellationToken = default) =>
        await db.Customers.AddAsync(customer, cancellationToken);

    public void Remove(MasterCustomerAccount customer) =>
        db.Customers.Remove(customer);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfSubscriptionRepository(MasterPanelDbContext db) : ISubscriptionRepository
{
    private IQueryable<Subscription> FullQuery() =>
        db.Subscriptions.Include(s => s.Invoices);

    public Task<Subscription?> FindByIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default) =>
        FullQuery().FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);

    public async Task<Subscription?> FindLatestByCustomerIdAsync(Guid customerAccountId, CancellationToken cancellationToken = default)
    {
        var items = await FullQuery()
            .Where(s => s.CustomerAccountId == customerAccountId)
            .ToListAsync(cancellationToken);

        return items.OrderByDescending(s => s.StartedAtUtc).FirstOrDefault();
    }

    public async Task<IReadOnlyList<Subscription>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await FullQuery().ToListAsync(cancellationToken);

    public async Task AddAsync(Subscription subscription, CancellationToken cancellationToken = default) =>
        await db.Subscriptions.AddAsync(subscription, cancellationToken);

    public void Remove(Subscription subscription) =>
        db.Subscriptions.Remove(subscription);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfSupportTicketRepository(MasterPanelDbContext db) : ISupportTicketRepository
{
    public Task<SupportTicket?> FindByIdAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        db.SupportTickets.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken);

    public async Task<IReadOnlyList<SupportTicket>> ListOpenAsync(CancellationToken cancellationToken = default) =>
        await db.SupportTickets
            .Where(t => t.Status != SupportTicketStatus.Resolved && t.Status != SupportTicketStatus.Closed)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SupportTicket ticket, CancellationToken cancellationToken = default) =>
        await db.SupportTickets.AddAsync(ticket, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfGlobalSettingRepository(MasterPanelDbContext db) : IGlobalSettingRepository
{
    public Task<GlobalPlatformSetting?> FindByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        db.GlobalSettings.FirstOrDefaultAsync(s => s.Key == key.Trim().ToLowerInvariant(), cancellationToken);

    public async Task<IReadOnlyList<GlobalPlatformSetting>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.GlobalSettings.OrderBy(s => s.Key).ToListAsync(cancellationToken);

    public async Task AddAsync(GlobalPlatformSetting setting, CancellationToken cancellationToken = default) =>
        await db.GlobalSettings.AddAsync(setting, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
