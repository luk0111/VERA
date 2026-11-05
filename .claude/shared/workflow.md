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
- Windows-only projects target `$(VeraWindowsTfm)`, all others `$(VeraTfm)`.
- Add a setting only together with the feature that uses it.
