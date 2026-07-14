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
    /// <summary>Raised on "collect-ticket-data" — the widget is asking for the current report data.</summary>
    public event System.Action? CollectTicketDataRequested;
    /// <summary>Raised on "tool-execution" — the widget is invoking an agent tool.</summary>
    public event Action<JsonElement>? ToolExecutionRequested;
    /// <summary>Raised on "height-update" — the widget's desired content height in pixels (for responsive sizing).</summary>
    public event Action<double>? HeightUpdated;

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

            case "collect-ticket-data":
                CollectTicketDataRequested?.Invoke();
                break;

            case "tool-execution":
                ToolExecutionRequested?.Invoke(msg.Data);
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

                // frontend-tool-execute, cleanup-drawings, screenshot-updated -> handled in SP-0 Part 2.
        }
    }
}
