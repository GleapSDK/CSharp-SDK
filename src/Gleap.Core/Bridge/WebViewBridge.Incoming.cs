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
    public event Action<string>? OpenUrlRequested;
    public event Action<JsonElement>? SendFeedbackRequested;

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
                    type.GetString() == "flow-started")
                {
                    var payload = msg.Data.TryGetProperty("data", out var d) ? d : default;
                    FeedbackFlowStarted?.Invoke(payload);
                }
                break;

            case "send-feedback":
                SendFeedbackRequested?.Invoke(msg.Data);
                break;

                // tool-execution, frontend-tool-execute, collect-ticket-data,
                // cleanup-drawings, screenshot-updated -> handled in SP-0 Part 2.
        }
    }
}
