# Feature: Distribution Build

Builds the application into a `dist/` folder at the repository root for quick
iteration. The developer runs the app directly from `dist/` while using Copilot
CLI to iterate on the source code.

## Research Notes

- **dotnet publish**: Use `dotnet publish -c Release -o dist/` for framework-dependent
  deployment. `--self-contained false` keeps the output small.
- **VS Code tasks**: `publish-dist` publishes to `dist/`, `stage-update` publishes
  to `dist/staging/` for hot restart testing.
- **Updater script**: Embedded as a resource in the App project. Extracted to a temp
  directory at runtime before launching. Verified by integration tests.

## Background

```gherkin
Given the solution is at the repository root
And the dist folder is at "dist/"
And the staging folder is at "dist/staging/"
```

## Scenarios

### Scenario: Build publishes to dist folder

```gherkin
Given the solution builds successfully
When I run the "publish-dist" VS Code task
Then the application is published to "dist/"
And the executable "dist/CopilotNexus.App.exe" exists
And all runtime dependencies are in "dist/"
And the "dist/staging/" folder exists but is empty
```

### Scenario: Dist folder is gitignored

```gherkin
Given the .gitignore file exists
When I check for "dist/" in .gitignore
Then "dist/" is listed as an ignored pattern
```

### Scenario: Rebuild updates dist folder

```gherkin
Given a previous build exists in "dist/"
When I run the "publish-dist" task again
Then the files in "dist/" are replaced with the new build
And the "dist/staging/" folder is not modified
```

### Scenario: Stage-update publishes to staging folder

```gherkin
Given the application is running from "dist/"
When I run the "stage-update" VS Code task
Then the new build is published to "dist/staging/"
And the existing dist files are not modified
```

## Integration Test Coverage

The following scenarios are verified by automated integration tests
in `test/CopilotNexus.Core.Tests/Integration/DistStagingUpdateTests.cs`:

### Scenario: Updater script copies staged files to dist

```gherkin
Given "dist/" contains v1 files
And "dist/staging/" contains v2 files
When the updater script runs
Then all v2 files replace v1 files in "dist/"
And new files from staging are added to "dist/"
And the updater log confirms success
```

### Scenario: Updater script clears staging after copy

```gherkin
Given the updater has copied staged files
When the copy completes successfully
Then "dist/staging/" is empty
```

### Scenario: Updater script handles subdirectories

```gherkin
Given "dist/staging/" contains files in subdirectories (e.g., "runtimes/win-x64/")
When the updater script runs
Then the subdirectory structure is preserved in "dist/"
```

### Scenario: Updater script skips when staging is empty

```gherkin
Given "dist/staging/" is empty
When the updater script runs
Then no files are modified in "dist/"
And a warning is logged
```

### Scenario: Updater script times out when app doesn't exit

```gherkin
Given the application process has not exited
When the updater waits for 30 seconds (2 seconds in tests)
Then the updater logs an error
And exits with a non-zero exit code
And no files are copied
```

### Scenario: End-to-end publish then stage update

```gherkin
Given an initial publish exists in "dist/" with 4 files
When a new build is staged with 5 files (including a new file)
And the updater copies them
Then all existing files are updated to v2
And the new file is added
```
