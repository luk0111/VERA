using Vera.Adapters.Desktop;
using Vera.Core;

namespace Vera.Adapters.Mac.Tests;

/// <summary>Numbering is shared with Windows; this runs on macOS too.</summary>
public sealed class ElementNumberingLimitTests
{
    [Fact]
    public void Up_to_300_elements_are_numbered_top_to_bottom()
    {
        var window = new ScreenRect(0, 0, 1000, 20000);
        var candidates = Enumerable.Range(0, 350)
            .Select(i => new RawElement("Button", $"B{i}", new ScreenRect(10, 10 + (i * 50), 40, 20), true, false, "", null))
            .ToList();

        var elements = ElementNumbering.Number("Big page", window, candidates);

        elements.Count.ShouldBe(ElementNumbering.MaxElements + 1);
        elements[^1].Name.ShouldBe("B299");
    }

    private static readonly ScreenRect Window = new(0, 0, 1000, 800);

    [Fact]
    public void A_link_listed_twice_in_the_same_place_gets_one_number()
    {
        var hide = Raw("Hyperlink", "hide", 100, 200);

        var elements = ElementNumbering.Number("Hacker News", Window, [hide, hide with { Bounds = new ScreenRect(102, 201, 30, 14) }]);

        elements.Count(e => e.Name == "hide").ShouldBe(1);
    }

    [Fact]
    public void The_same_name_in_two_places_stays_twice()
    {
        var elements = ElementNumbering.Number("Hacker News", Window, [Raw("Hyperlink", "hide", 100, 200), Raw("Hyperlink", "hide", 100, 260)]);

        elements.Count(e => e.Name == "hide").ShouldBe(2);
    }

    [Theory]
    [InlineData("WebArea")]
    [InlineData("Document")]
    public void Nameless_rows_inside_a_web_page_are_skipped(string pageRole)
    {
        var elements = ElementNumbering.Number("Hacker News", Window,
        [
            new RawElement(pageRole, "Hacker News", new ScreenRect(0, 100, 1000, 700), true, false, "", null),
            Raw("ListItem", "", 50, 200),
            Raw("ListItem", "Story row", 50, 240),
            Raw("Hyperlink", "86 comments", 300, 200),
        ]);

        // On Windows the page itself is numbered too (a Document can be typed into, e.g. Notepad's page), so look past it.
        elements.Skip(1).Where(e => e.Role != pageRole).Select(e => e.Name).ShouldBe(["86 comments", "Story row"]);
    }

    [Fact]
    public void Nameless_rows_in_an_apps_own_list_stay()
    {
        var elements = ElementNumbering.Number("Finder", Window, [Raw("ListItem", "", 50, 200), Raw("ListItem", "", 50, 230)]);

        elements.Count.ShouldBe(3);
    }

    [Fact]
    public void Skipped_doubles_and_rows_leave_room_for_more_of_the_page()
    {
        var candidates = new List<RawElement> { new("WebArea", "Big page", new ScreenRect(0, 0, 1000, 20000), true, false, "", null) };
        for (var i = 0; i < 300; i++)
        {
            var link = Raw("Hyperlink", $"Story {i}", 300, 10 + (i * 50));
            candidates.AddRange([link, link, Raw("ListItem", "", 10, 10 + (i * 50))]);
        }

        var elements = ElementNumbering.Number("Big page", new ScreenRect(0, 0, 1000, 20000), candidates);

        elements.Count.ShouldBe(ElementNumbering.MaxElements + 1);
        elements[^1].Name.ShouldBe("Story 299");
    }

    private static RawElement Raw(string role, string name, int x, int y) => new(role, name, new ScreenRect(x, y, 30, 14), true, false, "", null);
}
