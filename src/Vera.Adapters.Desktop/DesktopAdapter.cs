using System.Diagnostics;
using System.Runtime.InteropServices;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

using Vera.Core;
using Vera.Input;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Vera.Adapters.Desktop;

/// <summary>
/// Sees and drives a running desktop app's window: its elements through UI Automation (FlaUI), its picture, mouse and keyboard through <see cref="IScreen"/>.
/// Actions use the latest <see cref="GetScreenStateAsync"/>: its element ids, and its screenshot for mapping clicks, so read the screen right before acting.
/// Starting an app is not supported yet: it attaches to a running one.
/// </summary>
public sealed class DesktopAdapter(IScreen screen) : IAppAdapter
{
    private const string WindowGone = "The app is closed or has no window.";
    private const string WindowMinimized = "The app's window is minimized. Restore it and try again.";
    private const string WindowMoved = "The window moved since VERA last looked at it. Read it again.";
    private const string WindowOffScreen = "The window is off the screen. Move it fully onto the screen and try again.";

    /// <summary>How long Windows gets to redraw a window it just brought to the front.</summary>
    private static readonly TimeSpan RedrawTime = TimeSpan.FromMilliseconds(200);

    private readonly UIA3Automation _automation = new();
    private readonly ForegroundSwitch _foreground = new(new Win32ForegroundApi());
    private IntPtr _window;

    // The latest reading. Its window bounds are also the latest screenshot's area, which clicks are mapped with.
    private ScreenState? _state;
    private ScreenRect? _windowBounds;

    /// <summary>
    /// Attaches to <see cref="AppTarget.ProcessId"/>'s window titled <see cref="AppTarget.Name"/>, the window the process picker showed;
    /// to the process's main window when none has that title.
    /// </summary>
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

            // shortcut: matched by title, so after the picked window's title changed (another tab) it falls back to the main window. Upgrade when AppTarget carries a window handle (Core v2).
            _window = ProcessWindows.Pick(ProcessWindows.Of(processId), target.Name, process.MainWindowHandle);
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

    /// <exception cref="InvalidOperationException">The window is gone, minimized or can't be brought to the front, or the screen can't be seen.</exception>
    public Task<ScreenState> GetScreenStateAsync(CancellationToken ct)
    {
        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException("Call StartAsync first.");
        }

