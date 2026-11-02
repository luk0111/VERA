namespace Vera.Core;

/// <summary>Sees and drives one app. One implementation per platform (desktop, web, remote). Disposing closes or detaches from the app.</summary>
public interface IAppAdapter : IAsyncDisposable
{
    /// <summary>Starts the app from <see cref="AppTarget.Location"/>, or attaches to <see cref="AppTarget.ProcessId"/> when it is set.</summary>
    Task StartAsync(AppTarget target, CancellationToken ct);

    /// <summary>Reads the current window into numbered elements, a screenshot and signals. Element ids are only valid for this state.</summary>
    Task<ScreenState> GetScreenStateAsync(CancellationToken ct);

    /// <summary>Clicks the element with this id from the latest <see cref="GetScreenStateAsync"/>.</summary>
    Task ClickAsync(int elementId, CancellationToken ct);

    /// <summary>Types <paramref name="text"/> into the element. The text is already resolved: it contains no secret placeholders.</summary>
    Task TypeAsync(int elementId, string text, CancellationToken ct);

    /// <summary>Presses keys, e.g. <c>Enter</c> or <c>Ctrl+S</c>.</summary>
    Task PressKeyAsync(string keys, CancellationToken ct);
}