using GleapSDK.Metadata;
using Xunit;

namespace Gleap.Core.Tests;

public class DefaultMetadataProviderTests
{
    [Fact]
    public void Collect_IncludesSdkTypeVersionAndLocale()
    {
        var provider = new DefaultMetadataProvider(sdkType: "NET/Test", sdkVersion: "0.1.0");
        var meta = provider.Collect();

        Assert.Equal("NET/Test", meta["sdkType"]);
        Assert.Equal("0.1.0", meta["sdkVersion"]);
        Assert.True(meta.ContainsKey("preferredUserLocale"));
    }
}
