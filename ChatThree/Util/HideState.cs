using ChatThree.GameFunctions;
using ChatThree.GameFunctions.Types;
using ChatThree.Ui;

namespace ChatThree.Util;

public static class HideStateHelper
{
    public static bool HideStateCheck(IChatWindow current, bool hideInBattle, bool hideDuringCutscenes, bool hideWhenNotLoggedIn, bool activate)
    {
        // if the chat has no hide state set, and the player has entered battle, we hide chat if they have configured it
        if (hideInBattle && current.CurrentHideState is HideState.None && Plugin.InBattle)
            current.CurrentHideState = HideState.Battle;

        // If the chat is hidden because of battle, we reset it here
        if (current.CurrentHideState is HideState.Battle && !Plugin.InBattle)
            current.CurrentHideState = HideState.None;

        // if the chat has no hide state and in a cutscene, set the hide state to cutscene
        if (hideDuringCutscenes && current.CurrentHideState is HideState.None && (Plugin.CutsceneActive || Plugin.GposeActive))
        {
            if (Chat.CheckHideFlags())
                current.CurrentHideState = HideState.Cutscene;
        }

        // if the chat is hidden because of a cutscene and no longer in a cutscene, set the hide state to none
        if (current.CurrentHideState is HideState.Cutscene or HideState.CutsceneOverride && !Plugin.CutsceneActive && !Plugin.GposeActive)
            current.CurrentHideState = HideState.None;

        // if the chat is hidden because of a cutscene and the chat has been activated, show chat
        if (current.CurrentHideState is HideState.Cutscene && activate)
            current.CurrentHideState = HideState.CutsceneOverride;

        // if the user hid the chat and is now activating chat, reset the hide state
        if (current.CurrentHideState is HideState.User && activate)
            current.CurrentHideState = HideState.None;

        return current.CurrentHideState is HideState.Cutscene or HideState.User or HideState.Battle || (hideWhenNotLoggedIn && !Plugin.ClientState.IsLoggedIn);
    }
}