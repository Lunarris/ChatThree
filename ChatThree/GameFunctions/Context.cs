using ChatThree.Util;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace ChatThree.GameFunctions;

public sealed unsafe class Context
{
    public static void InviteToNoviceNetwork(string name, ushort world)
    {
        // can specify content id if we have it, but there's no need
        InfoProxyNoviceNetwork.Instance()->InviteToNoviceNetwork(0, 0, world, name.ToTerminatedBytes());
    }

    public static void TryOn(uint itemId, byte stainId)
    {
        AgentTryon.TryOn(0xFF, itemId, stainId);
    }

    public static void LinkItem(uint itemId)
    {
        AgentChatLog.Instance()->LinkItem(itemId);
    }

    public static void LinkStatus(uint statusId)
    {
        AgentChatLog.Instance()->ContextStatusId = statusId;
    }

    public static void OpenItemComparison(uint itemId)
    {
        AgentItemComp.Instance()->CompareItem(0x4D, itemId, 0, 0);
    }

    public static void SearchForRecipesUsingItem(uint itemId)
    {
        AgentRecipeProductList.Instance()->SearchForRecipesUsingItem(itemId);
    }

    public static void SearchForItem(uint itemId)
    {
        ItemFinderModule.Instance()->SearchForItem(itemId);
    }
}
