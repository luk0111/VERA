using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using Vera.Adapters.Desktop;

namespace Vera.Adapters.Mac;

/// <summary>
/// Watches the tester's mouse and keyboard in one Mac app and tells what they did, so VERA can write it as steps (#48).
/// An event tap that only listens notes each left click and key; the Accessibility API then reads the element under the mouse.
/// The rules are the same as on Windows (<see cref="StepRecording"/>). Command is written as <c>ctrl</c> and Option as <c>alt</c>,
/// so a recorded step runs on both; the Control key isn't recorded. VERA's own clicks and typing are ignored.
/// </summary>
public static class MacStepRecorder
{
    private const string NoAccess =
        "VERA isn't allowed to watch the mouse and keyboard. In System Settings > Privacy & Security, turn VERA's app (Terminal or Rider) on under " +
        "Input Monitoring and Accessibility, then restart VERA.";

    private const uint SessionEventTap = 1;
    private const uint HeadInsertEventTap = 0;
    private const uint ListenOnly = 1;
    private const uint LeftMouseDown = 1;
    private const uint KeyDown = 10;
    private const uint TapDisabledByTimeout = 0xFFFFFFFE;
    private const uint TapDisabledByUserInput = 0xFFFFFFFF;
    private const uint KeycodeField = 9;
    private const uint SourceProcessField = 41;

    /// <summary>Messaging timeout for Accessibility reads, so an app that hangs can't stall the recording.</summary>
    private const float ReadTimeoutSeconds = 0.25f;

    private static readonly Dictionary<ushort, string> KeyNames =
        MacKeyCombo.MainKeys.ToDictionary(k => k.Value.KeyCode, k => k.Key);

    /// <summary>
    /// Records until <paramref name="ct"/> is cancelled, then still gives the text of the field the tester was typing in.
    /// Only clicks on <paramref name="processId"/>'s elements and keys pressed while it is in front count.
    /// </summary>
    /// <exception cref="InvalidOperationException">macOS doesn't let VERA watch the mouse and keyboard (Input Monitoring or Accessibility is off).</exception>
    public static async IAsyncEnumerable<RecordedAction> RecordAsync(int processId, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!MacNative.AccessibilityAllowedOrPrompt() || !(MacNative.CGPreflightListenEventAccess() || MacNative.CGRequestListenEventAccess()))
        {
            throw new InvalidOperationException(NoAccess);
        }

        var events = Channel.CreateUnbounded<HookInput>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var tap = await Tap.StartAsync(events.Writer, processId).ConfigureAwait(false);
        using var stop = ct.Register(tap.Stop);
        using var elements = new AxElements(processId);

