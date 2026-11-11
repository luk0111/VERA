# Input layer

How VERA sees and controls the app under test. It only works with pixels on the VERA machine's screen, so the app can be on the same PC or behind a remote tool. Guide with the harness and EchoApp: `src/Vera.Input/README.md`.

## Rules

- Agent and adapter code reference `Vera.Input.Abstractions` only. Only the wiring code references `Vera.Input`: `Vera.Application` (it calls `AddVeraInput`) and the tools.
- Coordinates are pixels in the most recent screenshot. Out-of-range values throw and are never moved to the edge.
- `ScreenUnavailableException` means stop the run. Retrying won't help.
- Key combos are written like `ctrl+s` or `shift+tab` (any case). The key names are in `src/Vera.Input/Injection/KeyCombo.cs`. `win+l` and `ctrl+alt+delete` are refused.
- Methods not built yet throw `NotImplementedException`, naming the milestone.
- An exe that uses `Vera.Input` needs a Per-Monitor V2 DPI manifest, a `VeraInput` config section and logging.
- Processes started by Claude Code can't send mouse or keyboard input (`SendInput` fails). A person runs those tests from their own terminal.
- Tests in `tests/Vera.Input.Tests` use `FakeScreen` or a recording sink. Tests that move the real mouse are marked `Integration`.
