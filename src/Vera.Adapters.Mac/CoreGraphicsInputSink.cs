using System.Runtime.InteropServices;

using Vera.Input;

namespace Vera.Adapters.Mac;

/// <summary>
/// The only place that posts mouse and keyboard events on macOS (<c>CGEventPost</c>). macOS drops them silently unless the app
/// VERA runs from has the Accessibility permission, so <see cref="MacScreen"/> checks that before every action.
/// </summary>
internal sealed partial class CoreGraphicsInputSink : IMacInputSink
{
    private const string CoreGraphicsLibrary = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const uint HidEventTap = 0;
    private const int HidSystemState = 1;
    private const uint MouseEventClickState = 1;

    private const uint LeftMouseDown = 1;
    private const uint LeftMouseUp = 2;
    private const uint RightMouseDown = 3;
    private const uint RightMouseUp = 4;
    private const uint MouseMoved = 5;
    private const uint OtherMouseDown = 25;
    private const uint OtherMouseUp = 26;
    private const uint LeftMouseDragged = 6;
    private const uint RightMouseDragged = 7;
    private const uint OtherMouseDragged = 27;
    private const uint ScrollUnitLine = 1;

    /// <summary>
    /// The modifier flags VERA sets itself. Other flags macOS puts on a new key event (e.g. "function key" on the arrows) are kept.
    /// </summary>
    private const ulong ModifierFlags = MacKeyCombo.CommandFlag | MacKeyCombo.ShiftFlag | MacKeyCombo.OptionFlag | ControlFlag;
    private const ulong ControlFlag = 0x40000;

    /// <summary>The virtual key the text events ride on. Apps read the text, not the key, so any key works.</summary>
    private const ushort TextCarrierKey = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct CgPoint
    {
        public double X;
        public double Y;
    }

    public void Send(MacInputEvent e)
    {
        var source = CGEventSourceCreate(HidSystemState);
        try
        {
            var cgEvent = e switch
            {
                MacInputEvent.MouseMove m => CGEventCreateMouseEvent(source, MouseMoved, Point(m.X, m.Y), 0),
                MacInputEvent.MouseButtonEvent b => MouseButton(source, b),
                MacInputEvent.MouseDrag d => CGEventCreateMouseEvent(source, DragType(d.Button), Point(d.X, d.Y), ButtonNumber(d.Button)),
                MacInputEvent.Scroll s => CGEventCreateScrollWheelEvent2(source, ScrollUnitLine, 2, s.Vertical, s.Horizontal, 0),
                MacInputEvent.Key k => Key(source, k),
                MacInputEvent.Text t => Text(source, t),
                _ => throw new ArgumentOutOfRangeException(nameof(e), e, "Unknown input event."),
            };

            if (cgEvent == IntPtr.Zero)
            {
                throw new InputRejectedException($"macOS could not make the input event {e}.");
            }

            try
            {
                CGEventPost(HidEventTap, cgEvent);
            }
            finally
            {
                MacNative.CFRelease(cgEvent);
            }
        }
        finally
        {
            if (source != IntPtr.Zero)
            {
                MacNative.CFRelease(source);
            }
        }
    }

    public (double X, double Y)? Cursor()
    {
        var cgEvent = CGEventCreate(IntPtr.Zero);
        if (cgEvent == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var point = CGEventGetLocation(cgEvent);
            return (point.X, point.Y);
        }
        finally
        {
            MacNative.CFRelease(cgEvent);
        }
    }

    private static uint DragType(Vera.Input.MouseButton button) => button switch
    {
        Vera.Input.MouseButton.Left => LeftMouseDragged,
        Vera.Input.MouseButton.Right => RightMouseDragged,
        Vera.Input.MouseButton.Middle => OtherMouseDragged,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown mouse button."),
    };

    private static uint ButtonNumber(Vera.Input.MouseButton button) => button switch
    {
        Vera.Input.MouseButton.Left => 0,
        Vera.Input.MouseButton.Right => 1,
        Vera.Input.MouseButton.Middle => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown mouse button."),
    };

    private static IntPtr MouseButton(IntPtr source, MacInputEvent.MouseButtonEvent b)
    {
        var (type, button) = b.Button switch
        {
            Vera.Input.MouseButton.Left => (b.Down ? LeftMouseDown : LeftMouseUp, 0u),
            Vera.Input.MouseButton.Right => (b.Down ? RightMouseDown : RightMouseUp, 1u),
            Vera.Input.MouseButton.Middle => (b.Down ? OtherMouseDown : OtherMouseUp, 2u),
            _ => throw new ArgumentOutOfRangeException(nameof(b), b.Button, "Unknown mouse button."),
        };

        var cgEvent = CGEventCreateMouseEvent(source, type, Point(b.X, b.Y), button);
        if (cgEvent != IntPtr.Zero)
        {
            CGEventSetIntegerValueField(cgEvent, MouseEventClickState, b.ClickState);
        }

        return cgEvent;
    }

    private static IntPtr Key(IntPtr source, MacInputEvent.Key k)
    {
        var cgEvent = CGEventCreateKeyboardEvent(source, k.KeyCode, k.Down);
        if (cgEvent != IntPtr.Zero)
        {
            // Set exactly the modifiers asked for, so a key the person holds doesn't change what VERA presses.
            CGEventSetFlags(cgEvent, (CGEventGetFlags(cgEvent) & ~ModifierFlags) | k.Flags);
        }

        return cgEvent;
    }

    private static IntPtr Text(IntPtr source, MacInputEvent.Text t)
    {
        var cgEvent = CGEventCreateKeyboardEvent(source, TextCarrierKey, t.Down);
        if (cgEvent != IntPtr.Zero)
        {
            CGEventSetFlags(cgEvent, CGEventGetFlags(cgEvent) & ~ModifierFlags);
            CGEventKeyboardSetUnicodeString(cgEvent, (nuint)t.Character.Length, t.Character);
        }

        return cgEvent;
    }

    private static CgPoint Point(double x, double y) => new() { X = x, Y = y };

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial IntPtr CGEventSourceCreate(int state);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial IntPtr CGEventCreateMouseEvent(IntPtr source, uint type, CgPoint position, uint button);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keyCode, [MarshalAs(UnmanagedType.U1)] bool keyDown);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial void CGEventSetIntegerValueField(IntPtr cgEvent, uint field, long value);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial ulong CGEventGetFlags(IntPtr cgEvent);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial void CGEventSetFlags(IntPtr cgEvent, ulong flags);

    [LibraryImport(CoreGraphicsLibrary, StringMarshalling = StringMarshalling.Utf16)]
    private static partial void CGEventKeyboardSetUnicodeString(IntPtr cgEvent, nuint length, string text);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial void CGEventPost(uint tap, IntPtr cgEvent);

    /// <summary>The fixed-argument version of <c>CGEventCreateScrollWheelEvent</c>, whose "..." arguments .NET can't pass reliably on Apple silicon.</summary>
    [LibraryImport(CoreGraphicsLibrary)]
    private static partial IntPtr CGEventCreateScrollWheelEvent2(IntPtr source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);

    /// <summary>An empty event; its location is where the pointer is now.</summary>
    [LibraryImport(CoreGraphicsLibrary)]
    private static partial IntPtr CGEventCreate(IntPtr source);

    [LibraryImport(CoreGraphicsLibrary)]
    private static partial CgPoint CGEventGetLocation(IntPtr cgEvent);
}
