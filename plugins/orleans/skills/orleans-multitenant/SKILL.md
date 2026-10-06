---
name: orleans-multitenant
description: Multitenancy for Microsoft Orleans 10 with the Orleans.Multitenant NuGet package — per-tenant grain storage and tenant separation of grain calls and streams. Use when an Orleans app needs multitenancy, tenant isolation or per-tenant storage (e.g. multi-tenant SaaS), even if the package is not referenced yet; this skill adds it. Also use in a repo that references Orleans.Multitenant when getting grains or streams, configuring storage or streams, subscribing to streams, or upgrading from 4.x.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.0"
  framework: orleans
  category: multitenancy
  library: Orleans.Multitenant
  library-version: "5.0.1"
  sources:
    - github.com/VincentH-Net/Orleans.Multitenant
---

# Orleans.Multitenant — tenant separation for grains, streams and storage

Use when a Microsoft Orleans 10 application needs multitenancy: each tenant's grain state in its own storage, and grain calls and streams that cannot cross tenants unless explicitly authorized. `Orleans.Multitenant` adds this to Orleans; do not hand-roll it with tenant prefixes in grain keys and per-call tenant checks.

## 1. When to use this skill

Apply when any of these is true:

- An Orleans application needs multitenancy, tenant isolation or per-tenant storage, and does not reference `Orleans.Multitenant` yet — add it (sections 3 and 4)
- The repo references `Orleans.Multitenant` and you write or change code that gets a grain or a stream, subscribes to a stream, or configures grain storage or stream providers
- Upgrading `Orleans.Multitenant` from 4.x to 5.x; follow [references/upgrade-4x-to-5x.md](references/upgrade-4x-to-5x.md)

Out of scope for the library, so the application must handle these:

- Which tenant a caller belongs to (e.g. from the authenticated user of an HTTP request), and the creation and lifecycle of tenants. Tenants are virtual, like grains: every tenant ID always exists
- Grains without a string key: only `IGrainWithStringKey` grains can be tenant specific

### Reference files

This file covers the common path and the mistakes to avoid. **Read the reference file before writing code** for:

- A storage provider other than Azure Table Storage (memory, Azure Blob Storage, .NET Aspire, ADO.NET), or the multitenant storage options: [references/storage-providers.md](references/storage-providers.md)
- A stream filter, a subscription that starts at the earliest available event, or receiving batches: [references/stream-filters-and-start-position.md](references/stream-filters-and-start-position.md)
- Grains or streams that are not tenant specific, or adding multitenancy to an existing application: [references/null-tenant-and-tenant-unaware-streams.md](references/null-tenant-and-tenant-unaware-streams.md)
- An upgrade from 4.x: [references/upgrade-4x-to-5x.md](references/upgrade-4x-to-5x.md)

The API documentation of the installed version is in the NuGet package folder, by default `~/.nuget/packages/orleans.multitenant/<version>/lib/net10.0/Orleans.Multitenant.xml`.

## 2. The one rule

The tenant ID is part of the key of a tenant grain or tenant stream. **Get every tenant grain and tenant stream with the tenant aware API. After that, use the regular Orleans API** for grain calls, publishing, subscribing, resuming, unsubscribing and storing subscription handles.

| Where | Tenant grain | Tenant stream |
|---|---|---|
| In a tenant grain, same tenant | `this.GetTenantGrainFactory().GetGrain<T>(key)` | `this.GetTenantStreamProvider(name).GetStream<T>(ns, key)` |
| In a tenant grain, other tenant | `this.GetTenantGrainFactory(tenantId).GetGrain<T>(key)` | `this.GetTenantStreamProvider(name, tenantId).GetStream<T>(ns, key)` |
| Same tenant as another grain | `factory.ForTenantOf(grain).GetGrain<T>(key)` | |
| No tenant grain: cluster client, API endpoint, stateless worker, grain service | `factory.ForTenant(tenantId).GetGrain<T>(key)` | `client.GetTenantStreamProvider(name, tenantId).GetStream<T>(ns, key)` |

`key` is always the key **within the tenant**. Tenant IDs and keys within a tenant can be any string.

Do not:

