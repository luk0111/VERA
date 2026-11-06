using Vera.Adapters.Desktop;
using Vera.Core;

namespace Vera.Adapters.Mac;

/// <summary>Turns what the macOS Accessibility API reports into the same words Windows uses, so numbering and the model's prompt work unchanged.</summary>
internal static class MacRoles
{
    private static readonly Dictionary<string, string> Common = new(StringComparer.Ordinal)
    {
        ["AXButton"] = "Button",
        ["AXTextField"] = "Edit",
        ["AXTextArea"] = "Edit",
        ["AXCheckBox"] = "CheckBox",
        ["AXSwitch"] = "CheckBox",
        ["AXRadioButton"] = "RadioButton",
        ["AXComboBox"] = "ComboBox",
        ["AXPopUpButton"] = "ComboBox",
        ["AXLink"] = "Hyperlink",
        ["AXMenuItem"] = "MenuItem",
        ["AXMenuBarItem"] = "MenuItem",
        ["AXTab"] = "TabItem",
        ["AXRow"] = "ListItem",
        ["AXCell"] = "ListItem",
        ["AXSlider"] = "Slider",
        ["AXMenuButton"] = "SplitButton",
    };

    /// <returns>The Windows word for <paramref name="role"/>. Roles without one lose their <c>AX</c> prefix, and numbering then leaves them out.</returns>
    public static string ToCommon(string role) =>
        Common.TryGetValue(role, out var common) ? common : role.StartsWith("AX", StringComparison.Ordinal) ? role[2..] : role;

    /// <summary>Picks the text a tester would call the element by: many macOS buttons have only a description.</summary>
    public static string Name(string? title, string? description, string? help) =>
        !string.IsNullOrWhiteSpace(title) ? title : !string.IsNullOrWhiteSpace(description) ? description : help ?? "";

    /// <summary>
    /// macOS reports positions in points, while a Retina screenshot has two pixels per point.
    /// Scaling makes bounds match the image, as they do on Windows.
    /// </summary>
    public static ScreenRect ToImagePixels(ScreenRect r, double scale) => new(
        (int)Math.Round(r.X * scale),
        (int)Math.Round(r.Y * scale),
        (int)Math.Round(r.Width * scale),
        (int)Math.Round(r.Height * scale));

    public static RawElement ToImagePixels(RawElement element, double scale) => element with { Bounds = ToImagePixels(element.Bounds, scale) };
}
