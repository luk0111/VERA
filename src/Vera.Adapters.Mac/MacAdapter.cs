using System.Diagnostics;

using Vera.Adapters.Desktop;
using Vera.Core;
using Vera.Input;

namespace Vera.Adapters.Mac;

/// <summary>
/// Reads a running Mac app's window: its elements through the Accessibility API, its picture through <see cref="IScreenshotSource"/>.
/// As on Windows, it only attaches and looks for now: starting an app and acting on it come with the agent loop.
/// </summary>
public sealed class MacAdapter(IScreenshotSource screen) : IAppAdapter
{
    private const string WindowGone = "The app is closed or has no window.";
    private const string WindowMinimized = "The app's window is minimized. Restore it and try again.";
    private const string AccessibilityMissing =
        "VERA can't read app windows yet. In System Settings, open Privacy & Security > Accessibility, turn on the app you started VERA from (Terminal or Rider), then restart it.";

    /// <summary>Stops a read from running for minutes on apps with huge trees, such as browsers.</summary>
    private const int MaxNodes = 5000;
    private const int MaxDepth = 40;

    /// <summary>How long an unresponsive app can hold up one question.</summary>
    private const float AppTimeoutSeconds = 2;

    /// <summary>How long macOS gets to redraw a window it just brought to the front.</summary>
    private static readonly TimeSpan RedrawTime = TimeSpan.FromMilliseconds(200);

    private IntPtr _app;

    /// <summary>Attaches to <see cref="AppTarget.ProcessId"/>, the app the picker showed.</summary>
    /// <exception cref="NotSupportedException">The target has no process id.</exception>
    /// <exception cref="InvalidOperationException">The process is gone, or VERA has no Accessibility permission.</exception>
    public Task StartAsync(AppTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ProcessId is not { } processId)
        {
            throw new NotSupportedException("Starting an app is not supported yet. Pick a running app.");
        }

        try
        {
            using var process = Process.GetProcessById(processId);
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException(WindowGone, e);
        }

        if (!MacNative.AccessibilityAllowedOrPrompt())
        {
            throw new InvalidOperationException(AccessibilityMissing);
        }

