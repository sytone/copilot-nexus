# Feature: Non-blocking session send input flow

Long-running prompts should not cause client request timeouts when sent through
Nexus. The service must accept input immediately, keep streaming output events,
and surface clear failures back to connected clients.

## Research Notes

- `SessionTabViewModel.SendInputAsync()` sends through `ICopilotSessionWrapper`
  and currently depends on completion timing to clear `IsProcessing`.
- In Nexus mode this flows through:
  `NexusSessionManager.SendInputAsync()` → `SessionHub.SendInput()` →
  `SessionManager.SendInputAsync()` → `ICopilotSessionWrapper.SendAsync()`.
- `SessionManager.SendInputAsync()` waits for the SDK wrapper to complete.
  For long prompts this can run much longer than client request windows.
- `SessionsController.SendInput()` also awaited the full send operation before
  returning `202 Accepted`, so HTTP clients could timeout before receiving the
  accepted response.
- `WebhookController` already uses a background dispatch pattern; this behavior
  is the baseline for non-blocking send semantics in Nexus APIs.

## Background

```gherkin
Given a session is active in Nexus
And session output is streamed through SignalR SessionOutput events
And sends can run for longer than one minute depending on prompt/tool activity
```

## Scenario: SignalR send invocation returns quickly

```gherkin
Given a user sends input from an active session tab
When the service accepts the send request
Then SessionHub.SendInput returns without waiting for the full SDK response
And streamed output continues to arrive through SessionOutput events
And the tab remains in processing state until an Idle output event is received
```

## Scenario: REST send endpoint does not block on long-running prompt

```gherkin
Given a client posts to /api/sessions/{id}/input with a long-running prompt
When the session starts processing the prompt
Then the endpoint responds with 202 Accepted before client timeout windows expire
And processing continues in the background
And output still streams to subscribed session clients
```

## Scenario: Background send failure is explicit to clients

```gherkin
Given a send operation is accepted for background execution
When the underlying session send fails
Then Nexus logs the failure with session context
And a system activity output containing the error message is emitted
And an Idle output event is emitted so clients can clear processing UI state
```
