using Vera.Core;

namespace Vera.Adapters.Desktop;

/// <summary>Where to click an element. Shared with the macOS adapter, which links this file.</summary>
internal static class ClickPoint
{
    /// <summary>
    /// The point to click for an element, in the window screenshot's pixels: the centre of the part of the element inside the window.
    /// Null when no part of it is inside. Both rectangles must be in the screenshot's pixels.
    /// </summary>
    public static (int X, int Y)? Of(ScreenRect element, ScreenRect window)
    {
        var left = Math.Max(element.X, window.X);
        var top = Math.Max(element.Y, window.Y);
        var right = Math.Min(element.X + element.Width, window.X + window.Width);
        var bottom = Math.Min(element.Y + element.Height, window.Y + window.Height);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        return (left + ((right - left) / 2) - window.X, top + ((bottom - top) / 2) - window.Y);
    }
}
