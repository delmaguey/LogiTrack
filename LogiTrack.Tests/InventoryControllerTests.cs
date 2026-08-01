using LogiTrack.Controllers;
using LogiTrack.Models;
using LogiTrack.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogiTrack.Tests
{
    public class InventoryControllerTests : IDisposable
    {
        private readonly SqliteInMemoryContextFactory _dbFactory = new();
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        private InventoryController CreateController(LogiTrackDBContext context) => new(context, NullLogger<InventoryController>.Instance, _cache)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        public void Dispose()
        {
            _dbFactory.Dispose();
            _cache.Dispose();
        }

        [Fact]
        public async Task GetInventoryItems_ReturnsAllItems()
        {
            using var context = _dbFactory.CreateContext();
            context.InventoryItems.AddRange(
                new InventoryItem { Name = "Widget", Quantity = 3, Location = "Bay 1" },
                new InventoryItem { Name = "Gadget", Quantity = 5, Location = "Bay 2" });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var result = await controller.GetInventoryItems(CancellationToken.None);

            var items = Assert.IsType<List<InventoryItem>>(result.Value);
            Assert.Equal(2, items.Count);
        }

        [Fact]
        public async Task GetInventoryItem_ExistingId_ReturnsItem()
        {
            using var context = _dbFactory.CreateContext();
            var item = new InventoryItem { Name = "Widget", Quantity = 3, Location = "Bay 1" };
            context.InventoryItems.Add(item);
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var result = await controller.GetInventoryItem(item.Id, CancellationToken.None);

            var returned = Assert.IsType<InventoryItem>(result.Value);
            Assert.Equal("Widget", returned.Name);
        }

        [Fact]
        public async Task GetInventoryItem_NonexistentId_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.GetInventoryItem(999, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task PostInventoryItem_Valid_ReturnsCreated()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);
            var item = new InventoryItem { Name = "Widget", Quantity = 3, Location = "Bay 1" };

            var result = await controller.PostInventoryItem(item, CancellationToken.None);

            var created = Assert.IsType<CreatedAtActionResult>(result.Result);
            var returned = Assert.IsType<InventoryItem>(created.Value);
            Assert.Equal("Widget", returned.Name);
            Assert.True(returned.Id > 0);
        }

        [Fact]
        public async Task PutInventoryItem_MismatchedId_ReturnsBadRequest()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);
            var item = new InventoryItem { Id = 5, Name = "Widget", Quantity = 3, Location = "Bay 1" };

            var result = await controller.PutInventoryItem(1, item, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task PutInventoryItem_UpdatesAllFieldsIncludingOrderId()
        {
            using var context = _dbFactory.CreateContext();
            var order = new Order { CustomerName = "Alice", DatePlaced = DateTime.UtcNow };
            var item = new InventoryItem { Name = "Original", Quantity = 1, Location = "Bay 1" };
            context.Orders.Add(order);
            context.InventoryItems.Add(item);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context);
            var update = new InventoryItem
            {
                Id = item.Id,
                Name = "Updated",
                Quantity = 9,
                Location = "Bay 9",
                OrderId = order.OrderId
            };

            var result = await controller.PutInventoryItem(item.Id, update, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            var stored = await context.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            Assert.Equal("Updated", stored.Name);
            Assert.Equal(9, stored.Quantity);
            Assert.Equal("Bay 9", stored.Location);
            Assert.Equal(order.OrderId, stored.OrderId);
        }

        [Fact]
        public async Task PutInventoryItem_NonexistentId_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);
            var update = new InventoryItem { Id = 999, Name = "Ghost", Quantity = 1, Location = "Nowhere" };

            var result = await controller.PutInventoryItem(999, update, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task DeleteInventoryItem_RemovesItem_ReturnsNoContent()
        {
            using var context = _dbFactory.CreateContext();
            var item = new InventoryItem { Name = "Widget", Quantity = 3, Location = "Bay 1" };
            context.InventoryItems.Add(item);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context);
            var result = await controller.DeleteInventoryItem(item.Id, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            Assert.False(await context.InventoryItems.AnyAsync(i => i.Id == item.Id));
        }

        [Fact]
        public async Task DeleteInventoryItem_NonexistentId_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.DeleteInventoryItem(999, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetInventoryItems_AfterCreatingItem_ReflectsNewItemOnNextCall()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var firstCall = await controller.GetInventoryItems(CancellationToken.None);
            var initialCount = Assert.IsType<List<InventoryItem>>(firstCall.Value).Count;

            await controller.PostInventoryItem(new InventoryItem { Name = "New", Quantity = 1, Location = "Bay 1" }, CancellationToken.None);

            var secondCall = await controller.GetInventoryItems(CancellationToken.None);
            var updatedItems = Assert.IsType<List<InventoryItem>>(secondCall.Value);

            Assert.Equal(initialCount + 1, updatedItems.Count);
        }
    }
}
