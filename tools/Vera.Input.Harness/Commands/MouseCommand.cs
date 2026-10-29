using System.ComponentModel;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input.Coordinates;
using Vera.Input.Platform;

namespace Vera.Input.Harness.Commands;

/// <summary>
/// Shows live where the mouse is: on the screen, and in screenshots of the capture monitor at 100%, 75% and 50%.
/// The reverse of <see cref="MapCommand"/>, checked against the real screen without clicking.
/// </summary>
internal sealed class MouseCommand(IDisplayCheck displayCheck, ICursorPosition cursor, TimeProvider timeProvider)
    : AsyncCommand<MouseCommand.Settings>
{
    private static readonly double[] Scales = [1.0, 0.75, 0.5];

    internal sealed class Settings : CommandSettings;

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var display = displayCheck.Run();
        AnsiConsole.MarkupLine("Move the mouse around. [grey]Press any key to stop.[/]");

        await AnsiConsole.Live(Render(display)).StartAsync(async live =>
        {
            while (!Console.KeyAvailable && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeProvider, CancellationToken.None);
                live.UpdateTarget(Render(display));
            }
        });

        while (Console.KeyAvailable)
        {
            Console.ReadKey(intercept: true);
        }

        return 0;
    }

    private Table Render(DisplayReport display)
    {
        var table = new Table().AddColumn("").AddColumn("Mouse position");
        ScreenPoint mouse;
        try
        {
            mouse = cursor.Read();
        }
        catch (Win32Exception e)
        {
            return table.AddRow("Screen", $"[red]unavailable:[/] {Markup.Escape(e.Message)}");
        }

        var monitor = display.Snapshot.Monitors.FirstOrDefault(m => m.Bounds.Contains(mouse.X, mouse.Y));
        table.AddRow("Screen", $"{mouse}" + (monitor is null ? "" : $" on monitor {monitor.Index}"));
        table.AddRow("Mouse units", CoordinateMapper.ScreenToNormalized(display.Snapshot.VirtualDesktop, mouse).ToString());

        var capture = display.CaptureMonitor;
        foreach (var scale in Scales)
        {
            var frame = FrameGeometry.ForScale(capture.Bounds, display.Snapshot.VirtualDesktop, scale);
            var place = capture.Bounds.Contains(mouse.X, mouse.Y)
                ? $"[green]{CoordinateMapper.ScreenToImage(frame, mouse)}[/]"
                : $"[grey]not on monitor {capture.Index}[/]";
            table.AddRow($"{scale * 100:0}% screenshot ({frame.ImageWidth}×{frame.ImageHeight})", place);
        }

        return table;
    }
}