- Use `GrainFactory.GetGrain<T>(key)` or `GetStreamProvider(name)` for tenant grains and tenant streams. This compiles. The grain then belongs to the null tenant instead of the caller's tenant, so tenants share it; events on the stream are blocked (section 8)
- Build or parse keys that contain a tenant ID yourself. Use `GetTenantId()` and `GetKeyWithinTenant()`, which exist for a grain (`IAddressable`), a `GrainId` and a `StreamId`
- Assume that code outside a tenant grain is guarded. The last table row is not checked by the library; pass the tenant ID that the application established for the caller

## 3. Prerequisites and installation

- .NET 10 and Microsoft Orleans 10.4.0 or later
- `Orleans.Multitenant` 5.x; 5.0.1 or later when the stream provider serializes events as JSON instead of with the Orleans serializer

Add the package to the silo, client and grain implementation projects:

```bash
dotnet add package Orleans.Multitenant
```

## 4. Configure the silo

The features are independent; add the ones the application needs. Any Orleans storage provider and any Orleans stream provider can be used.

```csharp
siloBuilder
.AddMultitenantCommunicationSeparation() // Without this, grain calls and streams are not separated between tenants
.AddMultitenantGrainStorageAsDefault<AzureTableGrainStorage, AzureTableStorageOptions, AzureTableGrainStorageOptionsValidator>(
    // Called during silo startup
    (silo, name) => silo.AddAzureTableGrainStorage(name, options =>
        options.TableServiceClient = new(connectionString)),

    // Called on the first grain state access for a tenant in a silo
    configureTenantOptions: (options, tenantId) =>
    {
        options.TableServiceClient = new(connectionString);
        options.TableName = $"OrleansGrainState{tenantId}";
    })
.AddMultitenantStreams("provider_name", (silo, name) => silo
    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(name)
    .AddMemoryGrainStorage(name));
```

- **Storage**: the type parameters are the storage type, options type and options validator type of the Orleans storage provider. Use `AddMultitenantGrainStorage(name, ...)` for a named provider. A regular storage provider can exist next to a multitenant one
- **Other storage providers**: a provider whose constructor has extra parameters (e.g. Azure Blob Storage, ADO.NET) needs `getProviderParameters`; see [references/storage-providers.md](references/storage-providers.md)
- **Stream filter**: register it inside the `addStreamProvider` function, with `.AddStreamFilter<MyStreamFilter>(name)` after the stream provider. Never register it for that provider after `AddMultitenantStreams`; see [references/stream-filters-and-start-position.md](references/stream-filters-and-start-position.md)
- **Cluster client**: register the stream provider on the client as usual, then use `client.GetTenantStreamProvider(name, tenantId)`

## 5. Streams

A `TenantStream<T>` has the same methods as an Orleans `IAsyncStream<T>`. It deliberately is not an `IAsyncStream<T>`, so do not try to cast or convert it. Subscription handles are regular `StreamSubscriptionHandle<T>`s.

```csharp
var stream = this.GetTenantStreamProvider("provider_name").GetStream<int>("stream_namespace", "stream_key_within_tenant");

await stream.OnNextAsync(1);
await stream.OnNextBatchAsync([2, 3]);

StreamSubscriptionHandle<int> handle = await stream.SubscribeAsync(OnNextAsync);
await handle.UnsubscribeAsync();

Task OnNextAsync(int item, StreamSequenceToken? token) { /* ... */ }
```

- **Explicit subscriptions**: resume them when the grain is activated, as Orleans prescribes:

  ```csharp
  public override async Task OnActivateAsync(CancellationToken cancellationToken)
  {
      var stream = this.GetTenantStreamProvider("provider_name").GetStream<int>("stream_namespace", this.GetKeyWithinTenant());

      foreach (var handle in await stream.GetAllSubscriptionHandles())
          await handle.ResumeAsync(OnNextAsync);
  }
  ```

- **Implicit subscriptions**: either call `SubscribeAsync` on the tenant stream when the grain is activated, or implement `IStreamSubscriptionObserver` (namespace `Orleans.Streams.Core`) and use `CreateTenantHandle<T>()`, not `Create<T>()`:

  ```csharp
  public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
  {
      var handle = handleFactory.CreateTenantHandle<int>();
      await handle.ResumeAsync(this);
  }
  ```

