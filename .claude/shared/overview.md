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
| `Vera.Input.Abstractions`, `Vera.Input` | Screenshots (whole monitor or one area), coordinate mapping, mouse move and click. See [input.md](input.md) |
| `Vera.Adapters.Desktop` | Reads a running window's elements with UI Automation (FlaUI), numbers them, and takes its screenshot through `Vera.Input` |
| `Vera.Llm` | Connects to the model (Ollama for now) through `Microsoft.Extensions.AI`. Draws element numbers on the screenshot (Set-of-Marks) and asks the model for one action |
| `Vera.Reporting` | Draws a red box around an element on a screenshot |
| `Vera.Application` | Connects the layers for the UI: chat about the selected app, find an element in a running app |
| `Vera.Cli` | The console app: start testing (chat), find an element, settings |
| Everything else | Empty project so far: `Vera.Agent`, `Vera.Replay`, `Vera.Adapters.Web`, `Vera.Adapters.Remote`, `Vera.RemoteAgent`, `Vera.Protos`, `Vera.Storage`, `Vera.Secrets` |
