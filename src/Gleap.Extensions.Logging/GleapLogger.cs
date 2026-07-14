using System;
using Microsoft.Extensions.Logging;
using GleapLogLevel = GleapSDK.LogLevel;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GleapSDK.Extensions.Logging;

/// <summary>
/// An <see cref="ILogger"/> that forwards each entry to <see cref="Gleap.Log(string, GleapSDK.LogLevel)"/>,
/// so it lands in Gleap's console log and rides along on submitted tickets.
/// </summary>
internal sealed class GleapLogger : ILogger
{
    private readonly string _category;

    public GleapLogger(string category) => _category = category;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(MsLogLevel logLevel) => logLevel != MsLogLevel.None;

    public void Log<TState>(
        MsLogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel) || formatter is null)
        {
            return;
        }

        var message = formatter(state, exception);
        if (exception != null)
        {
            message = message + " " + exception;
        }
        if (!string.IsNullOrEmpty(_category))
        {
            message = "[" + _category + "] " + message;
        }

        Gleap.Log(message, Map(logLevel));
    }

    /// <summary>Collapses the six framework levels onto Gleap's three.</summary>
    internal static GleapLogLevel Map(MsLogLevel level) => level switch
    {
        MsLogLevel.Critical => GleapLogLevel.Error,
        MsLogLevel.Error => GleapLogLevel.Error,
        MsLogLevel.Warning => GleapLogLevel.Warning,
        _ => GleapLogLevel.Info
    };

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
