using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Vera.Adapters.Desktop;

/// <summary>
/// Watches the tester's mouse and keyboard in one Windows app and tells what they did, so VERA can write it as steps (#48).
/// Windows hooks only note each left click and key; UI Automation then reads the element under the mouse. The rules are in <see cref="StepRecording"/>.
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

    /// <summary>
    /// Records until <paramref name="ct"/> is cancelled, then still gives the text of the field the tester was typing in.
    /// Only clicks on <paramref name="processId"/>'s elements and keys pressed while it is in front count.
    /// </summary>
    /// <exception cref="InvalidOperationException">Windows refused to let VERA watch the mouse and keyboard.</exception>
    public static async IAsyncEnumerable<RecordedAction> RecordAsync(int processId, [EnumeratorCancellation] CancellationToken ct)
    {
        var events = Channel.CreateUnbounded<HookInput>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var hooks = await Hooks.StartAsync(events.Writer, (uint)processId).ConfigureAwait(false);
        using var stop = ct.Register(hooks.Stop);
        using var automation = new UIA3Automation();

        // The channel ends when the hooks stop, after the events already noted.
        await foreach (var action in StepRecording.RecordAsync(events.Reader, new UiaElements(automation, processId), CancellationToken.None).ConfigureAwait(false))
        {
            yield return action;
        }
    }

    /// <summary>VERA's name for a Windows virtual key, or null for one it has none for (modifiers included).</summary>
    internal static string? KeyName(uint vk) => vk switch
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

    private static bool IsGone(Exception e) => e is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException;

    private sealed class UiaElements(UIA3Automation automation, int processId) : IRecordedElements<AutomationElement>
    {
        public AutomationElement? At(int x, int y) => Read(() =>
        {
            var walker = automation.TreeWalkerFactory.GetControlViewWalker();
            for (var e = automation.FromPoint(new System.Drawing.Point(x, y)); e is not null; e = walker.GetParent(e))
            {
                if (e.Properties.ProcessId.ValueOrDefault != processId)
                {
                    return null;
                }

                if (ElementNumbering.IsActionable(Role(e)))
                {
                    return e;
                }
            }

            return null;
        });

        public AutomationElement? FocusedTextField() => Read(() =>
        {
            var e = automation.FocusedElement();
            return e is not null
                && e.Properties.ProcessId.ValueOrDefault == processId
                && Role(e) is "Edit" or "Document" or "ComboBox"
                && !e.Properties.IsPassword.ValueOrDefault
                ? e
                : null;
        });

        public string Role(AutomationElement element) => Read(() => element.Properties.ControlType.ValueOrDefault.ToString()) ?? "";

        public string Name(AutomationElement element) => Read(() => element.Properties.Name.ValueOrDefault) ?? "";

        public bool IsPassword(AutomationElement element) => Read(() => (bool?)element.Properties.IsPassword.ValueOrDefault) ?? false;

        public bool IsTextArea(AutomationElement element) => Role(element) == "Document";

        public string? Text(AutomationElement element) => Read(() =>
            element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault
            ?? element.Patterns.Text.PatternOrDefault?.DocumentRange.GetText(-1));

        // An element can go away at any moment; it then reads as nothing.
        private static T? Read<T>(Func<T?> read)
        {
            try
            {
                return read();
            }
            catch (Exception e) when (IsGone(e))
            {
                return default;
            }
        }
    }

    /// <summary>
    /// The low-level mouse and keyboard hooks, on their own thread: Windows calls them from that thread's message loop.
    /// They only note the event; Windows drops a hook that takes too long, so all reading happens elsewhere.
    /// </summary>
    private sealed class Hooks : IDisposable
    {
        private readonly ChannelWriter<HookInput> _events;
        private readonly uint _processId;

        // Kept in fields so the garbage collector doesn't free them while Windows still calls them.
        private readonly HOOKPROC _mouseProc;
        private readonly HOOKPROC _keyProc;

        private uint _threadId;
        private bool _ctrl;
        private bool _shift;
        private bool _alt;

        private Hooks(ChannelWriter<HookInput> events, uint processId)
        {
            _events = events;
            _processId = processId;
            _mouseProc = OnMouse;
            _keyProc = OnKey;
        }

        public static Task<Hooks> StartAsync(ChannelWriter<HookInput> events, uint processId)
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
                var flags = (uint)info->flags;
                if ((flags & KeyInjected) == 0)
                {
                    var down = wParam.Value is WmKeyDown or WmSysKeyDown && (flags & KeyUp) == 0;
                    if (!TrackModifier(info->vkCode, down) && down)
                    {
                        _ = PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow(), out var foreground);
                        if (foreground == _processId)
                        {
                            _events.TryWrite(new KeyInput(KeyName(info->vkCode), _ctrl, _shift, _alt));
                        }
                    }
                }
            }

            return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        }

        // Modifiers are followed wherever they're pressed, so a combo started before switching to the app still reads right.
        private bool TrackModifier(uint vk, bool down)
        {
            switch (vk)
            {
                case 0x11 or 0xA2 or 0xA3:
                    _ctrl = down;
                    return true;
                case 0x10 or 0xA0 or 0xA1:
                    _shift = down;
                    return true;
                case 0x12 or 0xA4 or 0xA5:
                    _alt = down;
                    return true;
                default:
                    return false;
            }
        }
    }
}
