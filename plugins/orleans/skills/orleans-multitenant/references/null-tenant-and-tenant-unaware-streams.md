# The null tenant and tenant unaware streams

Use this when part of the application is not tenant specific: grains or streams whose keys are defined by third-party code, shared grains, or an existing application that adds multitenancy.

## The null tenant

A grain that was not obtained with the tenant aware API has tenant ID `null`; it belongs to the null tenant. The empty string `""` is a regular tenant ID, not the null tenant.

- The null tenant cannot be specified in the tenant aware API. To get null tenant grains, use the regular Orleans `IGrainFactory`
- `null` is a valid value for both parameters of `ICrossTenantAuthorizer.IsAccessAuthorized`. Calls between a tenant grain and a null tenant grain need authorization like any other cross-tenant call:

  ```csharp
  class CrossTenantAuthorizer : ICrossTenantAuthorizer
  {
      public bool IsAccessAuthorized(string? sourceTenantId, string? targetTenantId)
      => sourceTenantId is null || targetTenantId is null; // Allow access between the null tenant and any tenant
  }
  ```

- Alternatively, exempt specific grain interfaces from authorization with an `IGrainCallTenantSeparator`, passed as the second parameter of `AddMultitenantCommunicationSeparation`. Keep the default exemption for `Orleans.*` interfaces:

  ```csharp
  class GrainCallTenantSeparator : IGrainCallTenantSeparator
  {
      public bool IsTenantSeparatedCall(IIncomingGrainCallContext context)
      => !context.InterfaceName.StartsWith("ThirdParty.Grains.", StringComparison.Ordinal)
      && !context.InterfaceName.StartsWith("Orleans.", StringComparison.Ordinal);
  }
  ```

- In storage, `configureTenantOptions` receives `MultitenantStorageOptions.TenantIdForNullTenant` (default `"Null"`) as the `tenantId` of the null tenant. Set it to a value that cannot be a tenant ID of the application, in the configuration section `MultitenantStorage:<provider name>` (the name of the default storage provider is `Default`) or with the `configureOptions` parameter of the `AddMultitenantGrainStorage` methods

## Adding multitenancy to an existing application

Two ways to keep existing grain state:

- Keep the existing grains in the null tenant: they keep their keys. In `configureTenantOptions`, point the null tenant at the storage that holds the existing state (e.g. the existing table), and verify that the existing state is still read
- Add a multitenant storage provider next to the existing storage provider, and use the multitenant one only for the new tenant grains

## Tenant unaware streams

To use streams that are not tenant specific (e.g. streams whose keys are defined by third-party code), use the regular Orleans `IStreamProvider` of a stream provider that was not added with `AddMultitenantStreams`. No `ICrossTenantAuthorizer` is needed for this, because such a stream provider has no tenant separation.

On a stream provider that was added with `AddMultitenantStreams`, events that are published with the regular Orleans API are blocked.
