using System.ComponentModel;

using Vera.Input.Coordinates;

using Windows.Win32;

namespace Vera.Input.Platform;

/// <summary>Reads where the mouse cursor is, in physical screen pixels (the process must be Per-Monitor V2 DPI aware).</summary>
public interface ICursorPosition
{
    /// <exception cref="Win32Exception">Windows refused, e.g. while the workstation is locked.</exception>
    ScreenPoint Read();
}

internal sealed class Win32CursorPosition : ICursorPosition
{
    public ScreenPoint Read()
    {
        if (!PInvoke.GetCursorPos(out var point))
        {
            throw new Win32Exception();
        }

        return new ScreenPoint(point.X, point.Y);
    }
}
