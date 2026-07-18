using Microsoft.AspNetCore.Http.HttpResults;
using Serilog;

var logFile = Environment.GetEnvironmentVariable("LOG_FILE")
    ?? Path.Combine("..", "..", "logs", "order-svc.log");

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
        doc.Info.Title       = "Order Service";
        doc.Info.Version     = "v1";
        doc.Info.Description = "Order management API — sits behind ConduitSharp (jwt-auth).";
        return Task.CompletedTask;
    });
});

var app = builder.Build();
app.MapOpenApi();

var orders = new[]
{
    new Order("ORD-001", "C-42", "WIDGET-A", 5,  "shipped",    49.95m  ),
    new Order("ORD-002", "C-17", "GADGET-X", 1,  "processing", 49.99m  ),
    new Order("ORD-003", "C-42", "GADGET-Y", 2,  "pending",    159.98m ),
};

app.MapGet("/api/orders", (HttpContext ctx) =>
{
    Log.Information("GET /api/orders → 200 ({Count} orders)", orders.Length);
    return TypedResults.Ok(orders);
})
.WithTags("Orders")
.WithSummary("List all orders")
.WithDescription("Returns all orders in the system.");

app.MapGet("/api/orders/{id}", Results<Ok<Order>, NotFound<ErrorResponse>>
    (string id, HttpContext ctx) =>
{
    var order = orders.FirstOrDefault(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    if (order is null)
    {
        Log.Warning("GET /api/orders/{Id} → 404", id);
        return TypedResults.NotFound(new ErrorResponse($"Order {id} not found."));
    }
    Log.Information("GET /api/orders/{Id} → 200 ({Status})", id, order.Status);
    return TypedResults.Ok(order);
})
.WithTags("Orders")
.WithSummary("Get order by ID")
.WithDescription("Returns a single order by its ID (e.g. ORD-001), or 404 if not found.");

app.MapPost("/api/orders", Results<Created<Order>, BadRequest<ErrorResponse>>
    (CreateOrderRequest req, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(req.CustomerId) || string.IsNullOrWhiteSpace(req.Sku) || req.Qty <= 0)
    {
        Log.Warning("POST /api/orders → 400 (invalid request)");
        return TypedResults.BadRequest(new ErrorResponse("customerId, sku, and qty > 0 are required."));
    }
    var newOrder = new Order(
        Id:         $"ORD-{Random.Shared.Next(100, 999)}",
        CustomerId: req.CustomerId,
        Sku:        req.Sku,
        Qty:        req.Qty,
        Status:     "pending",
        Total:      req.Qty * req.UnitPrice);
    Log.Information("POST /api/orders → 201 ({Id})", newOrder.Id);
    return TypedResults.Created($"/api/orders/{newOrder.Id}", newOrder);
})
.WithTags("Orders")
.WithSummary("Create order")
.WithDescription("Creates a new order and returns it with a generated ID and 'pending' status.");

app.MapPost("/api/upload/file", () => TypedResults.Ok(new { status = "uploaded" }))
   .WithTags("Uploads")
   .WithSummary("Dummy upload endpoint")
   .ExcludeFromDescription();

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy", "order-svc")))
   .WithTags("Health")
   .WithSummary("Health check")
   .ExcludeFromDescription();

Log.Information("OrderService starting on {Urls}", string.Join(", ", app.Urls));
app.Run();

record Order(string Id, string CustomerId, string Sku, int Qty, string Status, decimal Total);
record CreateOrderRequest(string CustomerId, string Sku, int Qty, decimal UnitPrice);
record ErrorResponse(string Error);
record HealthResponse(string Status, string Service);
