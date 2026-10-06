# Stream filters and subscription start position

## Stream filters

Register an Orleans stream filter for a multitenant stream provider in the same function that registers the stream provider:

```csharp
.AddMultitenantStreams(
    "provider_name", (silo, name) => silo
    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(name)
    .AddMemoryGrainStorage(name)
    .AddStreamFilter<MyStreamFilter>(name)
 )
```

```csharp
class MyStreamFilter : IStreamFilter // In namespace Orleans.Streams.Filtering
{
    // item is the event as it was sent (e.g. an int for a TenantStream<int>);
    // streamId.GetTenantId() and streamId.GetKeyWithinTenant() identify the tenant stream
    public bool ShouldDeliver(StreamId streamId, object item, string? filterData)
    => item is int number && number % 2 == 0; // Only deliver even numbers
}
```

- The filter is invoked for events that were sent with the tenant aware API, and it receives the same events as it would without multitenancy. Events that were sent with a tenant unaware API are blocked before they reach the filter
- `filterData` is the value that the subscriber passed to `SubscribeAsync`
- **Do not register a stream filter for the stream provider after `AddMultitenantStreams`.** Orleans only uses the stream filter that was registered last for a stream provider, so that would disable tenant separation for the provider. To guard against it, the silo then fails to start with an `OrleansConfigurationException`

## Subscription start position

By default a subscription starts at the latest event. To start at the earliest event that the stream provider still has available, subscribe an `IAsyncObserver<T>` or an `IAsyncBatchObserver<T>` with a `StreamSubscriptionStartPosition`:

```csharp
var handle = await stream.SubscribeAsync(observer, StreamSubscriptionStartPosition.EarliestAvailable);
```

The overload for an `IAsyncObserver<T>` also takes an optional `filterData`:

```csharp
var handle = await stream.SubscribeAsync(observer, StreamSubscriptionStartPosition.Latest, "filter data");
```

## Receiving batches

Subscribe with a batch delegate or an `IAsyncBatchObserver<T>` to receive events in batches:

```csharp
StreamSubscriptionHandle<int> handle = await stream.SubscribeAsync(OnNextBatchAsync);

Task OnNextBatchAsync(IList<SequentialItem<int>> items) { /* ... */ }
```
