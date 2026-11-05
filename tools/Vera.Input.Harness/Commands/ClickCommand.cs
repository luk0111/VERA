using System.ComponentModel;
using System.Globalization;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input.Coordinates;
using Vera.Input.Platform;

namespace Vera.Input.Harness.Commands;

/// <summary>
/// Clicks a point in a screenshot, the way the agent will: take a screenshot, then click in its pixels. Afterwards it
/// checks that the cursor is on the screen pixel the point maps to.
/// </summary>
internal sealed class ClickCommand(IScreen screen, IDisplayCheck displayCheck, ICursorPosition cursor, TimeProvider timeProvider)
    : AsyncCommand<ClickCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<X>")]
        [Description("X in the screenshot, 0 = left edge")]
        public int X { get; init; }

        [CommandArgument(1, "<Y>")]
        [Description("Y in the screenshot, 0 = top edge")]
        public int Y { get; init; }

        [CommandOption("--button <BUTTON>")]
        [Description("left, right or middle. Default: left")]
        public MouseButton Button { get; init; } = MouseButton.Left;

        [CommandOption("--clicks <COUNT>")]
        [Description("1, 2 (double click) or 3. Default: 1")]
        public int Clicks { get; init; } = 1;

        [CommandOption("--scale <SCALE>")]
        [Description("The screenshot's scale, e.g. 0.5. Default: Capture.DefaultScale")]
        public double? Scale { get; init; }

        [CommandOption("--delay <SECONDS>")]
        [Description("Wait this long first, to bring the target window to the front. Default: 0")]
        public int Delay { get; init; }

        public override ValidationResult Validate() =>
            Clicks is < 1 or > 3 ? ValidationResult.Error("--clicks must be 1, 2 or 3.")
            : Delay < 0 ? ValidationResult.Error("--delay can't be negative.")
            : Scale is <= 0 or > 1 ? ValidationResult.Error("--scale must be greater than 0 and at most 1.")
            : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        for (var left = settings.Delay; left > 0; left--)
        {
            AnsiConsole.MarkupLine($"[grey]Clicking in {left}…[/]");
            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
        }

        // The click is mapped with the latest screenshot, so take one at the scale asked for.
        var shot = await screen.ScreenshotAsync(settings.Scale, cancellationToken);
        var point = new ImagePoint(settings.X, settings.Y);
        if (point.X < 0 || point.X >= shot.Width || point.Y < 0 || point.Y >= shot.Height)
        {
            AnsiConsole.MarkupLine($"[red]Outside the screenshot:[/] {point} isn't in x 0–{shot.Width - 1}, y 0–{shot.Height - 1}.");
            return 1;
        }

        await screen.ClickAsync(point.X, point.Y, settings.Button, settings.Clicks, cancellationToken);

        var display = displayCheck.Run();
        var frame = new FrameGeometry(shot.Width, shot.Height, display.CaptureMonitor.Bounds, display.Snapshot.VirtualDesktop);
        var expected = CoordinateMapper.ImageToScreen(frame, point);
        var actual = cursor.Read();

        var scale = shot.Scale.ToString("0.###", CultureInfo.InvariantCulture);
        AnsiConsole.MarkupLine(
            $"Clicked {settings.Button} ×{settings.Clicks} at {point} in the {shot.Width}×{shot.Height} screenshot (scale {scale})");
        if (actual == expected)
        {
            AnsiConsole.MarkupLine($"Cursor is on [green]{actual}[/], as expected.");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Cursor is on {actual}, expected {expected}.[/]");
        return 1;
    }
}
