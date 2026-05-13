using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pulse;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SignalR plus Pulse services for the hub type <typeparamref name="THub"/> (default <see cref="PulseHub"/>).
    /// For multi-instance scale-out, add a backplane in <paramref name="configureSignalR"/>, e.g. <c>b =&gt; b.AddStackExchangeRedis(...)</c>.
    /// </summary>
    public static IServiceCollection AddPulse<THub>(this IServiceCollection services, Action<PulseOptions>? configure = null, Action<ISignalRServerBuilder>? configureSignalR = null)
        where THub : PulseHub
    {
        var o = services.AddOptions<PulseOptions>();
        if (configure is not null) o.Configure(configure);
        services.TryAddSingleton<IConnectionRegistry, InMemoryConnectionRegistry>();
        services.TryAddSingleton<IChannelAuthorizer, AllowAllChannelAuthorizer>();
        services.TryAddSingleton<IPulsePublisher, PulsePublisher<THub>>();
        configureSignalR?.Invoke(services.AddSignalR());
        if (configureSignalR is null) services.AddSignalR();
        return services;
    }

    public static IServiceCollection AddPulse(this IServiceCollection services, Action<PulseOptions>? configure = null, Action<ISignalRServerBuilder>? configureSignalR = null)
        => services.AddPulse<PulseHub>(configure, configureSignalR);

    /// <summary>Maps the hub at <paramref name="pattern"/>, requiring authorization unless <see cref="PulseOptions.RequireAuthorization"/> is false.</summary>
    public static HubEndpointConventionBuilder MapPulse<THub>(this IEndpointRouteBuilder endpoints, string pattern) where THub : PulseHub
    {
        var builder = endpoints.MapHub<THub>(pattern);
        if (endpoints.ServiceProvider.GetRequiredService<IOptions<PulseOptions>>().Value.RequireAuthorization) builder.RequireAuthorization();
        return builder;
    }

    public static HubEndpointConventionBuilder MapPulse(this IEndpointRouteBuilder endpoints, string pattern) => endpoints.MapPulse<PulseHub>(pattern);
}
