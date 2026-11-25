using Vera.Application;
using Vera.Core;

namespace Vera.Cli.Screens;

internal static class StartTestScreen
{
    public static async Task ShowAsync(AppSettings settings, IRunningAppSource runningApps, Func<ChatSession> newChatSession, ChatAppContextReader appReader,
        Func<FindElementService> newFind, Func<StepRunner> newSteps, StepFileStore store)
    {
        if (ProcessPicker.Pick(settings, runningApps, "Start testing an app") is { } app)
        {
            await ChatScreen.ShowAsync(settings, app, newChatSession(), appReader, newFind(), newSteps(), store);
        }
    }
}
