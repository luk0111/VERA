using System.ComponentModel;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Vera.Input.Harness.Commands;

/// <summary>Types text into the window in front, after a countdown so you can click into it.</summary>
internal sealed class TypeCommand(IScreen screen, TimeProvider timeProvider) : AsyncCommand<TypeCommand.Settings>
{
    /// <summary>
    /// Typed when no text is given: Romanian letters with comma below (ș ț) and with cedilla (ş ţ), which are
    /// different characters, symbols that need Shift or AltGr on some layouts, then Enter and Tab.
    /// </summary>
    internal const string TestLine = "ăâîșțĂÂÎȘȚ şţŞŢ €@#$%^&*{}[]|\\~<>\"' Test 123\nLine 2\tafter a tab";

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[TEXT]")]
        [Description("What to type. A new line presses Enter, a tab presses Tab. Default: a test line with ă ș ț ş ţ and symbols")]
        public string? Text { get; init; }

        [CommandOption("--delay <SECONDS>")]
        [Description("Wait this long first, to click into the target window. Default: 3")]
        public int Delay { get; init; } = 3;

        public override ValidationResult Validate() =>
            Delay < 0 ? ValidationResult.Error("--delay can't be negative.") : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var text = string.IsNullOrEmpty(settings.Text) ? TestLine : settings.Text;
        AnsiConsole.MarkupLine($"Will type: {Show(text)}");

        for (var left = settings.Delay; left > 0; left--)
        {
            AnsiConsole.MarkupLine($"[grey]Typing in {left}… click into the target window.[/]");
            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);
        }

        try
        {
            await screen.TypeTextAsync(text, ct: cancellationToken);
        }
        catch (ArgumentException e)
        {
            AnsiConsole.MarkupLine($"[red]Can't type that:[/] {Markup.Escape(e.Message)}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Typed {text.Length} characters.[/] Compare them with what the window shows.");
        return 0;
    }

    /// <summary>The text on one line, with Enter shown as ⏎ and Tab as →.</summary>
    private static string Show(string text) =>
        Markup.Escape(text.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "⏎", StringComparison.Ordinal).Replace("\t", "→", StringComparison.Ordinal));
}
