using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

namespace Vera.Input.Options;

/// <summary>The <c>VeraInput</c> section of appsettings.json. Checked when the app starts, so bad values fail early.</summary>
public sealed record VeraInputOptions
{
    public const string SectionName = "VeraInput";

    [ValidateObjectMembers]
    public DisplayOptions Display { get; init; } = new();

    [ValidateObjectMembers]
    public CaptureOptions Capture { get; init; } = new();

    [ValidateObjectMembers]
    public RdpOptions Rdp { get; init; } = new();

    [ValidateObjectMembers]
    public DelayOptions Delays { get; init; } = new();

    [ValidateObjectMembers]
    public TextOptions Text { get; init; } = new();

    [ValidateObjectMembers]
    public JournalOptions Journal { get; init; } = new();
}

public enum CaptureTarget
{
    PrimaryMonitor,

    /// <summary>The monitor at <see cref="DisplayOptions.MonitorIndex"/>.</summary>
    MonitorIndex,
}

/// <summary>What the screen of the VERA machine is expected to look like. A mismatch is logged at startup.</summary>
public sealed record DisplayOptions
{
    [Range(1, 32768)]
    public int ExpectedWidth { get; init; } = 1920;

    [Range(1, 32768)]
    public int ExpectedHeight { get; init; } = 1080;

    /// <summary>The Windows display scale, e.g. 100 or 125.</summary>
    [Range(100, 500)]
    public int ExpectedScalePercent { get; init; } = 100;

    [EnumDataType(typeof(CaptureTarget))]
    public CaptureTarget CaptureTarget { get; init; } = CaptureTarget.PrimaryMonitor;

    /// <summary>Used when <see cref="CaptureTarget"/> is <see cref="Options.CaptureTarget.MonitorIndex"/>.</summary>
    [Range(0, 15)]
    public int MonitorIndex { get; init; }

    /// <summary><c>true</c> refuses to start on a mismatch; <c>false</c> only warns.</summary>
    public bool StrictMismatch { get; init; }
}

public sealed record CaptureOptions
{
    /// <summary>Image size relative to the screen when the caller doesn't pass a scale, e.g. 0.5.</summary>
    [Range(0.05, 1.0)]
    public double DefaultScale { get; init; } = 1.0;

    /// <summary>Optional cap on the longer image side in pixels, applied after the scale.</summary>
    /// <remarks>Not used yet: planned once the AI service, and so its image size limit, is known.</remarks>
    [Range(16, 32768)]
    public int? MaxLongEdge { get; init; }

    /// <summary>A sampled pixel darker than this (0–255) counts as black.</summary>
    [Range(0, 255)]
    public int BlackLuminanceThreshold { get; init; } = 10;

    /// <summary>The frame is rejected as black when more than this share of the samples is black.</summary>
    [Range(0.5, 1.0)]
    public double BlackPixelRatio { get; init; } = 0.995;

    /// <summary>Checks every Nth pixel for the black-frame test.</summary>
    [Range(1, 64)]
    public int SampleStride { get; init; } = 4;

    /// <summary>Logs a warning after this many identical frames in a row.</summary>
    [Range(1, 10000)]
    public int StaleWarningAfterIdenticalFrames { get; init; } = 10;
}

public sealed record RdpOptions
{
    /// <summary><c>false</c> skips the RDP window checks, for local tests against EchoApp.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Window class of the RDP client. <c>TscShellContainerClass</c> is mstsc.</summary>
    [Required]
    public string WindowClass { get; init; } = "TscShellContainerClass";

    public bool RequireForeground { get; init; } = true;

    /// <summary>Whether Win+… combos reach the remote ("Apply Windows key combinations: On the remote computer").</summary>
    public bool WinKeyForwarded { get; init; }
}

/// <summary>Waits between and after actions, in milliseconds, so the remote app and the RDP chain can keep up.</summary>
public sealed record DelayOptions
{
    [Range(0, 60000)]
    public int AfterMove { get; init; } = 50;

    [Range(0, 60000)]
    public int MouseDownUp { get; init; } = 50;

    [Range(0, 60000)]
    public int BetweenClicks { get; init; } = 80;

    [Range(0, 60000)]
    public int AfterClick { get; init; } = 300;

    [Range(0, 60000)]
    public int BetweenKeystrokes { get; init; } = 15;

    [Range(0, 60000)]
    public int AfterKey { get; init; } = 200;

    [Range(0, 60000)]
    public int AfterType { get; init; } = 300;

    [Range(0, 60000)]
    public int AfterScroll { get; init; } = 300;

    [Range(0, 60000)]
    public int ScrollNotch { get; init; } = 30;

    /// <summary>Number of intermediate mouse moves in a drag (a count, not milliseconds).</summary>
    [Range(1, 1000)]
    public int DragSteps { get; init; } = 20;

    [Range(0, 60000)]
    public int DragStepDelay { get; init; } = 15;

    [Range(0, 60000)]
    public int HoverDwell { get; init; } = 800;

    /// <summary>Wait after setting the clipboard before pasting, so RDP can sync it to the remote.</summary>
    [Range(0, 60000)]
    public int ClipboardSync { get; init; } = 400;
}

public enum AutoClipboardFor
{
    /// <summary>Always types with Unicode key events.</summary>
    Never,

    /// <summary>Pastes runs of non-ASCII characters through the clipboard.</summary>
    NonAscii,
}

public sealed record TextOptions
{
    /// <summary>Used when the caller passes <see cref="TextInputMethod.Auto"/>.</summary>
    [EnumDataType(typeof(TextInputMethod))]
    public TextInputMethod DefaultMethod { get; init; } = TextInputMethod.Auto;

    /// <summary>What <see cref="TextInputMethod.Auto"/> pastes instead of typing. Decided after the RDP tests.</summary>
    [EnumDataType(typeof(AutoClipboardFor))]
    public AutoClipboardFor AutoClipboardFor { get; init; } = AutoClipboardFor.Never;

    /// <summary>Puts the previous clipboard text back after a paste.</summary>
    public bool RestoreClipboard { get; init; } = true;
}

public sealed record JournalOptions
{
    public bool Enabled { get; init; }

    public bool ScreenshotAfterEachAction { get; init; } = true;

    [Required]
    public string Folder { get; init; } = "runs";

    [Range(0.05, 1.0)]
    public double ScreenshotScale { get; init; } = 0.5;
}

/// <summary>Checks the data annotations above, including the nested sections. Generated at compile time.</summary>
[OptionsValidator]
internal sealed partial class VeraInputOptionsValidator : IValidateOptions<VeraInputOptions>;