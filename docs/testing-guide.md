# Testing Guide

## Overview

Copilot Family has three test projects covering unit tests, ViewModel tests, and
UI automation tests. All tests use **xUnit 2.7** as the test framework.

| Project | Type | Tests | What It Covers |
|---|---|---|---|
| `CopilotFamily.Core.Tests` | Unit | 65 | Models, SessionManager, persistence, integration |
| `CopilotFamily.App.Tests` | Unit | 47 | ViewModels, XAML value converters |
| `CopilotFamily.UI.Tests` | UI Automation | 12 | App launch, tab management, user interactions |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or later)
- Windows 10/11 (required for WPF and FlaUI)
- No Copilot CLI needed for tests — mock services are used

## Running Tests

### All Unit Tests

```bash
dotnet test CopilotFamily.slnx
```

### Individual Test Projects

```bash
# Core business logic tests (fastest, no WPF dependency)
dotnet test test/CopilotFamily.Core.Tests

# App ViewModel and converter tests
dotnet test test/CopilotFamily.App.Tests

# UI automation tests (launches the app — requires a display)
dotnet test test/CopilotFamily.UI.Tests
```

### Filtered by Test Name

```bash
# Run a single test
dotnet test --filter "SessionManagerTests.CreateSessionAsync_AddsSessionToList"

# Run all tests in a class
dotnet test --filter "FullyQualifiedName~SessionTabViewModelTests"

# Run all streaming-related tests
dotnet test --filter "DisplayName~Delta"
```

### Verbose Output

```bash
dotnet test --verbosity normal
```

## Test Projects in Detail

### CopilotFamily.Core.Tests

Pure unit tests with no UI or WPF dependencies. Target framework: `net8.0`.

**Test classes:**

| Class | Tests | Coverage |
|---|---|---|
| `SessionMessageTests` | 10 | Message creation, streaming append, property change notifications |
| `SessionInfoTests` | 8 | ID generation, name/state/model properties |
| `SessionManagerTests` | 15 | Session lifecycle, events, multi-session coordination, disposal |

**Key patterns:**

- Uses **Moq** to mock `ICopilotClientService` and `ICopilotSessionWrapper`
- Uses `NullLogger<T>` from `Microsoft.Extensions.Logging.Abstractions`
- `SessionManagerTests` verifies session create/remove/send/dispose flows
  without a real Copilot SDK connection

**Example — mocking the client service:**

```csharp
var mockClientService = new Mock<ICopilotClientService>();
var mockSession = new Mock<ICopilotSessionWrapper>();

mockClientService
    .Setup(c => c.CreateSessionAsync(
        It.IsAny<string?>(),
        It.IsAny<SessionConfiguration?>(),
        It.IsAny<Func<ToolPermissionRequest, Task<PermissionDecision>>?>(),
        It.IsAny<Func<AgentUserInputRequest, Task<AgentUserInputResponse>>?>(),
        It.IsAny<CancellationToken>()))
    .ReturnsAsync(mockSession.Object);

var manager = new SessionManager(mockClientService.Object, NullLogger<SessionManager>.Instance);
```

### CopilotFamily.App.Tests

ViewModel and converter tests. Target framework: `net8.0-windows` (WPF types needed
for converters).

**Test classes:**

| Class | Tests | Coverage |
|---|---|---|
| `MainWindowViewModelTests` | 12 | Tab creation, closing, selection, session manager delegation |
| `SessionTabViewModelTests` | 19 | Input handling, send/abort/clear commands, streaming deltas, disposal |
| `EmptyToVisibleConverterTests` | 4 | Zero → Visible, non-zero → Collapsed |
| `MessageRoleToBrushConverterTests` | 5 | Role → color mapping |
| `MessageRoleToLabelConverterTests` | 3 | Role → display label mapping |

**Key patterns:**

- **`SynchronousUiDispatcher`** — test helper at
  `test/CopilotFamily.App.Tests/Helpers/SynchronousUiDispatcher.cs` that implements
  `IUiDispatcher` by executing actions inline on the calling thread. This avoids
  needing a WPF Dispatcher in unit tests.
- Moq's `Raise()` is used to simulate SDK output events:

```csharp
// Simulate a streaming delta arriving from the SDK
mockSession.Raise(
    s => s.OutputReceived += null,
    mockSession.Object,
    new SessionOutputEventArgs("test", "Hello ", MessageRole.Assistant, OutputKind.Delta));
```

- `NullLogger.Instance` and `NullLogger<T>.Instance` are passed to all constructors
  that require `ILogger`

### CopilotFamily.UI.Tests

FlaUI-based UI automation tests that launch the actual application. Target framework:
`net8.0-windows`.

**Important:** These tests launch the app with `--test-mode --reset-state` which uses
mock services and clears any persisted session state so each test run starts clean.
No Copilot CLI installation is needed.

> **Note on visibility:** FlaUI requires a rendered window — UI tests will briefly show
> the app on your desktop. To reduce disruption, the `--minimized` flag is available
> but some FlaUI interactions (click, type) require a visible window. For CI, use a
> Windows agent with desktop experience.

**Important:** The app must be built before running UI tests:

```bash
dotnet build src/CopilotFamily.App
dotnet test test/CopilotFamily.UI.Tests
```

**Test classes:**

