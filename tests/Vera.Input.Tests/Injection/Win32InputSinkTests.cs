using Vera.Input.Coordinates;
using Vera.Input.Injection;

using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Vera.Input.Tests.Injection;

public sealed class Win32InputSinkTests
{
    [Fact]
    public void A_move_is_absolute_over_all_monitors()
    {
        var input = Win32InputSink.ToInput(InputEvent.MouseMove(new NormalizedPoint(32768, 1000)));

        input.type.ShouldBe(INPUT_TYPE.INPUT_MOUSE);
        input.Anonymous.mi.dx.ShouldBe(32768);
        input.Anonymous.mi.dy.ShouldBe(1000);
        input.Anonymous.mi.dwFlags.ShouldBe(
            MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK);
    }

    [Theory]
    [InlineData(MouseButton.Left, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP)]
    [InlineData(MouseButton.Right, MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP)]
    [InlineData(MouseButton.Middle, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP)]
    internal void Down_and_up_use_the_buttons_flags_and_do_not_move(MouseButton button, MOUSE_EVENT_FLAGS down, MOUSE_EVENT_FLAGS up)
    {
        var downInput = Win32InputSink.ToInput(InputEvent.MouseDown(button));
        var upInput = Win32InputSink.ToInput(InputEvent.MouseUp(button));

        downInput.Anonymous.mi.dwFlags.ShouldBe(down);
        upInput.Anonymous.mi.dwFlags.ShouldBe(up);
        downInput.Anonymous.mi.dx.ShouldBe(0);
        downInput.Anonymous.mi.dy.ShouldBe(0);
    }
}
