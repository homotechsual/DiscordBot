# GitHub Copilot Instructions for HomotechsualBot

Essential guidelines for AI code generation on this project.

## Command Formatting

When providing terminal commands to users, always wrap commands in fenced code blocks.

## CRITICAL: Version Management

Never manually edit `src/HomotechsualBot/HomotechsualBot.csproj` version or `CHANGELOG.md` version entries.

Run the VersionManager workflow when changes affect shipped bot runtime behavior (for example, code under `src/HomotechsualBot/`, runtime configuration behavior, commands, services, or user-visible functionality).

Do not require a version bump for workflow/CI-only, deployment-only, docs-only, tests-only, or tooling-only changes that do not change bot runtime behavior.

When a version bump is required, use the VersionManager tool. Build it before use:

```bash
# Step 1: Build VersionManager as Release
dotnet build tools/VersionManager/VersionManager.csproj -c Release

# Step 2 (optional): Check if version bump is needed based on git commits
dotnet artifacts/bin/VersionManager/release/VersionManager.dll check-commits

# Step 3: Bump version in both csproj and changelog
dotnet artifacts/bin/VersionManager/release/VersionManager.dll bump --version X.Y.Z --type patch --message "Your description"

# Step 4: Validate consistency
dotnet artifacts/bin/VersionManager/release/VersionManager.dll validate

# Step 5: Build main project
dotnet build
```

Build validation enforces version consistency. If `HomotechsualBot.csproj` and `CHANGELOG.md` versions differ, build fails.

## Commit Message Format

Use Conventional Commits:

```text
type(scope): description
```

Types: `feat`, `fix`, `refactor`, `chore`, `docs`

Example: `feat(status): add richer /about command details`

## Build Verification

After any code changes:

```bash
dotnet build
```

## Logging

Use structured logging:

```csharp
_logger.LogInformation("Operation started for {UserId}", userId);
_logger.LogError(ex, "Failed request to {Url}", apiUrl);
```

Critical startup phases should log entry and exit.

## Quick Checklist Before Commit

* \[ ] Code compiles with `dotnet build`
* \[ ] Version bumped with VersionManager (required only for runtime behavior changes)
* \[ ] `CHANGELOG.md` updated via VersionManager (required only for runtime behavior changes)
* \[ ] Conventional Commit message used
* \[ ] No manual version edits

