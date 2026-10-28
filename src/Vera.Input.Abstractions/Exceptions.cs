namespace Vera.Input;

public enum ScreenUnavailableReason
{
    BlackFrame,
    RdpWindowMissing,
    RdpWindowMinimized,
    RdpWindowNotForeground,
    WorkstationLocked,
    NoInteractiveSession,

    /// <summary>The RDP window is in front but doesn't fill the capture monitor (e.g. not full screen).</summary>
    RdpWindowNotCoveringScreen,
}

/// <summary>The screen can't be seen or controlled right now. The run should stop rather than retry blindly.</summary>
public sealed class ScreenUnavailableException : Exception
{
    public ScreenUnavailableException(ScreenUnavailableReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public ScreenUnavailableException(ScreenUnavailableReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public ScreenUnavailableReason Reason { get; }
}

/// <summary>Windows injected fewer input events than were sent (e.g. blocked by UIPI or a locked desktop).</summary>
public sealed class InputRejectedException : Exception
{
    public InputRejectedException()
    {
    }

    public InputRejectedException(string message)
        : base(message)
    {
    }

    public InputRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The key combo can't be parsed or isn't supported (e.g. <c>ctrl+alt+del</c>). The message lists what is allowed.</summary>
public sealed class InvalidKeyComboException : Exception
{
    public InvalidKeyComboException()
    {
    }

    public InvalidKeyComboException(string message)
        : base(message)
    {
    }

    public InvalidKeyComboException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}