using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Vera.Input.Injection;

/// <summary>Turns <see cref="InputEvent"/>s into Win32 <c>INPUT</c>s (mouse or keyboard) and injects them with <c>SendInput</c>.</summary>
internal sealed unsafe partial class Win32InputSink(ILogger<Win32InputSink> logger) : IInputSink
{
    public void Send(params IReadOnlyList<InputEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        var inputs = new INPUT[events.Count];
        for (var i = 0; i < inputs.Length; i++)
        {
            inputs[i] = ToInput(events[i]);
        }

        var inserted = PInvoke.SendInput(inputs, sizeof(INPUT));
        if (inserted != inputs.Length)
        {
            // Usually UIPI (the target runs as administrator and VERA doesn't) or a locked desktop.
            var error = Marshal.GetLastPInvokeError();
            var detail = new Win32Exception(error).Message;
            LogRejected(logger, inserted, inputs.Length, error, detail);
            throw new InputRejectedException(
                $"Windows injected {inserted} of {inputs.Length} input events (error {error}: {detail}). " +
                "Is the target app running as administrator, or is the screen locked?");
        }
    }

    internal static INPUT ToInput(InputEvent e) => e.Kind switch
    {
        InputEventKind.MouseMove => Mouse(new MOUSEINPUT
        {
            dx = e.Point.X,
            dy = e.Point.Y,
            dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK,
        }),
        InputEventKind.MouseDown => Mouse(new MOUSEINPUT { dwFlags = ButtonFlag(e.Button, down: true) }),
        InputEventKind.MouseUp => Mouse(new MOUSEINPUT { dwFlags = ButtonFlag(e.Button, down: false) }),
        InputEventKind.CharDown => Keyboard(e.Char, KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE),
        InputEventKind.CharUp => Keyboard(e.Char, KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP),
        InputEventKind.KeyDown => Keyboard(e.ScanCode, KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE),
        InputEventKind.KeyUp => Keyboard(e.ScanCode, KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP),
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.Kind, "Unknown input event kind."),
    };

    private static INPUT Mouse(MOUSEINPUT mouse) => new() { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = mouse } };

    /// <summary>
    /// <paramref name="scan"/> is a UTF-16 unit with <c>KEYEVENTF_UNICODE</c>, or a scancode with
    /// <c>KEYEVENTF_SCANCODE</c>. The virtual-key code stays 0 in both cases.
    /// </summary>
    private static INPUT Keyboard(ushort scan, KEYBD_EVENT_FLAGS flags) =>
        new() { type = INPUT_TYPE.INPUT_KEYBOARD, Anonymous = { ki = new KEYBDINPUT { wScan = scan, dwFlags = flags } } };

    private static MOUSE_EVENT_FLAGS ButtonFlag(MouseButton button, bool down) => button switch
    {
        MouseButton.Left => down ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP,
        MouseButton.Right => down ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP,
        MouseButton.Middle => down ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown mouse button."),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "SendInput injected {Inserted} of {Count} events (error {Error}: {Detail})")]
    private static partial void LogRejected(ILogger logger, uint inserted, int count, int error, string detail);
}
