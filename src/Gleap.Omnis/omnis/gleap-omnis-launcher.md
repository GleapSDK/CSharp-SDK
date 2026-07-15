# Omnis native launcher — reference

A ready-made native launcher for the Gleap Omnis SDK: a floating button (bottom-right), an unread
badge, and a **slide-in / slide-out animation** of the OBrowser field — so the customer does not wire
their own trigger and the messenger opens with the same polish as the web / C# SDK.

This builds on `gleap-omnis-glue.md` (the base construct / timer / event / open plumbing). It is
**reference notation**: reproduce it in your Omnis object class; it could not be executed on the build
machine (no Omnis). All state comes from the same COM object `iGleap` (ProgId `Gleap.OmnisClient`).

The SDK now surfaces everything the launcher needs, matching the other SDKs:
- `iGleap.$IsFeedbackButtonVisible()` — the effective visibility (project config default, overridden by
  `$ShowFeedbackButton`).
- `iGleap.$FeedbackButtonPosition()` — `BOTTOM_RIGHT` / `BOTTOM_LEFT` / `BUTTON_HIDE` / … for placement.
- App events `notificationCount` (badge) and `feedbackButtonVisibility` (show/hide at runtime).

---

## 1. Layout

On the Gleap window place, at the bottom-right corner:
- `oBrowser`  — the messenger field, from the base glue. Start it **collapsed** (`$height:0`) and just
  below its target position so it can slide up.
- `launcherButton` — a round 52×52 button (background = your brand colour; icon = chat glyph).
- `badge` — a small label pinned to the launcher's top-right, hidden by default.

```
; --- $construct (after the base-glue Initialize + IsReady) ---
Calculate iOpen as kFalse
Calculate iPanelH as 680                          ; target messenger height
Calculate iPanelTop as $cinst.$height-iPanelH-96  ; resting Y (above the launcher)

; place the launcher per the project's configured position
Do iGleap.$FeedbackButtonPosition() Returns lPos
Do method PlaceLauncher (lPos)                    ; BOTTOM_RIGHT / BOTTOM_LEFT / …

; honour the initial visibility (config BUTTON_HIDE -> hidden)
Do iGleap.$IsFeedbackButtonVisible() Returns lVisible
Calculate $cinst.$objs.launcherButton.$visible as lVisible

; start collapsed
Calculate $cinst.$objs.oBrowser.$height as 0
Calculate $cinst.$objs.oBrowser.$top as $cinst.$height
```

## 2. Open / close from the launcher (with the slide animation)

```
; --- launcherButton $event ---
On evClick
  If iOpen
    Do method CloseGleap
  Else
    Do method OpenGleap
  End If

; --- OpenGleap method ---
Do iGleap.$CaptureScreenshot()                    ; grab the app window BEFORE the panel shows
Calculate $cinst.$objs.oBrowser.$top as $cinst.$height     ; start below
Calculate $cinst.$objs.oBrowser.$height as iPanelH
Calculate $cinst.$objs.oBrowser.$visible as kTrue
Calculate iAnimDir as 1                            ; 1 = opening
Calculate iAnimStep as 0
Do $cinst.$objs.animTimer.$settimer(16)           ; ~60 fps
Do iGleap.$Open()                                  ; tell the widget it is open
Calculate iOpen as kTrue

; --- CloseGleap method ---
Calculate iAnimDir as -1                           ; -1 = closing
Calculate iAnimStep as 0
Do $cinst.$objs.animTimer.$settimer(16)
Do iGleap.$Close()
Calculate iOpen as kFalse
```

## 3. The slide animation (`animTimer`)

A short eased tween (~280 ms) of the field's `$top` between the resting position and just below the
window. Opening also fades the launcher icon from chat → close (optional).

```
; --- animTimer $event ---
On evTimer
  Calculate iAnimStep as iAnimStep+1
  Calculate lT as min(iAnimStep/17,1)              ; 17 ticks * 16ms ≈ 280ms
  Calculate lEase as 1-((1-lT)*(1-lT)*(1-lT))      ; easeOutCubic
  If iAnimDir=1                                     ; opening: slide up into place
    Calculate $cinst.$objs.oBrowser.$top as $cinst.$height-((($cinst.$height-iPanelTop))*lEase)
  Else                                              ; closing: slide back down
    Calculate $cinst.$objs.oBrowser.$top as iPanelTop+((($cinst.$height-iPanelTop))*lEase)
  End If
  If lT>=1                                          ; animation done
    Do $cinst.$objs.animTimer.$cleartimer()
    If iAnimDir=-1
      Calculate $cinst.$objs.oBrowser.$visible as kFalse
      Calculate $cinst.$objs.oBrowser.$height as 0
    End If
  End If
```

## 4. Badge + runtime show/hide (extend `HandleAppEvent`)

Add these `type` branches to the base glue's `HandleAppEvent` (§5 there):

