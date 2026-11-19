---
paths:
  - "src/**/*.cs"
  - "maui/**/*.cs"
  - "unity/**/*.cs"
  - "tests/**/*.cs"
---

# Testing

Tests come first and stay fixed while the code is written: an agent that may change a test will change it to get green.

- **New behaviour or a bug fix:** write the test first, from the requirement and not from the code. Use the `test-writer` subagent if you can start one. See it fail for the expected reason and commit it on its own, then write the code without touching it.
- **Refactor without a behaviour change:** tests stay unchanged. Nothing covers the code? Pin today's behaviour with a test first.
- **Never edit, skip or delete an existing test to make a change pass,** and never detect test inputs or hardcode answers. No `Skip`, retries or sleeps to make a test pass. If a test looks wrong, stop and report `BLOCKED` with the reason. A test changes only when the required behaviour changed; say so in the PR.

## What needs what

| Code | Test |
|---|---|
| Logic in `Gleap.Core`: session, config, buffers, redaction, feedback assembly, outbound | a unit test in `tests/Gleap.Core.Tests/<Type>Tests.cs` |
| What the SDK sends to the widget or does on a widget message | drive `ManagedBackend` with `FakeWebViewChannel.SimulateIncoming`, assert on `ExecutedScripts` (`WebViewBridgeIncomingTests.cs`) |
| What the SDK sends to the Gleap API, and its answer to failures | `FakeHttpTransport`: queue responses, assert on `Calls` (`ApiClientIdentityTests.cs`) |
| Anything that depends on time | `FakeClock`, never the real clock or a delay |
| A public call of the `Gleap` class | `GleapFacadeTests.cs`, through `UseBackend` |
| `Gleap.Extensions.Logging` | `GleapLoggerTests.cs` |
| The WebView2 control, the MAUI channels, the Unity runtime | no automated test here: move the decision into Core and test it there; verify the rest by hand and say how (`WINDOWS-RUNBOOK.md`) |

Every path that can drop, retry or repeat work gets a test: a request that fails once, one that keeps failing, an answer in a shape we did not expect, a widget message that arrives twice or before `ping`, a call before `InitializeAsync`.

## How tests are written

- xUnit.net v3: `[Fact]`, and `[Theory]` with `[InlineData]` for cases. Names state the behaviour: `Poll_EmitsOutboundSent_PerAction`. One behaviour per test, arrange / act / assert.
- Pass `TestContext.Current.CancellationToken` where a method takes a token (xUnit1051); old tests pass `CancellationToken.None`, no precedent.
- Fake only what we do not own, with the hand-written fakes in `tests/Gleap.Core.Tests/Fakes`; extend a fake rather than adding a mocking library. Never fake the class under test.
- No delay decides a result. Complete a `TaskCompletionSource` and await the task you are waiting for; one old test waits with `Task.Delay(20)`, no precedent.
- Assert behaviour, not that code ran: the JSON the widget gets, the request the API gets, the event a listener sees, what the buffer holds.
- Tests never reach the network or the live widget.

## What to run

- While you work: `dotnet test --project tests/Gleap.Core.Tests --filter-class '*<Type>Tests'`.
- Before you finish: `sh scripts/lint-new.sh`, `dotnet test --project tests/Gleap.Core.Tests`, and `sh scripts/api-new.sh` when you changed a shipped project.
- `sh scripts/coverage.sh` holds a floor of 76 % line coverage of Core and Logging. Raise it when coverage rises; never lower it without saying why in the PR.

If no test reaches the code you changed, that is not a pass: say in your report what no test covered.
