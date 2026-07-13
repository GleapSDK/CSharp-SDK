using System;
using System.Collections.Generic;
using System.Text.Json;
using GleapSDK.Bridge;

namespace GleapSDK.Session;

/// <summary>
/// Subscribes to the bridge's ping handshake and pushes the mandatory bootstrap
/// sequence (widget-status-update -> config-update -> session-update ->
/// prefill-form-data), before the bridge flushes queued navigation commands.
/// </summary>
public sealed class WidgetBootstrapper
{
    private readonly WebViewBridge _bridge;
    private readonly Func<SessionSnapshot> _snapshot;

    public WidgetBootstrapper(WebViewBridge bridge, Func<SessionSnapshot> snapshot)
    {
        _bridge = bridge;
        _snapshot = snapshot;
        _bridge.PingReceived += OnPing;
    }

    private void OnPing()
    {
        var s = _snapshot();

        _bridge.Send(new GleapBridgeMessage
        {
            Name = "widget-status-update",
            Data = new Dictionary<string, object> { ["isWidgetOpen"] = true }
        });

        _bridge.Send(new GleapBridgeMessage
        {
            Name = "config-update",
            Data = new Dictionary<string, object>
            {
                ["config"] = RawJson(s.FlowConfigJson),
                ["actions"] = RawJson(s.ProjectActionsJson),
                ["overrideLanguage"] = s.Language,
                ["isApp"] = true
            }
        });

        _bridge.Send(new GleapBridgeMessage
        {
            Name = "session-update",
            Data = new Dictionary<string, object?>
            {
                ["sessionData"] = new Dictionary<string, object?>
                {
                    ["gleapId"] = s.GleapId,
                    ["gleapHash"] = s.GleapHash,
                    ["userId"] = s.UserId,
                    ["name"] = s.Name,
                    ["email"] = s.Email
                },
                ["apiUrl"] = s.ApiUrl,
                ["sdkKey"] = s.SdkKey
            }
        });

        if (s.PreFillFormData != null)
        {
            _bridge.Send(new GleapBridgeMessage
            {
                Name = "prefill-form-data",
                Data = s.PreFillFormData
            });
        }
    }

    /// <summary>Wrap already-serialized JSON so the serializer emits it verbatim.</summary>
    private static JsonElement RawJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
