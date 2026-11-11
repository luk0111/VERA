# Input layer

How VERA sees and controls the app under test. It only works with pixels on the VERA machine's screen, so the app can be on the same PC or behind a remote tool. Guide with the harness and EchoApp: `src/Vera.Input/README.md`.

## Rules

- Agent and adapter code reference `Vera.Input.Abstractions` only. Only the wiring code references `Vera.Input`: `Vera.Application` (it calls `AddVeraInput`) and the tools.
- Coordinates are pixels in the most recent screenshot. Out-of-range values throw and are never moved to the edge.
- `ScreenUnavailableException` means stop the run. Retrying won't help.
- Key combos are written like `ctrl+s` or `shift+tab` (any case). The key names are in `src/Vera.Input/Injection/KeyCombo.cs`. `win+l` and `ctrl+alt+delete` are refused.
- Methods not built yet throw `NotImplementedException`, naming the milestone.
- `DesktopAdapter` acts through `IScreen`. It clicks the centre of an element's visible part, mapped with its latest window screenshot, so read the screen right before acting. If the window moved since then, it refuses. Typing clicks the field, presses Ctrl+A, then types, and the text is logged as sensitive (length only).
- An exe that uses `Vera.Input` needs a Per-Monitor V2 DPI manifest, a `VeraInput` config section and logging. Test projects that read windows need the manifest too, or window bounds come out scaled until the first screenshot.
- Processes started by Claude Code may be unable to send mouse or keyboard input (`SendInput` fails with error 87). Then a person runs those tests from their own terminal.
- Tests in `tests/Vera.Input.Tests` use `FakeScreen` or a recording sink. Tests that move the real mouse are marked `Integration`.
