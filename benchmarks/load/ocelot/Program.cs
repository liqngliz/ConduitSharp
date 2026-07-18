// Minimal Ocelot host for head-to-head benchmarking. Config identical in spirit to
// scenario-a.json: one catch-all route, round-robin across the two nginx nodes.
//
// OCELOT_RETRY=1 swaps in ocelot-retry.json and registers RetryQoSProvider, giving Ocelot the
// retry it ships no way to configure. Off by default so the stock scenarios stay stock.
using Ocelot.Bench;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Polly;

var withRetry = Environment.GetEnvironmentVariable("OCELOT_RETRY") is "1" or "broken";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(withRetry ? "ocelot-retry.json" : "ocelot.json");
var ocelot = builder.Services.AddOcelot(builder.Configuration);
if (withRetry)
{
    // The handler buffers the body before the pipeline runs — without it the retry cannot replay
    // anything (see BufferingPollyHandler). OCELOT_RETRY=broken skips the buffering to demonstrate
    // that failure rather than assert it.
    var buffer = Environment.GetEnvironmentVariable("OCELOT_RETRY") != "broken";
    ocelot.AddPolly<RetryQoSProvider>((route, _, _) => buffer
        ? new BufferingPollyHandler(route, new RetryQoSProvider())
        : new NonBufferingPollyHandler(route, new RetryQoSProvider()));
}
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var app = builder.Build();
await app.UseOcelot();
app.Run();
