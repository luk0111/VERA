using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Vera.Adapters.Desktop;

/// <summary>
/// Watches for the tester pressing Esc while VERA runs steps, so they can take the mouse and keyboard back at once (#47).
/// Esc counts wherever it's pressed. Keys sent by a program (such as a <c>press esc</c> step) are ignored. The app still gets the key.
/// </summary>
public static class StopKey
{
    private const uint WmQuit = 0x0012;
    private const nuint WmKeyDown = 0x0100;
    private const nuint WmSysKeyDown = 0x0104;
    private const uint VkEscape = 0x1B;
    private const uint KeyInjected = 0x10;

    /// <summary>Starts watching; <paramref name="stop"/> is called once, on the first Esc. Dispose the result to stop watching.</summary>
    /// <exception cref="InvalidOperationException">Windows refused to let VERA watch the keyboard.</exception>
    public static async Task<IDisposable> StartAsync(Action stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        return await Hook.StartAsync(stop).ConfigureAwait(false);
    }

    /// <summary>The low-level keyboard hook, on its own thread: Windows calls it from that thread's message loop.</summary>
    private sealed class Hook : IDisposable
    {
        private readonly Action _stop;

        // Kept in a field so the garbage collector doesn't free it while Windows still calls it.
        private readonly HOOKPROC _keyProc;

        private uint _threadId;
        private int _stopped;

        private Hook(Action stop)
        {
            _stop = stop;
            _keyProc = OnKey;
        }

        public static Task<Hook> StartAsync(Action stop)
        {
            var hook = new Hook(stop);
            var started = new TaskCompletionSource<Hook>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => hook.Run(started)) { IsBackground = true, Name = "VERA stop key" };
            thread.Start();
            return started.Task;
        }

        public void Dispose() => PInvoke.PostThreadMessage(_threadId, WmQuit, default, default);

        private void Run(TaskCompletionSource<Hook> started)
        {
            try
            {
                using var module = PInvoke.GetModuleHandle((string?)null);
                using var keyboard = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _keyProc, module, 0);
                if (keyboard.IsInvalid)
                {
                    started.SetException(new InvalidOperationException("Windows did not let VERA watch the keyboard."));
                    return;
                }

                // Makes the thread's message queue, so a dispose posted right away isn't lost.
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
        }

        private unsafe LRESULT OnKey(int code, WPARAM wParam, LPARAM lParam)
        {
            if (code >= 0 && wParam.Value is WmKeyDown or WmSysKeyDown)
            {
                var info = (KBDLLHOOKSTRUCT*)lParam.Value;
                if (info->vkCode == VkEscape && ((uint)info->flags & KeyInjected) == 0 && Interlocked.Exchange(ref _stopped, 1) == 0)
                {
                    // Off the hook's thread: Windows drops a hook that takes too long.
                    ThreadPool.QueueUserWorkItem(_ => _stop());
                }
            }

            return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        }
    }
}
