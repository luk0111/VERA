using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

using Vera.Input.Platform;

namespace Vera.Input.Harness;

/// <summary>
/// The menu shown when the harness starts without a command. It asks a few questions, then runs the same command you
/// could type (<c>info</c>, <c>screenshot</c>) with the answers as settings on top of appsettings.json.
/// </summary>
internal static class HarnessMenu
{
    private const string Screenshot = "Take a screenshot";
    private const string Info = "Show display info and settings";
    private const string Quit = "Quit";

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("[bold]Vera.Input.Harness[/]: what do you want to test?")
                .AddChoices(Screenshot, Info, Quit));

            switch (choice)
            {
                case Screenshot:
                    await TakeScreenshotAsync();
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

        var monitors = ReadMonitors();
        if (monitors.Count > 1)
        {
            var monitor = AnsiConsole.Prompt(new SelectionPrompt<MonitorInfo>()
                .Title("Which monitor?")
                .UseConverter(Describe)
                .AddChoices(monitors.OrderByDescending(m => m.IsPrimary)));
            overrides["VeraInput:Display:CaptureTarget"] = "MonitorIndex";
            overrides["VeraInput:Display:MonitorIndex"] = monitor.Index.ToString(CultureInfo.InvariantCulture);
        }

        var checkRdp = AnsiConsole.Confirm("Check the RDP window first? (say no if you aren't connected over RDP)", defaultValue: false);
        overrides["VeraInput:Rdp:Enabled"] = checkRdp ? "true" : "false";

        var delay = AnsiConsole.Prompt(new TextPrompt<int>("Wait how many seconds first?")
            .DefaultValue(checkRdp ? 3 : 0)
            .Validate(seconds => seconds >= 0 ? ValidationResult.Success() : ValidationResult.Error("Can't be negative.")));

        AnsiConsole.WriteLine();
        await HarnessApp.RunAsync(["screenshot", "--delay", delay.ToString(CultureInfo.InvariantCulture)], overrides);
    }

    /// <summary>Lists the monitors without starting the host, so a bad appsettings.json shows up later, in the command itself.</summary>
    private static List<MonitorInfo> ReadMonitors()
    {
        using var services = HarnessApp.CreateBuilder().Services.BuildServiceProvider();
        return [.. services.GetRequiredService<IDisplayInfo>().Read().Monitors];
    }

    private static string Describe(MonitorInfo m) =>
        Markup.Escape($"Monitor {m.Index}: {m.Bounds}, {m.ScalePercent}%") + (m.IsPrimary ? " [green](primary)[/]" : "");
}
