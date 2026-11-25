using Vera.Core;

namespace Vera.Application.Tests;

public sealed class ScrollDistanceTests
{
    [Theory]
    [InlineData(30, 3, 6)]   // 10 pixels a notch: 18 would be 60%, but at most twice as many.
    [InlineData(120, 12, 18)]
    [InlineData(250, 1, 1)]  // At least one notch.
    [InlineData(40, 40, 50)] // At most what a scroll step may.
    public void The_next_scroll_moves_about_60_percent_of_the_window(int moved, int notches, int expected) =>
        ScrollDistance.Next(Screen(("Toolbar", 0), ("A", 280)), Screen(("Toolbar", 0), ("A", 280 - moved)), vertical: true, forward: true, notches).ShouldBe(expected);

    [Fact]
    public void Scrolling_up_measures_what_moved_down() =>
        ScrollDistance.Next(Screen(("A", 20)), Screen(("A", 140)), vertical: true, forward: false, 12).ShouldBe(18);

    [Fact]
    public void A_header_that_shrinks_does_not_count_as_the_page() =>
        ScrollDistance.Next(Screen(("Header", 40), ("A", 290)), Screen(("Header", 20), ("A", 50)), vertical: true, forward: true, 3).ShouldBe(2);

    [Fact]
    public void One_wrong_measurement_is_not_a_big_jump() =>
        ScrollDistance.Next(Screen(("Header", 40), ("A", 100)), Screen(("Header", 20), ("B", 100)), vertical: true, forward: true, 3).ShouldBe(6);

    [Fact]
    public void Something_that_moved_the_other_way_is_not_the_page() =>
        ScrollDistance.Next(Screen(("Toolbar", 0), ("Bar", 10)), Screen(("Toolbar", 0), ("Bar", 15)), vertical: true, forward: true, 3).ShouldBe(3);

    [Fact]
    public void After_a_jump_past_everything_it_scrolls_less()
    {
        var before = Screen(("Toolbar", 0), ("A", 10));
        var after = Screen(("Toolbar", 0), ("B", 10));

        ScrollDistance.Jumped(before, after, vertical: true).ShouldBeTrue();
        ScrollDistance.Next(before, after, vertical: true, forward: true, 3).ShouldBe(1);
    }

    [Fact]
    public void When_nothing_can_be_measured_it_scrolls_the_same_again()
    {
        ScrollDistance.Jumped(Screen(("Toolbar", 0)), Screen(("Toolbar", 0)), vertical: true).ShouldBeFalse();
        ScrollDistance.Next(Screen(("Toolbar", 0)), Screen(("Toolbar", 0)), vertical: true, forward: true, 3).ShouldBe(3);
    }

    private static ScreenState Screen(params (string Name, int Y)[] elements) =>
        new("App", [new UiElement(0, "Window", "App", new ScreenRect(0, 0, 400, 300), true, "window"), .. elements.Select((e, i) =>
            new UiElement(i + 1, "Button", e.Name, new ScreenRect(0, e.Y, 10, 10), true, e.Name))], ReadOnlyMemory<byte>.Empty, []);
}
