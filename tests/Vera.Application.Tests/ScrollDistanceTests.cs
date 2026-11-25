using Vera.Core;

namespace Vera.Application.Tests;

public sealed class ScrollDistanceTests
{
    [Theory]
    [InlineData(30, 3, 18)]  // 10 pixels a notch.
    [InlineData(250, 1, 1)]  // At least one notch.
    [InlineData(1, 3, 50)]   // At most what a scroll step may.
    public void The_next_scroll_moves_about_60_percent_of_the_window(int moved, int notches, int expected) =>
        ScrollDistance.Next(Screen(("Toolbar", 0), ("A", 280)), Screen(("Toolbar", 0), ("A", 280 - moved)), vertical: true, notches).ShouldBe(expected);

    [Fact]
    public void After_a_jump_past_everything_it_scrolls_less() =>
        ScrollDistance.Next(Screen(("Toolbar", 0), ("A", 10)), Screen(("Toolbar", 0), ("B", 10)), vertical: true, 3).ShouldBe(1);

    [Fact]
    public void When_nothing_can_be_measured_it_scrolls_the_same_again() =>
        ScrollDistance.Next(Screen(("Toolbar", 0)), Screen(("Toolbar", 0)), vertical: true, 3).ShouldBe(3);

    private static ScreenState Screen(params (string Name, int Y)[] elements) =>
        new("App", [new UiElement(0, "Window", "App", new ScreenRect(0, 0, 400, 300), true, "window"), .. elements.Select((e, i) =>
            new UiElement(i + 1, "Button", e.Name, new ScreenRect(0, e.Y, 10, 10), true, e.Name))], ReadOnlyMemory<byte>.Empty, []);
}
