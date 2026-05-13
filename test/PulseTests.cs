using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pulse.Tests;

/// <summary>Authenticates from X-Tenant / X-User headers (test only).</summary>
public sealed class HeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("sub", user!) };
        if (Request.Headers.TryGetValue("X-Tenant", out var t)) claims.Add(new Claim("tenant_id", t!));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), "test")));
    }
}

public sealed class DenySecret : IChannelAuthorizer
{
    public ValueTask<bool> CanJoinAsync(PulseIdentity identity, string channel, CancellationToken ct = default) => ValueTask.FromResult(channel != "secret");
}

public sealed class PulseFixture : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly TestServer _server;
    private readonly List<HubConnection> _connections = [];
    public IPulsePublisher Publisher => _app.Services.GetRequiredService<IPulsePublisher>();
    public IConnectionRegistry Registry => _app.Services.GetRequiredService<IConnectionRegistry>();

    private PulseFixture(WebApplication app) { _app = app; _server = (TestServer)app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>(); }

    public static async Task<PulseFixture> StartAsync(Action<IServiceCollection>? more = null, Action<PulseOptions>? configure = null)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        b.Logging.ClearProviders();
        b.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderAuth>("test", _ => { });
        b.Services.AddAuthorization();
        b.Services.AddPulse(configure);
        more?.Invoke(b.Services);
        var app = b.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPulse("/hub");
        await app.StartAsync();
        return new PulseFixture(app);
    }

    public async Task<(HubConnection Conn, List<string> Messages)> ConnectAsync(string? tenant, string? user)
    {
        var messages = new List<string>();
        var conn = new HubConnectionBuilder()
            .WithUrl(new Uri(_server.BaseAddress, "hub"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                if (user is not null) o.Headers["X-User"] = user;
                if (tenant is not null) o.Headers["X-Tenant"] = tenant;
            }).Build();
        conn.On<string>("msg", m => { lock (messages) messages.Add(m); });
        _connections.Add(conn);
        await conn.StartAsync();
        return (conn, messages);
    }

    public static async Task Eventually(Func<bool> condition, int ms = 3000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(20);
    }

    public static async Task Quiet() => await Task.Delay(300); // negative assertions: give a wrong delivery time to arrive

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _connections) await c.DisposeAsync();
        await _app.DisposeAsync();
    }
}

public class PulseTests
{
    [Fact]
    public async Task Tenant_broadcast_reaches_only_that_tenants_connections()
    {
        await using var f = await PulseFixture.StartAsync();
        var (_, a1) = await f.ConnectAsync("acme", "u1");
        var (_, a2) = await f.ConnectAsync("acme", "u2");
        var (_, g1) = await f.ConnectAsync("globex", "u3");

        await f.Publisher.BroadcastToTenantAsync("acme", "msg", "hello acme");

        await PulseFixture.Eventually(() => a1.Count == 1 && a2.Count == 1);
        await PulseFixture.Quiet();
        Assert.Equal(["hello acme"], a1);
        Assert.Equal(["hello acme"], a2);
        Assert.Empty(g1);
    }

    [Fact]
    public async Task User_targeting_reaches_all_of_a_users_connections_within_the_tenant_only()
    {
        await using var f = await PulseFixture.StartAsync();
        var (_, tab1) = await f.ConnectAsync("acme", "u1");
        var (_, tab2) = await f.ConnectAsync("acme", "u1");
        var (_, other) = await f.ConnectAsync("acme", "u2");
        var (_, sameUserOtherTenant) = await f.ConnectAsync("globex", "u1");

        await f.Publisher.SendToUserAsync("acme", "u1", "msg", "for u1");

        await PulseFixture.Eventually(() => tab1.Count == 1 && tab2.Count == 1);
        await PulseFixture.Quiet();
        Assert.Single(tab1); Assert.Single(tab2);
        Assert.Empty(other); Assert.Empty(sameUserOtherTenant);
    }

    [Fact]
    public async Task Targeted_connection_send_is_tenant_checked()
    {
        await using var f = await PulseFixture.StartAsync();
        var (conn, msgs) = await f.ConnectAsync("acme", "u1");
        var id = conn.ConnectionId!;

        Assert.False(await f.Publisher.SendToConnectionAsync("globex", id, "msg", "cross-tenant"));
        Assert.False(await f.Publisher.SendToConnectionAsync("acme", "unknown-connection", "msg", "x"));
        Assert.True(await f.Publisher.SendToConnectionAsync("acme", id, "msg", "direct"));

        await PulseFixture.Eventually(() => msgs.Count == 1);
        await PulseFixture.Quiet();
        Assert.Equal(["direct"], msgs);
    }

