using Microsoft.Extensions.Logging;

namespace ConduitSharp.Plugin.BodyCapture;

/// <summary>
/// Re-homes HttpLogging's loggers under this plugin's category so request capture obeys this
/// plugin's log level rather than the framework's Warning-filtered <c>Microsoft.AspNetCore.*</c>,
/// and lands in Loki tagged as body-capture.
/// </summary>
internal sealed class BodyCaptureLoggerFactory(ILoggerFactory inner, string category) : ILoggerFactory
{

    public ILogger CreateLogger(string categoryName) =>
        inner.CreateLogger(categoryName.StartsWith("Microsoft.AspNetCore.HttpLogging", StringComparison.Ordinal)
            ? category
            : categoryName);

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
    public void Dispose() { }
}
