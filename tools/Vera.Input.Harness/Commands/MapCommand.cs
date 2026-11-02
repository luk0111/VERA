using System.ComponentModel;
using System.Globalization;

using Microsoft.Extensions.Options;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input.Coordinates;
using Vera.Input.Options;
using Vera.Input.Platform;

namespace Vera.Input.Harness.Commands;

/// <summary>Shows where a point in a screenshot lands on the screen, and the mouse units Windows would get. Nothing moves.</summary>
internal sealed class MapCommand(IDisplayCheck displayCheck, IOptions<VeraInputOptions> options) : Command<MapCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<X>")]
        [Description("X in the screenshot, 0 = left edge")]
        public int X { get; init; }

        [CommandArgument(1, "<Y>")]
        [Description("Y in the screenshot, 0 = top edge")]
        public int Y { get; init; }

        [CommandOption("--scale <SCALE>")]
        [Description("The screenshot's scale, e.g. 0.5. Default: Capture.DefaultScale")]
        public double? Scale { get; init; }

        public override ValidationResult Validate() =>
            Scale is <= 0 or > 1 ? ValidationResult.Error("--scale must be greater than 0 and at most 1.") : ValidationResult.Success();
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var display = displayCheck.Run();
        var frame = FrameGeometry.ForScale(
            display.CaptureMonitor.Bounds, display.Snapshot.VirtualDesktop, settings.Scale ?? options.Value.Capture.DefaultScale);
        var scale = frame.Scale.ToString("0.###", CultureInfo.InvariantCulture);
        AnsiConsole.MarkupLine(
            $"Screenshot {frame.ImageWidth}×{frame.ImageHeight} (scale {scale}) of monitor {display.CaptureMonitor.Index} " +
            $"({Markup.Escape(frame.CaptureBounds.ToString())})");

        var point = new ImagePoint(settings.X, settings.Y);
        if (point.X < 0 || point.X >= frame.ImageWidth || point.Y < 0 || point.Y >= frame.ImageHeight)
        {
            AnsiConsole.MarkupLine(
                $"[red]Outside the screenshot:[/] {point} isn't in x 0–{frame.ImageWidth - 1}, y 0–{frame.ImageHeight - 1}. A click there would be refused.");
            return 1;
        }

        var screen = CoordinateMapper.ImageToScreen(frame, point);
        var normalized = CoordinateMapper.ScreenToNormalized(frame.VirtualDesktop, screen);
        AnsiConsole.MarkupLine($"{point} in the screenshot → [green]{screen}[/] on screen → mouse units {normalized}");
        return 0;
    }
}
