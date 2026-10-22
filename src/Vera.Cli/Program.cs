using Spectre.Console;

AnsiConsole.Write(new FigletText("VERA").Color(Color.Teal));
AnsiConsole.MarkupLine("[green]Spectre works.[/]");

var name = AnsiConsole.Ask<string>("What's your [blue]name[/]?");
AnsiConsole.MarkupLine($"Hi [bold]{Markup.Escape(name)}[/]!");