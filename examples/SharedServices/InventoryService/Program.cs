using Microsoft.AspNetCore.Http.HttpResults;
using Serilog;

var logFile = Environment.GetEnvironmentVariable("LOG_FILE")
    ?? Path.Combine("..", "..", "logs", "inventory-svc.log");

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(logFile, rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, ctx, _) =>
    {
        doc.Info.Title       = "Inventory Service";
        doc.Info.Version     = "v1";
        doc.Info.Description = "Stock look-up API — sits behind ConduitSharp (api-key-auth + rate-limit).";
        return Task.CompletedTask;
    });
});

var app = builder.Build();
app.MapOpenApi();

var items = new List<InventoryItem>
{
    new InventoryItem(1, "WIDGET-A", "Widget A",     142, 9.99m  ),
    new InventoryItem(2, "WIDGET-B", "Widget B",     57,  14.99m ),
    new InventoryItem(3, "GADGET-X", "Gadget X",     8,   49.99m ),
    new InventoryItem(4, "GADGET-Y", "Gadget Y Pro", 0,   79.99m ),
};

app.MapGet("/api/inventory", (HttpContext ctx) =>
{
    Log.Information("GET /api/inventory → 200 ({Count} items)", items.Count);
    return TypedResults.Ok(items);
})
.WithTags("Inventory")
.WithSummary("List all inventory items")
.WithDescription("Returns the full catalogue with current stock levels and prices.");

app.MapPost("/api/inventory", (NewItemRequest request, HttpContext ctx) =>
{
    var id   = items.Max(i => i.Id) + 1;
    var item = new InventoryItem(id, $"ITEM-{id}", request.Name, request.Quantity, 0m);
    items.Add(item);
    Log.Information("POST /api/inventory → 200 (created item {Id})", id);
    return TypedResults.Ok(item);
})
.WithTags("Inventory")
.WithSummary("Add an inventory item")
.WithDescription("Creates a new inventory item and returns it.");

app.MapGet("/api/inventory/{id:int}", Results<Ok<InventoryItem>, NotFound<ErrorResponse>>
    (int id, HttpContext ctx) =>
{
    var item = items.FirstOrDefault(i => i.Id == id);
    if (item is null)
    {
        Log.Warning("GET /api/inventory/{Id} → 404", id);
        return TypedResults.NotFound(new ErrorResponse($"Item {id} not found."));
    }
    Log.Information("GET /api/inventory/{Id} → 200", id);
    return TypedResults.Ok(item);
})
.WithTags("Inventory")
.WithSummary("Get item by ID")
.WithDescription("Returns a single inventory item, or 404 if the ID does not exist.");

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy", "inventory-svc")))
   .WithTags("Health")
   .WithSummary("Health check")
   .ExcludeFromDescription();

Log.Information("InventoryService starting on {Urls}", string.Join(", ", app.Urls));
app.Run();

record InventoryItem(int Id, string Sku, string Name, int Stock, decimal Price);
record NewItemRequest(string Name, int Quantity);
record ErrorResponse(string Error);
record HealthResponse(string Status, string Service);
