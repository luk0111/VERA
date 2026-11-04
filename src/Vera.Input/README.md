# Vera.Input

This part of VERA is its eyes and hands. It looks at the screen by taking screenshots, and it will soon use the mouse and keyboard the way a person does.

It only works with what's visible on the screen, so it doesn't need to know how the app being tested was built.

## What works today

| Feature | Ready? |
|---|---|
| Take a screenshot, at full size or smaller | Yes |
| Take a screenshot of one area, such as an app's window | Yes |
| Work out where a point in a screenshot is on the real screen | Yes |
| Move the mouse and click | Not yet |
| Type text | Not yet |
| Press key combinations, such as Ctrl+S | Not yet |
| Scroll, drag and hover | Not yet |

So far, this has only been tested with the app on the same PC. It hasn't been tested through remote desktop tools such as RDP or AnyDesk.

## Try it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Open a terminal in the main VERA folder and run:

```powershell
dotnet run --project tools/Vera.Input.Harness
```

A small menu opens where you can take a screenshot or check the screen setup. Screenshots are saved in `runs/manual/`, and details of each run go in `logs/`.

## If something goes wrong

VERA stops and shows a message explaining the problem. The most common causes are:

- **The PC is locked or asleep:** unlock it and try again.
- **The screenshot is completely black:** the screen is probably off, asleep or disconnected.
- **A setting is wrong:** the message names the setting. Settings are in `appsettings.json`.

## For developers

- **Writing agent code or tests:** use `Vera.Input.Abstractions`. It has no Windows-specific code.
- **Testing without a real screen:** use `FakeScreen`. It plays back saved PNG images and records every action.
- **Starting VERA in a program:** only the program that starts VERA uses `Vera.Input`. Call `AddVeraInput` when setting up the program, and copy `app.manifest` from `tools/Vera.Input.Harness`.
- **Coordinates:** a position always means a pixel in the most recent screenshot.
- **Features not built yet:** calling them on the real screen throws `NotImplementedException`. `FakeScreen` accepts them, so tests can already use them.