    [Fact]
    public async Task Channels_are_tenant_scoped_and_joinable_by_clients()
    {
        await using var f = await PulseFixture.StartAsync();
        var (a, am) = await f.ConnectAsync("acme", "u1");
        var (g, gm) = await f.ConnectAsync("globex", "u2");
        var (_, notJoined) = await f.ConnectAsync("acme", "u3");
        await a.InvokeAsync("JoinChannel", "orders");
        await g.InvokeAsync("JoinChannel", "orders"); // same name, different tenant

        await f.Publisher.SendToChannelAsync("acme", "orders", "msg", "acme orders");

        await PulseFixture.Eventually(() => am.Count == 1);
        await PulseFixture.Quiet();
        Assert.Equal(["acme orders"], am);
        Assert.Empty(gm);
        Assert.Empty(notJoined);

        await a.InvokeAsync("LeaveChannel", "orders");
        await f.Publisher.SendToChannelAsync("acme", "orders", "msg", "again");
        await PulseFixture.Quiet();
        Assert.Single(am);
    }

    [Fact]
    public async Task Invalid_denied_and_excess_channels_are_rejected()
    {
        await using var f = await PulseFixture.StartAsync(s => s.AddSingleton<IChannelAuthorizer, DenySecret>(), o => o.MaxChannelsPerConnection = 2);
        var (c, _) = await f.ConnectAsync("acme", "u1");

        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync("JoinChannel", "bad name!"));
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync("JoinChannel", "secret"));
        await c.InvokeAsync("JoinChannel", "a");
        await c.InvokeAsync("JoinChannel", "b");
        await c.InvokeAsync("JoinChannel", "a"); // re-joining is fine
        await Assert.ThrowsAsync<HubException>(() => c.InvokeAsync("JoinChannel", "c"));
    }

    [Fact]
    public async Task Connections_without_a_tenant_are_dropped_and_unauthenticated_are_refused()
    {
        await using var f = await PulseFixture.StartAsync();
        var (noTenant, _) = await f.ConnectAsync(null, "u1");
        await PulseFixture.Eventually(() => noTenant.State == HubConnectionState.Disconnected);
        Assert.Equal(HubConnectionState.Disconnected, noTenant.State);

        await Assert.ThrowsAnyAsync<Exception>(() => f.ConnectAsync(null, null)); // 401
    }

    [Fact]
    public async Task Registry_tracks_presence_and_cleans_up_on_disconnect()
    {
        await using var f = await PulseFixture.StartAsync();
        var (c1, _) = await f.ConnectAsync("acme", "u1");
        await f.ConnectAsync("acme", "u2");
        Assert.Equal(2, f.Registry.GetConnections("acme").Count);
        Assert.Single(f.Registry.GetConnections("acme", "u1"));

        await c1.StopAsync();
        await PulseFixture.Eventually(() => f.Registry.GetConnections("acme").Count == 1);
        Assert.Single(f.Registry.GetConnections("acme"));
    }

    [Fact]
    public void Group_names_cannot_collide_across_scopes()
    {
        Assert.NotEqual(PulseGroups.Tenant("a:c:b"), PulseGroups.Channel("a", "b"));
        Assert.NotEqual(PulseGroups.User("a", "x:y"), PulseGroups.User("a:u:x", "y"));
        Assert.True(PulseGroups.IsValidChannelName("orders.eu-1"));
        Assert.False(PulseGroups.IsValidChannelName("../x"));
    }

    [Fact]
    public async Task Broadcast_to_all_tenants_reaches_everyone()
    {
        await using var f = await PulseFixture.StartAsync();
        var (_, a) = await f.ConnectAsync("acme", "u1");
        var (_, g) = await f.ConnectAsync("globex", "u2");
        await f.Publisher.BroadcastToAllTenantsAsync("msg", "maintenance");
        await PulseFixture.Eventually(() => a.Count == 1 && g.Count == 1);
        Assert.Single(a); Assert.Single(g);
    }
}
