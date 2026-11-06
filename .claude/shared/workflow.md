# Workflow

## Commands

Run from the repo root.

```powershell
dotnet build Vera.slnx
dotnet run --project src/Vera.Cli
dotnet test --solution Vera.slnx
dotnet test --solution Vera.slnx -- --filter-not-trait "Category=Integration"
dotnet run --project tools/Vera.Input.Harness
```

The fourth command is the set CI runs: it skips tests marked `[Trait("Category", "Integration")]`, which need a real desktop, app or model.

## Branches

- Flow: working branch → `internal` → `beta` → `main` (releases only). Hotfixes: `fix/*` → `main`. CI rejects PRs that break this order.
- Create `feat/<topic>` branches from `origin/internal`.

## Code rules

- Warnings are errors, and nullable is on.
- Package versions go in `Directory.Packages.props`, never in a `.csproj`.
- Windows-only projects target `$(VeraWindowsTfm)`, all others `$(VeraTfm)`. `Vera.Cli`, `Vera.Application` and `Vera.Application.Tests` target `$(VeraHostTfm)`, which is the Windows target on Windows and `$(VeraTfm)` elsewhere. Their Windows-only code goes inside `#if WINDOWS`.

## macOS

`Vera.Cli` runs on macOS, where chat and finding an element work. Mouse and keyboard control, `Vera.Input` and the tools are Windows-only.

- Build and run with the same commands. Install the .NET 10 SDK and Ollama first.
- Reading a window needs two permissions, in System Settings > Privacy & Security: Accessibility, and Screen & System Audio Recording. Turn them on for the app VERA is started from (Terminal or Rider), then restart that app.
- `dotnet test` reports errors for `Vera.Input.Tests` and `Vera.Adapters.Desktop.Tests`, which can only start on Windows. The tests in the other projects still run.
- Add a setting only together with the feature that uses it.
