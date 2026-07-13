using GleapSDK;
using GleapSDK.Bridge;
using GleapSDK.Serialization;
using Xunit;

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
    }

    [Fact]
    public void HelpCenterSearch_MapsTerm()
    {
        var msg = WidgetCommands.SearchHelpCenter("reset password", showBackButton: true);
        Assert.Equal("open-helpcenter-search", msg.Name);
        Assert.Contains("\"term\":\"reset password\"", _json.Serialize(msg));
    }
}
