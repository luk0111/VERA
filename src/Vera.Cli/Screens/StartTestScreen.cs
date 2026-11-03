using Vera.Application;

namespace Vera.Cli.Screens;

internal static class StartTestScreen
{
    public static async Task ShowAsync(AppSettings settings, Func<ChatSession> newChatSession)
    {
        if (ProcessPicker.Pick(settings, "Start testing an app") is { } app)
        {
            await ChatScreen.ShowAsync(settings, app, newChatSession());
        }
    }
}
