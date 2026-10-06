# Tenant storage providers

## Contents

- How tenant storage providers are created
- Memory storage (development and tests)
- Azure Blob Storage
- .NET Aspire with Azure Blob Storage
- ADO.NET
- Multitenant storage options
- When creating a tenant storage provider fails

The Azure Table Storage example is in `SKILL.md`.

## How tenant storage providers are created

`AddMultitenantGrainStorageAsDefault` and `AddMultitenantGrainStorage(name, ...)` exist on `ISiloBuilder` and on `IServiceCollection`. Their type parameters are the storage type, options type and options validator type of the Orleans storage provider. Each tenant gets its own instance of that provider.

| Parameter | Called | Purpose |
|---|---|---|
| `addStorageProvider` `(silo, name)` | During silo startup | Registers the Orleans storage provider, so that common dependencies of the tenant provider instances are initialized |
| `configureTenantOptions` `(options, tenantId)` | On the first grain state access for a tenant in a silo | Sets the options for that tenant's provider instance, e.g. a table, container or database per tenant |
| `getProviderParameters` `(services, providerName, tenantProviderName, options)` | Just before that tenant's provider instance is created | Returns the constructor parameters for the provider instance. Optional |
| `configureOptions` | During silo startup | Configures `MultitenantStorageOptions`. Optional |

By default the provider instance is created with the tenant provider name and the tenant options. Pass `getProviderParameters` when the provider's constructor needs something else: a wrapped options type, or a parameter that is not registered as a service. Do not include `tenantProviderName` in the returned parameters; it is added automatically.

## Memory storage (development and tests)

```csharp
siloBuilder
.AddMultitenantGrainStorageAsDefault<MemoryGrainStorage, MemoryGrainStorageOptions, MemoryGrainStorageOptionsValidator>(
    (silo, name) => silo.AddMemoryGrainStorage(name))
```

## Azure Blob Storage

The constructor of the Orleans Azure Blob storage provider has an `IBlobContainerFactory` parameter. This is not registered as a service; Orleans creates it from the storage options. Do the same for the tenant storage providers:

```csharp
siloBuilder
.AddMultitenantGrainStorageAsDefault<AzureBlobGrainStorage, AzureBlobStorageOptions, AzureBlobStorageOptionsValidator>(
    (silo, name) => silo.AddAzureBlobGrainStorage(name, options =>
        options.BlobServiceClient = new(blobStorageConnectionString)),

    configureTenantOptions: (options, tenantId) => {
        options.BlobServiceClient = new(blobStorageConnectionString);
        options.ContainerName = $"grainstate-{tenantId.ToLowerInvariant()}"; // Blob container names must be lowercase
    },

    getProviderParameters: (services, providerName, tenantProviderName, options) =>
        [options, options.BuildContainerFactory(services, options)]
 )
```

## .NET Aspire with Azure Blob Storage

In the AppHost, do not call `WithGrainStorage`; pass only the blob resource to the silo project, because the multitenant grain storage is added in the silo code:

```csharp
var storage = builder.AddAzureStorage("orleans").RunAsEmulator();
var clusteringTable = storage.AddTables("clustering");
var grainStorage = storage.AddBlobs("grain-state");

var orleans = builder.AddOrleans("default")
                     .WithClustering(clusteringTable);

var yourProject = builder
    .AddProject<Projects. ...>("...")
    .WithReference(orleans)
    .WithReference(grainStorage) // Instead of .WithGrainStorage(grainStorage)
    .WaitFor(clusteringTable)
    .WaitFor(grainStorage);
```

In the silo project, use the blob client that Aspire registers:

```csharp
builder.AddKeyedAzureTableClient("clustering");
builder.AddKeyedAzureBlobClient("grain-state");

builder.UseOrleans(
    silo => silo
    .AddMultitenantGrainStorageAsDefault<AzureBlobGrainStorage, AzureBlobStorageOptions, AzureBlobStorageOptionsValidator>(
        (silo, name) => silo.AddAzureBlobGrainStorage(name, (OptionsBuilder<AzureBlobStorageOptions> options) =>
            options.Configure<IServiceProvider>((options, services) =>
                options.BlobServiceClient = services.GetRequiredKeyedService<BlobServiceClient>("grain-state"))
        ),

        configureTenantOptions: (options, tenantId) =>
        {
            #pragma warning disable CA1308 // Normalize strings to uppercase
            options.ContainerName += "-" + tenantId.ToLowerInvariant();
            #pragma warning restore CA1308 // Normalize strings to uppercase
        },

        getProviderParameters: (services, providerName, tenantProviderName, options) =>
        {
            options.BlobServiceClient = services.GetRequiredKeyedService<BlobServiceClient>("grain-state");
            return [options, options.BuildContainerFactory(services, options)];
        }
    )
);
```

## ADO.NET

The constructor of the Orleans ADO.NET storage provider expects an `IOptions<AdoNetGrainStorageOptions>` instead of an `AdoNetGrainStorageOptions`. Wrap the options:

```csharp
.AddMultitenantGrainStorageAsDefault<AdoNetGrainStorage, AdoNetGrainStorageOptions, AdoNetGrainStorageOptionsValidator>(
    (silo, name) => silo.AddAdoNetGrainStorage(name, options => options.ConnectionString = sqlConnectionString),

    configureTenantOptions: (options, tenantId) => options.ConnectionString = sqlConnectionString.Replace("[DatabaseName]", tenantId, StringComparison.Ordinal),

    getProviderParameters: (services, providerName, tenantProviderName, options) => [Options.Create(options)]
)
```

## Multitenant storage options

`MultitenantStorageOptions` is read from the configuration section `MultitenantStorage:<provider name>`; the name of the default storage provider is `Default`. It can also be set with the `configureOptions` parameter.

| Option | Default | Purpose |
|---|---|---|
| `TenantIdForNullTenant` | `"Null"` | The `tenantId` that `configureTenantOptions` receives for the null tenant. Choose a value that cannot be a tenant ID of the application |
| `TenantStorageProviderInitTimeout` | 20 seconds | The timeout for initializing a tenant's provider instance. Between 1 and 600 seconds |

## When creating a tenant storage provider fails

An `InvalidOperationException` with `Could not create storage provider ... for tenant ...` on the first grain state access for a tenant means that the provider's constructor has a parameter that is not registered as a service. Pass `getProviderParameters`, as in the Azure Blob Storage and ADO.NET examples.
