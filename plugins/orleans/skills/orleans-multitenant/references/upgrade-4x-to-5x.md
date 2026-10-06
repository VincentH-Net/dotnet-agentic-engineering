# Upgrade Orleans.Multitenant from 4.x to 5.x

Version 5 makes tenant streams transparent after a tenant stream is obtained: subscription handles are regular Orleans handles. The breaking changes are limited to tenant streams and to the required Orleans version; tenant grains, grain calls, cross-tenant authorization and tenant storage work as in 4.x.

The serialized form of stream events is unchanged: subscriptions that were made with 4.x keep working, and 4.x and 5.x silos can exchange tenant stream events during a rolling upgrade. Verify the latter for the stream provider that the application uses.

## Steps

1. **Orleans**: upgrade the Orleans packages to 10.4.0 or later, then `Orleans.Multitenant` to the latest 5.x
2. **Handle types**: replace `StreamSubscriptionHandle<TenantEvent<T>>` with `StreamSubscriptionHandle<T>` everywhere, except for a member in grain state (step 3). `TenantEvent<T>` is no longer public; search for `TenantEvent` and remove every use, including workarounds for the old handle type such as wrappers, reflection and casts
3. **Handles stored in grain state**: a subscription handle that 4.x stored in grain state cannot be read by 5.x. If only the type of the state member is changed, the grain state fails to load and the grain cannot be activated. Give the member a new name and a new `[Id]`, and do not reuse the old name or id:

   ```csharp
   // 4.x
   [Id(1)] public StreamSubscriptionHandle<TenantEvent<int>>? Handle { get; set; }

   // 5.x
   [Id(2)] public StreamSubscriptionHandle<int>? Subscription { get; set; }
   ```

   The new member is empty after the upgrade. Get the handle from the stream when the grain is activated:

   ```csharp
   public override async Task OnActivateAsync(CancellationToken cancellationToken)
   {
       var stream = this.GetTenantStreamProvider("provider_name").GetStream<int>("stream_namespace", this.GetKeyWithinTenant());

       foreach (var handle in await stream.GetAllSubscriptionHandles())
           state.State.Subscription = await handle.ResumeAsync(OnNextAsync);

       await state.WriteStateAsync(); // state is the grain's IPersistentState
   }
   ```

4. **Handler token**: declare the token parameter of `onNextAsync` handlers as `StreamSequenceToken?`; with nullable reference types enabled, a non-nullable parameter causes warning CS8622
5. **Stream keys**: `GetStream<T>(namespace, key)` no longer accepts a key that includes the tenant ID. Where such a key was passed (e.g. `this.GetPrimaryKeyString()` in a grain with an implicit subscription), pass `this.GetKeyWithinTenant()`, or use `GetStream<T>(StreamId)`
6. **Stream filters**: a stream filter that is registered for a multitenant stream provider after `AddMultitenantStreams` now fails silo startup with an `OrleansConfigurationException`; in 4.x it silently disabled tenant separation. Move it into the `addStreamProvider` function of `AddMultitenantStreams`, as `.AddStreamFilter<MyStreamFilter>(name)` after the stream provider. A filter that was registered inside `addStreamProvider` was silently ignored in 4.x and is now invoked

## New in 5.x

- `ResumeAsync` on a handle, with an observer or with delegates, works for subscriptions to tenant streams
- `handleFactory.CreateTenantHandle<T>()` for implicit subscriptions that use `IStreamSubscriptionObserver`
- `SubscribeAsync` with batch delegates, and with a `StreamSubscriptionStartPosition`
- Any string is a valid stream key within a tenant, as it already was for grain keys

## Verify

- The solution builds without references to `TenantEvent`
- Grains that store a subscription handle in their state activate, and receive events after a restart
- Events that are published before and after the upgrade are received by subscribers that subscribed before the upgrade
