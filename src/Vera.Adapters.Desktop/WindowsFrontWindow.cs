using Vera.Core;
using Windows.Win32;

namespace Vera.Adapters.Desktop;

/// <summary>The window in front on Windows, e.g. Windows Terminal. A console app's own window can be hidden behind the terminal's, so the handle is taken from what is in front.</summary>
public sealed class WindowsFrontWindow : IFrontWindow
{
    private readonly ForegroundSwitch _switch = new(new Win32ForegroundApi());

    public FrontWindow? Current()
    {
        nint window = PInvoke.GetForegroundWindow();
        return window == 0 ? null : new FrontWindow(window);
    }

    public async Task<bool> BringBackAsync(FrontWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(window);
        try
        {
            await _switch.BringToFrontAsync((nint)window.Id, ct).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
