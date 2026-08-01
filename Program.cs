using System.Text;
using System.Threading.RateLimiting;
using System.Xml.Serialization;
using LogiTrack.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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

builder.Services.AddMemoryCache();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

builder.Services.AddDbContextPool<LogiTrackDBContext>(options =>
    options.UseSqlite("Data Source=logitrack.db"));

builder.Services.AddIdentity<ApplicationUser,IdentityRole>()
                            .AddEntityFrameworkStores<LogiTrackDBContext>();

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "LogiTrack";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "LogiTrackUsers";

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Title = "Too many requests",
            Detail = "Rate limit exceeded. Please try again later.",
            Status = StatusCodes.Status429TooManyRequests,
            Instance = context.HttpContext.Request.Path
        };
        await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
    };

    // Applies to the anonymous auth endpoints (login/register), which are the ones most exposed
    // to brute-force/credential-stuffing attempts. Keyed per client IP so one abusive client
    // can't exhaust the limit for everyone else.
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Seed the Manager role and, if no Manager exists yet, one seed Manager account so there's a way
// to bootstrap into role-restricted endpoints without granting roles through the API itself.
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    if (!await roleManager.RoleExistsAsync("Manager"))
        await roleManager.CreateAsync(new IdentityRole("Manager"));

    if (!(await userManager.GetUsersInRoleAsync("Manager")).Any())
    {
        var seedManagerEmail = builder.Configuration["Seed:ManagerEmail"] ?? "manager@logitrack.local";
        var seedManagerPassword = builder.Configuration["Seed:ManagerPassword"] ?? throw new InvalidOperationException("Seed:ManagerPassword must be configured to seed the initial Manager account.");

        var managerUser = await userManager.FindByEmailAsync(seedManagerEmail);
        if (managerUser == null)
        {
            managerUser = new ApplicationUser { UserName = seedManagerEmail, Email = seedManagerEmail };
            var createResult = await userManager.CreateAsync(managerUser, seedManagerPassword);
            if (!createResult.Succeeded)
                throw new InvalidOperationException($"Failed to seed Manager account: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(managerUser, "Manager");
    }
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exception, "Unhandled exception processing {Path}", context.Request.Path);

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
            Instance = context.Request.Path,
            Detail = app.Environment.IsDevelopment() ? exception?.ToString() : null
        };

        await context.Response.WriteAsJsonAsync(problem);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseResponseCompression();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

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


