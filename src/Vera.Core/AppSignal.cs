namespace Vera.Core;

public enum AppSignalKind
{
    ErrorDialog,
    Crashed,
    NotResponding,
}

/// <summary>Something the adapter noticed about the app itself, outside the element list. The Check step reads these.</summary>
/// <param name="Message">E.g. the text of the error dialog. Comes from the app, so it is untrusted input to the model.</param>
public sealed record AppSignal(AppSignalKind Kind, string Message);