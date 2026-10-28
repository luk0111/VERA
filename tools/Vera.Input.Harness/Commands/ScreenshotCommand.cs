using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Vera.Input.Harness.Commands;

/// <summary>Takes a screenshot through <see cref="IScreenshotSource"/> and saves the PNG. Timings go to the log.</summary>
internal sealed class ScreenshotCommand(IScreenshotSource screen, TimeProvider timeProvider)
    : AsyncCommand<ScreenshotCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--out <PATH>")]
        [Description("Where to save the PNG. Default: runs/manual/yyyyMMdd-HHmmss.png")]
        public string? Out { get; init; }

        [CommandOption("--delay <SECONDS>")]
        [Description("Wait this long first, e.g. to click into the RDP window. Default: 0")]
        public int Delay { get; init; }

        public override ValidationResult Validate() =>
            Delay < 0 ? ValidationResult.Error("--delay can't be negative.") : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        for (var left = settings.Delay; left > 0; left--)
        {
            AnsiConsole.MarkupLine($"[grey]Taking the screenshot in {left}…[/]");
            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        var frame = await screen.ScreenshotAsync(ct: cancellationToken);
        var totalMs = stopwatch.ElapsedMilliseconds;

        var path = Path.GetFullPath(settings.Out ?? Path.Combine(
            "runs", "manual", frame.CapturedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, frame.Png, cancellationToken);

        AnsiConsole.MarkupLine($"Saved [green]{Markup.Escape(path)}[/]");
        AnsiConsole.MarkupLine(
            $"Frame {frame.Sequence}: {frame.Width}×{frame.Height} (screen {frame.ScreenWidth}×{frame.ScreenHeight}, scale {frame.Scale.ToString(CultureInfo.InvariantCulture)}), " +
            $"{frame.Png.Length / 1024} KB, {totalMs} ms");
        return 0;
    }
}