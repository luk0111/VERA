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
}

public sealed record DisplayOptions
{
    /// <summary>The monitor to capture. Not set means the primary monitor.</summary>
    [Range(0, 15)]
    public int? MonitorIndex { get; init; }
}

public sealed record CaptureOptions
{
    /// <summary>Image size relative to the screen when the caller doesn't pass a scale, e.g. 0.5.</summary>
    [Range(0.05, 1.0)]
    public double DefaultScale { get; init; } = 1.0;
}

public sealed record RdpOptions
{
    /// <summary><c>true</c> adds the RDP window checks, for when the app under test is reached over RDP. Off by default: the app is on this PC.</summary>
    public bool Enabled { get; init; }

    /// <summary>Window class of the RDP client. <c>TscShellContainerClass</c> is mstsc.</summary>
    [Required]
    public string WindowClass { get; init; } = "TscShellContainerClass";

    public bool RequireForeground { get; init; } = true;
}

/// <summary>Checks the data annotations above, including the nested sections. Generated at compile time.</summary>
[OptionsValidator]
internal sealed partial class VeraInputOptionsValidator : IValidateOptions<VeraInputOptions>;
