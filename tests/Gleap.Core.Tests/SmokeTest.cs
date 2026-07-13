using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void CoreAssembly_ExposesJsonSerializerContract()
    {
        Assert.NotNull(typeof(IJsonSerializer));
    }
}