```
; lEvt is JSON {"type":...}
Calculate lType as <parse "type" from lEvt>

If lType="notificationCount"
  Calculate lCount as <parse "count">
  If lCount>0
    Calculate $cinst.$objs.badge.$text as con(lCount)   ; "99+" if you prefer, when >99
    Calculate $cinst.$objs.badge.$visible as kTrue
  Else
    Calculate $cinst.$objs.badge.$visible as kFalse
  End If

Else If lType="feedbackButtonVisibility"                ; $ShowFeedbackButton at runtime
  Calculate lVisible as <parse "visible">               ; boolean
  Calculate $cinst.$objs.launcherButton.$visible as lVisible
  If not(lVisible)
    Calculate $cinst.$objs.badge.$visible as kFalse
  End If

Else If lType="widgetClosed"                            ; widget closed itself (its own X)
  If iOpen
    Do method CloseGleap
  End If
End If
```

## 5. Hiding the button

Two ways, both honoured by the launcher above — exactly like the other SDKs:
- **From the Gleap dashboard**: set the feedback button position to *hidden*. `FeedbackButtonPosition()`
  returns `BUTTON_HIDE`, so `IsFeedbackButtonVisible()` is false at start and the launcher is hidden.
- **At runtime from code**: `Do iGleap.$ShowFeedbackButton(kFalse)` (or `kTrue`). This raises the
  `feedbackButtonVisibility` app event, which the branch above uses to show/hide the launcher live.

In both cases the messenger can still be opened programmatically (`$Open()`, `$StartBot("")`,
`$OpenHelpCenter()`, …) while the button is hidden — matching the web/native SDK behaviour.

## 6. In-app notification preview toasts

These are the small preview cards that pop above the launcher when a new message / news / checklist
arrives (what `GleapNotificationStack` renders in the C# SDK). The SDK surfaces each one as an
`outbound` app event with `actionType="notification"`; you render the native card and open its target
when tapped. (`SetDisableInAppNotifications(kTrue)` suppresses these at the source — the event never
fires; banners/modals/surveys are unaffected.)

The `data` field of the event is the raw action JSON:

```
{ "sender": { "name": "Ada", "profileImageUrl": "https://…" },
  "text": "Hi, any update?",
  "conversation": { "shareToken": "abc" },      // present for chat replies -> open this conversation
  "news":      { "id": "n1" },                    // OR a news article
  "checklist": { "id": "c1", "nextStepTitle": "…" } }  // OR a checklist
```

Render a small card (avatar + sender name + text) at the bottom-right, above the launcher; slide it in;
auto-dismiss after ~6 s; on tap open the target and reveal the messenger. Cap the stack at ~2 cards and
de-duplicate by `outboundId` (matching the native SDKs).

```
; --- add to HandleAppEvent (§4) ---
Else If lType="outbound"
  Calculate lAction as <parse "actionType">
  If lAction="notification"
    Do method ShowNotificationCard (<parse "data">, <parse "outboundId">)
  End If
  ;; "banner" / "modal" are larger outbound surfaces (a second OBrowser field loading
  ;; outboundmedia.gleap.io) — still host-rendered chrome, out of scope for this launcher.
End If

; --- ShowNotificationCard (pData JSON, pOutboundId) ---
; de-dup: if a card for pOutboundId already shows, replace its content instead of stacking a new one
Calculate lText   as <parse "text" from pData>
Calculate lName   as <parse "sender.name" from pData>
Calculate lAvatar as <parse "sender.profileImageUrl" from pData>
Do <build/position a card field group at bottom-right, above the launcher; fill avatar/name/text>
Do <slide the card in (reuse the easeOutCubic tween from §3)>
Do $cinst.$objs.cardDismissTimer.$settimer(6000)     ; auto-dismiss after 6s

; remember the tap target for this card:
Calculate iCardShareToken as <parse "conversation.shareToken" from pData>
Calculate iCardNewsId     as <parse "news.id" from pData>
Calculate iCardChecklistId as <parse "checklist.id" from pData>

; --- notificationCard $event: On evClick ---
If len(iCardShareToken)>0
  Do iGleap.$OpenConversation(iCardShareToken)
Else If len(iCardNewsId)>0
  Do iGleap.$OpenNewsArticle(iCardNewsId,kTrue)
Else If len(iCardChecklistId)>0
  Do iGleap.$OpenChecklist(iCardChecklistId,kTrue)
End If
Do method OpenGleap                                   ; reveal the messenger (§2)
Do <hide the card>
```

---

### Notes
- **Position:** `PlaceLauncher` should map `BOTTOM_LEFT` to the left corner, and honour any `buttonX`/
  `buttonY` offsets you read from the config if you support them; `BOTTOM_RIGHT` is the default.
- **Screenshot timing:** `$CaptureScreenshot()` is called in `OpenGleap` *before* the field is shown, so
  the bug-report screenshot is the Omnis app, not the Gleap panel.
- **Tuning:** 17 ticks × 16 ms ≈ 280 ms matches the C# SDK's slide duration; adjust `iPanelH`/easing to
  taste. If OBrowser repaint lag is visible during the tween, animate `$top` only (leave `$height` fixed).
