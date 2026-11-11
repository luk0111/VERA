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
| `Vera.Adapters.Desktop` | Reads a running window's elements with UI Automation (FlaUI), numbers them, and takes its screenshot through `Vera.Input`. Clicks, types into and presses keys in the window through `Vera.Input` |
| `Vera.Adapters.Mac` | The macOS version of the desktop adapter: lists running apps, reads a window's elements with the Accessibility API, and takes screenshots with `screencapture`. It shares numbering with `Vera.Adapters.Desktop`. See [workflow.md](workflow.md#macos) |
| `Vera.Llm` | Connects to the model (Ollama, or Gemini's free tier) through `Microsoft.Extensions.AI`. Draws element numbers on the screenshot (Set-of-Marks) and asks the model for one action |
| `Vera.Secrets` | Stores secrets such as API keys in Windows Credential Manager |
| `Vera.Reporting` | Draws a red box around an element on a screenshot |
| `Vera.Application` | Connects the layers for the UI: chat about the selected app, find an element in a running app. When a chat starts, it reads the app's window once and tells the model the app's name, window title, system and up to 40 named elements, as JSON in an `<app>` block. `/refresh` reads the window again |
| `Vera.Cli` | The console app: start testing (chat), find an element, settings |
| Everything else | Empty project so far: `Vera.Agent`, `Vera.Replay`, `Vera.Adapters.Web`, `Vera.Adapters.Remote`, `Vera.RemoteAgent`, `Vera.Protos`, `Vera.Storage` |
