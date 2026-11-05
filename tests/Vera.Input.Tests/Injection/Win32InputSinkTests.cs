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

    [Fact]
    public void A_character_is_sent_as_unicode()
    {
        var down = Win32InputSink.ToInput(InputEvent.CharDown('ș'));
        var up = Win32InputSink.ToInput(InputEvent.CharUp('ș'));

        down.type.ShouldBe(INPUT_TYPE.INPUT_KEYBOARD);
        down.Anonymous.ki.wScan.ShouldBe((ushort)'ș');
        down.Anonymous.ki.wVk.ShouldBe((VIRTUAL_KEY)0);
        down.Anonymous.ki.dwFlags.ShouldBe(KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE);
        up.Anonymous.ki.dwFlags.ShouldBe(KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP);
    }

    [Fact]
    public void A_key_is_sent_by_scancode()
    {
        var down = Win32InputSink.ToInput(InputEvent.KeyDown(KeyboardDriver.EnterScanCode));
        var up = Win32InputSink.ToInput(InputEvent.KeyUp(KeyboardDriver.EnterScanCode));

        down.type.ShouldBe(INPUT_TYPE.INPUT_KEYBOARD);
        down.Anonymous.ki.wScan.ShouldBe(KeyboardDriver.EnterScanCode);
        down.Anonymous.ki.dwFlags.ShouldBe(KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE);
        up.Anonymous.ki.dwFlags.ShouldBe(KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP);
    }
}
