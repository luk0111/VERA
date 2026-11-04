namespace Vera.Input.EchoApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Applies ApplicationHighDpiMode=PerMonitorV2 from the .csproj, so positions are real screen pixels.
        ApplicationConfiguration.Initialize();
        Application.Run(new EchoForm());
    }
}
