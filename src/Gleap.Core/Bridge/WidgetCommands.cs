using System.Collections.Generic;

namespace GleapSDK.Bridge;

/// <summary>
/// Builds the navigation command messages the web widget understands.
/// Rule from the native SDKs: hideBackButton == !showBackButton.
/// </summary>
public static class WidgetCommands
{
    private static object Hide(bool showBackButton, params (string key, object value)[] extra)
    {
        var d = new Dictionary<string, object> { ["hideBackButton"] = !showBackButton };
        foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    public static GleapBridgeMessage StartBot(string botId, bool showBackButton) =>
        new() { Name = "start-bot", Data = Hide(showBackButton, ("botId", botId)) };

    public static GleapBridgeMessage StartConversation(bool showBackButton) =>
        StartBot("", showBackButton);

    public static GleapBridgeMessage OpenConversations(bool showBackButton) =>
        new() { Name = "open-conversations", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenConversation(string shareToken) =>
        new() { Name = "open-conversation", Data = new Dictionary<string, object> { ["shareToken"] = shareToken } };

    public static GleapBridgeMessage StartClassicForm(string formId, bool showBackButton) =>
        new() { Name = "start-feedbackflow", Data = Hide(showBackButton, ("flow", formId)) };

    public static GleapBridgeMessage OpenHelpCenter(bool showBackButton) =>
        new() { Name = "open-helpcenter", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenHelpCenterArticle(string articleId, bool showBackButton) =>
        new() { Name = "open-help-article", Data = Hide(showBackButton, ("articleId", articleId)) };

    public static GleapBridgeMessage OpenHelpCenterCollection(string collectionId, bool showBackButton) =>
        new() { Name = "open-help-collection", Data = Hide(showBackButton, ("collectionId", collectionId)) };

    public static GleapBridgeMessage SearchHelpCenter(string term, bool showBackButton) =>
        new() { Name = "open-helpcenter-search", Data = Hide(showBackButton, ("term", term)) };

    public static GleapBridgeMessage OpenNews(bool showBackButton) =>
        new() { Name = "open-news", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenNewsArticle(string articleId, bool showBackButton) =>
        new() { Name = "open-news-article", Data = Hide(showBackButton, ("id", articleId)) };

    public static GleapBridgeMessage OpenFeatureRequests(bool showBackButton) =>
        new() { Name = "open-feature-requests", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenChecklists(bool showBackButton) =>
        new() { Name = "open-checklists", Data = Hide(showBackButton) };

    public static GleapBridgeMessage OpenChecklist(string checklistId, bool showBackButton) =>
        new() { Name = "open-checklist", Data = Hide(showBackButton, ("id", checklistId)) };

    public static GleapBridgeMessage StartChecklist(string outboundId, bool showBackButton) =>
        new() { Name = "start-checklist", Data = Hide(showBackButton, ("outboundId", outboundId)) };

    public static GleapBridgeMessage AskAI(string question, bool showBackButton) =>
        new() { Name = "ask-ai", Data = Hide(showBackButton, ("question", question)) };

    public static GleapBridgeMessage StartSurvey(string surveyId, SurveyFormat format)
    {
        var formatStr = format == SurveyFormat.SurveyFull ? "survey_full" : "survey";
        return new()
        {
            Name = "start-survey",
            Data = new Dictionary<string, object>
            {
                ["flow"] = surveyId,
                ["isSurvey"] = true,
                ["format"] = formatStr,
                ["hideBackButton"] = false
            }
        };
    }
}
