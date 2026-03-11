# Installation & Operations Guide

This guide covers how to install, run, update, and manage Copilot Nexus — both the
**Nexus backend service** and the **desktop application**.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10/11
- [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli) installed and authenticated
- Repository cloned locally (e.g., `C:\repos\copilot-nexus`)

---

## Directory Layout

Everything installs under `%LOCALAPPDATA%\CopilotNexus\`:

```
%LOCALAPPDATA%\CopilotNexus\
├── nexus\              ← CLI + Service binaries (CopilotNexus.Cli.exe, CopilotNexus.Service.exe)
├── app\                ← Desktop app binaries
├── staging\            ← Pending updates (not inside install dirs)
│   ├── nexus\
│   └── app\
├── logs\               ← Shared log files
├── app-state.json      ← Saved tab/session state
└── nexus.lock          ← PID file for the running Nexus process
```

> **Important:** The `staging/` folder is a sibling of the install dirs — updates are
> staged here and then copied into `nexus/` or `app/` during an update cycle.

---

## 1. Build the Solution

From the repo root:

```powershell
# Via nexus CLI (recommended after install)
nexus build

# Or with a specific configuration
nexus build -c Debug

# Or directly with dotnet
dotnet build CopilotNexus.slnx
```

This builds all projects: Core, App, Nexus, and test projects. On success it
shows elapsed time and suggests `nexus publish` as the next step.

---

## 2. Install (First-Time Setup)

The `install` command builds both components and publishes them to the install directory:

```powershell
# From the repo root — run via dotnet
dotnet run --project src/CopilotNexus.Cli -- install
```

This will:

1. Create the `%LOCALAPPDATA%\CopilotNexus\` directory structure
2. Run `dotnet publish` for **Nexus** → `%LOCALAPPDATA%\CopilotNexus\nexus\`
3. Run `dotnet publish` for **App** → `%LOCALAPPDATA%\CopilotNexus\app\`

After installation, set up the `nexus` alias (see next section) so you can manage
everything from any terminal.

---

## 3. Set Up the `nexus` Alias

To avoid typing the full executable path every time, create a `nexus` alias.

### Option A: PowerShell alias (current session only)

```powershell
Set-Alias nexus "$env:LOCALAPPDATA\CopilotNexus\nexus\CopilotNexus.Cli.exe"
```

### Option B: PowerShell profile (persistent across sessions)

Add the alias to your PowerShell profile so it's available every time you open a terminal:

```powershell
# Open your profile in an editor (creates it if it doesn't exist)
if (!(Test-Path $PROFILE)) { New-Item -Path $PROFILE -Force }
notepad $PROFILE
```

Add this line:

```powershell
Set-Alias nexus "$env:LOCALAPPDATA\CopilotNexus\nexus\CopilotNexus.Cli.exe"
```

Save and reload:

```powershell
. $PROFILE
```

### Option C: Add to PATH (works in PowerShell, CMD, and other tools)

```powershell
# Add the Nexus install directory to your user PATH
$nexusDir = "$env:LOCALAPPDATA\CopilotNexus\nexus"
$currentPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($currentPath -notlike "*$nexusDir*") {
    [Environment]::SetEnvironmentVariable("PATH", "$currentPath;$nexusDir", "User")
}
```

> Restart your terminal after modifying PATH. The CLI executable is named
> `CopilotNexus.Cli.exe` — you can rename it to `nexus.exe` after install if preferred.

### Verify

```powershell
nexus --help
```

All examples below use the `nexus` alias. If you haven't set it up, substitute
`& "$env:LOCALAPPDATA\CopilotNexus\nexus\CopilotNexus.Cli.exe"` or
`dotnet run --project src/CopilotNexus.Cli --`.

---

## 4. Start the Nexus Service

```powershell
# Start as a background process (default — returns immediately)
nexus start

# Start on a custom port
nexus start --url http://localhost:6000

# Start in the current process (foreground — blocks until stopped)
nexus start --interactive
```

By default, `nexus start` launches Nexus as a **background process** and returns
immediately. It prints the PID and URL so you can verify it's running. If Nexus is
already running (detected via the lock file), it tells you instead of starting a second
instance.

Use `--interactive` when you want to see log output in the terminal or when debugging.

When Nexus starts, it:

- Writes its PID to `%LOCALAPPDATA%\CopilotNexus\nexus.lock`
- Initializes the Copilot SDK session manager
- Starts the SignalR hub and REST API
- Cleans up the lock file on shutdown

---

## 5. Check Nexus Status

```powershell
nexus status