        Release();
        _app = MacNative.AXUIElementCreateApplication(processId);
        _ = MacNative.AXUIElementSetMessagingTimeout(_app, AppTimeoutSeconds);
        return Task.CompletedTask;
    }

    /// <exception cref="InvalidOperationException">The window is gone or minimized, or the screen can't be seen.</exception>
    public Task<ScreenState> GetScreenStateAsync(CancellationToken ct)
    {
        if (_app == IntPtr.Zero)
        {
            throw new InvalidOperationException("Call StartAsync first.");
        }

        // Accessibility calls block, so they run off the caller's thread.
        return Task.Run(() => ReadWindowAsync(ct), ct);
    }

    public Task ClickAsync(int elementId, CancellationToken ct) => throw ActingNotSupported();

    public Task TypeAsync(int elementId, string text, CancellationToken ct) => throw ActingNotSupported();

    public Task PressKeyAsync(string keys, CancellationToken ct) => throw ActingNotSupported();

    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }

    private async Task<ScreenState> ReadWindowAsync(CancellationToken ct)
    {
        var window = CopyMainWindow();
        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException(WindowGone);
        }

        try
        {
            if (ReadBool(window, "AXMinimized") == true)
            {
                throw new InvalidOperationException(WindowMinimized);
            }

            // A screenshot copies what is on screen, so a window in front of the app would be sent to the model instead.
            if (ReadBool(_app, "AXFrontmost") != true)
            {
                _ = MacNative.AXUIElementSetAttributeValue(_app, MacNative.Key("AXFrontmost"), MacNative.CfTrue());
                _ = MacNative.AXUIElementPerformAction(window, MacNative.Key("AXRaise"));
                await Task.Delay(RedrawTime, ct).ConfigureAwait(false);
            }

            var bounds = ReadBounds(window) ?? throw new InvalidOperationException(WindowGone);
            var title = ReadString(window, "AXTitle") ?? "";

            var candidates = new List<RawElement>();
            var visited = 0;
            ReadChildren(window, 0, candidates, ref visited, ct);

            var frame = await screen.ScreenshotAreaAsync(bounds.X, bounds.Y, bounds.Width, bounds.Height, ct).ConfigureAwait(false);
            var scaled = candidates.ConvertAll(c => MacRoles.ToImagePixels(c, frame.Scale));
            return new ScreenState(title, ElementNumbering.Number(title, MacRoles.ToImagePixels(bounds, frame.Scale), scaled), frame.Png, []);
        }
        catch (ScreenUnavailableException e)
        {
            throw new InvalidOperationException(e.Message, e);
        }
        finally
        {
            MacNative.CFRelease(window);
        }
    }

    // Main window first; an app in the middle of launching may only list its windows.
    private IntPtr CopyMainWindow()
    {
        if (Copy(_app, "AXMainWindow") is var main && main != IntPtr.Zero)
        {
            return main;
        }

        var windows = Copy(_app, "AXWindows");
        if (windows == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            return MacNative.IsArray(windows) && MacNative.CFArrayGetCount(windows) > 0
                ? MacNative.CFRetain(MacNative.CFArrayGetValueAtIndex(windows, 0))
                : IntPtr.Zero;
        }
        finally
        {
            MacNative.CFRelease(windows);
        }
    }

    private static void ReadChildren(IntPtr parent, int depth, List<RawElement> found, ref int visited, CancellationToken ct)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        var children = Copy(parent, "AXChildren");
        if (children == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (!MacNative.IsArray(children))
            {
                return;
            }

            var count = MacNative.CFArrayGetCount(children);
            for (nint i = 0; i < count && visited < MaxNodes; i++)
            {
                ct.ThrowIfCancellationRequested();
                visited++;
                var child = MacNative.CFArrayGetValueAtIndex(children, i);
                if (TryRead(child) is { } raw)
                {
                    found.Add(raw);
                }

                ReadChildren(child, depth + 1, found, ref visited, ct);
            }
        }
        finally
        {
            MacNative.CFRelease(children);
        }
    }

    // An element can vanish while the tree is read; skip it rather than fail the whole read.
    private static RawElement? TryRead(IntPtr element)
    {
        if (ReadString(element, "AXRole") is not { } role || ReadBounds(element) is not { } bounds)
        {
            return null;
        }

        return new RawElement(
            MacRoles.ToCommon(role),
            MacRoles.Name(ReadString(element, "AXTitle"), ReadString(element, "AXDescription"), ReadString(element, "AXHelp")),
            bounds,
            ReadBool(element, "AXEnabled") ?? true,
            IsOffscreen: false, // macOS has no such flag; numbering drops elements outside the window.
            ReadString(element, "AXIdentifier") ?? "",
            ReadString(element, "AXValue"));
    }

    private static ScreenRect? ReadBounds(IntPtr element)
    {
        if (ReadPair(element, "AXPosition", MacNative.AxValueCgPoint) is not { } position
            || ReadPair(element, "AXSize", MacNative.AxValueCgSize) is not { } size)
        {
            return null;
        }

        return new ScreenRect(
            (int)Math.Round(position.First),
            (int)Math.Round(position.Second),
            (int)Math.Round(size.First),
            (int)Math.Round(size.Second));
    }

    private static MacNative.DoublePair? ReadPair(IntPtr element, string attribute, int type)
    {
        var value = Copy(element, attribute);
        if (value == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return MacNative.AXValueGetValue(value, type, out var pair) ? pair : null;
        }
        finally
        {
            MacNative.CFRelease(value);
        }
    }

    private static string? ReadString(IntPtr element, string attribute)
    {
        var value = Copy(element, attribute);
        try
        {
            return MacNative.ReadString(value);
        }
        finally
        {
            if (value != IntPtr.Zero)
            {
                MacNative.CFRelease(value);
            }
        }
    }

    private static bool? ReadBool(IntPtr element, string attribute)
    {
        var value = Copy(element, attribute);
        try
        {
            return MacNative.ReadBool(value);
        }
        finally
        {
            if (value != IntPtr.Zero)
            {
                MacNative.CFRelease(value);
            }
        }
    }

    private static IntPtr Copy(IntPtr element, string attribute) =>
        MacNative.AXUIElementCopyAttributeValue(element, MacNative.Key(attribute), out var value) == MacNative.AxSuccess ? value : IntPtr.Zero;

    private void Release()
    {
        if (_app != IntPtr.Zero)
        {
            MacNative.CFRelease(_app);
            _app = IntPtr.Zero;
        }
    }

    private static NotSupportedException ActingNotSupported() => new("Acting on the app comes with the agent loop.");
}
