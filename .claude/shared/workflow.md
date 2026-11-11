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

The fourth command is the set CI runs: it skips tests marked `[Trait("Category", "Integration")]`, which need a real desktop, app or model. Locally it ends with "Zero tests ran" (exit code 8) because `Vera.Agent.Tests` is still empty. CI ignores that exit code.

Close any running VERA before building. Otherwise the build fails with "The file is locked by: Vera.Cli".

## Local settings

`src/Vera.Cli/appsettings.json` is shared. To change settings only for yourself, such as using another Ollama model, create `src/Vera.Cli/appsettings.Local.json` next to it. Git ignores it, and its values override the shared file. For example, to add a profile and make it your default:

```json
{ "Llm": { "DefaultProfile": "local-4b", "Profiles": { "local-4b": {
  "Provider": "Ollama", "DisplayName": "Local 4B", "Endpoint": "http://127.0.0.1:11434",
  "Model": "qwen3-vl:4b", "ContextSize": 8192, "Think": false } } } }
```

Rebuild after creating or changing it, because it is copied to the build output. If you delete it, also delete the copy in `bin/`.

### Hiding personal data from hosted models

A model whose `Endpoint` isn't on this computer (`localhost`, `127.0.0.1`, `::1`), such as Gemini, gets the app's text only after VERA hides emails, phone and card numbers, codes and what's typed in login fields. Password fields are never read, for any model. Screenshots are still sent as they are (#31). Words with no fixed shape, such as your name or usernames, go in `appsettings.Local.json`:

```json
{ "Privacy": { "HideWords": [ "Jane", "janed123" ] } }
```

## Branches

- Flow: working branch → `internal` → `beta` → `main` (releases only). Hotfixes: `fix/*` → `main`. CI rejects PRs that break this order.
- Create `feat/<topic>` branches from `origin/internal`.

## Code rules

- Warnings are errors, and nullable is on.
- Add a setting only together with the feature that uses it.
- Package versions go in `Directory.Packages.props`, never in a `.csproj`.
- Windows-only projects target `$(VeraWindowsTfm)`, all others `$(VeraTfm)`. `Vera.Cli`, `Vera.Application` and `Vera.Application.Tests` target `$(VeraHostTfm)`, which is the Windows target on Windows and `$(VeraTfm)` elsewhere. Their Windows-only code goes inside `#if WINDOWS`.

## macOS

`Vera.Cli` runs on macOS, where chat and finding an element work. Mouse and keyboard control, `Vera.Input` and the tools are Windows-only.

- Build and run with the same commands. Install the .NET 10 SDK and Ollama first.
- Reading a window needs two permissions, in System Settings > Privacy & Security: Accessibility, and Screen & System Audio Recording. Turn them on for the app VERA is started from (Terminal or Rider), then restart that app.
- `dotnet test` reports errors for `Vera.Input.Tests` and `Vera.Adapters.Desktop.Tests`, which can only start on Windows. The tests in the other projects still run.
