using System.Diagnostics;

using Vera.Core;

namespace Vera.Adapters.Desktop;

/// <summary>
/// Lists the Windows processes with a visible main window, one row per window the tester could pick:
/// a browser with two windows is listed twice. The main window decides which processes count, which keeps out background helpers.
/// </summary>
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
                        var name = $"{process.ProcessName}.exe";
                        var windows = ProcessWindows.AppWindows(process.Id);
                        if (windows.Count == 0)
                        {
                            apps.Add(new RunningAppInfo(process.Id, name, process.MainWindowTitle, process.MainWindowHandle));
                        }

                        apps.AddRange(windows.Select(w => new RunningAppInfo(process.Id, name, w.Title, w.Handle)));
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
