using System.Collections.Concurrent;

namespace Pulse;

public sealed class InMemoryConnectionRegistry : IConnectionRegistry
{
    private readonly ConcurrentDictionary<string, PulseIdentity> _connections = new();

    public void Add(string connectionId, PulseIdentity identity) => _connections[connectionId] = identity;
    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);
    public PulseIdentity? Get(string connectionId) => _connections.GetValueOrDefault(connectionId);

    public IReadOnlyList<string> GetConnections(string tenantId, string? userId = null)
        => [.. _connections.Where(kv => kv.Value.TenantId == tenantId && (userId is null || kv.Value.UserId == userId)).Select(kv => kv.Key)];
}
