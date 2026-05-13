using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pulse;

/// <summary>
/// Base hub. On connect the connection joins its tenant group and its user group (derived from the authenticated principal, never from client input).
/// Clients may join/leave named channels scoped to their own tenant. Subclass per product to add hub methods; map with <c>MapPulse&lt;THub&gt;</c>.
/// </summary>
public class PulseHub(IConnectionRegistry registry, IChannelAuthorizer channels, IOptions<PulseOptions> options, ILogger<PulseHub> logger) : Hub
{
    private readonly PulseOptions _options = options.Value;

    public override async Task OnConnectedAsync()
    {
        var identity = PulseIdentityResolver.Resolve(Context.User, _options);
        if (identity is null)
        {
            if (_options.RequireTenant)
            {
                logger.LogWarning("Rejecting connection {ConnectionId}: no '{Claim}' claim", Context.ConnectionId, _options.TenantClaimType);
                Context.Abort();
                return;
            }
            await base.OnConnectedAsync();
            return;
        }

        registry.Add(Context.ConnectionId, identity);
        await Groups.AddToGroupAsync(Context.ConnectionId, PulseGroups.Tenant(identity.TenantId));
        if (identity.UserId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, PulseGroups.User(identity.TenantId, identity.UserId));
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Remove(Context.ConnectionId); // SignalR removes group memberships itself
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Joins a channel inside the caller's own tenant.</summary>
    public async Task JoinChannel(string channel)
    {
        var identity = registry.Get(Context.ConnectionId) ?? throw new HubException("Not connected to a tenant.");
        if (!PulseGroups.IsValidChannelName(channel)) throw new HubException("Invalid channel name.");
        if (Joined.Count >= _options.MaxChannelsPerConnection && !Joined.Contains(channel)) throw new HubException("Channel limit reached.");
        if (!await channels.CanJoinAsync(identity, channel, Context.ConnectionAborted)) throw new HubException("Not allowed to join this channel.");

        Joined.Add(channel);
        await Groups.AddToGroupAsync(Context.ConnectionId, PulseGroups.Channel(identity.TenantId, channel));
    }

    public async Task LeaveChannel(string channel)
    {
        var identity = registry.Get(Context.ConnectionId) ?? throw new HubException("Not connected to a tenant.");
        if (!PulseGroups.IsValidChannelName(channel)) throw new HubException("Invalid channel name.");
        Joined.Remove(channel);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, PulseGroups.Channel(identity.TenantId, channel));
    }

    private HashSet<string> Joined
    {
        get
        {
            if (Context.Items.TryGetValue("pulse.channels", out var v) && v is HashSet<string> set) return set;
            var created = new HashSet<string>();
            Context.Items["pulse.channels"] = created;
            return created;
        }
    }
}
