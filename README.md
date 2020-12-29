# Benchmarking JSON serialization and `HMACSHA256` hashing with `string`/`byte[]`, `MemoryStream`, and `RecyclableMemoryStream` #

I recently had a requirement to serialize an object to JSON and compute an `HMACSHA256` hash of this JSON. Knowing that the objects I'm serializing can be quite large, and that there will be a lot in flight at any given time, I wanted to test the effects of serializing and hashing on garbage collection and the Large Object Heap (LOH). My goal was to minimize allocations, specifically on the LOH.

> NOTE: This code needs to run in an Azure Function. Due to the [Azure Functions runtime's not-quite-first-class support](https://techcommunity.microsoft.com/t5/apps-on-azure/net-5-support-on-azure-functions/ba-p/1973055) of `.NET 5`, I chose to remain on `.NET Core 3.1`. This meant that I couldn't use `System.Text.Json` because some of the functionality I needed does not exist in `.NET Core 3.1`. So, all JSON serialization is done with `Newtonsoft.Json`.

To simulate the payload, I created an 'envelope' class that holds a list of widgets. A widget is a POCO with a handful of primitive properties:

```csharp
public class WidgetEnvelope
{
    public List<Widget> Widgets { get; private set; } = new List<Widget>();
}

public class Widget
{
    public Guid Id { get; set; }

    public string Property0 { get; set; } = null!;
    public string Property1 { get; set; } = null!;
    public string Property2 { get; set; } = null!;
    public string Property3 { get; set; } = null!;
    public string Property4 { get; set; } = null!;
    public string Property5 { get; set; } = null!;
    public string Property6 { get; set; } = null!;
    public string Property7 { get; set; } = null!;
    public string Property8 { get; set; } = null!;
    public string Property9 { get; set; } = null!;

    public DateTime CreatedUtc { get; set; }
}
```

I initialized a single envelope with 3,000 widgets. When serialized to JSON, the resulting string is ~1 MB.

Using `BenchmarkDotNet`, each `Benchmark` method serializes this envelope and hashes the resulting JSON to compute the signature:

```csharp
//
// Benchmark methods
//

[Benchmark(Baseline = true)]
public void SerializeToStringAndSign()
{
    var signature = SerializeAndSignHelper.SerializeToStringAndSign(WidgetEnvelope);
}

[Benchmark]
public void SerializeToMemoryStreamAndSign()
{
    var signature = SerializeAndSignHelper.SerializeToMemoryStreamAndSign(WidgetEnvelope);
}

[Benchmark]
public void SerializeToRecyclableMemoryStreamAndSign()
{
    var signature = SerializeAndSignHelper.SerializeToRecyclableMemoryStreamAndSign(WidgetEnvelope);
}
```

## First attempt: `string`/`byte[]` (baseline) ##

For the baseline case, I tested the following:

```csharp
/// <summary>
/// Serialize the object to a JSON string, then compute a hash of the JSON byte[].
/// </summary>
public static string SerializeToStringAndSign(object value)
{
    // Serialize to a JSON string.
    var widgetEnvJson = JsonConvert.SerializeObject(value, _camelCaseSettings);

    // Get the byte[] representation of the JSON string.
    var itemsJsonBytes = Encoding.UTF8.GetBytes(widgetEnvJson);

    // Hash the JSON byte[].
    using var hmac = new HMACSHA256(key: _hmacKey);
    var hashedBytes = hmac.ComputeHash(itemsJsonBytes);

    return ToFriendlyHashString(hashedBytes);
}
```

There are at least two significant allocations happening:
- The `string` returned by `JsonConvert.SerializeObject`
- The `byte[]` returned by `Encoding.UTF8.GetBytes`

Since our test JSON is > 85,000 bytes, these allocations will both end up on the LOH.

What if we could reduce allocations by using a `MemoryStream`?


## Second attempt: `MemoryStream` ##

Next, I tried reducing allocations by using a `MemoryStream` to hold the serialized JSON, as well to compute the signature:

```csharp
/// <summary>
/// Serialize to a new MemoryStream, then compute the hash of the stream contents.
/// </summary>
public static string SerializeToMemoryStreamAndSign(object value)
{
    // *** 
    // Every invocation creates a new MemoryStream. If value is large, the jsonStream
    //   will end up on the LOH.
    // ***
    using (var jsonStream = new MemoryStream())
    {
        // Serialize the object JSON into the stream. Leave the stream open so that we can compute
        //   its hash.
        using (var streamWriter = new StreamWriter(jsonStream, _utf8EncodingNoBOM, leaveOpen: true))
        {
            _camelCaseSerializer.Serialize(streamWriter, value);
        }

        // Hash the JSON stream.
        jsonStream.Position = 0L;

        using var hmac = new HMACSHA256(key: _hmacKey);
        var hashedBytes = hmac.ComputeHash(jsonStream);

        return ToFriendlyHashString(hashedBytes);
    }
}
```

This is slightly better because we eliminated one of the allocations, but for each invocation, we're creating a new `MemoryStream` that must hold our large serialized envelope, and thus will end up on the LOH.

Is there a way to keep this `MemoryStream` off of the LOH?


## Third attempt: `RecyclableMemoryStream` ##

It turns out I wasn't the first person to run into this problem, and someone has already created a solution. The fine folks from the [Bing](https://www.bing.com) engineering team have created and open sourced a library called [Microsoft.IO.RecyclableMemoryStream](https://github.com/Microsoft/Microsoft.IO.RecyclableMemoryStream). It's essentially a drop-in replacement for `MemoryStream`, with the key difference being that its underlying buffers are sourced from a pool of preallocated buffers, preventing runaway LOH allocations and resulting in fewer `Gen 2` collections. When you request a `RecyclableMemoryStream`, it grabs a buffer from the pool. When you dispose the `RecyclableMemoryStream`, it returns the buffer to the pool. 

That's a gross oversimplification, but you can get the details here:
- [GitHub repo](https://github.com/Microsoft/Microsoft.IO.RecyclableMemoryStream)
- [Announcing Microsoft.IO.RecycableMemoryStream](http://www.philosophicalgeek.com/2015/02/06/announcing-microsoft-io-recycablememorystream/)

To start with, I created a static `RecyclableMemoryStreamManager` field:

```csharp
private static readonly RecyclableMemoryStreamManager _memoryStreamManager = new RecyclableMemoryStreamManager();
```

This class is thread-safe, and can be used as a singleton. It is responsible for handing out new intances of `RecyclableMemoryStream`.

Next, instead of creating a new `MemoryStream` on each invocation, I grabbed a `RecyclableMemoryStream` from the `RecyclableMemoryStreamManager`:

```csharp
/// <summary>
/// Serialize to a RecyclableMemoryStream, then compute the hash of the stream contents.
/// </summary>
public static string SerializeToRecyclableMemoryStreamAndSign(object value)
{
    // ***
    // Every invocation gets a RecyclableMemoryStream from the recyclable memory stream manager. It uses
    //   preallocated, pooled buffers to avoid runaway allocations on the LOH.
    // ***
    using (var jsonStream = _memoryStreamManager.GetStream(tag: nameof(SerializeToRecyclableMemoryStreamAndSign)))
    {
        // Serialize the object JSON into the stream. Leave the stream open so that we can compute
        //   its hash.
        using (var streamWriter = new StreamWriter(jsonStream, _utf8EncodingNoBOM, leaveOpen: true))
        {
            _camelCaseSerializer.Serialize(streamWriter, value);
        }

        // Hash the JSON stream.
        jsonStream.Position = 0L;

        using var hmac = new HMACSHA256(key: _hmacKey);
        var hashedBytes = hmac.ComputeHash(jsonStream);

        return ToFriendlyHashString(hashedBytes);
    }
}
```

So now there is no per-invocation LOH allocation to hold the serialized JSON. Instead, the underlying buffer comes from a pool, and once we dispose of the `RecyclableMemoryStream`, the buffer is returned to the pool for reuse.


## BenchmarkDotNet Results ##

``` ini

BenchmarkDotNet=v0.12.1, OS=Windows 10.0.19042
Intel Core i7-1065G7 CPU 1.30GHz, 1 CPU, 8 logical and 4 physical cores
.NET Core SDK=3.1.404
  [Host]        : .NET Core 3.1.10 (CoreCLR 4.700.20.51601, CoreFX 4.700.20.51901), X64 RyuJIT
  .NET Core 3.1 : .NET Core 3.1.10 (CoreCLR 4.700.20.51601, CoreFX 4.700.20.51901), X64 RyuJIT

Job=.NET Core 3.1  Runtime=.NET Core 3.1  

```
|                                   Method |      Mean |     Error |    StdDev | Ratio | RatioSD |    Gen 0 |    Gen 1 |    Gen 2 |  Allocated |
|----------------------------------------- |----------:|----------:|----------:|------:|--------:|---------:|---------:|---------:|-----------:|
|                 SerializeToStringAndSign | 11.914 ms | 0.2310 ms | 0.4559 ms |  1.00 |    0.00 | 984.3750 | 984.3750 | 984.3750 | 5832.55 KB |
|           SerializeToMemoryStreamAndSign | 10.681 ms | 0.2112 ms | 0.1872 ms |  0.87 |    0.04 | 984.3750 | 984.3750 | 984.3750 | 4547.45 KB |
| SerializeToRecyclableMemoryStreamAndSign |  7.998 ms | 0.1478 ms | 0.2023 ms |  0.67 |    0.03 | 109.3750 |        - |        - |     453 KB |


You can see that while replacing `string`/`byte[]` with `MemoryStream` reduced `Allocations` (per operation) from `5832.55 KB` to `4547.45 KB`, there is still significant `Gen 1` and `Gen 2` activity (collects per 1000 operations).

When we replaced `MemoryStream` with `RecyclableMemoryStream`, the results were staggering. Not only did `Allocations` drop from `4547.45 KB` to `453 KB`, we eliminated all `Gen 1` and `Gen 2` collects, and we drastically reduced `Gen 0` collects. Astounding!


## Conclusion ##

If you're using `MemoryStream` to hold 85,000 bytes or more, you should seriously consider using `RecyclableMemoryStream` as a replacement to ease memory pressure in your application.

This was a high-level overview of `RecyclableMemoryStream`. Its behavior is configurable, so I urge you to dig into the documentation to gain a fuller understanding of its capabilities.
