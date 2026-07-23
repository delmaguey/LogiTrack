using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LogiTrack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LogiTrack.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly LogiTrackDBContext _context;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(LogiTrackDBContext context, ILogger<OrdersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/Orders
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
        {
            _logger.LogInformation("GetOrders called.");
            var orders = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .ToListAsync();
            _logger.LogInformation("GetOrders returned {OrderCount} orders.", orders.Count);
            return orders;
        }

        // GET: api/Orders/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Order>> GetOrder(int id)
        {
            _logger.LogInformation("GetOrder called for id {OrderId}.", id);
            var order = await _context.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
                return NotFoundResource("Order", id);

            return order;
        }

        // POST: api/Orders
        [HttpPost]
        public async Task<ActionResult<Order>> PostOrder(Order order)
        {
            _logger.LogInformation("PostOrder called. Item count: {ItemCount}.", order.Items?.Count ?? 0);
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Keep incoming items to process them separately
            var incomingItems = order.Items?.ToList() ?? new List<InventoryItem>();

            // Create the order without attaching items first to get an OrderId
            var newOrder = new Order
            {
                CustomerName = order.CustomerName,
                DatePlaced = order.DatePlaced
            };

            _context.Orders.Add(newOrder);
            await _context.SaveChangesAsync();

            // Attach incoming items: if item.Id == 0 -> new item, otherwise attach existing item
            foreach (var item in incomingItems)
            {
                if (item.Id == 0)
                {
                    var newItem = new InventoryItem
                    {
                        Name = item.Name,
                        Quantity = item.Quantity,
                        Location = item.Location,
                        OrderId = newOrder.OrderId
                    };

                    _context.InventoryItems.Add(newItem);
                }
                else
                {
                    var existingItem = await _context.InventoryItems.FindAsync(item.Id);
                    if (existingItem == null)
                    {
                        return NotFoundResource("InventoryItem", item.Id);
                    }

                    existingItem.OrderId = newOrder.OrderId;
                    existingItem.Order = newOrder;
                    _context.Entry(existingItem).State = EntityState.Modified;
                }
            }

            await _context.SaveChangesAsync();

            var created = await _context.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == newOrder.OrderId);

            _logger.LogInformation("Created Order {OrderId} with {ItemCount} items.", newOrder.OrderId, incomingItems.Count);
            return CreatedAtAction(nameof(GetOrder), new { id = newOrder.OrderId }, created);
        }

        // PUT: api/Orders/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrder(int id, Order order)
        {
            _logger.LogInformation("PutOrder called for id {OrderId}.", id);
            if (id != order.OrderId)
                return BadRequest();

            var existing = await _context.Orders.FindAsync(id);
            if (existing == null)
                return NotFoundResource("Order", id);

            existing.CustomerName = order.CustomerName;
            existing.DatePlaced = order.DatePlaced;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await OrderExistsAsync(id))
                    return NotFoundResource("Order", id);
                throw;
            }

            _logger.LogInformation("Updated Order {OrderId} successfully.", id);
            return NoContent();
        }

        // DELETE: api/Orders/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            _logger.LogInformation("DeleteOrder called for id {OrderId}.", id);
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
                return NotFoundResource("Order", id);

            // Detach any related inventory items in the database first.
            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE InventoryItems SET OrderId = NULL WHERE OrderId = {0}",
                id);

            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted Order {OrderId}.", id);
            return NoContent();
        }

        // POST: api/Orders/{id}/items
        // Adds a single InventoryItem to the order. If item.Id == 0 creates a new item, otherwise attaches existing item.
        [HttpPost("{id}/items")]
        public async Task<IActionResult> AddItemToOrder(int id, InventoryItem item)
        {
            _logger.LogInformation("AddItemToOrder called for Order {OrderId}.", id);
            var order = await _context.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null)
                return NotFoundResource("Order", id);

            if (item == null)
                return BadRequest("Item is required.");

            if (item.Id == 0)
            {
                item.OrderId = id;
                _context.InventoryItems.Add(item);
            }
            else
            {
                var existing = await _context.InventoryItems.FindAsync(item.Id);
                if (existing == null)
                    return NotFoundResource("InventoryItem", item.Id);

                existing.OrderId = id;
                existing.Order = order;
                _context.Entry(existing).State = EntityState.Modified;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Added InventoryItem to Order {OrderId}.", id);
            return NoContent();
        }

        // DELETE: api/Orders/{id}/items/{itemId}
        // Removes the item from the order (sets OrderId to null).
        [HttpDelete("{id}/items/{itemId}")]
        public async Task<IActionResult> RemoveItemFromOrder(int id, int itemId)
        {
            _logger.LogInformation("RemoveItemFromOrder called for Order {OrderId}, Item {InventoryItemId}.", id, itemId);
            var order = await _context.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null)
                return NotFoundResource("Order", id);

            var item = await _context.InventoryItems.FindAsync(itemId);
            if (item == null || item.OrderId != id)
                return NotFoundResource("InventoryItem", itemId);

            item.OrderId = null;
            item.Order = null;
            _context.Entry(item).State = EntityState.Modified;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Removed InventoryItem {InventoryItemId} from Order {OrderId}.", itemId, id);
            return NoContent();
        }

        private async Task<bool> OrderExistsAsync(int id)
        {
            return await _context.Orders.AnyAsync(e => e.OrderId == id);
        }

        private ActionResult NotFoundResource(string resource, object id)
        {
            _logger?.LogWarning("{Resource} {Id} not found. Path={Path}", resource, id, HttpContext.Request.Path);
            return NotFound(new ProblemDetails
            {
                Title = $"{resource} no encontrado",
                Detail = $"{resource} con id {id} no existe.",
                Status = StatusCodes.Status404NotFound,
                Instance = HttpContext.Request.Path
            });
        }
    }
}
