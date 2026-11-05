using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

using Vera.Input.Coordinates;
using Vera.Input.Platform;

namespace Vera.Input.Harness;

/// <summary>
/// The menu shown when the harness starts without a command. It asks a few questions, then runs the same command you
/// could type (<c>info</c>, <c>screenshot</c>, <c>map</c>, <c>mouse</c>, <c>click</c>, <c>type</c>) with the answers as settings on top of appsettings.json.
/// </summary>
internal static class HarnessMenu
{
    private const string Screenshot = "Take a screenshot";
    private const string Map = "Map a point (screenshot → screen)";
    private const string Mouse = "Where's my mouse? (live)";
    private const string Click = "Click a point (moves the real mouse)";
    private const string Type = "Type text (uses the real keyboard)";
    private const string Info = "Show display info and settings";
    private const string Quit = "Quit";

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("[bold]Vera.Input.Harness[/]: what do you want to test?")
                .AddChoices(Screenshot, Map, Mouse, Click, Type, Info, Quit));

            switch (choice)
            {
                case Screenshot:
                    await TakeScreenshotAsync();
                    break;
                case Map:
                    await MapPointAsync();
                    break;
                case Mouse:
                    await ShowMouseAsync();
                    break;
                case Click:
                    await ClickPointAsync();
                    break;
                case Type:
                    await TypeTextAsync();
                    break;
                case Info:
                    await HarnessApp.RunAsync(["info"]);
                    break;
                default:
                    return 0;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule());
        }
    }

    private static async Task TakeScreenshotAsync()
    {
        var overrides = new Dictionary<string, string?>();
        var monitor = AskMonitor(overrides);
        var scale = AskScale(monitor.Bounds);

        var delay = AnsiConsole.Prompt(new TextPrompt<int>("Wait how many seconds first?")
            .DefaultValue(0)
            .Validate(seconds => seconds >= 0 ? ValidationResult.Success() : ValidationResult.Error("Can't be negative.")));

        AnsiConsole.WriteLine();
        await HarnessApp.RunAsync(
            ["screenshot", "--delay", delay.ToString(CultureInfo.InvariantCulture), "--scale", scale.ToString(CultureInfo.InvariantCulture)],
            overrides);
    }

    private static async Task MapPointAsync()
    {
        var overrides = new Dictionary<string, string?>();
        var monitor = AskMonitor(overrides);
        var scale = AskScale(monitor.Bounds);
        var (width, height) = FrameGeometry.ScaledSize(monitor.Bounds.Width, monitor.Bounds.Height, scale);

        var x = AskCoordinate($"X in the screenshot (0–{width - 1}):", width);
        var y = AskCoordinate($"Y in the screenshot (0–{height - 1}):", height);

        AnsiConsole.WriteLine();
        await HarnessApp.RunAsync(
            ["map", x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture), "--scale", scale.ToString(CultureInfo.InvariantCulture)],
            overrides);
    }

    private static async Task ClickPointAsync()
    {
        var overrides = new Dictionary<string, string?>();
        var monitor = AskMonitor(overrides);
        var scale = AskScale(monitor.Bounds);
        var (width, height) = FrameGeometry.ScaledSize(monitor.Bounds.Width, monitor.Bounds.Height, scale);

        var x = AskCoordinate($"X in the screenshot (0–{width - 1}):", width);
        var y = AskCoordinate($"Y in the screenshot (0–{height - 1}):", height);
        var button = AnsiConsole.Prompt(new SelectionPrompt<MouseButton>()
            .Title("Which button?")
            .AddChoices(MouseButton.Left, MouseButton.Right, MouseButton.Middle));
        var clicks = AnsiConsole.Prompt(new SelectionPrompt<int>()
            .Title("How many clicks?")
            .UseConverter(c => c switch { 1 => "1 (single)", 2 => "2 (double)", _ => "3 (triple)" })
            .AddChoices(1, 2, 3));
        var delay = AnsiConsole.Prompt(new TextPrompt<int>("Wait how many seconds first, to bring the target window to the front?")
            .DefaultValue(3)
            .Validate(seconds => seconds >= 0 ? ValidationResult.Success() : ValidationResult.Error("Can't be negative.")));

        AnsiConsole.WriteLine();
        await HarnessApp.RunAsync(
            [
                "click", x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture),
                "--button", button.ToString(), "--clicks", clicks.ToString(CultureInfo.InvariantCulture),
                "--scale", scale.ToString(CultureInfo.InvariantCulture), "--delay", delay.ToString(CultureInfo.InvariantCulture),
            ],
            overrides);
    }

    private static async Task TypeTextAsync()
    {
        var text = AnsiConsole.Prompt(new TextPrompt<string>("Text to type (Enter = the test line):")
            .AllowEmpty());
        var delay = AnsiConsole.Prompt(new TextPrompt<int>("Wait how many seconds first, to click into the target window?")
            .DefaultValue(3)
            .Validate(seconds => seconds >= 0 ? ValidationResult.Success() : ValidationResult.Error("Can't be negative.")));

        AnsiConsole.WriteLine();
        string[] args = text.Length == 0
            ? ["type", "--delay", delay.ToString(CultureInfo.InvariantCulture)]
            : ["type", text, "--delay", delay.ToString(CultureInfo.InvariantCulture)];
        await HarnessApp.RunAsync(args);
    }

    private static async Task ShowMouseAsync()
    {
        var overrides = new Dictionary<string, string?>();
        AskMonitor(overrides);

        AnsiConsole.WriteLine();
        await HarnessApp.RunAsync(["mouse"], overrides);
    }

    /// <summary>Asks which monitor to use when there's more than one, and sets it as the capture monitor.</summary>
    private static MonitorInfo AskMonitor(Dictionary<string, string?> overrides)
    {
        var monitors = ReadMonitors();
        if (monitors.Count == 1)
        {
            return monitors[0];
        }

        var monitor = AnsiConsole.Prompt(new SelectionPrompt<MonitorInfo>()
            .Title("Which monitor?")
            .UseConverter(Describe)
            .AddChoices(monitors.OrderByDescending(m => m.IsPrimary)));
        overrides["VeraInput:Display:MonitorIndex"] = monitor.Index.ToString(CultureInfo.InvariantCulture);
        return monitor;
    }

    private static int AskCoordinate(string question, int size) =>
        AnsiConsole.Prompt(new TextPrompt<int>(question)
            .DefaultValue(size / 2)
            .Validate(v => v >= 0 && v < size ? ValidationResult.Success() : ValidationResult.Error($"Must be between 0 and {size - 1}.")));

    /// <summary>Asks for the image size. The presets show the resulting size for the chosen monitor.</summary>
    private static double AskScale(ScreenRect bounds)
    {
        const double other = 0;
        var scale = AnsiConsole.Prompt(new SelectionPrompt<double>()
            .Title("What size?")
            .UseConverter(s => s == other
                ? "Other…"
                : $"{s * 100:0}% ({Size(bounds, s)})")
            .AddChoices(1.0, 0.75, 0.5, other));

        return scale != other ? scale : AnsiConsole.Prompt(new TextPrompt<double>("Scale, e.g. 0.6:")
            .Validate(s => s is > 0 and <= 1 ? ValidationResult.Success() : ValidationResult.Error("Must be greater than 0 and at most 1.")));
    }

    /// <summary>Lists the monitors without starting the host, so a bad appsettings.json shows up later, in the command itself.</summary>
    private static List<MonitorInfo> ReadMonitors()
    {
        using var services = HarnessApp.CreateBuilder().Services.BuildServiceProvider();
        return [.. services.GetRequiredService<IDisplayInfo>().Read().Monitors];
    }

    private static string Size(ScreenRect bounds, double scale)
    {
        var (width, height) = FrameGeometry.ScaledSize(bounds.Width, bounds.Height, scale);
        return $"{width}×{height}";
    }

    private static string Describe(MonitorInfo m) =>
        Markup.Escape($"Monitor {m.Index}: {m.Bounds}, {m.ScalePercent}%") + (m.IsPrimary ? " [green](primary)[/]" : "");
}
