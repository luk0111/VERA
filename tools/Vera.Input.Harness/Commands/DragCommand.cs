using System.ComponentModel;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input.Coordinates;
using Vera.Input.Platform;

namespace Vera.Input.Harness.Commands;

/// <summary>Takes a screenshot, drags from one point in it to another, and checks that the cursor ended on the second.</summary>
internal sealed class DragCommand(IScreen screen, IDisplayCheck displayCheck, ICursorPosition cursor, TimeProvider timeProvider)
    : AsyncCommand<DragCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<FROM_X>")]
        [Description("Start X in the screenshot")]
        public int FromX { get; init; }

        [CommandArgument(1, "<FROM_Y>")]
        [Description("Start Y in the screenshot")]
        public int FromY { get; init; }

        [CommandArgument(2, "<TO_X>")]
        [Description("End X in the screenshot")]
        public int ToX { get; init; }

        [CommandArgument(3, "<TO_Y>")]
        [Description("End Y in the screenshot")]
        public int ToY { get; init; }

        [CommandOption("--button <BUTTON>")]
        [Description("left, right or middle. Default: left")]
        public MouseButton Button { get; init; } = MouseButton.Left;

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
        await HarnessCountdown.RunAsync("Dragging", settings.Delay, timeProvider, cancellationToken);
        var shot = await screen.ScreenshotAsync(settings.Scale, cancellationToken);
        if (!HarnessCountdown.Inside(shot, settings.FromX, settings.FromY) || !HarnessCountdown.Inside(shot, settings.ToX, settings.ToY))
        {
            return 1;
        }

        await screen.DragAsync(settings.FromX, settings.FromY, settings.ToX, settings.ToY, settings.Button, cancellationToken);
        AnsiConsole.MarkupLine(
            $"Dragged {settings.Button} from ({settings.FromX}, {settings.FromY}) to ({settings.ToX}, {settings.ToY}) in the {shot.Width}×{shot.Height} screenshot.");
        return HarnessCountdown.CheckCursor(shot, new ImagePoint(settings.ToX, settings.ToY), displayCheck, cursor);
    }
}
