using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Vera.Adapters.Mac;

/// <summary>Calls into macOS frameworks. Objects returned by "Copy" and "Create" calls are owned by the caller and need <see cref="CFRelease"/>.</summary>
internal static partial class MacNative
{
    public const int AxSuccess = 0;
    public const int AxValueCgPoint = 1;
    public const int AxValueCgSize = 2;
    public const uint WindowListOnScreenOnly = 1;
    public const uint WindowListExcludeDesktopElements = 16;

    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ApplicationServicesLibrary = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreGraphicsLibrary = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const int NumberSInt64 = 4;

    private static readonly ConcurrentDictionary<string, IntPtr> Keys = new(StringComparer.Ordinal);

    /// <summary>A CGPoint or CGSize.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DoublePair
    {
        public double First;
        public double Second;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CfRange
    {
        public nint Location;
        public nint Length;
    }

    /// <summary>A CFString for an attribute or dictionary key. Kept for the life of the process, so it is never released.</summary>
    public static IntPtr Key(string text) => Keys.GetOrAdd(text, static t => CFStringCreateWithCharacters(IntPtr.Zero, t, t.Length));

    /// <returns>The text, or null when <paramref name="value"/> is not a CFString.</returns>
    public static string? ReadString(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID())
        {
            return null;
        }

        var length = CFStringGetLength(value);
        if (length == 0)
        {
            return "";
        }

        var buffer = new ushort[length];
        CFStringGetCharacters(value, new CfRange { Location = 0, Length = length }, buffer);
        return new string(MemoryMarshal.Cast<ushort, char>(buffer));
    }

    public static long? ReadNumber(IntPtr value) =>
        value != IntPtr.Zero && CFGetTypeID(value) == CFNumberGetTypeID() && CFNumberGetValue(value, NumberSInt64, out var number) ? number : null;

    public static bool? ReadBool(IntPtr value) =>
        value != IntPtr.Zero && CFGetTypeID(value) == CFBooleanGetTypeID() ? CFBooleanGetValue(value) : null;

    public static bool IsArray(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFArrayGetTypeID();

    /// <summary>The shared CFBoolean for true. It is not owned by the caller.</summary>
    public static IntPtr CfTrue()
    {
        var library = NativeLibrary.Load(CoreFoundationLibrary);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCFBooleanTrue"));
    }

    /// <summary>Checks Accessibility access and, when it is missing, shows the macOS prompt that leads to System Settings.</summary>
    public static bool AccessibilityAllowedOrPrompt()
    {
        if (AXIsProcessTrusted())
        {
            return true;
        }

        var library = NativeLibrary.Load(CoreFoundationLibrary);
        var keyCallbacks = NativeLibrary.GetExport(library, "kCFTypeDictionaryKeyCallBacks");
        var valueCallbacks = NativeLibrary.GetExport(library, "kCFTypeDictionaryValueCallBacks");
        var options = CFDictionaryCreate(IntPtr.Zero, [Key("AXTrustedCheckOptionPrompt")], [CfTrue()], 1, keyCallbacks, valueCallbacks);
        try
        {
            return AXIsProcessTrustedWithOptions(options);
        }
        finally
        {
            CFRelease(options);
        }
    }

    [LibraryImport(CoreFoundationLibrary)]
    public static partial void CFRelease(IntPtr value);

    /// <summary>Keeps an object alive after the array holding it is released.</summary>
    [LibraryImport(CoreFoundationLibrary)]
    public static partial IntPtr CFRetain(IntPtr value);

    [LibraryImport(CoreFoundationLibrary)]
    public static partial nint CFArrayGetCount(IntPtr array);

    [LibraryImport(CoreFoundationLibrary)]
    public static partial IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [LibraryImport(CoreFoundationLibrary)]
    public static partial IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [LibraryImport(ApplicationServicesLibrary)]
    public static partial IntPtr AXUIElementCreateApplication(int processId);

    [LibraryImport(ApplicationServicesLibrary)]
    public static partial int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [LibraryImport(ApplicationServicesLibrary)]
    public static partial int AXUIElementSetAttributeValue(IntPtr element, IntPtr attribute, IntPtr value);

    [LibraryImport(ApplicationServicesLibrary)]
    public static partial int AXUIElementPerformAction(IntPtr element, IntPtr action);

    [LibraryImport(ApplicationServicesLibrary)]
    public static partial int AXUIElementSetMessagingTimeout(IntPtr element, float seconds);

    [LibraryImport(ApplicationServicesLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool AXValueGetValue(IntPtr value, int type, out DoublePair pair);

    [LibraryImport(CoreGraphicsLibrary)]
    public static partial IntPtr CGWindowListCopyWindowInfo(uint options, uint relativeToWindow);

    [LibraryImport(CoreGraphicsLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CGPreflightScreenCaptureAccess();

    [LibraryImport(CoreGraphicsLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CGRequestScreenCaptureAccess();

    [LibraryImport(ApplicationServicesLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrusted();

    [LibraryImport(ApplicationServicesLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrustedWithOptions(IntPtr options);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFGetTypeID(IntPtr value);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFStringGetTypeID();

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFNumberGetTypeID();

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFBooleanGetTypeID();

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nuint CFArrayGetTypeID();

    [LibraryImport(CoreFoundationLibrary, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CFStringCreateWithCharacters(IntPtr allocator, string text, nint length);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial nint CFStringGetLength(IntPtr text);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial void CFStringGetCharacters(IntPtr text, CfRange range, [Out] ushort[] buffer);

    [LibraryImport(CoreFoundationLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberGetValue(IntPtr number, int type, out long value);

    [LibraryImport(CoreFoundationLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFBooleanGetValue(IntPtr value);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);
}
