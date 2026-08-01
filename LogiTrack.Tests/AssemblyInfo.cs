using Xunit;

// OrdersController and InventoryController each keep a single static CacheInvalidationToken
// shared by every instance (by design - it's what lets a write from one request invalidate
// reads from another). That means tests touching those controllers share process-wide state;
// running test classes in parallel would let one test's cache invalidation bleed into another's
// assertions. Sequential execution keeps the suite deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
