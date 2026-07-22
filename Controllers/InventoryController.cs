using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LogiTrack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LogiTrack.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class InventoryController : ApiControllerBase
    {
        private readonly LogiTrackDBContext _context;

        public InventoryController(LogiTrackDBContext context, ILogger<InventoryController> logger)
            : base(logger)
        {
            _context = context;
        }

        // GET: api/Inventory
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<InventoryItem>>> GetInventoryItems()
        {
            Logger.LogInformation("GetInventoryItems called.");
            var items = await _context.InventoryItems.AsNoTracking().ToListAsync();
            Logger.LogInformation("GetInventoryItems returned {InventoryItemCount} items.", items.Count);
            return items;
        }

        // GET: api/Inventory/5
        [HttpGet("{id:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<InventoryItem>> GetInventoryItem(int id)
        {
            Logger.LogInformation("GetInventoryItem called for id {InventoryItemId}.", id);
            var item = await _context.InventoryItems.FindAsync(id);

            if (item == null)
                return NotFoundResource("InventoryItem", id);

            return item;
        }

        // POST: api/Inventory
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<InventoryItem>> PostInventoryItem(InventoryItem item)
        {
            Logger.LogInformation("PostInventoryItem called.");
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            _context.InventoryItems.Add(item);
            await _context.SaveChangesAsync();

            Logger.LogInformation("Created InventoryItem {InventoryItemId}.", item.Id);
            return CreatedAtAction(nameof(GetInventoryItem), new { id = item.Id }, item);
        }

        // PUT: api/Inventory/5
        [HttpPut("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PutInventoryItem(int id, InventoryItem item)
        {
            Logger.LogInformation("PutInventoryItem called for id {InventoryItemId}.", id);
            if (id != item.Id)
                return BadRequestResource("Invalid request", "El id en la ruta no coincide con el id del item.");

            _context.Entry(item).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await InventoryItemExistsAsync(id))
                {
                    Logger.LogWarning("InventoryItem {InventoryItemId} not found during update.", id);
                    return NotFoundResource("InventoryItem", id);
                }
                throw;
            }

            Logger.LogInformation("Updated InventoryItem {InventoryItemId}.", id);
            return NoContent();
        }

        // DELETE: api/InventoryItems/5
        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteInventoryItem(int id)
        {
            Logger.LogInformation("DeleteInventoryItem called for id {InventoryItemId}.", id);
            var item = await _context.InventoryItems.FindAsync(id);
            if (item == null)
            {
                Logger.LogWarning("InventoryItem {InventoryItemId} not found for delete.", id);
                return NotFoundResource("InventoryItem", id);
            }

            _context.InventoryItems.Remove(item);
            await _context.SaveChangesAsync();

            Logger.LogInformation("Deleted InventoryItem {InventoryItemId}.", id);
            return NoContent();
        }

        private async Task<bool> InventoryItemExistsAsync(int id)
        {
            return await _context.InventoryItems.AnyAsync(e => e.Id == id);
        }
    }
}
