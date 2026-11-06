using System.Diagnostics;

using Vera.Core;

namespace Vera.Adapters.Desktop;

/// <summary>Lists the Windows processes with a visible main window.</summary>
public sealed class WindowsRunningApps : IRunningAppSource
{
    public IReadOnlyList<RunningAppInfo> List()
    {
        var apps = new List<RunningAppInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        apps.Add(new RunningAppInfo(process.Id, $"{process.ProcessName}.exe", process.MainWindowTitle));
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the list was being read.
                }
            }
        }

        return apps;
    }
}
