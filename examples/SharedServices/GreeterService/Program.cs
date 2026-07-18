using GreeterService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;

var logFile = Environment.GetEnvironmentVariable("LOG_FILE")
    ?? Path.Combine("..", "..", "logs", "greeter-svc.log");

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(logFile, rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// gRPC over cleartext requires HTTP/2-only listeners: without TLS there is no ALPN
// negotiation, so clients connect with HTTP/2 prior knowledge and the endpoint must
// speak HTTP/2 unconditionally.
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http2));

builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<GreeterImpl>();

Log.Information("GreeterService (gRPC, h2c) starting on {Urls}", string.Join(", ", app.Urls));
app.Run();
