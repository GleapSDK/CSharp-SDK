using GleapSDK.Extensions.Logging;
using GleapLogLevel = GleapSDK.LogLevel;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Gleap.Core.Tests;

public class GleapLoggerTests
{
    [Theory]
    [InlineData(MsLogLevel.Trace, GleapLogLevel.Info)]
    [InlineData(MsLogLevel.Debug, GleapLogLevel.Info)]
    [InlineData(MsLogLevel.Information, GleapLogLevel.Info)]
    [InlineData(MsLogLevel.Warning, GleapLogLevel.Warning)]
    [InlineData(MsLogLevel.Error, GleapLogLevel.Error)]
    [InlineData(MsLogLevel.Critical, GleapLogLevel.Error)]
    public void Map_CollapsesFrameworkLevels_OntoGleapLevels(MsLogLevel input, GleapLogLevel expected)
        => Assert.Equal(expected, GleapLogger.Map(input));
}
