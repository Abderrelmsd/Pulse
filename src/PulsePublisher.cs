using Microsoft.AspNetCore.SignalR;

namespace Pulse;

internal sealed class PulsePublisher<THub>(IHubContext<THub> hub, IConnectionRegistry registry) : IPulsePublisher where THub : Hub
{
    public Task BroadcastToTenantAsync(string tenantId, string method, object? payload, CancellationToken ct = default)
        => hub.Clients.Group(PulseGroups.Tenant(Required(tenantId))).SendAsync(method, payload, ct);

    public Task SendToUserAsync(string tenantId, string userId, string method, object? payload, CancellationToken ct = default)
        => hub.Clients.Group(PulseGroups.User(Required(tenantId), Required(userId))).SendAsync(method, payload, ct);

    public Task SendToChannelAsync(string tenantId, string channel, string method, object? payload, CancellationToken ct = default)
        => hub.Clients.Group(PulseGroups.Channel(Required(tenantId), Required(channel))).SendAsync(method, payload, ct);

    public async Task<bool> SendToConnectionAsync(string tenantId, string connectionId, string method, object? payload, CancellationToken ct = default)
    {
        if (registry.Get(Required(connectionId))?.TenantId != Required(tenantId)) return false;
        await hub.Clients.Client(connectionId).SendAsync(method, payload, ct);
        return true;
    }

    public Task BroadcastToAllTenantsAsync(string method, object? payload, CancellationToken ct = default)
        => hub.Clients.All.SendAsync(method, payload, ct);

    private static string Required(string v) => string.IsNullOrWhiteSpace(v) ? throw new ArgumentException("Value is required.") : v;
}
