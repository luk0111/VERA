using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input.Capture;
using Vera.Input.Platform;

namespace Vera.Input.Harness.Commands;

/// <summary>Copies the capture monitor and saves it as a PNG.</summary>
internal sealed class ScreenshotCommand(IDisplayCheck displayCheck, IScreenGrabber grabber, TimeProvider timeProvider)
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
        var bounds = displayCheck.Run().CaptureMonitor.Bounds;

        for (var left = settings.Delay; left > 0; left--)
        {
            AnsiConsole.MarkupLine($"[grey]Taking the screenshot in {left}…[/]");
            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        var frame = grabber.Grab(bounds);
        var grabMs = stopwatch.ElapsedMilliseconds;

        var path = Path.GetFullPath(settings.Out ?? Path.Combine(
            "runs", "manual", frame.CapturedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SavePng(frame, path);

        AnsiConsole.MarkupLine($"Saved [green]{Markup.Escape(path)}[/]");
        AnsiConsole.MarkupLine($"{frame.Width}×{frame.Height} from {Markup.Escape(frame.Bounds.ToString())}, grab {grabMs} ms, {new FileInfo(path).Length / 1024} KB");
        return 0;
    }

    // Temporary until step 2.2 adds the frame encoder to Vera.Input.
    private static void SavePng(RawFrame frame, string path)
    {
        var handle = GCHandle.Alloc(frame.Bgra, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(frame.Width, frame.Height, frame.Stride, PixelFormat.Format32bppRgb, handle.AddrOfPinnedObject());
            bitmap.Save(path, ImageFormat.Png);
        }
        finally
        {
            handle.Free();
        }
    }
}