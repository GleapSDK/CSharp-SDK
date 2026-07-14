using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GleapSDK.Extensions.Logging;

/// <summary>Registration helpers for the Gleap logger provider.</summary>
public static class GleapLoggingBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="GleapLoggerProvider"/> to the logging pipeline so application logs ride
    /// along on Gleap tickets. Idiomatic one-liner: <c>builder.Logging.AddGleap();</c>.
    /// </summary>
    public static ILoggingBuilder AddGleap(this ILoggingBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, GleapLoggerProvider>());
        return builder;
    }
}
