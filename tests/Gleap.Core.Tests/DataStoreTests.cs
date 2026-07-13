using GleapSDK.Data;

namespace Gleap.Core.Tests;

public class DataStoreTests
{
    [Fact]
    public void CustomData_SetRemoveClear()
    {
        var store = new CustomDataStore();
        store.Set("a", "1");
        store.Merge(new System.Collections.Generic.Dictionary<string, object> { ["b"] = 2 });
        Assert.Equal(2, store.Snapshot().Count);
        store.Remove("a");
        Assert.False(store.Snapshot().ContainsKey("a"));
        store.Clear();
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void TicketAttributes_SetUnsetClear()
    {
        var store = new TicketAttributeStore();
        store.Set("priority", "high");
        Assert.Equal("high", store.Snapshot()["priority"]);
        store.Unset("priority");
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void Tags_Replace()
    {
        var store = new TagStore();
        store.Set(new[] { "vip", "beta" });
        Assert.Equal(new[] { "vip", "beta" }, store.Snapshot().ToArray());
    }

    [Fact]
    public void Attachments_AddClear()
    {
        var store = new AttachmentStore();
        store.Add("Zm9v", "a.txt");
        Assert.Equal("a.txt", store.Snapshot().Single().FileName);
        store.Clear();
        Assert.Empty(store.Snapshot());
    }
}
