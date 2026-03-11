# Feature: Hot Restart with Staged Updates

The running application detects when a new build has been staged and offers the
user a restart. On acceptance, the app saves state, exits, an updater replaces
the running files with the staged version, and the app relaunches with state
restored.

## Research Notes

- **WPF process lifecycle**: The app must fully exit before files can be replaced.
  The Copilot CLI child process (spawned by the SDK) must also be terminated,
  otherwise `copilot.exe` will hold file locks.
- **Updater must live outside the install dir**: `CopilotFamily.Updater.exe` is a
  standalone console app that runs independently of the main app process. It is
  published alongside the app but does not overwrite itself during updates.
- **FileSystemWatcher reliability**: On Windows, FSW can miss events under heavy
  I/O. Use a combination of FSW for responsiveness and a periodic timer (30s)
  as a fallback.
- **Atomic update**: Copy to a temp dir first, then swap, to avoid partial updates
  on failure. However, for simplicity in dev iteration, direct copy with retry
  is acceptable — this is a developer tool, not a production installer.

## Background

```gherkin
Given the application is running from "dist/CopilotFamily.App.exe"
And a staging folder exists at "dist/staging/"
And the updater script is embedded as a resource and extracted to a temp path at runtime
```

## Scenarios

### Scenario: New build is staged for update

```gherkin
Given the application is running
When a new build is published to "dist/staging/"
Then the application detects the staged update within 5 seconds
And a notification bar appears at the top of the window: "A new version is available."
And the notification has "Restart Now" and "Later" buttons
```

### Scenario: User accepts the restart

```gherkin
Given a staged update notification is showing
When the user clicks "Restart Now"
Then the application disconnects all SDK sessions (preserving state on disk)
And the application saves tab metadata to the state file
And the application extracts the updater script to a temp directory
And the application launches the updater script with arguments: app PID, dist path, staging path, app exe path
And the application exits (including terminating the Copilot CLI child process)
And the updater waits for the application process to exit (up to 30 seconds)
And the updater copies all files from "dist/staging/" to "dist/"
And the updater clears the "dist/staging/" folder
And the updater launches "dist/CopilotFamily.App.exe"
And the updater exits
And the new application instance starts and resumes SDK sessions via ResumeSessionAsync
And all tabs and conversation history are fully restored
```

### Scenario: User defers the restart

```gherkin
Given a staged update notification is showing
When the user clicks "Later"
Then the notification is dismissed
And the staged files remain in "dist/staging/"
And the notification reappears after 5 minutes
```

### Scenario: Updater handles file locking

```gherkin
Given the application has exited
When the updater attempts to copy staged files
And some files are temporarily locked by the OS
Then the updater retries the copy up to 10 times with 1-second delays
And logs each retry attempt
And reports success when all files are copied
```

### Scenario: Updater times out waiting for app to exit

```gherkin
Given the updater is waiting for the application to exit
When the application has not exited after 30 seconds
Then the updater logs an error
And the updater does not copy any files
And the updater exits with a non-zero exit code
```

### Scenario: Updater handles missing staging files gracefully

```gherkin
Given the application has exited
When the updater runs but "dist/staging/" is empty
Then the updater logs a warning
And the updater launches the application from "dist/" without copying
```

### Scenario: Staging via VS Code task

```gherkin
Given the solution builds successfully
When I run the "stage-update" VS Code task
Then the new build is published to "dist/staging/"
And the running application detects the update
```

### Scenario: Multiple rapid stage attempts

```gherkin
Given a staged update notification is already showing
When a second build is published to "dist/staging/"
Then the notification remains visible (not duplicated)
And the staged files reflect the most recent build
```
