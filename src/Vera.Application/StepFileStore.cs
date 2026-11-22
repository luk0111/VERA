using System.Text.RegularExpressions;

namespace Vera.Application;

/// <summary>
/// Saves chains of steps as text files (<c>&lt;name&gt;.txt</c>, one step per line as written), so a tester can run them again or edit them in Notepad.
/// Secret placeholders stay placeholders. Blank lines and <c>#</c> comments in a file are skipped when it's loaded.
/// </summary>
public sealed partial class StepFileStore(string folder)
{
    /// <summary>The full path of the folder the files go in.</summary>
    public string Folder { get; } = Path.GetFullPath(folder);

    /// <summary>Whether <paramref name="name"/> can be a file name: letters, digits, spaces, <c>_</c> and <c>-</c>.</summary>
    public static bool IsValidName(string name) => Name().IsMatch(name);

    /// <summary>The names of the saved chains, sorted.</summary>
    public IReadOnlyList<string> List() =>
        Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "*.txt").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    public bool Exists(string name) => File.Exists(PathOf(name));

    /// <summary>Saves the steps under <paramref name="name"/>, replacing a file with that name. Returns the file's path.</summary>
    public string Save(string name, IEnumerable<StepCommand> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var path = PathOf(name);
        Directory.CreateDirectory(Folder);
        File.WriteAllLines(path, steps.Select(s => s.ToString()));
        return path;
    }

    /// <summary>Deletes the saved file for good (not to the Recycle Bin). Nothing happens if there is none.</summary>
    public void Delete(string name) => File.Delete(PathOf(name));

    /// <exception cref="FormatException">A line is not a step (the message names the line), or the file has no steps.</exception>
    public IReadOnlyList<StepCommand> Load(string name)
    {
        var steps = new List<StepCommand>();
        var lines = File.ReadAllLines(PathOf(name));
        for (var i = 0; i < lines.Length; i++)
        {
            try
            {
                if (StepParser.Parse(lines[i]) is { } step)
                {
                    steps.Add(step);
                }
            }
            catch (FormatException e)
            {
                throw new FormatException($"Line {i + 1} of {name}.txt: {e.Message}", e);
            }
        }

        return steps.Count > 0 ? steps : throw new FormatException($"{name}.txt has no steps.");
    }

    private string PathOf(string name) =>
        IsValidName(name) ? Path.Combine(Folder, name + ".txt") : throw new ArgumentException($"\"{name}\" can't be a file name.", nameof(name));

    [GeneratedRegex(@"^[A-Za-z0-9_\-]([A-Za-z0-9_\- ]*[A-Za-z0-9_\-])?$")]
    private static partial Regex Name();
}
