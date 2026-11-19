using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Vera.Adapters.Desktop;

/// <summary>One thing the tester did while VERA recorded (#48).</summary>
public abstract record RecordedAction;

/// <summary>A click on an element, e.g. a <c>Button</c> named <c>Save</c>.</summary>
public sealed record RecordedClick(string Role, string Name) : RecordedAction;

/// <summary>The text a field holds after the tester typed in it.</summary>
public sealed record RecordedType(string Role, string Name, string Text) : RecordedAction;

/// <summary>A key or combo in VERA's names, e.g. <c>enter</c> or <c>ctrl+s</c>.</summary>
public sealed record RecordedPress(string Keys) : RecordedAction;

/// <summary>Something the tester did that VERA can't write as a step yet, with the reason to show them.</summary>
public sealed record RecordedSkip(string Reason) : RecordedAction;

/// <summary>
/// Watches the tester's mouse and keyboard in one app and tells what they did, so VERA can write it as steps.
/// Windows hooks only note each left click and key; UI Automation then reads the element under the mouse.
/// Typing is not caught key by key: when the tester leaves a text field, VERA reads the field's text.
/// Input sent by a program (such as VERA running steps) is ignored.
/// </summary>
public static class StepRecorder
{
    private const uint WmQuit = 0x0012;
    private const nuint WmLButtonDown = 0x0201;
    private const nuint WmKeyDown = 0x0100;
    private const nuint WmSysKeyDown = 0x0104;
    private const uint MouseInjected = 0x01;
    private const uint KeyInjected = 0x10;
    private const uint KeyUp = 0x80;

    private static readonly HashSet<string> TextRoles = new(StringComparer.Ordinal) { "Edit", "Document", "ComboBox" };

    /// <summary>
    /// Records until <paramref name="ct"/> is cancelled, then still gives the text of the field the tester was typing in.
    /// Only clicks on <paramref name="processId"/>'s elements and keys pressed while it is in front count.
    /// </summary>
    /// <exception cref="InvalidOperationException">Windows refused to let VERA watch the mouse and keyboard.</exception>
    public static async IAsyncEnumerable<RecordedAction> RecordAsync(int processId, [EnumeratorCancellation] CancellationToken ct)
    {
        var events = Channel.CreateUnbounded<RawInput>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var hooks = await Hooks.StartAsync(events.Writer, (uint)processId).ConfigureAwait(false);
        using var stop = ct.Register(hooks.Stop);
        using var automation = new UIA3Automation();

        // The text field being typed in, and its text when VERA last looked (null: not read yet, so give it at the end anyway).
        var field = (AutomationElement?)null;
        var fieldText = (string?)null;
        var ctrl = false;
        var shift = false;
        var alt = false;

        // The channel ends when the hooks stop, after the events already noted.
        await foreach (var input in events.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (input is KeyInput key)
            {
                if (UpdateModifier(key.Vk, key.Down, ref ctrl, ref shift, ref alt) || !key.Down || !key.InApp)
                {
                    continue;
                }

                // Typing can start without a click, e.g. in a field that had the focus already or one reached with Tab.
                if (field is null && FocusedTextField(automation, processId) is { } focused)
                {
                    (field, fieldText) = (focused, null);
                }

                var keys = KeyName(key.Vk, ctrl, shift, alt, FocusOf(field));
                if (keys is null)
                {
                    continue;
                }

                if (Typed(field, ref fieldText) is { } typed)
                {
                    yield return typed;
                }

                // The key may move the focus (Tab) or close the field (Esc); the next typing finds the field again.
                field = null;
                yield return new RecordedPress(keys);
            }
            else if (input is ClickInput click)
            {
                if (Typed(field, ref fieldText) is { } typed)
                {
                    yield return typed;
                }

                field = null;
                if (ElementAt(automation, click.X, click.Y, processId) is not { } element)
                {
                    continue;
                }

                var (role, name) = (element.Properties.ControlType.ValueOrDefault.ToString(), element.Properties.Name.ValueOrDefault ?? "");
                if (string.IsNullOrWhiteSpace(name))
                {
                    yield return new RecordedSkip($"Skipped a click on a {role} with no name.");
                    continue;
                }

                yield return new RecordedClick(role, name);

                // A password field's text is never read (#30).
                if (element.Properties.IsPassword.ValueOrDefault)
                {
                    yield return new RecordedSkip($"VERA doesn't record typing into the password field \"{name}\". Add a step like: type \"{{{{secret:name}}}}\" into it.");
                }
                else if (TextRoles.Contains(role))
                {
                    (field, fieldText) = (element, TextOf(element));
                }
            }
        }

        if (Typed(field, ref fieldText) is { } last)
        {
            yield return last;
        }
    }

