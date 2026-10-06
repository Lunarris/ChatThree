using System.Numerics;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Config;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ChatThree.Ui.ChatLog;

public partial class ChatLog
{
    private unsafe void MoveTooltip(AddonEvent type, AddonArgs args)
    {
        // Only move if the user has the "Next to Cursor" option selected
        if (!Plugin.GameConfig.TryGet(UiControlOption.DetailTrackingType, out uint selected) || selected != 0)
            return;

        if (LastViewport != ImGuiHelpers.MainViewport.Handle)
            return;

        var atk = args.Addon;
        if (atk.IsNull)
            return;

        var atkBase = (AtkUnitBase*)atk.Address;
        if (atkBase->WindowNode == null)
            return;

        if (!atkBase->IsVisible)
            return;

        var component = atkBase->WindowNode->AtkResNode;
        var atkPos = new Vector2(component.ScreenX, component.ScreenY);
        var atkSize = new Vector2(component.GetWidth() * component.ScaleX, component.GetHeight() * component.GetScaleY());

        var chatRect = new MathUtil.Rectangle(LastWindowPos, LastWindowSize);
        var addonRect = new MathUtil.Rectangle(atkPos, atkSize);

        if (!chatRect.HasOverlap(addonRect))
            return;

        var viewportSize = ImGuiHelpers.MainViewport.Size;
        var isLeft = chatRect.SizeX < viewportSize.X / 2;
        var isTop = chatRect.SizeY < viewportSize.Y / 2;

        var mousePos = ImGui.GetMousePos();

        // addon spawned left of mouse cursor
        if (addonRect.X < mousePos.X)
        {
            if (isLeft)
                addonRect.X = (short)mousePos.X + 5;
        }
        else
        {
            if (!isLeft)
                addonRect.X = Math.Max(0, (short)mousePos.X - 5 - addonRect.Width);
        }

        if (!chatRect.HasOverlap(addonRect))
        {
            atkBase->SetPosition((short) addonRect.X, (short) addonRect.Y);
            return;
        }

        // addon spawned above mouse cursor
        if (addonRect.Y < mousePos.Y)
        {
            if (isTop)
                addonRect.Y = (short)mousePos.Y + 5;
        }
        else
        {
            if (!isTop)
                addonRect.Y = Math.Max(0, (short)mousePos.Y - 5 - addonRect.Height); // prevent it going below 0
        }

        if (!chatRect.HasOverlap(addonRect))
        {
            atkBase->SetPosition((short) addonRect.X, (short) addonRect.Y);
            return;
        }

        // Spawning right/bottom of mouse cursor didn't solve the overlap, so we spawn it next to the chat
        var x = isLeft ? chatRect.SizeX : LastWindowPos.X - atkSize.X;
        var y = Math.Clamp(chatRect.SizeY - atkSize.Y, 0, float.MaxValue);
        y -= isTop ? 0 : Plugin.Config.TooltipOffset; // offset to prevent cut-off on the bottom

        atkBase->SetPosition((short) x, (short) y);
    }
}