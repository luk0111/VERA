using System.Text.RegularExpressions;

using Vera.Core;
using Vera.Reporting;

namespace Vera.Application;

/// <summary>
/// The saved secrets VERA typed while it runs, and where. A field shows what was typed into it, and an app may show it elsewhere
/// (e.g. "Signed in as …"), so every later reading of the app would carry the real value to the model, a message or a screenshot.
/// <see cref="Hide"/> takes it out of a reading before anything sees it. One trail is shared by steps runs, Find and the chat,
/// for as long as VERA runs. Kept in memory only, never logged or saved.
/// </summary>
public sealed class SecretTrail
{
    /// <summary>Shorter values are too common to hide everywhere ("12" in "2012"); they are still hidden in the field they went into.</summary>
    internal const int MinLengthAnywhere = 3;

    /// <summary>From this length a value is hidden even inside a longer word, e.g. "s3cret" in "s3cret123". Shorter ones only as a whole word.</summary>
    internal const int MinLengthInsideWords = 6;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new();
    private readonly HashSet<string> _values = new(StringComparer.Ordinal);
    private readonly List<(int ProcessId, UiElement Field)> _fields = [];

    /// <summary>Remembers the secret values going into a field of an app, before they are typed.</summary>
    internal void Typed(int processId, UiElement field, IEnumerable<string> values)
    {
        lock (_lock)
        {
            RemoveField(processId, field);
            _fields.Add((processId, field));
            _values.UnionWith(values.Where(v => v.Length > 0));
        }
    }

    /// <summary>Text without a secret went into the field, so its text can be read again. The values stay hidden elsewhere.</summary>
    internal void Forget(int processId, UiElement field)
    {
        lock (_lock)
        {
            RemoveField(processId, field);
        }
    }

    private void RemoveField(int processId, UiElement field) =>
        _fields.RemoveAll(f => f.ProcessId == processId && f.Field.Role == field.Role && f.Field.Name == field.Name && f.Field.Locator == field.Locator);

    /// <summary>
    /// The reading with every typed secret replaced by <see cref="SecretPlaceholder.MaskText"/> in names, values and the title.
    /// A field a secret went into shows only <see cref="SecretPlaceholder.MaskText"/>. Both are painted over on the screenshot.
    /// </summary>
    public ScreenState Hide(int processId, ScreenState screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        string[] values;
        UiElement[] fields;
        lock (_lock)
        {
            if (_values.Count == 0 && _fields.Count == 0)
            {
                return screen;
            }

            // Longest first, so a value that contains a shorter one is hidden whole.
            values = [.. _values.OrderByDescending(v => v.Length)];
            fields = [.. _fields.Where(f => f.ProcessId == processId).Select(f => f.Field)];
        }

        var typedInto = fields.Select(f => ElementFinder.Same(screen.Elements, f)).OfType<UiElement>().Select(e => e.Id).ToHashSet();
        var cover = new List<ScreenRect>();
        var elements = screen.Elements.Select(e =>
        {
            var inField = typedInto.Contains(e.Id);
            var hidden = e with
            {
                Name = Mask(e.Name, values),
                Value = e.Value is null ? null : inField && e.Value.Length > 0 ? SecretPlaceholder.MaskText : Mask(e.Value, values),
            };

            // Never the window itself: that would black out the whole screenshot.
            if (e.Id >= 1 && (hidden != e || inField))
            {
                cover.Add(e.Bounds);
            }

            return hidden;
        }).ToList();

        var png = cover.Count > 0 && !screen.ScreenshotPng.IsEmpty && screen.Elements.Count > 0
            ? ElementHighlight.Cover(screen.ScreenshotPng, screen.Elements[0].Bounds, cover)
            : screen.ScreenshotPng;
        return screen with { WindowTitle = Mask(screen.WindowTitle, values), Elements = elements, ScreenshotPng = png };
    }

    private static string Mask(string text, string[] values)
    {
        foreach (var value in values)
        {
            if (value.Length >= MinLengthInsideWords)
            {
                text = text.Replace(value, SecretPlaceholder.MaskText, StringComparison.Ordinal);
            }
            else if (value.Length >= MinLengthAnywhere && text.Contains(value, StringComparison.Ordinal))
            {
                text = Regex.Replace(
                    text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}])", SecretPlaceholder.MaskText, RegexOptions.CultureInvariant, RegexTimeout);
            }
        }

        return text;
    }
}
