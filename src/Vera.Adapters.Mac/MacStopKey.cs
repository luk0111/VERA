using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vera.Adapters.Mac;

/// <summary>
/// Watches for the tester pressing Esc while VERA runs steps, so they can take the mouse and keyboard back at once (#47); the Mac version of
/// <c>StopKey</c>. Esc counts wherever it's pressed. VERA's own keys (a <c>press esc</c> step) are ignored. The tap only listens, so the app still gets the key.
/// </summary>
public static class MacStopKey
{
    private const string NoAccess =
        "VERA isn't allowed to watch the keyboard. In System Settings > Privacy & Security, turn VERA's app (Terminal or Rider) on under " +
        "Input Monitoring, then restart VERA.";

    private const uint SessionEventTap = 1;
    private const uint HeadInsertEventTap = 0;
    private const uint ListenOnly = 1;
    private const uint KeyDown = 10;
    private const uint TapDisabledByTimeout = 0xFFFFFFFE;
    private const uint TapDisabledByUserInput = 0xFFFFFFFF;
    private const uint KeycodeField = 9;
    private const uint SourceProcessField = 41;

    private static readonly ushort EscKeyCode = MacKeyCombo.MainKeys["esc"].KeyCode;

    /// <summary>Starts watching; <paramref name="stop"/> is called once, on the first Esc. Dispose the result to stop watching.</summary>
    /// <exception cref="InvalidOperationException">macOS doesn't let VERA watch the keyboard (Input Monitoring is off).</exception>
    public static async Task<IDisposable> StartAsync(Action stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        if (!(MacNative.CGPreflightListenEventAccess() || MacNative.CGRequestListenEventAccess()))
        {
            throw new InvalidOperationException(NoAccess);
        }

        return await Tap.StartAsync(stop).ConfigureAwait(false);
    }

    /// <summary>Whether a key-down from <paramref name="sourceProcessId"/> stops the run: Esc, not sent by VERA itself.</summary>
    internal static bool Stops(ushort keyCode, long sourceProcessId) => keyCode == EscKeyCode && sourceProcessId != Environment.ProcessId;

    /// <summary>The event tap, on its own thread with its own run loop.</summary>
    private sealed class Tap : IDisposable
    {
        private readonly Action _stop;
        private IntPtr _port;
        private volatile bool _closing;
        private int _stopped;

        private Tap(Action stop) => _stop = stop;

        public static Task<Tap> StartAsync(Action stop)
        {
            var tap = new Tap(stop);
            var started = new TaskCompletionSource<Tap>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => tap.Run(started)) { IsBackground = true, Name = "VERA stop key" };
            thread.Start();
            return started.Task;
        }

        public void Dispose() => _closing = true;

        private unsafe void Run(TaskCompletionSource<Tap> started)
        {
            var self = GCHandle.Alloc(this);
            var source = IntPtr.Zero;
            try
            {
                const ulong mask = 1UL << (int)KeyDown;
                _port = MacNative.CGEventTapCreate(SessionEventTap, HeadInsertEventTap, ListenOnly, mask, &OnEvent, GCHandle.ToIntPtr(self));
                if (_port == IntPtr.Zero)
                {
                    started.SetException(new InvalidOperationException(NoAccess));
                    return;
                }

                source = MacNative.CFMachPortCreateRunLoopSource(IntPtr.Zero, _port, 0);
                var mode = MacNative.RunLoopDefaultMode();
                MacNative.CFRunLoopAddSource(MacNative.CFRunLoopGetCurrent(), source, mode);
                MacNative.CGEventTapEnable(_port, enable: true);
                started.SetResult(this);

                // Short turns, so a dispose is seen even if it came before the loop first ran.
                while (!_closing)
                {
                    MacNative.CFRunLoopRunInMode(mode, 0.2, returnAfterSourceHandled: false);
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                started.TrySetException(new InvalidOperationException("Stopping with Esc works only on macOS.", e));
            }
            finally
            {
                if (_port != IntPtr.Zero)
                {
                    MacNative.CGEventTapEnable(_port, enable: false);
                    MacNative.CFMachPortInvalidate(_port);
                    MacNative.CFRelease(_port);
                }

                if (source != IntPtr.Zero)
                {
                    MacNative.CFRelease(source);
                }

                self.Free();
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static IntPtr OnEvent(IntPtr proxy, uint type, IntPtr cgEvent, IntPtr userInfo)
        {
            // Nothing may be thrown back into macOS; a missed event is better than a crash.
            try
            {
                (GCHandle.FromIntPtr(userInfo).Target as Tap)?.Note(type, cgEvent);
            }
#pragma warning disable CA1031 // See above.
            catch (Exception)
#pragma warning restore CA1031
            {
            }

            return cgEvent;
        }

        private void Note(uint type, IntPtr cgEvent)
        {
            // macOS turns off a tap it thinks is too slow; turn it back on.
            if (type is TapDisabledByTimeout or TapDisabledByUserInput)
            {
                MacNative.CGEventTapEnable(_port, enable: true);
                return;
            }

            if (type == KeyDown
                && Stops((ushort)MacNative.CGEventGetIntegerValueField(cgEvent, KeycodeField), MacNative.CGEventGetIntegerValueField(cgEvent, SourceProcessField))
                && Interlocked.Exchange(ref _stopped, 1) == 0)
            {
                // Off the tap's thread: macOS turns off a tap that takes too long.
                ThreadPool.QueueUserWorkItem(_ => _stop());
            }
        }
    }
}
