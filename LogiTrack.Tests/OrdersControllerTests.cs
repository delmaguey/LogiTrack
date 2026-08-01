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
    public class OrdersControllerTests : IDisposable
    {
        private readonly SqliteInMemoryContextFactory _dbFactory = new();
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        private OrdersController CreateController(LogiTrackDBContext context) => new(context, NullLogger<OrdersController>.Instance, _cache)
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
        public async Task GetOrders_ReturnsPagedOrders_WithHeaders()
        {
            using var context = _dbFactory.CreateContext();
            context.Orders.AddRange(
                new Order { CustomerName = "Alice", DatePlaced = DateTime.UtcNow },
                new Order { CustomerName = "Bob", DatePlaced = DateTime.UtcNow });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var result = await controller.GetOrders(pageNumber: 1, pageSize: 1);

            var okResult = Assert.IsType<List<Order>>(result.Value);
            Assert.Single(okResult);
            Assert.Equal("2", controller.Response.Headers["X-Total-Count"]);
            Assert.Equal("1", controller.Response.Headers["X-Page-Number"]);
            Assert.Equal("1", controller.Response.Headers["X-Page-Size"]);
        }

        [Fact]
        public async Task GetOrders_InvalidPagination_ReturnsBadRequest()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.GetOrders(pageNumber: 0, pageSize: 20);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetOrder_ExistingId_ReturnsOrder()
        {
            using var context = _dbFactory.CreateContext();
            var order = new Order { CustomerName = "Alice", DatePlaced = DateTime.UtcNow };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var result = await controller.GetOrder(order.OrderId, CancellationToken.None);

            var returned = Assert.IsType<Order>(result.Value);
            Assert.Equal("Alice", returned.CustomerName);
        }

        [Fact]
        public async Task GetOrder_NonexistentId_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.GetOrder(999, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task PostOrder_WithNewItem_CreatesOrderAndItem()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var request = new Order
            {
                CustomerName = "Charlie",
                DatePlaced = DateTime.UtcNow,
                Items = new List<InventoryItem>
                {
                    new() { Id = 0, Name = "Widget", Quantity = 3, Location = "Bay 1" }
                }
            };

            var result = await controller.PostOrder(request, CancellationToken.None);

            var created = Assert.IsType<CreatedAtActionResult>(result.Result);
            var order = Assert.IsType<Order>(created.Value);
            Assert.Equal("Charlie", order.CustomerName);
            Assert.Single(order.Items);
            Assert.Equal("Widget", order.Items.First().Name);
        }

        [Fact]
        public async Task PostOrder_WithNonexistentExistingItemId_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var request = new Order
            {
                CustomerName = "Charlie",
                DatePlaced = DateTime.UtcNow,
                Items = new List<InventoryItem>
                {
                    new() { Id = 999, Name = "Ghost", Quantity = 1, Location = "Nowhere" }
                }
            };

            var result = await controller.PostOrder(request, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task PutOrder_UpdatesFields_ReturnsNoContent()
        {
            using var context = _dbFactory.CreateContext();
            var order = new Order { CustomerName = "Original", DatePlaced = DateTime.UtcNow };
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context);
            var update = new Order { OrderId = order.OrderId, CustomerName = "Updated", DatePlaced = order.DatePlaced };

            var result = await controller.PutOrder(order.OrderId, update, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            var stored = await context.Orders.AsNoTracking().FirstAsync(o => o.OrderId == order.OrderId);
            Assert.Equal("Updated", stored.CustomerName);
        }

        [Fact]
        public async Task PutOrder_NonexistentId_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);
            var update = new Order { OrderId = 999, CustomerName = "Nobody", DatePlaced = DateTime.UtcNow };

            var result = await controller.PutOrder(999, update, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task DeleteOrder_RemovesOrder_AndDetachesItems()
        {
            using var context = _dbFactory.CreateContext();
            var order = new Order { CustomerName = "ToDelete", DatePlaced = DateTime.UtcNow };
            var item = new InventoryItem { Name = "Widget", Quantity = 1, Location = "Bay 1", Order = order };
            context.Orders.Add(order);
            context.InventoryItems.Add(item);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context);
            var result = await controller.DeleteOrder(order.OrderId, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            Assert.False(await context.Orders.AnyAsync(o => o.OrderId == order.OrderId));
            var reloadedItem = await context.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            Assert.Null(reloadedItem.OrderId);
        }

        [Fact]
        public async Task DeleteOrder_NonexistentId_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.DeleteOrder(999, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task AddItemToOrder_NewItem_ReturnsNoContentAndAttachesItem()
        {
            using var context = _dbFactory.CreateContext();
            var order = new Order { CustomerName = "Alice", DatePlaced = DateTime.UtcNow };
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context);
            var newItem = new InventoryItem { Id = 0, Name = "Gadget", Quantity = 2, Location = "Bay 2" };

            var result = await controller.AddItemToOrder(order.OrderId, newItem, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            var stored = await context.InventoryItems.AsNoTracking().FirstAsync(i => i.Name == "Gadget");
            Assert.Equal(order.OrderId, stored.OrderId);
        }

        [Fact]
        public async Task AddItemToOrder_NonexistentOrder_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);
            var newItem = new InventoryItem { Id = 0, Name = "Gadget", Quantity = 2, Location = "Bay 2" };

            var result = await controller.AddItemToOrder(999, newItem, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task RemoveItemFromOrder_Success_SetsOrderIdNull()
        {
            using var context = _dbFactory.CreateContext();
            var order = new Order { CustomerName = "Alice", DatePlaced = DateTime.UtcNow };
            var item = new InventoryItem { Name = "Widget", Quantity = 1, Location = "Bay 1", Order = order };
            context.Orders.Add(order);
            context.InventoryItems.Add(item);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context);
            var result = await controller.RemoveItemFromOrder(order.OrderId, item.Id, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            var stored = await context.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == item.Id);
            Assert.Null(stored.OrderId);
        }

        [Fact]
        public async Task RemoveItemFromOrder_ItemNotBelongingToOrder_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            var orderA = new Order { CustomerName = "Alice", DatePlaced = DateTime.UtcNow };
            var orderB = new Order { CustomerName = "Bob", DatePlaced = DateTime.UtcNow };
            var item = new InventoryItem { Name = "Widget", Quantity = 1, Location = "Bay 1", Order = orderA };
            context.Orders.AddRange(orderA, orderB);
            context.InventoryItems.Add(item);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context);
            var result = await controller.RemoveItemFromOrder(orderB.OrderId, item.Id, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetOrders_AfterCreatingOrder_ReflectsNewOrderOnNextCall()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var firstPage = await controller.GetOrders(pageNumber: 1, pageSize: 20);
            var initialCount = Assert.IsType<List<Order>>(firstPage.Value).Count;

            await controller.PostOrder(new Order { CustomerName = "New", DatePlaced = DateTime.UtcNow }, CancellationToken.None);

            var secondPage = await controller.GetOrders(pageNumber: 1, pageSize: 20);
            var updatedOrders = Assert.IsType<List<Order>>(secondPage.Value);

            Assert.Equal(initialCount + 1, updatedOrders.Count);
        }
    }
}
