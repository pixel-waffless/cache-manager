# Cache Manager

Cache Manager is a .NET 10 library for resolving named cache providers behind a common interface. It currently supports in-memory cache and multiple Redis providers.

## Features

- Named cache providers resolved through `ICacheManager`.
- In-memory and Redis implementations.
- Multiple Redis connections, one shared `ConnectionMultiplexer` per Redis provider name.
- Case-insensitive provider names.
- Provider-scoped namespaces are applied internally to every key.
- Safe Redis connection logging without exposing connection strings.
- Lazy in-memory fallback provider when a configured provider is unavailable.

## Installation

Reference the project or package from your application, then register it in dependency injection:

```csharp
using CacheManager.Extensions;

builder.Services.AddCacheManager(builder.Configuration);
```

## Configuration

Configure providers under the `CacheSettings` section.

```json
{
  "CacheSettings": {
    "Providers": [
      {
        "Name": "Default",
        "Type": "Memory",
        "Namespace": "default",
        "ExpirationMinutes": 60
      },
      {
        "Name": "OrdersRedis",
        "Type": "Redis",
        "Namespace": "orders",
        "ConnectionString": "localhost:6379",
        "ExpirationMinutes": 30,
        "FailureThreshold": 3,
        "DisconnectSeconds": 30
      }
    ]
  }
}
```

You can also configure providers in code:

```csharp
using CacheManager.Extensions;
using CacheManager.Settings;
using CacheManager.Types;

builder.Services.AddCacheManager(
    builder.Configuration,
    settings: options =>
    {
        options.Providers.Add(new CacheProviderSettings
        {
            Name = "Default",
            Type = ProviderType.Memory,
            Namespace = "default",
            ExpirationMinutes = 60
        });

        options.Providers.Add(new CacheProviderSettings
        {
            Name = "OrdersRedis",
            Type = ProviderType.Redis,
            Namespace = "orders",
            ConnectionString = "localhost:6379",
            ExpirationMinutes = 30
        });
    });
```

## Usage

Resolve `ICacheManager` and request a provider by name.

```csharp
using CacheManager;

public sealed class OrdersService(ICacheManager cacheManager)
{
    public async Task<OrderDto?> GetOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        var cache = cacheManager.GetCacheProvider("OrdersRedis");
        var key = orderId;

        var cached = await cache.GetAsync<OrderDto>(key, cancellationToken);
        if (cached is not null)
            return cached;

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is not null)
            await cache.SetAsync(key, order, cancellationToken);

        return order;
    }
}
```

## Primitive Values

The generic API supports reference types, structs, and primitive values:

```csharp
cache.Set("metrics:count", 12);
var count = cache.Get<int>("metrics:count");
```

`Get<T>` and `GetAsync<T>` return `T?`. For reference types, a cache miss returns `null`.
For value types, a miss returns `default(T)`. For example, a missing `int` returns `0`.

`Set` and `SetAsync` ignore null values. This keeps the provider contract simple: a returned `null` for reference types means there was no cached value available.

If a caller must distinguish a value-type miss from a real default value, add a higher-level key-existence check or extend the contract with a `TryGet`/result type.

## Provider Namespaces

Each provider has an internal namespace. Configure it with `Namespace`; when it is omitted, the provider `Name` is used.

The namespace is applied internally to every key:

```csharp
cache.Set("1", order);
```

With `Namespace = "orders"`, the physical key becomes `orders:1` in the underlying provider.

`Clear` and `ClearAsync` do not receive a namespace. They clear only the keys that belong to the provider namespace.

```csharp
var orders = cacheManager.GetCacheProvider("OrdersRedis");
orders.Set("1", order);
orders.Set("2", order);

orders.Clear();
```

The example removes keys under the configured `orders:` namespace only.

## Redis Lifecycle

Redis connections are managed by `IRedisConnectionFactory`, registered as a singleton by `AddCacheManager(...)`.

The factory keeps a case-insensitive registry keyed by provider name:

- `OrdersRedis`, `ordersredis`, and `ORDERSREDIS` reuse the same Redis connection.
- Different provider names can point to different Redis connection strings.
- Created connections are disposed when the DI container disposes of the singleton factory.

`RedisCacheProvider` uses the factory but does not own or dispose the connection directly.

## Fallback Behavior

If a requested provider name does not exist, or if a provider fails while being initialized, the manager returns an internal in-memory fallback provider.

Fallback details:

- The fallback is created lazily and reused for later fallback requests.
- The fallback is named `Fallback`.
- The fallback uses the `Fallback` namespace.
- The fallback uses a 1440-minute expiration.
- The fallback is not added to `ICacheProvidersCollection`.
- The fallback is not cached under the missing or failed provider name.

This means a provider can be added or fixed later with the same requested name, and the manager can create the real provider instead of reusing the fallback for that name.

## Notes

- Redis connection strings are not written to logs.
- Provider names are case-insensitive.
- Invalid keys or null values are ignored and logged as warnings.

## License

This project is released under a source-available license.

You may use it, redistribute complete and unmodified copies, and access its source code.

You may not modify it, distribute modified versions, or sell the Software as a whole as a standalone product without the prior express written permission of the rights' holder.

See the `License` file for details.
