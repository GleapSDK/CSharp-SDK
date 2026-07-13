using System;
using System.Collections.Generic;
using System.Text.Json;
using GleapSDK.Bridge;

namespace GleapSDK.Session;

/// <summary>
/// Subscribes to the bridge's ping handshake and pushes the mandatory bootstrap
/// sequence (config-update -> session-update -> prefill-form-data), before the
/// bridge flushes queued navigation commands. <c>widget-status-update</c> is not
/// sent here — <see cref="GleapSDK.ManagedBackend.Open"/> is the sole source of that
/// message, matching the native SDKs' order (config/session/flush, then open).
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
        SendConfigUpdate();
        SendSessionUpdate();
        SendPrefill();
    }

    private void SendConfigUpdate()
    {
        var s = _snapshot();
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
    }

    /// <summary>Sends session-update from the current snapshot. Called on ping and again after identify.</summary>
    public void SendSessionUpdate()
    {
        var s = _snapshot();
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
    }

    private void SendPrefill()
    {
        var s = _snapshot();
        if (s.PreFillFormData != null)
        {
            _bridge.Send(new GleapBridgeMessage { Name = "prefill-form-data", Data = s.PreFillFormData });
        }
    }

    /// <summary>Wrap already-serialized JSON so the serializer emits it verbatim.</summary>
    private static JsonElement RawJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
