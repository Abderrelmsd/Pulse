namespace Pulse;

public sealed class PulseOptions
{
    public const string SectionName = "Pulse";

    /// <summary>Claim carrying the tenant id (Passport tokens use <c>tenant_id</c>).</summary>
    public string TenantClaimType { get; set; } = "tenant_id";

    /// <summary>Claims tried in order for the user id.</summary>
    public string[] UserClaimTypes { get; set; } = ["sub", System.Security.Claims.ClaimTypes.NameIdentifier];

    /// <summary>Reject connections without a tenant claim (recommended; disable only for tenant-less public hubs).</summary>
    public bool RequireTenant { get; set; } = true;

    /// <summary>Require an authenticated user on the hub endpoint.</summary>
    public bool RequireAuthorization { get; set; } = true;

    public int MaxChannelsPerConnection { get; set; } = 50;
}
