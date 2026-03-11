---
name: conventional-commit
description: 'Generate conventional commit messages and maintain a CHANGELOG.md. Enforces the Conventional Commits specification for all commits and ensures the changelog stays current with every feature, fix, or breaking change.'
---

### Instructions

Use the [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) specification
for all commit messages in this project.

### Workflow

**Follow these steps:**

1. Run `git status` to review changed files.
2. Run `git diff` or `git diff --cached` to inspect changes.
3. Stage your changes with `git add <file>`.
4. Construct your commit message using the structure below.
5. Execute the commit:

```bash
git commit -m "type(scope): description"
```

### Commit Message Structure

```
type(scope): description

[optional body — more detailed explanation]

[optional footer — BREAKING CHANGE: details, or issue references]
```

### Allowed Types

| Type       | When to Use                                   |
| ---------- | --------------------------------------------- |
| `feat`     | A new feature or user-facing enhancement      |
| `fix`      | A bug fix                                     |
| `docs`     | Documentation only changes                    |
| `style`    | Formatting, whitespace — no code logic change |
| `refactor` | Code restructuring without behaviour change   |
| `perf`     | Performance improvement                       |
| `test`     | Adding or updating tests                      |
| `build`    | Build system or dependency changes            |
| `ci`       | CI/CD configuration changes                   |
| `chore`    | Maintenance tasks (deps, configs)             |
| `revert`   | Reverting a previous commit                   |

### Scopes for This Project

Use one of these scopes when relevant:

- `core` — CopilotNexus.Core project (models, services, interfaces)
- `app` — CopilotNexus.App project (UI, ViewModels, XAML)
- `ui` — Visual/styling changes
- `tests` — Test projects
- `docs` — Documentation
- `ci` — GitHub Actions, build scripts

### Examples

```
feat(app): add session model selector dropdown
fix(core): handle null content in streaming delta events
docs: update architecture overview with logging section
refactor(app): extract tab creation into factory method
test(tests): add FlaUI UI automation tests
chore: update Serilog packages to latest
feat!: replace process-based sessions with SDK integration

BREAKING CHANGE: ICopilotProcess removed, use ICopilotSessionWrapper instead.
```

### Validation Rules

- **type** — Required. Must be one of the allowed types above.
- **scope** — Optional but recommended for clarity.
- **description** — Required. Use imperative mood ("add", not "added"). Under 72 characters.
- **body** — Optional. Use for additional context on complex changes.
- **footer** — Use `BREAKING CHANGE:` for breaking changes. Reference issues with `Fixes #123`.

### Changelog Rule

After every commit that includes a `feat`, `fix`, or breaking change, update
`CHANGELOG.md` in the project root:

1. Add the entry under the `[Unreleased]` section.
2. Group entries by type: `Added`, `Fixed`, `Changed`, `Removed`.
3. Each entry is a single line: `- Description of the change`.
4. When a release is cut, rename `[Unreleased]` to `[version] - YYYY-MM-DD` and
   add a fresh `[Unreleased]` section above it.
