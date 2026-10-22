# VERA

VERA is an application for testing Windows and web apps. You describe the check in plain language, and an AI agent inside VERA carries it out on the real app.

You write the task the way you'd explain it to a colleague, for example "Log in with a wrong password, an error message should appear". No code and no selectors. The agent reads the app's screen, works through the steps and checks whether the expected result shows up.

When it's done, you get a verdict, the shortest steps to reproduce the problem, a screenshot for every step and a video of the run. If a test fails, VERA replays the steps three times without the AI, so you can tell a real bug in the app from a mistake made by the agent.

## Built with

VERA is written in C# on .NET 10. For now it runs as a console app built with Spectre.Console. It drives desktop apps through Windows UI Automation (FlaUI) and web apps through Playwright, and stores its results in SQLite.

The AI model isn't fixed. It can be a model running locally, one on an in-house server, or a hosted API.

## Project layout

The code lives in `src/`, split into layers: `Core`, `Agent`, `Llm`, `Replay`, the `Adapters` for desktop, web and remote apps, `Storage`, `Secrets`, `Reporting`, `Application` and the `Cli`. Each layer gets its own test project in `tests/`.

## Getting started

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Then build, run and test from the repo root:

```powershell
dotnet build Vera.slnx
dotnet run --project src/Vera.Cli
dotnet test --solution Vera.slnx
```
