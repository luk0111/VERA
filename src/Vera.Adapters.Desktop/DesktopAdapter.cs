using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;

using Vera.Core;

namespace Vera.Adapters.Desktop;

/// <summary>
/// Reads a running desktop app's window through UI Automation (FlaUI). For now it only attaches and looks:
/// starting an app and acting on it come with the agent loop.
/// </summary>
public sealed class DesktopAdapter : IAppAdapter
{
    private const string WindowGone = "The app is closed or has no window.";

    private readonly UIA3Automation _automation = new();
    private IntPtr _window;

    /// <summary>Attaches to <see cref="AppTarget.ProcessId"/>'s main window, the window the process picker showed.</summary>
    /// <exception cref="NotSupportedException">The target has no process id.</exception>
    /// <exception cref="InvalidOperationException">The process is gone or has no window.</exception>
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
            _window = process.MainWindowHandle;
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException(WindowGone, e);
        }

        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException(WindowGone);
        }

        return Task.CompletedTask;
    }

    /// <exception cref="InvalidOperationException">The window is gone.</exception>
    public Task<ScreenState> GetScreenStateAsync(CancellationToken ct)
    {
        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException("Call StartAsync first.");
        }

        // UI Automation calls block, so they run off the caller's thread.
        return Task.Run(() => ReadWindow(ct), ct);
    }

    public Task ClickAsync(int elementId, CancellationToken ct) => throw ActingNotSupported();

    public Task TypeAsync(int elementId, string text, CancellationToken ct) => throw ActingNotSupported();

    public Task PressKeyAsync(string keys, CancellationToken ct) => throw ActingNotSupported();

    public ValueTask DisposeAsync()
    {
        _automation.Dispose();
        return ValueTask.CompletedTask;
    }

    private ScreenState ReadWindow(CancellationToken ct)
    {
        try
        {
            var window = _automation.FromHandle(_window);
            var r = window.BoundingRectangle;
            var bounds = new ScreenRect(r.X, r.Y, r.Width, r.Height);
            var title = window.Properties.Name.ValueOrDefault ?? "";

            var candidates = new List<RawElement>();
            foreach (var element in window.FindAllDescendants())
            {
                ct.ThrowIfCancellationRequested();
                if (TryRead(element) is { } raw)
                {
                    candidates.Add(raw);
                }
            }

            return new ScreenState(title, ElementNumbering.Number(title, bounds, candidates), Screenshot(r), []);
        }
        // FlaUI reports a window that has gone as ElementNotAvailableException, which has no message of its own.
        catch (Exception e) when (e is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
            throw new InvalidOperationException(WindowGone, e);
        }
    }

    // An element can vanish while the tree is read; skip it rather than fail the whole read.
    private static RawElement? TryRead(AutomationElement element)
    {
        try
        {
            var p = element.Properties;
            var r = p.BoundingRectangle.ValueOrDefault;
            return new RawElement(
                p.ControlType.ValueOrDefault.ToString(),
                p.Name.ValueOrDefault ?? "",
                new ScreenRect(r.X, r.Y, r.Width, r.Height),
                p.IsEnabled.ValueOrDefault,
                p.IsOffscreen.ValueOrDefault,
                p.AutomationId.ValueOrDefault ?? "",
                element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static byte[] Screenshot(System.Drawing.Rectangle area)
    {
        using var capture = Capture.Rectangle(area);
        using var png = new MemoryStream();
        capture.Bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    private static NotSupportedException ActingNotSupported() => new("Acting on the app comes with the agent loop.");
}
