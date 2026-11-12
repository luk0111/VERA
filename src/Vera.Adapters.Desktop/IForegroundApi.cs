namespace Vera.Adapters.Desktop;

/// <summary>The Windows calls <see cref="ForegroundSwitch"/> needs, so its retry order can be tested without a desktop.</summary>
internal interface IForegroundApi
{
    /// <summary>
    /// Whether the window in front belongs to <paramref name="window"/>'s process.
    /// Compares processes, not windows: a dialog of the app counts as the app, and bringing the main window forward would take the focus from it.
    /// </summary>
    bool IsInFront(nint window);

    /// <summary>Asks Windows to bring the window to the front. Windows may refuse without saying so.</summary>
    void Request(nint window);

    /// <summary>Sends an input event that does nothing, which makes this process the last one to get input.</summary>
    void SendEmptyInput();

    /// <summary>Asks again while sharing the input state of the window that is in front now.</summary>
    void RequestAttached(nint window);
}