    /// <summary>Where the keyboard is: in a one-line field, a text area (where Enter and Tab are part of the text), or elsewhere.</summary>
    internal enum Focus
    {
        Other,
        Field,
        TextArea,
    }

    /// <summary>
    /// The key as a <c>press</c> step writes it, or null when it isn't one: plain typing (the field's text covers it),
    /// editing keys inside a text field, and a modifier on its own. Enter, Tab, Esc, F1–F12 and Ctrl/Alt combos count.
    /// </summary>
    internal static string? KeyName(uint vk, bool ctrl, bool shift, bool alt, Focus focus)
    {
        var name = vk switch
        {
            0x0D => "enter",
            0x09 => "tab",
            0x1B => "esc",
            0x20 => "space",
            0x08 => "backspace",
            0x2D => "insert",
            0x2E => "delete",
            0x24 => "home",
            0x23 => "end",
            0x21 => "pageup",
            0x22 => "pagedown",
            0x25 => "left",
            0x26 => "up",
            0x27 => "right",
            0x28 => "down",
            >= 0x70 and <= 0x7B => $"f{vk - 0x6F}",
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString().ToLowerInvariant(),
            _ => null,
        };

        if (name is null)
        {
            return null;
        }

        var isAction = vk is 0x1B or >= 0x70 and <= 0x7B
            || (vk is 0x0D or 0x09 && focus != Focus.TextArea)
            || alt
            || (ctrl && !(focus != Focus.Other && IsEditingKey(name)));

        if (!isAction)
        {
            return null;
        }

        return (ctrl ? "ctrl+" : "") + (shift ? "shift+" : "") + (alt ? "alt+" : "") + name;
    }

    // Ctrl with these only edits the text being typed (select all, copy, paste, undo, move by word), which the field's text already shows.
    private static bool IsEditingKey(string name) =>
        name is "a" or "c" or "v" or "x" or "z" or "y" or "left" or "right" or "up" or "down" or "home" or "end" or "backspace" or "delete";

    private static bool UpdateModifier(uint vk, bool down, ref bool ctrl, ref bool shift, ref bool alt)
    {
        switch (vk)
        {
            case 0x11 or 0xA2 or 0xA3:
                ctrl = down;
                return true;
            case 0x10 or 0xA0 or 0xA1:
                shift = down;
                return true;
            case 0x12 or 0xA4 or 0xA5:
                alt = down;
                return true;
            default:
                return false;
        }
    }

    private static Focus FocusOf(AutomationElement? field)
    {
        try
        {
            return field is null ? Focus.Other
                : field.Properties.ControlType.ValueOrDefault.ToString() == "Document" ? Focus.TextArea
                : Focus.Field;
        }
        catch (Exception e) when (IsGone(e))
        {
            return Focus.Other;
        }
    }

