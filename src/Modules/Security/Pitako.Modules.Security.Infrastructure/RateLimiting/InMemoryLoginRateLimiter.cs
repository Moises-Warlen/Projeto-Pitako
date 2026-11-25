using System.Collections.Concurrent;
using Pitako.Modules.Security.Application.Abstractions;

namespace Pitako.Modules.Security.Infrastructure.RateLimiting;

/// <summary>
/// Limitador de tentativas em memória para instância única/desenvolvimento.
/// Em produção multi-instância será substituído pelo adaptador distribuído em Redis.
/// </summary>
public sealed class InMemoryLoginRateLimiter : ILoginRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _attempts = new();
    private readonly SecurityPolicyOptions _policy;

    public InMemoryLoginRateLimiter(SecurityPolicyOptions? policy = null)
    {
        _policy = policy ?? new SecurityPolicyOptions();
    }

    public bool IsAllowed(string key, DateTimeOffset utcNow)
    {
        if (!_attempts.TryGetValue(key, out var queue))
            return true;

        lock (queue)
        {
            PruneExpired(queue, utcNow);
            return queue.Count < _policy.RateLimitMaxAttemptsPerWindow;
        }
    }

    public void RegisterAttempt(string key, DateTimeOffset utcNow)
    {
        var queue = _attempts.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            PruneExpired(queue, utcNow);
            queue.Enqueue(utcNow);
        }
    }

    public void Reset(string key) => _attempts.TryRemove(key, out _);

    private void PruneExpired(Queue<DateTimeOffset> queue, DateTimeOffset utcNow)
    {
        var cutoff = utcNow.Subtract(_policy.RateLimitWindow);
        while (queue.Count > 0 && queue.Peek() <= cutoff)
        {
            queue.Dequeue();
        }
    }
}
