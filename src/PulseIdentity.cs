using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Pulse;

/// <summary>Who is on the other end of a connection.</summary>
public sealed record PulseIdentity(string TenantId, string? UserId);

/// <summary>Deterministic SignalR group names. Components are URL-escaped so tenant/user/channel ids can never collide across scopes.</summary>
public static partial class PulseGroups
{
    public static string Tenant(string tenantId) => $"t:{Esc(tenantId)}";
    public static string User(string tenantId, string userId) => $"t:{Esc(tenantId)}:u:{Esc(userId)}";
    public static string Channel(string tenantId, string channel) => $"t:{Esc(tenantId)}:c:{Esc(channel)}";

    public static bool IsValidChannelName(string? name) => name is not null && ChannelRegex().IsMatch(name);

    private static string Esc(string s) => Uri.EscapeDataString(s);

    [GeneratedRegex(@"^[A-Za-z0-9:_\-\.]{1,100}$")]
    private static partial Regex ChannelRegex();
}

internal static class PulseIdentityResolver
{
    public static PulseIdentity? Resolve(ClaimsPrincipal? user, PulseOptions o)
    {
        var tenant = user?.FindFirst(o.TenantClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(tenant)) return null;
        var userId = o.UserClaimTypes.Select(t => user!.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        return new PulseIdentity(tenant, userId);
    }
}
