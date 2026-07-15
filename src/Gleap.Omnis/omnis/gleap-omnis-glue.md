# Omnis 4GL glue — reference

This is the Omnis-side integration you import/adapt on your Omnis Studio box. It is **reference notation**:
the shapes, call order, and event handling are exact, but Omnis Studio's IDE is where you paste it into an
object class and wire the OBrowser control, so treat the snippets as the spec to reproduce, not a
copy-paste-and-run file. It could not be executed on the build machine (no Omnis there).

Prerequisites on the Windows machine:
- `Gleap.Omnis.dll` registered for COM (`regasm Gleap.Omnis.dll /codebase`, or a reg-free COM manifest).
  ProgId: **`Gleap.OmnisClient`**. See the "Using .NET objects in Omnis Studio" tech note (**tnex0001**).
- `web/gleap-omnis-bridge.html` deployed and loadable by OBrowser as an **Omnis html-control**. The two-way
  OBrowser bridge (`$callmethod` out, `sendMessageToFatClient` → `evControlEvent` in) only works for
  html-controls — model the control packaging on the official **Omnis-JSCBridge** sample.
- An `OBrowser` field on the window that will show Gleap (call it `oBrowser`), and a timer object.

The COM contract is all strings/scalars; structured data is JSON. See `IGleapOmnisClient` for every method.

---

## 1. Construct: create the COM object, configure, initialize

```
; --- $construct of the window/instance that owns Gleap ---
Do $cinst.$objs.gleap.$... ; (an Object instance var iGleap for the COM object)

; Create the COM automation object (see tnex0001 for your Omnis version's exact COM-create verb)
Do iGleap.$createobject("Gleap.OmnisClient") Returns lOk

; Optional configuration — call BEFORE Initialize
Do iGleap.$SetAppIdentity("Your Omnis App","2.4.1")
Do iGleap.$SetWindowHandle(kWindowHandleAsLong)   ; top-level HWND for native screenshots
; Do iGleap.$SetApiUrl("https://api.your-selfhost")  ; only when self-hosting
; Do iGleap.$SetFrameUrl("https://messenger.your-selfhost")

; Start the SDK (loads session + config asynchronously; returns immediately)
Do iGleap.$Initialize("YOUR_GLEAP_SDK_KEY")

; Wait until config/session are loaded, then load the bridge page into OBrowser.
; (Use a short poll or a timer tick — do NOT block the UI thread.)
Repeat
  Do iGleap.$IsReady() Returns lReady
Until lReady   ; add a timeout guard in real code

Do iGleap.$BridgePagePath() Returns lPath   ; absolute path to gleap-omnis-bridge.html
Calculate $cinst.$objs.oBrowser.$urlorcontrolname as con("file:///",lPath)
; If self-hosting the frame: append "?frame=<frameOrigin>" to the URL.
```

## 2. Timer: drain the two queues each tick

The glue owns a small timer (≈50 ms). Each tick it forwards queued host→widget payloads to the bridge page
and dispatches host-app callbacks. This is the robust, COM-event-free transport.

```
; --- $timer method ---
; a) host -> widget: deliver every queued payload to the bridge page
Repeat
  Do iGleap.$DequeueWidgetMessage() Returns lMsg
  If len(lMsg)>0
    Do $cinst.$objs.oBrowser.$callmethod("gleapDeliver",lMsg)   ; -> window.gleapDeliver(lMsg)
  End If
Until len(lMsg)=0

; b) host-app callbacks: react to widget events the app cares about
Repeat
  Do iGleap.$DequeueAppEvent() Returns lEvt
  If len(lEvt)>0
    Do method HandleAppEvent (lEvt)   ; lEvt is JSON: {"type":...}
  End If
Until len(lEvt)=0
```

## 3. Receive: OBrowser -> Omnis via evControlEvent

When the bridge page calls `sendMessageToFatClient("gleap", <json>)`, OBrowser fires `evControlEvent`.
Hand the JSON straight to `PushMessage`, then immediately drain the widget queue so handshake replies
(config/session on the frame's `ping`) reach the widget with no timer lag.

```
; --- $event method of the oBrowser field ---
On evControlEvent
  If pEventInfo.$id = "gleap"
    Do iGleap.$PushMessage(pEventInfo.$data)   ; widget -> Gleap.Core
    ; flush responses right away (same as the timer's part a)
    Repeat
      Do iGleap.$DequeueWidgetMessage() Returns lMsg
      If len(lMsg)>0
        Do $cinst.$objs.oBrowser.$callmethod("gleapDeliver",lMsg)
      End If
    Until len(lMsg)=0
  End If
```

## 4. Open / close and other calls

Drive the messenger from your UI (a menu item, a toolbar button, your own launcher):

```
; capture the app window first so the bug-report screenshot shows the app, not the widget
Do iGleap.$CaptureScreenshot()
Do <make the oBrowser field / its window visible>
Do iGleap.$Open()
; ... later
Do iGleap.$Close()
Do <hide the oBrowser field>
```

Identity, data, and reports — all string/JSON:

```
Do iGleap.$IdentifyContact("user-123","{""email"":""a@b.com"",""name"":""Ada""}","")  ; last arg = userHash, "" to omit
Do iGleap.$SetCustomData("orderId","4711")
Do iGleap.$SetTags("[""vip"",""desktop""]")
Do iGleap.$StartClassicForm("bugreport",kTrue)
Do iGleap.$OpenHelpCenter(kTrue)
Do iGleap.$SendSilentCrashReport("Unhandled error in module X",2,"")   ; severity 2 = high
```

## 5. Handle app events (`HandleAppEvent`)

`DequeueAppEvent` returns JSON objects. Parse `type` and react:

| `type` | Payload | Do |
|--------|---------|----|
| `initialized` | — | (optional) SDK ready |
| `widgetOpened` / `widgetClosed` | — | keep your own open-state / show-hide the field |
| `widgetHeightChanged` | `height` | resize the OBrowser field for responsive layout |
| `notificationCount` | `count` | update your launcher's unread badge |
| `feedbackButtonVisibility` | `visible` | show/hide your launcher (runtime `ShowFeedbackButton`) |
| `outbound` | `actionType`, `outboundId`, `data` | render the in-app notification / banner / modal (see the launcher reference) |
| `checklistUpdated` | `data` (checklistId, status, completedSteps, totalSteps) | update your own checklist UI |
| `checklistStepCompleted` | `data` (stepId, stepIndex, stepTitle, …) | react to a completed step |
| `checklistCompleted` | `data` (checklistId, …) | celebrate / mark done |
| `customAction` | `name`, `shareToken` | run your app-defined action |
| `openURL` | `url` | app-specific deep link (http/https links are opened in the browser for you) |
| `feedbackFlowStarted`, `feedbackSent`, `toolExecution` | — | optional hooks |

## 6. Shutdown

```
; --- $destruct ---
Do iGleap.$Shutdown()
Calculate iGleap as #NULL
```

---

### Notes
- **Native mode**: the frame is told it is in an app via `config-update {isApp:true}`, which Gleap.Core sends
  automatically on the `ping` handshake. You do not send it yourself.
- **Screenshot timing**: `CaptureScreenshot()` grabs the Omnis top-level window (`SetWindowHandle`) via
  `PrintWindow`. Call it just before the widget becomes visible.
- **Threading**: all COM methods are safe to call from the Omnis UI thread and never throw across the COM
  boundary (errors are swallowed and logged) so a bad argument can't destabilise Omnis.
- **Launcher / banners / modals** are yours to render (native Omnis UI), fed by the app events above — the
  same division the C# SDK uses (its WPF layer renders that chrome).
