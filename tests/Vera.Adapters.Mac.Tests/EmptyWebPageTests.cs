using Vera.Adapters.Desktop;
using Vera.Core;

namespace Vera.Adapters.Mac.Tests;

/// <summary>Safari builds a page's elements only when asked, so VERA reads again while the page is still empty.</summary>
public sealed class EmptyWebPageTests
{
    private static readonly RawElement Page = new("WebArea", "Hacker News", new ScreenRect(0, 100, 1000, 700), true, false, "", null);
    private static readonly RawElement Toolbar = new("Button", "Reload", new ScreenRect(10, 10, 30, 30), true, false, "", null);

    [Fact]
    public void A_page_with_nothing_inside_is_empty()
    {
        MacAdapter.HasEmptyWebPage([Toolbar, Page]).ShouldBeTrue();
    }

    [Fact]
    public void A_page_with_links_inside_is_not_empty()
    {
        var link = new RawElement("Hyperlink", "new", new ScreenRect(50, 150, 30, 14), true, false, "", null);

        MacAdapter.HasEmptyWebPage([Toolbar, Page, link]).ShouldBeFalse();
    }

    [Fact]
    public void A_window_without_a_page_is_not_waited_for()
    {
        MacAdapter.HasEmptyWebPage([Toolbar]).ShouldBeFalse();
    }
}
