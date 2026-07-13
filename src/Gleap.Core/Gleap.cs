using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK;

/// <summary>
/// Public entry point. Platform packages create the backend (managed or native-bridge)
/// and attach it via <see cref="UseBackend"/>; app code then calls the static API.
/// </summary>
public static class Gleap
{
    private static IGleapBackend? _backend;

    private static IGleapBackend Backend =>
        _backend ?? throw new System.InvalidOperationException(
            "Gleap backend not attached. A platform package must call Gleap.UseBackend(...).");

    /// <summary>Attach the platform backend. Called by platform packages, not app code.</summary>
    public static void UseBackend(IGleapBackend backend) => _backend = backend;

    public static Task InitializeAsync(string token, CancellationToken ct = default) =>
        Backend.InitializeAsync(token, ct);

    public static void Open() => Backend.Open();
    public static void Close() => Backend.Close();
    public static void StartConversation(bool showBackButton = true) => Backend.StartConversation(showBackButton);
    public static void StartBot(string botId, bool showBackButton = true) => Backend.StartBot(botId, showBackButton);
    public static void OpenConversation(string shareToken) => Backend.OpenConversation(shareToken);
    public static void OpenHelpCenter(bool showBackButton = true) => Backend.OpenHelpCenter(showBackButton);
    public static void OpenNews(bool showBackButton = true) => Backend.OpenNews(showBackButton);
    public static void ShowSurvey(string surveyId, SurveyFormat format = SurveyFormat.Survey) => Backend.ShowSurvey(surveyId, format);

    public static void Log(string message, LogLevel level = LogLevel.Info) => Backend.Log(message, level);
    public static void TrackEvent(string name, object? data = null) => Backend.TrackEvent(name, data);
    public static void TrackPage(string pageName) => Backend.TrackPage(pageName);
    public static void SetCustomData(string key, string value) => Backend.SetCustomData(key, value);
    public static void AttachCustomData(IReadOnlyDictionary<string, object> data) => Backend.AttachCustomData(data);
    public static void RemoveCustomDataForKey(string key) => Backend.RemoveCustomDataForKey(key);
    public static void ClearCustomData() => Backend.ClearCustomData();
    public static void SetTicketAttribute(string key, object value) => Backend.SetTicketAttribute(key, value);
    public static void UnsetTicketAttribute(string key) => Backend.UnsetTicketAttribute(key);
    public static void ClearTicketAttributes() => Backend.ClearTicketAttributes();
    public static void SetTags(string[] tags) => Backend.SetTags(tags);
    public static void AddAttachment(string base64File, string fileName) => Backend.AddAttachment(base64File, fileName);
    public static void RemoveAllAttachments() => Backend.RemoveAllAttachments();
    public static void SetNetworkLogsBlacklist(string[] blacklist) => Backend.SetNetworkLogsBlacklist(blacklist);
    public static void SetNetworkLogPropsToIgnore(string[] propsToIgnore) => Backend.SetNetworkLogPropsToIgnore(propsToIgnore);

    /// <summary>Test-only reset so xUnit cases don't leak backend state.</summary>
    internal static void ResetForTest() => _backend = null;
}