        // UI Automation calls block, so they run off the caller's thread.
        return Task.Run(() => ReadWindowAsync(ct), ct);
    }

    /// <summary>Clicks the centre of the element's visible part, after bringing the window to the front.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The id is not an element of the latest reading.</exception>
    /// <exception cref="InvalidOperationException">No reading yet, or the window is gone, minimized or moved, or the screen can't be controlled.</exception>
    public async Task ClickAsync(int elementId, CancellationToken ct)
    {
        var (state, window) = LatestReading();
        var point = PointOf(state, window, elementId);

        await BeforeActingAsync(window, ct).ConfigureAwait(false);
        await ActAsync(() => screen.ClickAsync(point.X, point.Y, ct: ct)).ConfigureAwait(false);
    }

    /// <summary>Scrolls the wheel over the centre of the element's visible part, after bringing the window to the front.</summary>
    /// <inheritdoc cref="ClickAsync" path="/exception"/>
    public async Task ScrollAsync(int elementId, int deltaY, int deltaX, CancellationToken ct)
    {
        var (state, window) = LatestReading();
        var point = PointOf(state, window, elementId, orWindow: true);

        await BeforeActingAsync(window, ct).ConfigureAwait(false);
        await ActAsync(() => screen.ScrollAsync(point.X, point.Y, deltaY, deltaX, ct)).ConfigureAwait(false);
    }

    /// <summary>Drags from the centre of one element's visible part to the other's, after bringing the window to the front.</summary>
    /// <inheritdoc cref="ClickAsync" path="/exception"/>
    public async Task DragAsync(int fromElementId, int toElementId, CancellationToken ct)
    {
        var (state, window) = LatestReading();
        var from = PointOf(state, window, fromElementId);
        var to = PointOf(state, window, toElementId);

        await BeforeActingAsync(window, ct).ConfigureAwait(false);
        await ActAsync(() => screen.DragAsync(from.X, from.Y, to.X, to.Y, ct: ct)).ConfigureAwait(false);
    }

    /// <summary>Glides the pointer onto the centre of the element's visible part and rests there, after bringing the window to the front.</summary>
    /// <inheritdoc cref="ClickAsync" path="/exception"/>
    public async Task HoverAsync(int elementId, CancellationToken ct)
    {
        var (state, window) = LatestReading();
        var point = PointOf(state, window, elementId);

        await BeforeActingAsync(window, ct).ConfigureAwait(false);
        await ActAsync(() => screen.MoveAsync(point.X, point.Y, ct)).ConfigureAwait(false);
    }

    // The point to act on for an element of the latest reading, in its screenshot's pixels.
    // Element 0, the window, only to scroll in: a click in its middle would hit whatever happens to be there.
    private static (int X, int Y) PointOf(ScreenState state, ScreenRect window, int elementId, bool orWindow = false)
    {
        var element = state.Elements.FirstOrDefault(e => e.Id >= (orWindow ? 0 : 1) && e.Id == elementId)
            ?? throw new ArgumentOutOfRangeException(nameof(elementId), elementId, $"Element {elementId} is not on the latest screen.");
        return ClickPoint.Of(element.Bounds, window)
            ?? throw new InvalidOperationException($"Element {elementId} is outside the window or off the screen. Move the window fully onto the screen.");
    }

    /// <summary>The part of <paramref name="window"/> inside <paramref name="desktop"/>; null when none of it is.</summary>
    internal static ScreenRect? VisiblePart(ScreenRect window, ScreenRect desktop)
    {
        var left = Math.Max(window.X, desktop.X);
        var top = Math.Max(window.Y, desktop.Y);
        var right = Math.Min(window.X + window.Width, desktop.X + desktop.Width);
        var bottom = Math.Min(window.Y + window.Height, desktop.Y + desktop.Height);
        return right > left && bottom > top ? new ScreenRect(left, top, right - left, bottom - top) : null;
    }

    /// <summary>
    /// The monitor most of the window is on, in physical pixels (VERA is DPI aware). Not the rectangle around all monitors:
    /// with monitors of different sizes it has corners no monitor shows, where the mouse can't go.
    /// </summary>
    internal static ScreenRect MonitorOf(nint window)
    {
        var monitor = PInvoke.MonitorFromWindow(new HWND(window), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!PInvoke.GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException(WindowOffScreen);
        }

        var r = info.rcMonitor;
        return new ScreenRect(r.left, r.top, r.right - r.left, r.bottom - r.top);
    }

    /// <summary>Replaces the element's text: clicks it, selects all with Ctrl+A, then types. Empty text clears it. The text is never logged.</summary>
    /// <inheritdoc cref="ClickAsync" path="/exception"/>
    public async Task TypeAsync(int elementId, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        await ClickAsync(elementId, ct).ConfigureAwait(false);
        await ActAsync(async () =>
        {
            await screen.KeyAsync("ctrl+a", ct: ct).ConfigureAwait(false);
            if (text.Length == 0)
            {
                await screen.KeyAsync("delete", ct: ct).ConfigureAwait(false);
            }
            else
            {
                // The text may be a filled-in secret, so only its length is logged.
                await screen.TypeTextAsync(text, new TypeTextOptions(Sensitive: true), ct).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Presses a key or combo such as <c>Enter</c> or <c>Ctrl+S</c> in the app, after bringing its window to the front.</summary>
    /// <exception cref="InvalidKeyComboException">The combo can't be parsed or isn't allowed. Nothing is pressed.</exception>
    /// <exception cref="InvalidOperationException">No reading yet, or the window is gone, minimized or moved, or the screen can't be controlled.</exception>
    public async Task PressKeyAsync(string keys, CancellationToken ct)
    {
        var (_, window) = LatestReading();
        await BeforeActingAsync(window, ct).ConfigureAwait(false);
        await ActAsync(() => screen.KeyAsync(keys, ct: ct)).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _automation.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<ScreenState> ReadWindowAsync(CancellationToken ct)
    {
        try
        {
            var window = _automation.FromHandle(_window);

            // A screenshot copies what is on screen, so a window in front of the app would be sent to the model instead.
            await BringToFrontAsync(window, ct).ConfigureAwait(false);

            var r = window.BoundingRectangle;

            // A window that is being closed can still answer, with an empty size.
            if (r.Width <= 0 || r.Height <= 0)
            {
                throw new InvalidOperationException(WindowGone);
            }

            // A maximized window reaches a few pixels past the monitor, and a window can be dragged half off it.
            // Only the part on its monitor can be captured and clicked, so that part is the window from here on.
            var bounds = VisiblePart(new ScreenRect(r.X, r.Y, r.Width, r.Height), MonitorOf(_window))
                ?? throw new InvalidOperationException(WindowOffScreen);
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

            var frame = await screen.ScreenshotAreaAsync(bounds.X, bounds.Y, bounds.Width, bounds.Height, ct).ConfigureAwait(false);
            var state = new ScreenState(title, ElementNumbering.Number(title, bounds, candidates), frame.Png, []);
            _state = state;
            _windowBounds = bounds;
            return state;
        }
        // FlaUI reports a window that has gone as ElementNotAvailableException, which has no message of its own.
        catch (Exception e) when (e is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
            throw new InvalidOperationException(WindowGone, e);
        }
        catch (ScreenUnavailableException e)
        {
            throw new InvalidOperationException(e.Message, e);
        }
    }

    private (ScreenState State, ScreenRect Window) LatestReading() =>
        _state is { } state && _windowBounds is { } window
            ? (state, window)
            : throw new InvalidOperationException("Read the screen first.");

    // Before each action: the window must still be where the latest reading saw it, since clicks are mapped with that reading's screenshot.
    private Task BeforeActingAsync(ScreenRect readBounds, CancellationToken ct) =>
        Task.Run(
            async () =>
            {
                try
                {
                    var window = _automation.FromHandle(_window);
                    await BringToFrontAsync(window, ct).ConfigureAwait(false);
                    var r = window.BoundingRectangle;
                    if (VisiblePart(new ScreenRect(r.X, r.Y, r.Width, r.Height), MonitorOf(_window)) != readBounds)
                    {
                        throw new InvalidOperationException(WindowMoved);
                    }
                }
                catch (Exception e) when (e is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
                {
                    throw new InvalidOperationException(WindowGone, e);
                }
            },
            ct);

    // A minimized window sits off every monitor: a screenshot of it would only be black, and a click would miss it.
    private async Task BringToFrontAsync(AutomationElement window, CancellationToken ct)
    {
        if (window.Patterns.Window.PatternOrDefault?.WindowVisualState.ValueOrDefault == WindowVisualState.Minimized)
        {
            throw new InvalidOperationException(WindowMinimized);
        }

        if (await _foreground.BringToFrontAsync(_window, ct).ConfigureAwait(false))
        {
            await Task.Delay(RedrawTime, ct).ConfigureAwait(false);
        }
    }

    // The input layer reports a locked or unusable screen as ScreenUnavailableException; callers get the same exception type as for reading.
    private static async Task ActAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (ScreenUnavailableException e)
        {
            throw new InvalidOperationException(e.Message, e);
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

                // A password field's text is never read, whatever model gets it (#30).
                p.IsPassword.ValueOrDefault ? null : element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault);
        }
        catch (COMException)
        {
            return null;
        }
    }
}
