using Microsoft.EntityFrameworkCore;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Domain;

namespace Pitako.Modules.Security.Infrastructure.Persistence;

public sealed class EfUserRepository : IUserRepository
{
    private readonly SecurityDbContext _dbContext;

    public EfUserRepository(SecurityDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    private IQueryable<User> AggregateQuery() =>
        _dbContext.Users
            .Include(u => u.ModulePermissions)
            .Include(u => u.Sessions)
            .Include(u => u.PasswordResetTokens);

    public Task<User?> FindByEmailAsync(UserScope scope, Guid? tenantId, string normalizedEmail, CancellationToken cancellationToken = default) =>
        AggregateQuery().FirstOrDefaultAsync(
            u => u.Scope == scope && u.TenantId == tenantId && u.NormalizedEmail == normalizedEmail,
            cancellationToken);

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        AggregateQuery().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<User?> FindByPasswordResetTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        AggregateQuery().FirstOrDefaultAsync(
            u => u.PasswordResetTokens.Any(t => t.TokenHash == tokenHash),
            cancellationToken);

    public Task<User?> FindBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        AggregateQuery().FirstOrDefaultAsync(
            u => u.Sessions.Any(s => s.Id == sessionId),
            cancellationToken);

    public async Task<IReadOnlyList<User>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await AggregateQuery()
            .Where(u => u.Scope == UserScope.Establishment && u.TenantId == tenantId)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        await _dbContext.Users.AddAsync(user, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class EfSecurityAuditRepository : ISecurityAuditRepository
{
    private readonly SecurityDbContext _dbContext;

    public EfSecurityAuditRepository(SecurityDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(SecurityAuditLog auditLog, CancellationToken cancellationToken = default) =>
        await _dbContext.SecurityAuditLogs.AddAsync(auditLog, cancellationToken);

    public async Task<IReadOnlyList<SecurityAuditLog>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SecurityAuditLogs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        return await ExecuteOrderedAuditQueryAsync(query, cancellationToken);
    }

    public async Task<IReadOnlyList<SecurityAuditLog>> ListSaasAsync(CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SecurityAuditLogs
            .AsNoTracking()
            .Where(x => x.Scope == UserScope.Saas);

        return await ExecuteOrderedAuditQueryAsync(query, cancellationToken);
    }

    private async Task<IReadOnlyList<SecurityAuditLog>> ExecuteOrderedAuditQueryAsync(
        IQueryable<SecurityAuditLog> query,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsNpgsql())
            return await query.OrderByDescending(x => x.OccurredAtUtc).ToListAsync(cancellationToken);

        var items = await query.ToListAsync(cancellationToken);
        return items.OrderByDescending(x => x.OccurredAtUtc).ToList();
    }
}
