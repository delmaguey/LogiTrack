using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

// Ties a group of IMemoryCache entries to one expiration token so they can all be evicted
// together on a single call to Invalidate(), instead of tracking and removing each key by hand.
public sealed class CacheInvalidationToken
{
    private CancellationTokenSource _tokenSource = new();
    private int _version;

    // Increments on every Invalidate(), so it doubles as a cheap ETag suffix: a client's cached
    // response is only stale once the version it saw no longer matches the current one.
    public int Version => _version;

    public MemoryCacheEntryOptions CreateEntryOptions(TimeSpan absoluteExpiration) => new MemoryCacheEntryOptions()
        .SetAbsoluteExpiration(absoluteExpiration)
        .AddExpirationToken(new CancellationChangeToken(_tokenSource.Token));

    public void Invalidate()
    {
        var oldTokenSource = Interlocked.Exchange(ref _tokenSource, new CancellationTokenSource());
        oldTokenSource.Cancel();
        oldTokenSource.Dispose();
        Interlocked.Increment(ref _version);
    }
}
