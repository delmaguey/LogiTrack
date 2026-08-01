using Microsoft.Extensions.Caching.Memory;

namespace LogiTrack.Tests
{
    public class CacheInvalidationTokenTests
    {
        [Fact]
        public void Invalidate_IncrementsVersion()
        {
            var token = new CacheInvalidationToken();
            var initialVersion = token.Version;

            token.Invalidate();

            Assert.Equal(initialVersion + 1, token.Version);
        }

        [Fact]
        public void Invalidate_EvictsEntriesCreatedWithTheToken()
        {
            var token = new CacheInvalidationToken();
            using var cache = new MemoryCache(new MemoryCacheOptions());

            cache.Set("key", "value", token.CreateEntryOptions(TimeSpan.FromMinutes(5)));
            Assert.True(cache.TryGetValue("key", out _));

            token.Invalidate();

            Assert.False(cache.TryGetValue("key", out _));
        }

        [Fact]
        public void Invalidate_DoesNotAffectEntriesFromADifferentToken()
        {
            var tokenA = new CacheInvalidationToken();
            var tokenB = new CacheInvalidationToken();
            using var cache = new MemoryCache(new MemoryCacheOptions());

            cache.Set("a", "value-a", tokenA.CreateEntryOptions(TimeSpan.FromMinutes(5)));
            cache.Set("b", "value-b", tokenB.CreateEntryOptions(TimeSpan.FromMinutes(5)));

            tokenA.Invalidate();

            Assert.False(cache.TryGetValue("a", out _));
            Assert.True(cache.TryGetValue("b", out _));
        }
    }
}