- **Stream key in a grain**: pass `this.GetKeyWithinTenant()` to `GetStream<T>(namespace, key)`, not `this.GetPrimaryKeyString()`; the latter includes the tenant ID. `GetStream<T>(StreamId)` does accept a stream ID of a tenant stream
- **Handles in grain state**: supported, with the Orleans serializer and with the default JSON grain storage serializer
- **Streams that are not tenant specific**: see [references/null-tenant-and-tenant-unaware-streams.md](references/null-tenant-and-tenant-unaware-streams.md)

## 6. Cross-tenant access

By default tenants cannot communicate, and only calls to `Orleans.*` grain interfaces are exempt. To allow specific access, pass an `ICrossTenantAuthorizer` factory:

```csharp
.AddMultitenantCommunicationSeparation(_ => new CrossTenantAuthorizer())
```

```csharp
class CrossTenantAuthorizer : ICrossTenantAuthorizer
{
    public bool IsAccessAuthorized(string? sourceTenantId, string? targetTenantId)
    => string.Equals(sourceTenantId, "RootTenant", StringComparison.Ordinal); // The root tenant may access any tenant
}
```

- `IsAccessAuthorized` is synchronous and runs when a grain calls a grain of another tenant, or gets a stream provider for another tenant. Keep it fast and in memory: **do not call grains or do I/O in it**, and do not block on async code. When the authorization data changes at runtime, keep it in an in-memory cache per silo that is refreshed in the background (get the cache from the `IServiceProvider` that the factory receives)
- Pass an `IGrainCallTenantSeparator` factory as the second parameter to control which grain calls need authorization
- **The null tenant**: a grain that was not obtained with the tenant aware API has tenant ID `null`. To use such grains on purpose (e.g. grains whose keys are defined by third-party code), see [references/null-tenant-and-tenant-unaware-streams.md](references/null-tenant-and-tenant-unaware-streams.md)

## 7. Checklist

- Every tenant grain and tenant stream is obtained as in section 2; search for `GrainFactory.GetGrain`, `.GetGrain<` on a plain `IGrainFactory` or `IClusterClient`, and `.GetStreamProvider(`
- No code builds or parses a key that contains a tenant ID
- `AddMultitenantCommunicationSeparation` is present when tenants must be isolated
- Every stream filter of a multitenant stream provider is registered inside `AddMultitenantStreams`
- Code outside tenant grains takes the tenant ID from the caller's established identity, not from request data that the caller controls
- A test covers that a grain of one tenant cannot call a grain of another tenant (expect `UnauthorizedAccessException`)

## 8. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `UnauthorizedAccessException`: `Tenant "A" attempted to access tenant "B"` | A grain called a grain of another tenant, or got a stream provider for another tenant, without authorization. `"NULL"` is the null tenant: that grain was obtained with the regular `IGrainFactory` | Get the grain or stream as in section 2, or authorize the access (section 6) |
| Events are published but never received; the silo log has an error with event ID `TenantUnawareStreamApiUsed` (`... was not sent with the tenant aware API`) | The event was published with `GetStreamProvider` on a multitenant stream provider | Publish with `GetTenantStreamProvider` |
| The silo fails to start with an `OrleansConfigurationException`: `... stream filter ... was registered for it afterwards` | A stream filter was registered after `AddMultitenantStreams` | Register it inside the `addStreamProvider` function (section 4) |
| `InvalidOperationException`: `Could not create storage provider ... for tenant ...` | The storage provider has constructor parameters that are not registered as services | Pass `getProviderParameters` (section 4) |
| `ArgumentException`: `streamId ... for tenant ... cannot be retrieved from a stream provider for tenant ...` | `GetStream<T>(StreamId)` was called with a stream ID of another tenant | Get the tenant stream provider for that tenant |
| Events arrive with empty content (`0`, `null`), without an error | The stream provider serializes events as JSON and the package is older than 5.0.1 | Upgrade to 5.0.1 or later |
| After an upgrade from 4.x, grain state fails to load with an exception that mentions `TenantEvent` | The state contains a subscription handle that 4.x stored | Follow step 3 in [references/upgrade-4x-to-5x.md](references/upgrade-4x-to-5x.md) |

## Further reading

- [Orleans.Multitenant repo](https://github.com/VincentH-Net/Orleans.Multitenant) — the readme for people, and an example solution
- [Orleans.Multitenant release notes](https://github.com/VincentH-Net/Orleans.Multitenant/releases)
- [Microsoft Orleans documentation](https://learn.microsoft.com/en-us/dotnet/orleans/)
