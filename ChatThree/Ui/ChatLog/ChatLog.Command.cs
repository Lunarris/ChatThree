using ChatThree.GameFunctions.Types;

namespace ChatThree.Ui.ChatLog;

public partial class ChatLog
{
    private void ToggleChat(string _, string arguments)
    {
        switch (arguments)
        {
            case "hide":
                CurrentHideState = HideState.User;
                break;
            case "show":
                CurrentHideState = HideState.None;
                break;
            case "toggle":
                CurrentHideState = CurrentHideState switch
                {
                    HideState.User or HideState.CutsceneOverride => HideState.None,
                    HideState.Cutscene => HideState.CutsceneOverride,
                    HideState.None => HideState.User,
                    _ => CurrentHideState,
                };
                break;
        }
    }

    private void ClearLog(string command, string arguments)
    {
        switch (arguments)
        {
            case "all":
                Plugin.MessageManager.ClearAllTabs();
                break;
            case "help":
                Plugin.ChatGui.Print("- /clearlog3: clears the active tab's log");
                Plugin.ChatGui.Print("- /clearlog3 all: clears all tabs' logs and the global history");
                Plugin.ChatGui.Print("- /clearlog3 help: shows this help");
                break;
            default:
                if (Plugin.LastTab > -1 && Plugin.LastTab < Plugin.Config.Tabs.Count)
                    Plugin.Config.Tabs[Plugin.LastTab].Clear();
                break;
        }
    }
}