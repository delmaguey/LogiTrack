using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogiTrack.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;

namespace LogiTrack.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class InventoryController : ApiControllerBase
    {
        private readonly LogiTrackDBContext _context;
        private readonly IMemoryCache _cache;
        private const string InventoryListCacheKey = "InventoryItems_All";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private static readonly CacheInvalidationToken _cacheInvalidation = new();

        private static string InventoryItemCacheKey(int id) => $"InventoryItem_{id}";

        public InventoryController(LogiTrackDBContext context, ILogger<InventoryController> logger, IMemoryCache cache)
            : base(logger)
        {
            _context = context;
            _cache = cache;
        }

        // GET: api/Inventory
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize]
        public async Task<ActionResult<IEnumerable<InventoryItem>>> GetInventoryItems(CancellationToken cancellationToken)
        {
            Logger.LogInformation("GetInventoryItems called.");

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var etag = $"\"inventory-list-v{_cacheInvalidation.Version}\"";
            if (IsETagMatch(etag))
            {
                SetCacheHeaders(etag, CacheDuration);
                return StatusCode(StatusCodes.Status304NotModified);
            }

            if (_cache.TryGetValue(InventoryListCacheKey, out List<InventoryItem>? items) && items != null)
            {
                Logger.LogInformation("GetInventoryItems served {InventoryItemCount} items from cache.", items.Count);
                SetCacheHeaders(etag, CacheDuration);
                return items;
            }

            items = await _context.InventoryItems.AsNoTracking().ToListAsync(cancellationToken);
            _cache.Set(InventoryListCacheKey, items, _cacheInvalidation.CreateEntryOptions(CacheDuration));

            stopwatch.Stop();
            Logger.LogInformation("GetInventoryItems returned {InventoryItemCount} items in {ElapsedMilliseconds} ms.", items.Count, stopwatch.ElapsedMilliseconds);
            SetCacheHeaders(etag, CacheDuration);
            return items;
        }

        // GET: api/Inventory/5
        [HttpGet("{id:int}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<InventoryItem>> GetInventoryItem(int id, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            Logger.LogInformation("GetInventoryItem called for id {InventoryItemId}.", id);

            var etag = $"\"inventory-{id}-v{_cacheInvalidation.Version}\"";
            if (IsETagMatch(etag))
            {
                SetCacheHeaders(etag, CacheDuration);
                return StatusCode(StatusCodes.Status304NotModified);
            }

            var cacheKey = InventoryItemCacheKey(id);
            if (!_cache.TryGetValue(cacheKey, out InventoryItem? item))
            {
                item = await _context.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

                if (item != null)
                    _cache.Set(cacheKey, item, _cacheInvalidation.CreateEntryOptions(CacheDuration));
            }

            if (item == null)
                return NotFoundResource("InventoryItem", id);

            stopwatch.Stop();
            Logger.LogInformation("GetInventoryItem returned item in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
            SetCacheHeaders(etag, CacheDuration);
            return item;
        }

        // POST: api/Inventory
        [HttpPost]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<InventoryItem>> PostInventoryItem(InventoryItem item, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            Logger.LogInformation("PostInventoryItem called.");
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            _context.InventoryItems.Add(item);
            await _context.SaveChangesAsync(cancellationToken);
            _cacheInvalidation.Invalidate();

            stopwatch.Stop();
            Logger.LogInformation("Created InventoryItem {InventoryItemId} in {ElapsedMilliseconds} ms.", item.Id, stopwatch.ElapsedMilliseconds);
            return CreatedAtAction(nameof(GetInventoryItem), new { id = item.Id }, item);
        }

        // PUT: api/Inventory/5
        [HttpPut("{id:int}")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PutInventoryItem(int id, InventoryItem item, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            Logger.LogInformation("PutInventoryItem called for id {InventoryItemId}.", id);
            if (id != item.Id)
                return BadRequestResource("Invalid request", "El id en la ruta no coincide con el id del item.");

            var rowsAffected = await _context.InventoryItems
                .Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Name, item.Name)
                    .SetProperty(i => i.Quantity, item.Quantity)
                    .SetProperty(i => i.Location, item.Location)
                    .SetProperty(i => i.OrderId, item.OrderId), cancellationToken);

            if (rowsAffected == 0)
            {
                Logger.LogWarning("InventoryItem {InventoryItemId} not found during update.", id);
                return NotFoundResource("InventoryItem", id);
            }

            _cacheInvalidation.Invalidate();
            stopwatch.Stop();
            Logger.LogInformation("Updated InventoryItem {InventoryItemId} in {ElapsedMilliseconds} ms.", id, stopwatch.ElapsedMilliseconds);
            return NoContent();
        }

        // DELETE: api/InventoryItems/5
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteInventoryItem(int id, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            Logger.LogInformation("DeleteInventoryItem called for id {InventoryItemId}.", id);

            var rowsAffected = await _context.InventoryItems
                .Where(i => i.Id == id)
                .ExecuteDeleteAsync(cancellationToken);

            if (rowsAffected == 0)
            {
                Logger.LogWarning("InventoryItem {InventoryItemId} not found for delete.", id);
                return NotFoundResource("InventoryItem", id);
            }

            _cacheInvalidation.Invalidate();

            stopwatch.Stop();
            Logger.LogInformation("Deleted InventoryItem {InventoryItemId} in {ElapsedMilliseconds} ms.", id, stopwatch.ElapsedMilliseconds);
            return NoContent();
        }
    }
}
