using System.Globalization;

using Microsoft.Extensions.Options;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input.Options;
using Vera.Input.Platform;

namespace Vera.Input.Harness.Commands;

/// <summary>Prints the DPI awareness and the effective <c>VeraInput</c> configuration (appsettings.json on top of the defaults).</summary>
internal sealed class InfoCommand(IOptions<VeraInputOptions> options, IDpiAwarenessCheck dpi, IDisplayCheck displayCheck)
    : Command<InfoCommand.Settings>
{
    internal sealed class Settings : CommandSettings;

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var display = displayCheck.Run();
        AnsiConsole.MarkupLine($"[bold]DPI awareness:[/] {dpi.Current}");
        AnsiConsole.WriteLine();
        WriteDisplay(display);
        AnsiConsole.WriteLine();

        var table = new Table().AddColumn("Setting").AddColumn("Value");
        foreach (var section in typeof(VeraInputOptions).GetProperties())
        {
            var values = section.GetValue(options.Value);
            foreach (var property in section.PropertyType.GetProperties())
            {
                table.AddRow(
                    Markup.Escape($"{section.Name}.{property.Name}"),
                    Markup.Escape(property.GetValue(values) is { } value ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "(not set)"));
            }
        }

        AnsiConsole.MarkupLine($"[bold]{VeraInputOptions.SectionName}[/] configuration");
        AnsiConsole.Write(table);
        return 0;
    }

    private static void WriteDisplay(DisplayReport report)
    {
        var snapshot = report.Snapshot;
        AnsiConsole.MarkupLine($"[bold]Display:[/] virtual desktop {Markup.Escape(snapshot.VirtualDesktop.ToString())}");

        var monitors = new Table().AddColumn("#").AddColumn("Device").AddColumn("Size").AddColumn("Position")
            .AddColumn("Scale").AddColumn("Refresh").AddColumn("Primary").AddColumn("Capture");
        foreach (var m in snapshot.Monitors)
        {
            monitors.AddRow(
                m.Index.ToString(CultureInfo.InvariantCulture),
                Markup.Escape(m.DeviceName),
                $"{m.Bounds.Width}×{m.Bounds.Height}",
                $"({m.Bounds.Left},{m.Bounds.Top})",
                $"{m.ScalePercent}%",
                m.RefreshHz > 0 ? $"{m.RefreshHz} Hz" : "?",
                m.IsPrimary ? "yes" : "",
                m == report.CaptureMonitor ? "[green]yes[/]" : "");
        }

        AnsiConsole.Write(monitors);
        AnsiConsole.MarkupLine(snapshot.IsRemoteSession
            ? "[yellow]Remote session:[/] yes, someone is connected to this machine over RDP"
            : "[bold]Remote session:[/] no");

        if (report.Mismatches.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Matches the expected display.[/]");
        }

        foreach (var mismatch in report.Mismatches)
        {
            AnsiConsole.MarkupLine($"[yellow]Mismatch:[/] {Markup.Escape(mismatch)}");
        }
    }
}