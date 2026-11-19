using System.ComponentModel;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Vera.Input.Harness.Commands;

/// <summary>Takes a screenshot, then scrolls the window under a point in it by wheel notches.</summary>
internal sealed class ScrollCommand(IScreen screen, TimeProvider timeProvider) : AsyncCommand<ScrollCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<X>")]
        [Description("X in the screenshot, 0 = left edge")]
        public int X { get; init; }

        [CommandArgument(1, "<Y>")]
        [Description("Y in the screenshot, 0 = top edge")]
        public int Y { get; init; }

        [CommandOption("--down <NOTCHES>")]
        [Description("Wheel notches down (negative = up). Default: 3")]
        public int Down { get; init; } = 3;

        [CommandOption("--right <NOTCHES>")]
        [Description("Wheel notches right (negative = left). Default: 0")]
        public int Right { get; init; }

        [CommandOption("--scale <SCALE>")]
        [Description("The screenshot's scale, e.g. 0.5. Default: Capture.DefaultScale")]
        public double? Scale { get; init; }

        [CommandOption("--delay <SECONDS>")]
        [Description("Wait this long first, to bring the target window to the front. Default: 0")]
        public int Delay { get; init; }

        public override ValidationResult Validate() =>
            Down == 0 && Right == 0 ? ValidationResult.Error("Scroll by at least one notch.")
            : Delay < 0 ? ValidationResult.Error("--delay can't be negative.")
            : Scale is <= 0 or > 1 ? ValidationResult.Error("--scale must be greater than 0 and at most 1.")
            : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await HarnessCountdown.RunAsync("Scrolling", settings.Delay, timeProvider, cancellationToken);
        var shot = await screen.ScreenshotAsync(settings.Scale, cancellationToken);
        if (!HarnessCountdown.Inside(shot, settings.X, settings.Y))
        {
            return 1;
        }

        await screen.ScrollAsync(settings.X, settings.Y, settings.Down, settings.Right, cancellationToken);
        AnsiConsole.MarkupLine($"Scrolled {settings.Down} down and {settings.Right} right at ({settings.X}, {settings.Y}) in the {shot.Width}×{shot.Height} screenshot.");
        return 0;
    }
}