# Query a specific URL
nexus status --url http://localhost:6000
```

Output shows:

- Lock file PID
- Service status (Running/Not responding)
- Active session count
- Available model count
- Whether updates are staged for Nexus or App

---

## 6. Stop the Nexus Service

```powershell
nexus stop
```

This reads the PID from `nexus.lock`, terminates the process, and removes the lock file.

---

## 7. Launch the Desktop App

```powershell
# Via Nexus CLI (recommended — finds the app automatically)
nexus winapp start

# With options
nexus winapp start --test-mode
nexus winapp start --nexus-url http://localhost:6000

# Or launch the app directly
& "$env:LOCALAPPDATA\CopilotNexus\app\CopilotNexus.App.exe"
& "$env:LOCALAPPDATA\CopilotNexus\app\CopilotNexus.App.exe" --nexus-url http://localhost:5280
& "$env:LOCALAPPDATA\CopilotNexus\app\CopilotNexus.App.exe" --test-mode
```

---

## 8. Typical Workflow (Start Everything)

```powershell
# 1. Start Nexus (backgrounds automatically)
nexus start

# 2. Check Nexus is running
nexus status

# 3. Launch the desktop app
nexus winapp start
```

Or during development (foreground mode in one terminal):

```powershell
# Terminal 1: Start Nexus interactively (see logs)
dotnet run --project src/CopilotNexus.Cli -- start --interactive

# Terminal 2: Launch app
dotnet run --project src/CopilotNexus.App
```

---

## 9. Updating

### Publish new builds to staging

After making code changes, `nexus publish` builds and stages the update:

```powershell
# Publish both Nexus and App to staging
nexus publish

# Publish only Nexus
nexus publish --component nexus

# Publish only the App
nexus publish --component app
```

> **Note:** `nexus publish` requires a prior `nexus install`. If no installation is
> detected it will tell you to run `nexus install` first.

`publish` outputs to the **staging** directory (`%LOCALAPPDATA%\CopilotNexus\staging\`),
not directly to the install directory. This keeps the running service untouched until
you explicitly apply the update.

### Apply staged updates

```powershell
# Apply all staged updates (stops Nexus if needed, copies, restarts)
nexus update

# Apply only Nexus
nexus update --component nexus

# Apply only App
nexus update --component app
```

### Desktop app auto-detection

The desktop app watches its staging directory automatically. When you run
`nexus publish --component app`, the app will show an "Update available" banner.
Click **Restart now** to apply the update.

### Quick update cycle

```powershell
# Build, stage, and apply in one go
nexus build && nexus publish && nexus update
```

Or for just the app (auto-detected):

```powershell
nexus build && nexus publish --component app
# → App shows update banner → click Restart
```

The `update` command:

1. Checks if files exist in the staging directory
2. Stops the running Nexus process (if updating Nexus)
3. Copies staged files to the install directory
4. Clears the staging directory
5. Restarts Nexus (if it was stopped)

---

## 10. CLI Command Reference

| Command | Description |
|---|---|
| `nexus start [--url URL] [--interactive]` | Start Nexus (background by default; `--interactive` for foreground) |
| `nexus stop` | Stop the running Nexus process |
| `nexus status [--url URL]` | Check if Nexus is running and show info |
| `nexus build [-c CONFIG]` | Build the solution from the repo (default: Release) |
| `nexus install` | Build and install both Nexus and App |
| `nexus publish [--component C]` | Build and stage an update (`nexus`, `app`, or `both`). Requires prior `nexus install`. |
| `nexus update [--component C]` | Apply staged update (`nexus`, `app`, or `both`) |
| `nexus winapp start [--nexus-url URL] [--test-mode]` | Launch the desktop app |
| `nexus --help` | Show help for all commands |

---

## 11. Troubleshooting

### Nexus won't start

```powershell
# Check if it's already running
nexus status

# Check for stale lock file
Get-Content "$env:LOCALAPPDATA\CopilotNexus\nexus.lock"

# Remove stale lock file if the PID is dead
Remove-Item "$env:LOCALAPPDATA\CopilotNexus\nexus.lock"
```

### App can't connect to Nexus

- Verify Nexus is running: `nexus status`
- Check the URL matches (default: `http://localhost:5280`)
- Check the health endpoint directly: `Invoke-RestMethod http://localhost:5280/health`

### Logs

All logs are written to `%LOCALAPPDATA%\CopilotNexus\logs\`:

```powershell
# View recent log entries
Get-Content "$env:LOCALAPPDATA\CopilotNexus\logs\*.log" -Tail 50
```

### Clean reinstall

```powershell
nexus stop
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\CopilotNexus"

# Reinstall from repo
dotnet run --project src/CopilotNexus.Cli -- install
```
