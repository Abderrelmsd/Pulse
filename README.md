# Pulse

Real-time, two-way updates between your server and browsers or apps, on **SignalR**, with tenant isolation built in. Pulse adds tenancy discipline to SignalR; it is not a new protocol.

Use it to push live changes (an order updated, a notification, a dashboard tick) to exactly the right people without ever leaking a message to another tenant.

## Install

```bash
dotnet add package Pulse
```

## Quick start

```csharp
builder.Services.AddPulse();                      // or AddPulse<MyProductHub>()
app.UseAuthentication(); app.UseAuthorization();
app.MapPulse("/hubs/app");                              // requires an authenticated user by default

await publisher.BroadcastToTenantAsync(tenantId, "orderUpdated", dto);        // everyone in the tenant
await publisher.SendToUserAsync(tenantId, userId, "notification", dto);       // all of one user's tabs and devices
await publisher.SendToChannelAsync(tenantId, "orders", "changed", dto);       // clients call hub.JoinChannel("orders")
await publisher.SendToConnectionAsync(tenantId, connectionId, "m", dto);      // one connection, tenant-checked
```

Each message carries a single payload object, which keeps the client contract simple.

## How isolation works

- **Tenant and user come from the authenticated principal** (the `tenant_id` and `sub` claims, configurable), never from anything the client sends. A connection without a tenant is dropped.
- **Groups are namespaced.** Tenant, user and channel groups are escaped per scope, so ids can never collide across tenants, users or channels.
- **Channels are client-joinable but always under the caller's own tenant.** Names must match `[A-Za-z0-9:_-.]{1,100}`, are capped per connection, and go through `IChannelAuthorizer`, which you can replace to add rules.
- **Single-connection sends are refused** unless the connection registry confirms that connection belongs to the given tenant.
- `BroadcastToAllTenantsAsync` is the one unscoped call. It is for operations use only.

## One hub per product

`PulseHub` is a base class. Products subclass it, map their own route with `MapPulse<THub>`, and `AddPulse<THub>` binds the publisher to that hub.

## Presence and scaling

- `IConnectionRegistry` tracks connections. The in-memory default knows only the local instance.
- **Several instances:** group sends work across instances once you add a backplane, for example `AddPulse(configureSignalR: b => b.AddStackExchangeRedis("..."))`. Checks that use the registry (such as single-connection sends) need a shared registry implementation in that setup. A single instance needs neither.

## Configuration

Section `Pulse`.

| Option | Default | Meaning |
|---|---|---|
| `TenantClaimType` | `tenant_id` | Claim that carries the tenant id (Passport tokens use this) |
| `RequireTenant` | `true` | Reject connections with no tenant claim. Disable only for tenant-less public hubs |
| `RequireAuthorization` | `true` | Require an authenticated user on the hub endpoint |
| `MaxChannelsPerConnection` | `50` | Channels one connection may join |

User id claims are tried in a configurable order (`sub` first).

## Depends on

Nothing else from this set of packages (it is independent of Wire and Bridge).
