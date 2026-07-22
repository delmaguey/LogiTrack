using System.Xml.Serialization;
using LogiTrack.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var options = new DbContextOptionsBuilder<LogiTrackDBContext>()
    .UseSqlite("Data Source=logitrack.db")
    .Options;

using (var context = new LogiTrackDBContext(options))
{
    // Add test inventory item if none exist
    if (!context.InventoryItems.Any())
    {
        context.InventoryItems.Add(new InventoryItem
        {
            Name = "Pallet Jack",
            Quantity = 12,
            Location = "Warehouse A"
        });

        context.SaveChanges();
    }

    // Add a sample order if none exist
    if (!context.Orders.Any())
    {
        var order = new Order
        {
            CustomerName = "Sample Customer",
            DatePlaced = DateTime.UtcNow
        };

        var inventoryItem = context.InventoryItems.First();
        order.AddItem(inventoryItem);

        context.Orders.Add(order);
        context.SaveChanges();
    }

    // Retrieve and print inventory to confirm
    var items = context.InventoryItems.ToList();
    foreach (var item in items)
    {
        item.DisplayInfo(); // Should print: Item: Pallet Jack | Quantity: 12 | Location: Warehouse A
    }

    // Retrieve and print order summaries efficiently
    var orderSummaries = context.Orders
        .AsNoTracking()
        .Include(o => o.Items)
        .Select(o => new
        {
            o.OrderId,
            o.CustomerName,
            ItemCount = o.Items.Count,
            TotalQuantity = o.Items.Sum(i => i.Quantity)
        })
        .ToList();

    foreach (var summary in orderSummaries)
    {
        Console.WriteLine($"Order {summary.OrderId}: {summary.CustomerName} - {summary.ItemCount} item(s), total quantity {summary.TotalQuantity}");
    }
}


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add support for MVC controllers
builder.Services.AddControllers();

builder.Services.AddDbContext<LogiTrackDBContext>(options => 
    options.UseSqlite("Data Source=logitrack.db"));

var app = builder.Build();



// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Map controller routes
app.MapControllers().WithOpenApi();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
var info = new InventoryItem();
info.Name = "Pallet Jack";
info.Quantity = 12;
info.Location = "Warehouse A";

info.DisplayInfo();

    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}


