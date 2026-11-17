using System.Diagnostics;
using System.Runtime.InteropServices;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

using Vera.Core;
using Vera.Input;

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

    /// <summary>How long Windows gets to redraw a window it just brought to the front.</summary>
    private static readonly TimeSpan RedrawTime = TimeSpan.FromMilliseconds(200);

    private readonly UIA3Automation _automation = new();
    private readonly ForegroundSwitch _foreground = new(new Win32ForegroundApi());
    private IntPtr _window;

    // The latest reading. Its window bounds are also the latest screenshot's area, which clicks are mapped with.
    private ScreenState? _state;
    private ScreenRect? _windowBounds;

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
        var element = state.Elements.FirstOrDefault(e => e.Id >= 1 && e.Id == elementId)
            ?? throw new ArgumentOutOfRangeException(nameof(elementId), elementId, $"Element {elementId} is not on the latest screen.");
        var point = ClickPoint(element.Bounds, window)
            ?? throw new InvalidOperationException($"Element {elementId} is outside the window.");

        await BeforeActingAsync(window, ct).ConfigureAwait(false);
        await ActAsync(() => screen.ClickAsync(point.X, point.Y, ct: ct)).ConfigureAwait(false);
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

    /// <summary>
    /// The point to click for an element, in the window screenshot's pixels: the centre of the part of the element inside the window.
    /// Null when no part of it is inside.
    /// </summary>
    internal static (int X, int Y)? ClickPoint(ScreenRect element, ScreenRect window)
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

            var frame = await screen.ScreenshotAreaAsync(r.X, r.Y, r.Width, r.Height, ct).ConfigureAwait(false);
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
                    if (new ScreenRect(r.X, r.Y, r.Width, r.Height) != readBounds)
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
