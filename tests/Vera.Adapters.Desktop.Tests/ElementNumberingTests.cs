using Vera.Core;

namespace Vera.Adapters.Desktop.Tests;

public sealed class ElementNumberingTests
{
    private static readonly ScreenRect Window = new(100, 100, 400, 300);

    [Fact]
    public void The_window_is_element_0()
    {
        var elements = ElementNumbering.Number("Calc", Window, []);

        var window = elements.ShouldHaveSingleItem();
        window.Id.ShouldBe(0);
        window.Role.ShouldBe("Window");
        window.Name.ShouldBe("Calc");
        window.Bounds.ShouldBe(Window);
    }

    [Fact]
    public void Only_actionable_visible_elements_inside_the_window_are_kept()
    {
        RawElement[] candidates =
        [
            Raw("Button", "OK", 120, 120),
            Raw("Text", "Label", 120, 160),
            Raw("Button", "Hidden", 120, 200) with { IsOffscreen = true },
            Raw("Button", "Zero", 120, 240) with { Bounds = new ScreenRect(120, 240, 0, 0) },
            Raw("Button", "Outside", 600, 120),
        ];

        var elements = ElementNumbering.Number("Calc", Window, candidates);

        elements.Skip(1).Select(e => e.Name).ShouldBe(["OK"]);
    }

    [Fact]
    public void Elements_are_numbered_from_1_in_reading_order()
    {
        RawElement[] candidates = [Raw("Button", "B", 300, 120), Raw("Button", "A", 120, 120), Raw("Edit", "C", 120, 200)];

        var elements = ElementNumbering.Number("Calc", Window, candidates);

        elements.Skip(1).Select(e => (e.Id, e.Name)).ShouldBe([(1, "A"), (2, "B"), (3, "C")]);
    }

    [Fact]
    public void At_most_40_elements_are_kept()
    {
        var candidates = Enumerable.Range(0, 50).Select(i => Raw("Button", $"B{i}", 110 + (i % 10 * 30), 110 + (i / 10 * 30))).ToList();

        var elements = ElementNumbering.Number("Calc", Window, candidates);

        elements.Count.ShouldBe(ElementNumbering.MaxElements + 1);
    }

    [Fact]
    public void The_locator_falls_back_to_role_and_name()
    {
        RawElement[] candidates = [Raw("Button", "OK", 120, 120) with { AutomationId = "okButton" }, Raw("Button", "Cancel", 200, 120)];

        var elements = ElementNumbering.Number("Calc", Window, candidates);

        elements.Skip(1).Select(e => e.Locator).ShouldBe(["okButton", "Button:Cancel"]);
    }

    private static RawElement Raw(string role, string name, int x, int y) =>
        new(role, name, new ScreenRect(x, y, 20, 20), IsEnabled: true, IsOffscreen: false, AutomationId: "", Value: null);
}
