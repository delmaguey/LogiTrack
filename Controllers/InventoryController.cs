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
            if (_cache.TryGetValue(InventoryListCacheKey, out List<InventoryItem>? items) && items != null)
            {
                Logger.LogInformation("GetInventoryItems served {InventoryItemCount} items from cache.", items.Count);
                return items;
            }

            items = await _context.InventoryItems.AsNoTracking().ToListAsync(cancellationToken);
            _cache.Set(InventoryListCacheKey, items, CacheDuration);

            stopwatch.Stop();
            Logger.LogInformation("GetInventoryItems returned {InventoryItemCount} items in {ElapsedMilliseconds} ms.", items.Count, stopwatch.ElapsedMilliseconds);
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
            var item = await _context.InventoryItems.FindAsync([id], cancellationToken);

            if (item == null)
                return NotFoundResource("InventoryItem", id);

            stopwatch.Stop();
            Logger.LogInformation("GetInventoryItem returned item in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
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
            _cache.Remove(InventoryListCacheKey);

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

            _context.Entry(item).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await InventoryItemExistsAsync(id, cancellationToken))
                {
                    Logger.LogWarning("InventoryItem {InventoryItemId} not found during update.", id);
                    return NotFoundResource("InventoryItem", id);
                }
                throw;
            }

            _cache.Remove(InventoryListCacheKey);
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
            var item = await _context.InventoryItems.FindAsync([id], cancellationToken);
            if (item == null)
            {
                Logger.LogWarning("InventoryItem {InventoryItemId} not found for delete.", id);
                return NotFoundResource("InventoryItem", id);
            }

            _context.InventoryItems.Remove(item);
            await _context.SaveChangesAsync(cancellationToken);
            _cache.Remove(InventoryListCacheKey);

            stopwatch.Stop();
            Logger.LogInformation("Deleted InventoryItem {InventoryItemId} in {ElapsedMilliseconds} ms.", id, stopwatch.ElapsedMilliseconds);
            return NoContent();
        }

        private async Task<bool> InventoryItemExistsAsync(int id, CancellationToken cancellationToken)
        {
            return await _context.InventoryItems.AnyAsync(e => e.Id == id, cancellationToken);
        }
    }
}
