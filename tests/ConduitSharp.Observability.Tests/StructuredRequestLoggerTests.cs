using Microsoft.Extensions.Logging;
using Xunit;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Observability.Logging;

namespace ConduitSharp.Observability.Tests;

public sealed class StructuredRequestLoggerTests
{
    private sealed class CapturingLogger : ILogger<StructuredRequestLogger>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static RequestObservation Obs(int status, string requestId = "req-x") =>
        new(requestId, "GET", "/api/test", "route-1", status, 42);

    // -------------------------------------------------------------------------
    // 2xx — logged at Information
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    public void OnRequestCompleted_2xxStatus_LogsAtInformation(int status)
    {
        var logger     = new CapturingLogger();
        var sut = new StructuredRequestLogger(logger);

        sut.OnRequestCompleted(Obs(status));

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
    }

    // -------------------------------------------------------------------------
    // 3xx / 4xx — logged at Information (not errors)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(301)]
    [InlineData(404)]
    [InlineData(429)]
    public void OnRequestCompleted_3xxAnd4xxStatus_LogsAtInformation(int status)
    {
        var logger     = new CapturingLogger();
        var sut = new StructuredRequestLogger(logger);

        sut.OnRequestCompleted(Obs(status));

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
    }

    // -------------------------------------------------------------------------
    // 5xx — logged at Error (matches OTel span status, which is also Error-only-on-5xx)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void OnRequestCompleted_5xxStatus_LogsAtError(int status)
    {
        var logger     = new CapturingLogger();
        var sut = new StructuredRequestLogger(logger);

        sut.OnRequestCompleted(Obs(status));

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
    }

    // -------------------------------------------------------------------------
    // Log message contains key fields
    // -------------------------------------------------------------------------

    [Fact]
    public void OnRequestCompleted_LogMessage_ContainsRequestId()
    {
        var logger     = new CapturingLogger();
        var sut = new StructuredRequestLogger(logger);

        sut.OnRequestCompleted(Obs(200, requestId: "abc-123"));

        Assert.Contains("abc-123", logger.Entries[0].Message);
    }
}
