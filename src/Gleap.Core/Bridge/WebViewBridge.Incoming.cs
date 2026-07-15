using System;
using System.Text.Json;

namespace GleapSDK.Bridge;

public sealed partial class WebViewBridge
{
    /// <summary>Raised on "ping" — orchestrator pushes the bootstrap sequence here.</summary>
    public event Action? PingReceived;
    public event Action? CloseWidgetRequested;
    /// <summary>(actionName, shareToken?)</summary>
    public event Action<string, string?>? CustomActionTriggered;
    public event Action<JsonElement>? FeedbackFlowStarted;
    /// <summary>Raised on any "notify-event" — (type, data). The widget emits <c>flow-started</c>,
    /// <c>conversation-started</c>, <c>rating-sent</c>, … ; the JS SDK re-emits them all.</summary>
    public event Action<string, JsonElement>? WidgetEventNotified;
    public event Action<string>? OpenUrlRequested;
    /// <summary>Raised on "open-image" — the user tapped an image/attachment in a conversation.</summary>
    public event Action<string>? OpenImageRequested;
    /// <summary>Raised on "play-ping" — the widget wants the message sound played.</summary>
    public event Action? PlayPingRequested;
    /// <summary>Raised on "checklist-loaded" — the widget surfaced a checklist.</summary>
    public event Action<JsonElement>? ChecklistLoaded;
    public event Action<JsonElement>? SendFeedbackRequested;
    /// <summary>Raised on "collect-ticket-data" — the widget is asking for the current report data.</summary>
    public event System.Action? CollectTicketDataRequested;
    /// <summary>Raised on "tool-execution" — the widget is notifying that an agent tool ran.</summary>
    public event Action<JsonElement>? ToolExecutionRequested;
    /// <summary>Raised on "frontend-tool-execute" — the agent is invoking a Frontend tool
    /// ({ toolCallId, name, params }) and is waiting for a "frontend-tool-result" reply.</summary>
    public event Action<JsonElement>? FrontendToolExecuteRequested;
    /// <summary>Raised on "height-update" — the widget's desired content height in pixels (for responsive sizing).</summary>
    public event Action<double>? HeightUpdated;
    /// <summary>Raised on "screenshot-updated" — the user-edited screenshot data-URI from the widget's editor.</summary>
    public event Action<string>? ScreenshotUpdated;
    /// <summary>Raised on "cleanup-drawings" — the user discarded their screenshot annotations.</summary>
    public event Action? DrawingsCleanedUp;

    partial void OnMessageReceived(string json)
    {
        IncomingBridgeMessage msg;
        try { msg = IncomingBridgeMessage.Parse(json); }
        catch { return; } // ignore malformed frames

        switch (msg.Name)
        {
            case "ping":
                _connected = true;
                PingReceived?.Invoke();   // orchestrator sends bootstrap while connected
                FlushQueue();             // then queued nav commands
                break;

            case "close-widget":
                CloseWidgetRequested?.Invoke();
                break;

            case "run-custom-action":
                var actionName = msg.Data.ValueKind == JsonValueKind.String ? msg.Data.GetString() : null;
                if (!string.IsNullOrEmpty(actionName))
                {
                    CustomActionTriggered?.Invoke(actionName!, msg.ShareToken);
                }

                break;

            case "open-url":
                if (msg.Data.ValueKind == JsonValueKind.String)
                {
                    OpenUrlRequested?.Invoke(msg.Data.GetString()!);
                }

                break;

            case "notify-event":
                if (msg.Data.ValueKind == JsonValueKind.Object &&
                    msg.Data.TryGetProperty("type", out var type) &&
                    type.ValueKind == JsonValueKind.String &&
                    type.GetString() is { Length: > 0 } eventType)
                {
                    var payload = msg.Data.TryGetProperty("data", out var d) ? d : default;
                    // Forward every type (the widget also emits conversation-started, rating-sent, …),
                    // keeping the dedicated flow-started event for existing subscribers.
                    WidgetEventNotified?.Invoke(eventType, payload);
                    if (eventType == "flow-started")
                    {
                        FeedbackFlowStarted?.Invoke(payload);
                    }
                }
                break;

            case "collect-ticket-data":
                CollectTicketDataRequested?.Invoke();
                break;

            case "tool-execution":
                ToolExecutionRequested?.Invoke(msg.Data);
                break;

            case "frontend-tool-execute":
                FrontendToolExecuteRequested?.Invoke(msg.Data);
                break;

            case "send-feedback":
                SendFeedbackRequested?.Invoke(msg.Data);
                break;

            case "height-update":
                if (msg.Data.ValueKind == JsonValueKind.Number && msg.Data.TryGetDouble(out var height))
                {
                    HeightUpdated?.Invoke(height);
                }
                else if (msg.Data.ValueKind == JsonValueKind.String
                    && double.TryParse(msg.Data.GetString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    HeightUpdated?.Invoke(parsed);
                }
                break;

            case "screenshot-updated":
                if (msg.Data.ValueKind == JsonValueKind.String)
                {
                    ScreenshotUpdated?.Invoke(msg.Data.GetString()!);
                }
                break;

            case "cleanup-drawings":
                DrawingsCleanedUp?.Invoke();
                break;

            case "open-image":
                // The widget panel is narrow; without a host viewer, tapping an attachment does nothing.
                var imageUrl = msg.Data.ValueKind == JsonValueKind.String
                    ? msg.Data.GetString()
                    : msg.Data.ValueKind == JsonValueKind.Object && msg.Data.TryGetProperty("url", out var u)
                        && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    OpenImageRequested?.Invoke(imageUrl!);
                }
                break;

            case "play-ping":
                PlayPingRequested?.Invoke();
                break;

            case "checklist-loaded":
                ChecklistLoaded?.Invoke(msg.Data);
                break;
        }
    }
}