    // The field's text, as a step if it changed since VERA last looked.
    private static RecordedAction? Typed(AutomationElement? field, ref string? lastText)
    {
        if (field is null)
        {
            return null;
        }

        try
        {
            var role = field.Properties.ControlType.ValueOrDefault.ToString();
            var name = field.Properties.Name.ValueOrDefault ?? "";
            var text = TextOf(field);
            if (text is null || text == lastText)
            {
                return null;
            }

            lastText = text;
            return text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal)
                ? new RecordedSkip($"Skipped typing into {role} \"{name}\": text with several lines can't be a step yet.")
                : new RecordedType(role, name, text);
        }
        catch (Exception e) when (IsGone(e))
        {
            return null;
        }
    }

    private static string? TextOf(AutomationElement element)
    {
        try
        {
            return element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault
                ?? element.Patterns.Text.PatternOrDefault?.DocumentRange.GetText(-1);
        }
        catch (Exception e) when (IsGone(e))
        {
            return null;
        }
    }

    // The app's text field that has the keyboard focus, unless it is a password field.
    private static AutomationElement? FocusedTextField(UIA3Automation automation, int processId)
    {
        try
        {
            var e = automation.FocusedElement();
            return e is not null
                && e.Properties.ProcessId.ValueOrDefault == processId
                && TextRoles.Contains(e.Properties.ControlType.ValueOrDefault.ToString())
                && !e.Properties.IsPassword.ValueOrDefault
                ? e
                : null;
        }
        catch (Exception e) when (IsGone(e))
        {
            return null;
        }
    }

    // The element under the point a tester could act on (a button rather than the text inside it), if it is the app's.
    private static AutomationElement? ElementAt(UIA3Automation automation, int x, int y, int processId)
    {
        try
        {
            var walker = automation.TreeWalkerFactory.GetControlViewWalker();
            for (var e = automation.FromPoint(new System.Drawing.Point(x, y)); e is not null; e = walker.GetParent(e))
            {
                if (e.Properties.ProcessId.ValueOrDefault != processId)
                {
                    return null;
                }

                if (ElementNumbering.IsActionable(e.Properties.ControlType.ValueOrDefault.ToString()))
                {
                    return e;
                }
            }

            return null;
        }
        catch (Exception e) when (IsGone(e))
        {
            return null;
        }
    }

    private static bool IsGone(Exception e) => e is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException;

    private abstract record RawInput;

    private sealed record ClickInput(int X, int Y) : RawInput;

    // InApp: the app was in front when the key went down or up.
    private sealed record KeyInput(uint Vk, bool Down, bool InApp) : RawInput;

    /// <summary>
    /// The low-level mouse and keyboard hooks, on their own thread: Windows calls them from that thread's message loop.
    /// They only note the event; Windows drops a hook that takes too long, so all reading happens elsewhere.
    /// </summary>
    private sealed class Hooks : IDisposable
    {
        private readonly ChannelWriter<RawInput> _events;
        private readonly uint _processId;

        // Kept in fields so the garbage collector doesn't free them while Windows still calls them.
        private readonly HOOKPROC _mouseProc;
        private readonly HOOKPROC _keyProc;

        private uint _threadId;

        private Hooks(ChannelWriter<RawInput> events, uint processId)
        {
            _events = events;
            _processId = processId;
            _mouseProc = OnMouse;
            _keyProc = OnKey;
        }

        public static Task<Hooks> StartAsync(ChannelWriter<RawInput> events, uint processId)
        {
            var hooks = new Hooks(events, processId);
            var started = new TaskCompletionSource<Hooks>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => hooks.Run(started)) { IsBackground = true, Name = "VERA step recorder" };
            thread.Start();
            return started.Task;
        }

        public void Stop() => PInvoke.PostThreadMessage(_threadId, WmQuit, default, default);

        public void Dispose() => Stop();

        private void Run(TaskCompletionSource<Hooks> started)
        {
            try
            {
                using var module = PInvoke.GetModuleHandle((string?)null);
                using var mouse = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _mouseProc, module, 0);
                using var keyboard = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _keyProc, module, 0);
                if (mouse.IsInvalid || keyboard.IsInvalid)
                {
                    started.SetException(new InvalidOperationException("Windows did not let VERA watch the mouse and keyboard."));
                    return;
                }

                // Makes the thread's message queue, so a stop posted right away isn't lost.
                PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
                _threadId = PInvoke.GetCurrentThreadId();
                started.SetResult(this);

                while (PInvoke.GetMessage(out _, HWND.Null, 0, 0) > 0)
                {
                }
            }
            catch (Exception e)
            {
                started.TrySetException(e);
            }
            finally
            {
                _events.TryComplete();
            }
        }

        private unsafe LRESULT OnMouse(int code, WPARAM wParam, LPARAM lParam)
        {
            if (code >= 0 && wParam.Value == WmLButtonDown)
            {
                var info = (MSLLHOOKSTRUCT*)lParam.Value;
                if ((info->flags & MouseInjected) == 0)
                {
                    _events.TryWrite(new ClickInput(info->pt.X, info->pt.Y));
                }
            }

            return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        }

        private unsafe LRESULT OnKey(int code, WPARAM wParam, LPARAM lParam)
        {
            if (code >= 0)
            {
                var info = (KBDLLHOOKSTRUCT*)lParam.Value;
                if (((uint)info->flags & KeyInjected) == 0)
                {
                    _ = PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow(), out var foreground);
                    var down = wParam.Value is WmKeyDown or WmSysKeyDown && ((uint)info->flags & KeyUp) == 0;
                    _events.TryWrite(new KeyInput(info->vkCode, down, foreground == _processId));
                }
            }

            return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        }
    }
}
