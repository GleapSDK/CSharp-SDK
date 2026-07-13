using Gleap.Core.Tests.Fakes;
using GleapSDK.Http;
using GleapSDK.Serialization;
using GleapSDK.Session;

namespace Gleap.Core.Tests;

public class ConfigManagerTests
{
    [Fact]
    public async Task Load_SplitsFlowConfigAndProjectActions()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200,
            "{\"flowConfig\":{\"color\":\"#fff\"},\"projectActions\":{\"x\":1}}"));
        var api = new ApiClient(http, new SystemTextJsonSerializer(), GleapEndpoints.Default, "key");
        var cfg = new ConfigManager(api);

        await cfg.LoadAsync("en", CancellationToken.None);

        Assert.True(cfg.IsLoaded);
        Assert.Contains("color", cfg.FlowConfigJson);
        Assert.Contains("\"x\":1", cfg.ProjectActionsJson);
    }

    [Fact]
    public async Task Load_HandlesMissingProjectActions()
    {
        var http = new FakeHttpTransport();
        http.Responses.Enqueue(new HttpResult(200, "{\"flowConfig\":{\"color\":\"#fff\"}}"));
        var api = new ApiClient(http, new SystemTextJsonSerializer(), GleapEndpoints.Default, "key");
        var cfg = new ConfigManager(api);

        await cfg.LoadAsync("en", CancellationToken.None);

        Assert.Equal("{}", cfg.ProjectActionsJson);
    }
}
