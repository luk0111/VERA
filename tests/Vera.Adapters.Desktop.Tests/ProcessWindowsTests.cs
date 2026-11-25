namespace Vera.Adapters.Desktop.Tests;

// #44: Brave runs all its windows in one process, and its main window isn't always the one the tester picked.
public sealed class ProcessWindowsTests
{
    private const nint Main = 1;

    [Fact]
    public void The_window_with_the_picked_title_is_used()
    {
        var windows = new List<(nint, string)> { (1, "News - Brave"), (2, "Stack Overflow - Brave") };

        ProcessWindows.Pick(windows, "Stack Overflow - Brave", Main).ShouldBe(2);
    }

    [Fact]
    public void Without_a_window_of_that_title_the_main_window_is_used()
    {
        var windows = new List<(nint, string)> { (1, "News - Brave"), (2, "Stack Overflow - Brave") };

        ProcessWindows.Pick(windows, "A tab that was closed - Brave", Main).ShouldBe(Main);
        ProcessWindows.Pick([], "Untitled - Notepad", Main).ShouldBe(Main);
    }

    [Theory]
    [InlineData(true, true, true, true)]     // the app's own window
    [InlineData(true, true, false, true)]    // a dialog it owns: its root owner is the app's window
    [InlineData(false, true, false, true)]   // a popup or menu of the same process
    [InlineData(false, true, true, false)]   // another main window of the same process, e.g. a second Brave window
    [InlineData(false, false, true, false)]  // another app
    public void What_counts_as_the_app_being_in_front(bool sameRootWindow, bool sameProcess, bool frontIsMainLike, bool counts)
    {
        Win32ForegroundApi.Counts(sameRootWindow, sameProcess, frontIsMainLike).ShouldBe(counts);
    }
}
