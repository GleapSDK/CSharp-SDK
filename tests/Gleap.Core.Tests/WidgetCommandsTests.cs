using GleapSDK;
using GleapSDK.Bridge;
using GleapSDK.Serialization;

namespace Gleap.Core.Tests;

public class WidgetCommandsTests
{
    private readonly IJsonSerializer _json = new SystemTextJsonSerializer();

    [Fact]
    public void StartBot_MapsBotId_And_HideBackButtonIsNegation()
    {
        var msg = WidgetCommands.StartBot("bot1", showBackButton: false);
        Assert.Equal("start-bot", msg.Name);
        var s = _json.Serialize(msg);
        Assert.Contains("\"botId\":\"bot1\"", s);
        Assert.Contains("\"hideBackButton\":true", s); // !showBackButton
    }

    [Fact]
    public void StartConversation_IsStartBot_WithEmptyBotId()
    {
        var msg = WidgetCommands.StartConversation(showBackButton: true);
        Assert.Equal("start-bot", msg.Name);
        var s = _json.Serialize(msg);
        Assert.Contains("\"botId\":\"\"", s);
        Assert.Contains("\"hideBackButton\":false", s);
    }

    [Fact]
    public void OpenConversation_PutsShareTokenInData()
    {
        var msg = WidgetCommands.OpenConversation("tok");
        Assert.Equal("open-conversation", msg.Name);
        Assert.Contains("\"shareToken\":\"tok\"", _json.Serialize(msg));
    }

    [Fact]
    public void StartSurvey_SetsFlagsAndFormat()
    {
        var msg = WidgetCommands.StartSurvey("s1", SurveyFormat.SurveyFull);
        Assert.Equal("start-survey", msg.Name);
        var s = _json.Serialize(msg);
        Assert.Contains("\"flow\":\"s1\"", s);
        Assert.Contains("\"isSurvey\":true", s);
        Assert.Contains("\"format\":\"survey_full\"", s);
        Assert.Contains("\"hideBackButton\":true", s);
    }

    [Fact]
    public void HelpCenterSearch_MapsTerm()
    {
        var msg = WidgetCommands.SearchHelpCenter("reset password", showBackButton: true);
        Assert.Equal("open-helpcenter-search", msg.Name);
        Assert.Contains("\"term\":\"reset password\"", _json.Serialize(msg));
    }

    [Fact]
    public void OpenConversations_MapsShowBackButton()
    {
        var msg = WidgetCommands.OpenConversations(true);
        Assert.Equal("open-conversations", msg.Name);
        Assert.Contains("\"hideBackButton\":false", _json.Serialize(msg));
    }

    [Fact]
    public void StartClassicForm_MapsFlowAndShowBackButton()
    {
        var msg = WidgetCommands.StartClassicForm("f1", false);
        Assert.Equal("start-feedbackflow", msg.Name);
        var s = _json.Serialize(msg);
        Assert.Contains("\"flow\":\"f1\"", s);
        Assert.Contains("\"hideBackButton\":true", s);
    }

    [Fact]
    public void StartFeedbackFlow_MapsFlowAndShowBackButton()
    {
        var msg = WidgetCommands.StartFeedbackFlow("bugreport", showBackButton: true);
        Assert.Equal("start-feedbackflow", msg.Name);
        var s = _json.Serialize(msg);
        Assert.Contains("\"flow\":\"bugreport\"", s);
        Assert.Contains("\"hideBackButton\":false", s);
    }

    [Fact]
    public void OpenHelpCenterArticle_MapsArticleId()
    {
        var msg = WidgetCommands.OpenHelpCenterArticle("a1", true);
        Assert.Equal("open-help-article", msg.Name);
        Assert.Contains("\"articleId\":\"a1\"", _json.Serialize(msg));
    }

    [Fact]
    public void OpenHelpCenterCollection_MapsCollectionId()
    {
        var msg = WidgetCommands.OpenHelpCenterCollection("c1", true);
        Assert.Equal("open-help-collection", msg.Name);
        Assert.Contains("\"collectionId\":\"c1\"", _json.Serialize(msg));
    }

    [Fact]
    public void OpenNewsArticle_MapsId()
    {
        var msg = WidgetCommands.OpenNewsArticle("n1", true);
        Assert.Equal("open-news-article", msg.Name);
        Assert.Contains("\"id\":\"n1\"", _json.Serialize(msg));
    }

    [Fact]
    public void OpenFeatureRequests_HasExpectedName()
    {
        var msg = WidgetCommands.OpenFeatureRequests(true);
        Assert.Equal("open-feature-requests", msg.Name);
    }

    [Fact]
    public void OpenChecklists_HasExpectedName()
    {
        var msg = WidgetCommands.OpenChecklists(true);
        Assert.Equal("open-checklists", msg.Name);
    }

    [Fact]
    public void OpenChecklist_MapsId()
    {
        var msg = WidgetCommands.OpenChecklist("cl1", true);
        Assert.Equal("open-checklist", msg.Name);
        Assert.Contains("\"id\":\"cl1\"", _json.Serialize(msg));
    }

    [Fact]
    public void StartChecklist_MapsOutboundId()
    {
        var msg = WidgetCommands.StartChecklist("o1", true);
        Assert.Equal("start-checklist", msg.Name);
        Assert.Contains("\"outboundId\":\"o1\"", _json.Serialize(msg));
    }

    [Fact]
    public void AskAI_MapsQuestion()
    {
        var msg = WidgetCommands.AskAI("q?", true);
        Assert.Equal("ask-ai", msg.Name);
        Assert.Contains("\"question\":\"q?\"", _json.Serialize(msg));
    }
}
