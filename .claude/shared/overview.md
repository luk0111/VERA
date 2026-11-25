# Overview

VERA (Verified Execution & Replay Agent) tests Windows and web apps. A tester describes a check in plain language, an AI agent carries it out on the real app, and VERA reports a verdict, steps to reproduce, screenshots and a video. Failed tests are replayed three times without the AI, to separate real bugs from agent mistakes.

Stack: C# on .NET 10, Spectre.Console, SQLite. The model can be local, in-house or a hosted API.

## Folders

| Folder | Contents |
|---|---|
| `src/` | The product, one project per layer |
| `tests/` | Test projects, one per layer |
| `tools/` | Manual tools for the input layer (harness and EchoApp) |

## Layers

| Project | What it does now |
|---|---|
| `Vera.Core` | Shared types: test task, verdict, agent action and step, UI element, screen state, `IAppAdapter`, `ILlmClient`, `{{secret:name}}` placeholders |
| `Vera.Input.Abstractions`, `Vera.Input` | Screenshots (whole monitor or one area), coordinate mapping, mouse move and click, typing text, key combos. See [input.md](input.md) |
| `Vera.Adapters.Desktop` | Reads a running window's elements with UI Automation (FlaUI), numbers them, and takes its screenshot through `Vera.Input`. Clicks, types into and presses keys in the window through `Vera.Input`. When a process has several windows (a browser), it uses the one with the picked title |
| `Vera.Adapters.Mac` | The macOS version of the desktop adapter: lists running apps, reads a window's elements with the Accessibility API, takes screenshots with `screencapture`, and clicks and types with CoreGraphics events (`MacScreen`). It shares numbering with `Vera.Adapters.Desktop`. See [workflow.md](workflow.md#macos) |
| `Vera.Llm` | Connects to the model (Ollama, or Gemini's free tier) through `Microsoft.Extensions.AI`. Draws element numbers on the screenshot (Set-of-Marks) and asks the model for one action |
| `Vera.Files` | Reads text files and the text layer of PDFs from disk (`IFileReader`, PdfPig): refuses secret files, cuts long text, reads PDF pages. Knows nothing about models or the chat |
| `Vera.Secrets` | Stores secrets such as API keys in Windows Credential Manager |
| `Vera.Reporting` | Draws a red box around an element on a screenshot |
| `Vera.Application` | Connects the layers for the UI: chat about the selected app (`/file` attaches a file to it), find an element in a running app and click it or type into it on request (it reads the window again and acts on the same element there). Run a chain of steps (`StepParser`, `StepRunner`): the command decides the action (click, type, press, expect), and the model finds each element as Find does. `scroll down until the orange cat` scrolls the window and looks again, up to 10 times, so it can ask the model up to 11 times. `expect no delete button` checks that something is gone. Before a risky click, drop or Delete key, VERA brings its own window to the front to ask (`IFrontWindow`), then reads the app again; a menu that closed meanwhile is opened once more. After every window read, Find, action and run, VERA's window gets the front back, so the tester never has to click back into it. When a chat starts, it reads the app's window once and tells the model the app's name, window title, system and its named elements (40 with the local model, half of its `MaxElements`; 250 with Gemini, all of it), as JSON in an `<app>` block. `/refresh` reads the window again. When the tester wants something tested, the model proposes steps in a `<steps>` block (`StepProposal`): every line must be a step `StepParser` knows, and nothing runs until the tester chooses Run. How a run went goes back to the model in a `<run>` block |
| `Vera.Cli` | The console app: start testing (one chat per app: ask about it, run, edit or save the steps the model proposes; `find the search button` shows the element, then adds it as a step or clicks or types into it at once; `/steps` writes, records, loads and runs steps, saved as text files in `steps/`; Esc stops a run; a live line shows what the running step is doing; unsaved steps of the tester's own are asked about before they are lost, `StepChain`), settings (including saving secrets for typing) |
| Everything else | Empty project so far: `Vera.Agent`, `Vera.Replay`, `Vera.Adapters.Web`, `Vera.Adapters.Remote`, `Vera.RemoteAgent`, `Vera.Protos`, `Vera.Storage` |
