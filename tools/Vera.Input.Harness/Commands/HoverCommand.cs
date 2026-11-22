using System.ComponentModel;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input.Coordinates;
using Vera.Input.Platform;

namespace Vera.Input.Harness.Commands;

/// <summary>
/// Takes a screenshot, glides the mouse to a point in it and waits <c>HoverDwell</c>, then saves a second screenshot so
/// you can see whether the tooltip or hover menu showed.
/// </summary>
internal sealed class HoverCommand(IScreen screen, IDisplayCheck displayCheck, ICursorPosition cursor, TimeProvider timeProvider)
    : AsyncCommand<HoverCommand.Settings>
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

        [CommandOption("--delay <SECONDS>")]
        [Description("Wait this long first, to bring the target window to the front. Default: 0")]
        public int Delay { get; init; }

        public override ValidationResult Validate() =>
            Delay < 0 ? ValidationResult.Error("--delay can't be negative.")
            : Scale is <= 0 or > 1 ? ValidationResult.Error("--scale must be greater than 0 and at most 1.")
            : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await HarnessCountdown.RunAsync("Hovering", settings.Delay, timeProvider, cancellationToken);
        var shot = await screen.ScreenshotAsync(settings.Scale, cancellationToken);
        if (!HarnessCountdown.Inside(shot, settings.X, settings.Y))
        {
            return 1;
        }

        await screen.MoveAsync(settings.X, settings.Y, cancellationToken);
        var after = await screen.ScreenshotAsync(settings.Scale, cancellationToken);
        var path = Path.GetFullPath($"hover-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        await File.WriteAllBytesAsync(path, after.Png, cancellationToken);

        AnsiConsole.MarkupLine($"Hovered at ({settings.X}, {settings.Y}). Screenshot after the hover: [link]{Markup.Escape(path)}[/]");
        return HarnessCountdown.CheckCursor(shot, new ImagePoint(settings.X, settings.Y), displayCheck, cursor);
    }
}
