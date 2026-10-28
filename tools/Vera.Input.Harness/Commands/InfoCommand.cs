using System.Globalization;

using Microsoft.Extensions.Options;

using Spectre.Console;
using Spectre.Console.Cli;

using Vera.Input.Options;
using Vera.Input.Platform;

namespace Vera.Input.Harness.Commands;

/// <summary>Prints the DPI awareness and the effective <c>VeraInput</c> configuration (appsettings.json on top of the defaults).</summary>
internal sealed class InfoCommand(IOptions<VeraInputOptions> options, IDpiAwarenessCheck dpi) : Command<InfoCommand.Settings>
{
    internal sealed class Settings : CommandSettings;

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        dpi.EnsurePerMonitorV2();
        AnsiConsole.MarkupLine($"[bold]DPI awareness:[/] {dpi.Current}");
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
}