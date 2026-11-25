using Vera.Core;
using Vera.Reporting;

namespace Vera.Application;

/// <summary>
/// The saved secrets a run typed, and where. A field shows what was typed into it, so every later reading would carry the real value
/// to the model, the step messages and the screenshots. <see cref="Hide"/> takes it out of a reading before anything sees it.
/// </summary>
internal sealed class SecretTrail
{
    private readonly List<string> _values = [];
    private readonly List<UiElement> _fields = [];

    /// <summary>Remembers a secret's value and the field it goes into, before it is typed. Kept in memory only, never logged.</summary>
    public void Typed(UiElement field, string value)
    {
        _fields.Add(field);
        if (value.Length > 0)
        {
            _values.Add(value);
        }
    }

    /// <summary>
    /// The reading with every typed secret replaced by <see cref="SecretPlaceholder.MaskText"/> in names, values and the title,
    /// and painted over on the screenshot where a field holds one or was typed into.
    /// </summary>
    public ScreenState Hide(ScreenState screen)
    {
        if (_fields.Count == 0)
        {
            return screen;
        }

        var typedInto = _fields.Select(f => ElementFinder.Same(screen.Elements, f)).OfType<UiElement>().Select(e => e.Id).ToHashSet();
        var cover = new List<ScreenRect>();
        var elements = screen.Elements.Select(e =>
        {
            var hidden = e with { Name = Mask(e.Name), Value = e.Value is null ? null : Mask(e.Value) };

            // Never the window itself: that would black out the whole screenshot.
            if (e.Id >= 1 && (hidden != e || typedInto.Contains(e.Id)))
            {
                cover.Add(e.Bounds);
            }

            return hidden;
        }).ToList();

        var png = cover.Count > 0 && !screen.ScreenshotPng.IsEmpty && screen.Elements.Count > 0
            ? ElementHighlight.Cover(screen.ScreenshotPng, screen.Elements[0].Bounds, cover)
            : screen.ScreenshotPng;
        return screen with { WindowTitle = Mask(screen.WindowTitle), Elements = elements, ScreenshotPng = png };
    }

    private string Mask(string text) => SecretPlaceholder.Mask(text, _values);
}
