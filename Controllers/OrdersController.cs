using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogiTrack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System.Diagnostics;

namespace LogiTrack.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ApiControllerBase
    {
        private readonly LogiTrackDBContext _context;
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

        // Shared by every cache entry this controller writes (list pages and individual orders).
        // Invalidating it evicts everything at once, so any write invalidates all cached reads.
        private static readonly CacheInvalidationToken _cacheInvalidation = new();

        public OrdersController(LogiTrackDBContext context, ILogger<OrdersController> logger, IMemoryCache cache)
            : base(logger)
        {
            _context = context;
            _cache = cache;
        }

        private static string OrdersListCacheKey(int pageNumber, int pageSize) => $"Orders_List_{pageNumber}_{pageSize}";

        private static string OrderCacheKey(int id) => $"Order_{id}";

        private static MemoryCacheEntryOptions CacheEntryOptions() => _cacheInvalidation.CreateEntryOptions(CacheDuration);

        // GET: api/Orders?pageNumber=1&pageSize=20
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders(int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        {
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100)
                return BadRequest("pageNumber must be >= 1 and pageSize must be between 1 and 100.");

            Logger.LogInformation("GetOrders called. Page {PageNumber}, Size {PageSize}.", pageNumber, pageSize);
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var etag = $"\"orders-list-{pageNumber}-{pageSize}-v{_cacheInvalidation.Version}\"";
            if (IsETagMatch(etag))
            {
                SetCacheHeaders(etag, CacheDuration);
                return StatusCode(StatusCodes.Status304NotModified);
            }

            var cacheKey = OrdersListCacheKey(pageNumber, pageSize);
            if (!_cache.TryGetValue(cacheKey, out (List<Order> Orders, int TotalCount) cached))
            {
                var totalCount = await _context.Orders.CountAsync(cancellationToken);
                var orders = await _context.Orders
                    .AsNoTracking()
                    .OrderBy(o => o.OrderId)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Include(o => o.Items)
                    .AsSplitQuery()
                    .ToListAsync(cancellationToken);

                cached = (orders, totalCount);
                _cache.Set(cacheKey, cached, CacheEntryOptions());
            }

            stopwatch.Stop();
            Logger.LogInformation("GetOrders returned {OrderCount} of {TotalCount} orders in {ElapsedMilliseconds} ms, Elapsed: {Elapsed},", cached.Orders.Count, cached.TotalCount, stopwatch.ElapsedMilliseconds, stopwatch.Elapsed);

            Response.Headers["X-Total-Count"] = cached.TotalCount.ToString();
            Response.Headers["X-Page-Number"] = pageNumber.ToString();
            Response.Headers["X-Page-Size"] = pageSize.ToString();
            SetCacheHeaders(etag, CacheDuration);

            return cached.Orders;
        }

        // GET: api/Orders/5
        [HttpGet("{id}")]
        [Authorize]
        public async Task<ActionResult<Order>> GetOrder(int id, CancellationToken cancellationToken)
        {
            Logger.LogInformation("GetOrder called for id {OrderId}.", id);
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var etag = $"\"order-{id}-v{_cacheInvalidation.Version}\"";
            if (IsETagMatch(etag))
            {
                SetCacheHeaders(etag, CacheDuration);
                return StatusCode(StatusCodes.Status304NotModified);
            }

            var cacheKey = OrderCacheKey(id);
            if (!_cache.TryGetValue(cacheKey, out Order? order))
            {
                order = await _context.Orders
                    .Include(o => o.Items)
                    .AsSplitQuery()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrderId == id, cancellationToken);

                if (order != null)
                    _cache.Set(cacheKey, order, CacheEntryOptions());
            }

            stopwatch.Stop();
            Logger.LogInformation("GetOrder returned order {OrderId} in {ElapsedMilliseconds} ms, Elapsed: {Elapsed},", id, stopwatch.ElapsedMilliseconds, stopwatch.Elapsed);

            if (order == null)
                return NotFoundResource("Order", id);

            SetCacheHeaders(etag, CacheDuration);
            return order;
        }

        // POST: api/Orders
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<Order>> PostOrder(Order order, CancellationToken cancellationToken)
        {
            Logger.LogInformation("PostOrder called. Item count: {ItemCount}.", order.Items?.Count ?? 0);
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            // Keep incoming items to process them separately
            var incomingItems = order.Items?.ToList() ?? new List<InventoryItem>();

            // Create the order without attaching items first to get an OrderId
            var newOrder = new Order
            {
                CustomerName = order.CustomerName,
                DatePlaced = order.DatePlaced
            };

            _context.Orders.Add(newOrder);

            // Batch-load all referenced existing items in one query instead of one FindAsync per item.
            var existingIds = incomingItems.Where(i => i.Id != 0).Select(i => i.Id).Distinct().ToList();
            var existingItemsById = existingIds.Count > 0
                ? await _context.InventoryItems.Where(i => existingIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken)
                : [];

            // Attach incoming items via navigation property so EF Core fixes up the FK once newOrder
            // gets its identity, letting the whole graph be persisted in a single SaveChangesAsync call.
            foreach (var item in incomingItems)
            {
                if (item.Id == 0)
                {
                    var newItem = new InventoryItem
                    {
                        Name = item.Name,
                        Quantity = item.Quantity,
                        Location = item.Location,
                        Order = newOrder
                    };

                    _context.InventoryItems.Add(newItem);
                }
                else
                {
                    if (!existingItemsById.TryGetValue(item.Id, out var existingItem))
                    {
                        return NotFoundResource("InventoryItem", item.Id);
                    }

                    existingItem.Order = newOrder;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            var created = await _context.Orders
                .Include(o => o.Items)
                .AsSplitQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == newOrder.OrderId, cancellationToken);

            _cacheInvalidation.Invalidate();

            stopwatch.Stop();
            Logger.LogInformation("Created Order {OrderId} with {ItemCount} items in {ElapsedMilliseconds} ms, Elapsed: {Elapsed},", newOrder.OrderId, incomingItems.Count, stopwatch.ElapsedMilliseconds, stopwatch.Elapsed);
            return CreatedAtAction(nameof(GetOrder), new { id = newOrder.OrderId }, created);
        }

        // PUT: api/Orders/5
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> PutOrder(int id, Order order, CancellationToken cancellationToken)
        {
            Logger.LogInformation("PutOrder called for id {OrderId}.", id);
            if (id != order.OrderId)
                return BadRequest();

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var rowsAffected = await _context.Orders
                .Where(o => o.OrderId == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.CustomerName, order.CustomerName)
                    .SetProperty(o => o.DatePlaced, order.DatePlaced), cancellationToken);

            if (rowsAffected == 0)
                return NotFoundResource("Order", id);

            _cacheInvalidation.Invalidate();

            stopwatch.Stop();
            Logger.LogInformation("Updated Order {OrderId} successfully in {ElapsedMilliseconds} ms, Elapsed: {Elapsed},", id, stopwatch.ElapsedMilliseconds, stopwatch.Elapsed);
            return NoContent();
        }

        // DELETE: api/Orders/5
        [HttpDelete("{id}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> DeleteOrder(int id, CancellationToken cancellationToken)
        {
            Logger.LogInformation("DeleteOrder called for id {OrderId}.", id);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var orderExists = await _context.Orders.AnyAsync(o => o.OrderId == id, cancellationToken);
            if (!orderExists)
                return NotFoundResource("Order", id);

            // Detach any related inventory items in the database first, without loading them.
            await _context.InventoryItems
                .Where(i => i.OrderId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.OrderId, (int?)null), cancellationToken);

            // Delete the order directly in the database, without loading or tracking it.
            await _context.Orders
                .Where(o => o.OrderId == id)
                .ExecuteDeleteAsync(cancellationToken);

            _cacheInvalidation.Invalidate();

            stopwatch.Stop();
            Logger.LogInformation("Deleted Order {OrderId} in {ElapsedMilliseconds} ms, Elapsed: {Elapsed},", id, stopwatch.ElapsedMilliseconds, stopwatch.Elapsed);
            return NoContent();
        }

        // POST: api/Orders/{id}/items
        // Adds a single InventoryItem to the order. If item.Id == 0 creates a new item, otherwise attaches existing item.
        [HttpPost("{id}/items")]
        [Authorize]
        public async Task<IActionResult> AddItemToOrder(int id, InventoryItem item, CancellationToken cancellationToken)
        {
            Logger.LogInformation("AddItemToOrder called for Order {OrderId}.", id);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var orderExists = await _context.Orders.AnyAsync(o => o.OrderId == id, cancellationToken);
            if (!orderExists)
                return NotFoundResource("Order", id);

            if (item == null)
                return BadRequest("Item is required.");

            if (item.Id == 0)
            {
                item.OrderId = id;
                _context.InventoryItems.Add(item);
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var rowsAffected = await _context.InventoryItems
                    .Where(i => i.Id == item.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.OrderId, id), cancellationToken);

                if (rowsAffected == 0)
                    return NotFoundResource("InventoryItem", item.Id);
            }

            _cacheInvalidation.Invalidate();

            stopwatch.Stop();
            Logger.LogInformation("Added InventoryItem to Order {OrderId} in {ElapsedMilliseconds} ms, Elapsed: {Elapsed},", id, stopwatch.ElapsedMilliseconds, stopwatch.Elapsed);
            return NoContent();
        }

        // DELETE: api/Orders/{id}/items/{itemId}
        // Removes the item from the order (sets OrderId to null).
        [HttpDelete("{id}/items/{itemId}")]
        [Authorize]
        public async Task<IActionResult> RemoveItemFromOrder(int id, int itemId, CancellationToken cancellationToken)
        {
            Logger.LogInformation("RemoveItemFromOrder called for Order {OrderId}, Item {InventoryItemId}.", id, itemId);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var orderExists = await _context.Orders.AnyAsync(o => o.OrderId == id, cancellationToken);
            if (!orderExists)
                return NotFoundResource("Order", id);

            var rowsAffected = await _context.InventoryItems
                .Where(i => i.Id == itemId && i.OrderId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.OrderId, (int?)null), cancellationToken);

            if (rowsAffected == 0)
                return NotFoundResource("InventoryItem", itemId);

            _cacheInvalidation.Invalidate();

            stopwatch.Stop();
            Logger.LogInformation("Removed InventoryItem {InventoryItemId} from Order {OrderId} in {ElapsedMilliseconds} ms, Elapsed: {Elapsed},", itemId, id, stopwatch.ElapsedMilliseconds, stopwatch.Elapsed);
            return NoContent();
        }
    }
}
