using GleapSDK.Outbound;

namespace Gleap.Core.Tests;

public class GleapNotificationTests
{
    [Fact]
    public void FromActionJson_ParsesChatMessage_WithSenderConversationAndNameSubstitution()
    {
        const string json = """
        {
          "actionType": "notification",
          "outbound": "ob-123",
          "sound": true,
          "data": {
            "text": "Hi {{name}}, thanks for reaching out!",
            "sender": { "name": "Alice Agent", "profileImageUrl": "https://img/a.png" },
            "conversation": { "shareToken": "share-abc" }
          }
        }
        """;

        var n = GleapNotification.FromActionJson(json, "Bob Builder");

        Assert.NotNull(n);
        Assert.Equal(GleapNotificationKind.Message, n!.Kind);
        Assert.Equal("ob-123", n.Outbound);
        Assert.True(n.Sound);
        Assert.Equal("Hi Bob, thanks for reaching out!", n.Text);
        Assert.Equal("Alice Agent", n.Sender?.Name);
        Assert.Equal("https://img/a.png", n.Sender?.ProfileImageUrl);
        Assert.Equal("share-abc", n.ConversationShareToken);
    }

    [Theory]
    [InlineData("Bob Builder", "Hi Bob")]
    [InlineData("tobias@gleap.io", "Hi tobias")]   // identified by email only
    [InlineData("ada.lovelace@x.com", "Hi ada")]
    [InlineData("bob+tag@x.com", "Hi bob")]
    public void FromActionJson_FirstName_SplitsLikeTheReferenceSdks(string userName, string expected)
    {
        var n = GleapNotification.FromActionJson("""{"outbound":"o","data":{"text":"Hi {{name}}"}}""", userName);

        Assert.Equal(expected, n!.Text);
    }

    [Fact]
    public void FromActionJson_SubstitutesEmptyName_Cleanly()
    {
        const string json = """
        { "outbound": "o", "data": { "text": "Hi {{name}}, welcome" } }
        """;

        var n = GleapNotification.FromActionJson(json, null);

        Assert.NotNull(n);
        Assert.Equal("Hi, welcome", n!.Text);
    }

    [Fact]
    public void FromActionJson_ParsesNews()
    {
        const string json = """
        {
          "outbound": "n-1",
          "data": {
            "type": "news",
            "text": "New release",
            "news": { "id": "news-9" },
            "coverImageUrl": "https://img/cover.png"
          }
        }
        """;

        var n = GleapNotification.FromActionJson(json, null);

        Assert.NotNull(n);
        Assert.Equal(GleapNotificationKind.News, n!.Kind);
        Assert.Equal("news-9", n.NewsId);
        Assert.Equal("https://img/cover.png", n.CoverImageUrl);
    }

    [Fact]
    public void FromActionJson_ParsesChecklist_WithWidgetPopupType()
    {
        const string json = """
        {
          "outbound": "c-1",
          "data": {
            "checklist": {
              "id": "cl-5",
              "popupType": "widget",
              "currentStep": 2,
              "totalSteps": 4,
              "nextStepTitle": "Invite a teammate"
            }
          }
        }
        """;

        var n = GleapNotification.FromActionJson(json, null);

        Assert.NotNull(n);
        Assert.Equal(GleapNotificationKind.Checklist, n!.Kind);
        Assert.Equal("cl-5", n.ChecklistId);
        Assert.Equal("widget", n.ChecklistPopupType);
        Assert.Equal(2, n.ChecklistCurrentStep);
        Assert.Equal(4, n.ChecklistTotalSteps);
        Assert.Equal("Invite a teammate", n.ChecklistNextStepTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"outbound\":\"x\"}")] // no data object -> nothing to show
    public void FromActionJson_ReturnsNull_ForUnusablePayloads(string json)
    {
        Assert.Null(GleapNotification.FromActionJson(json, "Bob"));
    }

    [Fact]
    public void FromActionJson_TextWithoutPlaceholder_IsUnchanged()
    {
        const string json = """
        { "outbound": "o", "data": { "text": "A plain message" } }
        """;

        var n = GleapNotification.FromActionJson(json, "Bob Builder");

        Assert.Equal("A plain message", n!.Text);
    }
}