| Class | Tests | Coverage |
|---|---|---|
| `MainWindowTests` | 5 | App launch, window title, new session button, tab creation, tab close |
| `SessionTabTests` | 7 | Input box, send/abort/clear buttons, message list, type-and-send flow |

**Infrastructure — `UITestBase`:**

All UI tests inherit from `UITestBase` which provides:

- **App launch** — finds the built `.exe` automatically and launches with `--test-mode --reset-state`
- **Element lookup** — `FindById()`, `WaitForElement()`, `GetButton()`, `GetTextBox()`
- **Timeouts** — 5s for app startup, 3s default for element lookup with retry
- **Cleanup** — closes and disposes the app in `Dispose()`

**AutomationIds used in tests:**

| AutomationId | Element | Location |
|---|---|---|
| `NewSessionButton` | ＋ New Session button | MainWindow.xaml |
| `SessionTabControl` | Tab control | MainWindow.xaml |
| `CloseTabButton` | ✕ close button on each tab | MainWindow.xaml |
| `InputTextBox` | Message input text box | SessionTabView.xaml |
| `SendButton` | Send button | SessionTabView.xaml |
| `AbortButton` | Abort button | SessionTabView.xaml |
| `ClearButton` | Clear button | SessionTabView.xaml |
| `MessagesList` | Messages list box | SessionTabView.xaml |

**Test execution notes:**

- Tests use `[Collection("UI")]` to run sequentially (each test launches a new app instance)
- Each test class creates its own app process and disposes it after
- `SessionTabTests` creates a tab in the constructor before each test
- `Thread.Sleep` calls allow mock streaming responses to complete

## Startup Flags

The application supports several command-line flags useful for testing and development:

| Flag | Purpose |
|---|---|
| `--test-mode` | Uses mock services instead of the real Copilot SDK |
| `--reset-state` | Clears persisted session state on startup (clean slate) |
| `--minimized` | Starts the main window minimized |

**Examples:**

```bash
# Run with mock services and no saved state (testing)
CopilotFamily.App.exe --test-mode --reset-state

# Clear leftover sessions and start fresh (troubleshooting)
CopilotFamily.App.exe --reset-state

# All flags combined (UI test runner uses this)
CopilotFamily.App.exe --test-mode --reset-state
```

## Adding New Tests

### Adding a Unit Test

1. Create a test class in the appropriate project:
   - Business logic → `CopilotFamily.Core.Tests`
   - ViewModel or UI logic → `CopilotFamily.App.Tests`

2. Follow the existing patterns — mock interfaces with Moq, use `NullLogger`:

```csharp
public class MyNewFeatureTests
{
    [Fact]
    public async Task MyFeature_WhenCondition_ShouldBehaveCorrectly()
    {
        // Arrange
        var mock = new Mock<ISessionManager>();
        // ... setup

        // Act
        var result = await sut.DoSomething();

        // Assert
        Assert.Equal(expected, result);
    }
}
```

3. Run: `dotnet test --filter "MyNewFeatureTests"`

### Adding a UI Test

1. Add a new test class in `CopilotFamily.UI.Tests` inheriting from `UITestBase`:

```csharp
[Collection("UI")]
public class MyNewUITests : UITestBase
{
    [Fact]
    public void MyElement_Exists_And_IsInteractive()
    {
        // Use inherited helpers
        var button = GetButton("MyAutomationId");
        Assert.True(button.IsEnabled);
        button.Click();
        Thread.Sleep(500);
        // verify outcome...
    }
}
```

2. Add `AutomationProperties.AutomationId` to any new XAML elements:

```xml
<Button AutomationProperties.AutomationId="MyNewButton" Content="Click Me"/>
```

3. Build the app, then run: `dotnet test test/CopilotFamily.UI.Tests`

## Debugging Tests

### Using Logs in Test Analysis

Core and App services log via `ILogger<T>`. In unit tests, `NullLogger` discards
all log output. To capture logs during test debugging, replace `NullLogger` with
a real logger:

```csharp
// Quick console logging for debugging a failing test
using var factory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
var logger = factory.CreateLogger<SessionManager>();
var manager = new SessionManager(mockClientService.Object, logger);
```

### Using Logs from UI Tests

When UI tests fail, check the app's log file:

```
%LOCALAPPDATA%\CopilotFamily\logs\copilot-family-YYYYMMDD.log
```

The app logs all lifecycle events — session creation, input sends, errors, and disposal —
which helps diagnose why a UI interaction may have failed.

### Running Tests in Visual Studio

Open `CopilotFamily.slnx` in Visual Studio. Tests appear in Test Explorer and can be
run/debugged individually with breakpoints.

### CI/CD Considerations

- **Core and App tests** run on any Windows agent with .NET 8 SDK
- **UI tests** require a display (or virtual display). On CI, either:
  - Use a Windows agent with desktop experience
  - Skip UI tests with `dotnet test --filter "Category!=UI"`
  - Use a virtual display solution

## Test Dependencies

| Package | Version | Used By | Purpose |
|---|---|---|---|
| `xunit` | 2.7.0 | All | Test framework |
| `xunit.runner.visualstudio` | 2.5.7 | All | VS Test Explorer integration |
| `Microsoft.NET.Test.Sdk` | 17.9.0 | All | .NET test host |
| `Moq` | 4.20.70 | Core, App | Interface mocking |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.2 | Core, App | `NullLogger` for tests |
| `FlaUI.UIA3` | 4.0.0 | UI | WPF UI automation |
