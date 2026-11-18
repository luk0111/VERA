using Spectre.Console;

using Vera.Input.Coordinates;
using Vera.Input.Platform;

namespace Vera.Input.Harness.Commands;

/// <summary>Small pieces the mouse commands share: the countdown, the bounds check and the cursor check.</summary>
internal static class HarnessCountdown
{
    /// <summary>Counts down <paramref name="seconds"/>, so you can bring the target window to the front.</summary>
    public static async Task RunAsync(string what, int seconds, TimeProvider time, CancellationToken ct)
    {
        for (var left = seconds; left > 0; left--)
        {
            AnsiConsole.MarkupLine($"[grey]{what} in {left}…[/]");
            await Task.Delay(TimeSpan.FromSeconds(1), time, ct);
        }
    }

    /// <summary>True when the point is in the screenshot; otherwise says why not.</summary>
    public static bool Inside(ScreenFrame shot, int x, int y)
    {
        if (x >= 0 && x < shot.Width && y >= 0 && y < shot.Height)
        {
            return true;
        }

        AnsiConsole.MarkupLine($"[red]Outside the screenshot:[/] ({x}, {y}) isn't in x 0–{shot.Width - 1}, y 0–{shot.Height - 1}.");
        return false;
    }

    /// <summary>Checks the cursor is on the screen pixel <paramref name="point"/> maps to. Returns the exit code.</summary>
    public static int CheckCursor(ScreenFrame shot, ImagePoint point, IDisplayCheck displayCheck, ICursorPosition cursor)
    {
        var display = displayCheck.Run();
        var frame = new FrameGeometry(shot.Width, shot.Height, display.CaptureMonitor.Bounds, display.Snapshot.VirtualDesktop);
        var expected = CoordinateMapper.ImageToScreen(frame, point);
        var actual = cursor.Read();
        if (actual == expected)
        {
            AnsiConsole.MarkupLine($"Cursor is on [green]{actual}[/], as expected.");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Cursor is on {actual}, expected {expected}.[/]");
        return 1;
    }
}