        // The channel ends when the tap stops, after the events already noted.
        await foreach (var action in StepRecording.RecordAsync(events.Reader, elements, CancellationToken.None).ConfigureAwait(false))
        {
            yield return action;
        }
    }

    /// <summary>VERA's name for a Mac key code (US positions), or null for one it has none for (modifiers included).</summary>
    internal static string? KeyName(ushort keyCode) => KeyNames.GetValueOrDefault(keyCode);

    /// <summary>A retained Accessibility element, released when no longer used.</summary>
    private sealed class AxElement : SafeHandle
    {
        public AxElement(IntPtr element)
            : base(IntPtr.Zero, ownsHandle: true) => SetHandle(element);

        public override bool IsInvalid => handle == IntPtr.Zero;

        public IntPtr Ref => handle;

        protected override bool ReleaseHandle()
        {
            MacNative.CFRelease(handle);
            return true;
        }
    }

    private sealed class AxElements : IRecordedElements<AxElement>, IDisposable
    {
        private const int MaxParents = 30;

        private readonly int _processId;
        private readonly IntPtr _systemWide = MacNative.AXUIElementCreateSystemWide();
        private readonly IntPtr _app;

        public AxElements(int processId)
        {
            _processId = processId;
            _app = MacNative.AXUIElementCreateApplication(processId);
            _ = MacNative.AXUIElementSetMessagingTimeout(_systemWide, ReadTimeoutSeconds);
            _ = MacNative.AXUIElementSetMessagingTimeout(_app, ReadTimeoutSeconds);
        }

        public AxElement? At(int x, int y)
        {
            if (MacNative.AXUIElementCopyElementAtPosition(_systemWide, x, y, out var e) != MacNative.AxSuccess)
            {
                return null;
            }

            // From the deepest element up to the nearest one a tester can act on (a button rather than the text inside it).
            for (var i = 0; i < MaxParents && e != IntPtr.Zero; i++)
            {
                var element = new AxElement(e);
                if (MacNative.AXUIElementGetPid(e, out var pid) != MacNative.AxSuccess || pid != _processId)
                {
                    element.Dispose();
                    return null;
                }

                if (ElementNumbering.IsActionable(Role(element)))
                {
                    return element;
                }

                e = MacAdapter.Copy(e, "AXParent");
                element.Dispose();
            }

            if (e != IntPtr.Zero)
            {
                MacNative.CFRelease(e);
            }

            return null;
        }

        public AxElement? FocusedTextField()
        {
            var e = MacAdapter.Copy(_app, "AXFocusedUIElement");
            if (e == IntPtr.Zero)
            {
                return null;
            }

            var element = new AxElement(e);
            if (Role(element) is "Edit" or "ComboBox" && !IsPassword(element))
            {
                return element;
            }

            element.Dispose();
            return null;
        }

        public string Role(AxElement element) => MacRoles.ToCommon(MacAdapter.ReadString(element.Ref, "AXRole") ?? "");

        public string Name(AxElement element) =>
            MacRoles.Name(
                MacAdapter.ReadString(element.Ref, "AXTitle"),
                MacAdapter.ReadString(element.Ref, "AXDescription"),
                MacAdapter.ReadString(element.Ref, "AXHelp"));

        public bool IsPassword(AxElement element) => MacAdapter.ReadString(element.Ref, "AXSubrole") == "AXSecureTextField";

        public bool IsTextArea(AxElement element) => MacAdapter.ReadString(element.Ref, "AXRole") == "AXTextArea";

        public string? Text(AxElement element) => MacAdapter.ReadString(element.Ref, "AXValue");

        public void Dispose()
        {
            MacNative.CFRelease(_systemWide);
            MacNative.CFRelease(_app);
        }
    }

    /// <summary>
    /// The event tap, on its own thread with its own run loop. It only listens: macOS delivers every event unchanged.
    /// The callback only notes the event; the element is read elsewhere.
    /// </summary>
    private sealed class Tap : IDisposable
    {
        private readonly ChannelWriter<HookInput> _events;
        private readonly int _processId;
        private readonly int _ownProcessId = Environment.ProcessId;
        private readonly IntPtr _systemWide = MacNative.AXUIElementCreateSystemWide();
        private IntPtr _port;
        private volatile bool _stopping;

        private Tap(ChannelWriter<HookInput> events, int processId)
        {
            _events = events;
            _processId = processId;
            _ = MacNative.AXUIElementSetMessagingTimeout(_systemWide, ReadTimeoutSeconds);
        }

        public static Task<Tap> StartAsync(ChannelWriter<HookInput> events, int processId)
        {
            var tap = new Tap(events, processId);
            var started = new TaskCompletionSource<Tap>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => tap.Run(started)) { IsBackground = true, Name = "VERA step recorder" };
            thread.Start();
            return started.Task;
        }

        public void Stop() => _stopping = true;

        public void Dispose() => Stop();

        private unsafe void Run(TaskCompletionSource<Tap> started)
        {
            var self = GCHandle.Alloc(this);
            var source = IntPtr.Zero;
            try
            {
                const ulong mask = (1UL << (int)LeftMouseDown) | (1UL << (int)KeyDown);
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

                // Short turns, so a stop is seen even if it came before the loop first ran.
                while (!_stopping)
                {
                    MacNative.CFRunLoopRunInMode(mode, 0.2, returnAfterSourceHandled: false);
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                started.TrySetException(new InvalidOperationException("Recording steps works only on macOS.", e));
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

                MacNative.CFRelease(_systemWide);
                self.Free();
                _events.TryComplete();
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

            if (MacNative.CGEventGetIntegerValueField(cgEvent, SourceProcessField) == _ownProcessId)
            {
                return;
            }

            if (type == LeftMouseDown)
            {
                var at = MacNative.CGEventGetLocation(cgEvent);
                _events.TryWrite(new ClickInput((int)Math.Round(at.First), (int)Math.Round(at.Second)));
            }
            else if (type == KeyDown && FrontProcessId() == _processId)
            {
                var flags = MacNative.CGEventGetFlags(cgEvent);
                _events.TryWrite(new KeyInput(
                    KeyName((ushort)MacNative.CGEventGetIntegerValueField(cgEvent, KeycodeField)),
                    Ctrl: (flags & MacKeyCombo.CommandFlag) != 0,
                    Shift: (flags & MacKeyCombo.ShiftFlag) != 0,
                    Alt: (flags & MacKeyCombo.OptionFlag) != 0));
            }
        }

        private int? FrontProcessId()
        {
            var app = MacAdapter.Copy(_systemWide, "AXFocusedApplication");
            if (app == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return MacNative.AXUIElementGetPid(app, out var pid) == MacNative.AxSuccess ? pid : null;
            }
            finally
            {
                MacNative.CFRelease(app);
            }
        }
    }
}
