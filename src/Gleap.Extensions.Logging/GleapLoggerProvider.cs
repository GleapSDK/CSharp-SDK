using Microsoft.Extensions.Logging;

namespace GleapSDK.Extensions.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> that routes application logs into Gleap's console log so they
/// appear on submitted tickets. This is the managed, best-practice equivalent of the native SDKs'
/// automatic console capture — .NET has no global console interception, so logs flow through the
/// standard logging pipeline. Register it with <c>builder.Logging.AddGleap()</c> (MAUI / generic-host
/// apps) or <c>loggerFactory.AddProvider(new GleapLoggerProvider())</c>.
/// </summary>
public sealed class GleapLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new GleapLogger(categoryName);

    public void Dispose() => System.GC.SuppressFinalize(this);
}
