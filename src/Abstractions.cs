namespace Pulse;

/// <summary>Server-side API for pushing messages to clients. Every scope except <see cref="BroadcastToAllTenantsAsync"/> is tenant-bound.</summary>
public interface IPulsePublisher
{
    /// <summary>To every connection of the tenant.</summary>
    Task BroadcastToTenantAsync(string tenantId, string method, object? payload, CancellationToken cancellationToken = default);

    /// <summary>To every connection (tabs/devices) of one user within a tenant.</summary>
    Task SendToUserAsync(string tenantId, string userId, string method, object? payload, CancellationToken cancellationToken = default);

    /// <summary>To members of a named channel inside the tenant (clients join via the hub's <c>JoinChannel</c>).</summary>
    Task SendToChannelAsync(string tenantId, string channel, string method, object? payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// To one connection. The connection must belong to <paramref name="tenantId"/> according to the <see cref="IConnectionRegistry"/>;
    /// returns false (and sends nothing) otherwise.
    /// </summary>
    Task<bool> SendToConnectionAsync(string tenantId, string connectionId, string method, object? payload, CancellationToken cancellationToken = default);

    /// <summary>System-wide announcement across all tenants. Operations use only.</summary>
    Task BroadcastToAllTenantsAsync(string method, object? payload, CancellationToken cancellationToken = default);
}

/// <summary>Tracks live connections for presence and tenant-safe targeted sends. The in-memory default sees only this instance; back it with shared storage for multi-instance targeted sends.</summary>
public interface IConnectionRegistry
{
    void Add(string connectionId, PulseIdentity identity);
    void Remove(string connectionId);
    PulseIdentity? Get(string connectionId);
    IReadOnlyList<string> GetConnections(string tenantId, string? userId = null);
}

/// <summary>Decides whether a connection may join a channel. Default: any channel within the caller's own tenant.</summary>
public interface IChannelAuthorizer
{
    ValueTask<bool> CanJoinAsync(PulseIdentity identity, string channel, CancellationToken cancellationToken = default);
}

internal sealed class AllowAllChannelAuthorizer : IChannelAuthorizer
{
    public ValueTask<bool> CanJoinAsync(PulseIdentity identity, string channel, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
}
