using System.Numerics;
using ChatThree.Code;
using ChatThree.GameFunctions.Types;
using ChatThree.Resources;
using ChatThree.Ui.Handler;
using ChatThree.Util;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace ChatThree.Ui;

public class Popout : Window, IChatWindow
{
    private readonly Plugin Plugin;
    private readonly Tab Tab;

    private long FrameTime;
    private long LastActivityTime = Environment.TickCount64;
    private bool Docked;
    private HideState OwnHideState = HideState.None;

    private const string ChatChannelPicker = "chat-popout-channel-picker";
    private List<(string Name, InputChannel Channel)> PickerChannels = [];

    public readonly InputHandler InputHandler;

    public Vector2 LastWindowPos { get; set; } = Vector2.Zero;
    public Vector2 LastWindowSize { get; set; } = Vector2.Zero;
    public HideState CurrentHideState
    {
        get => Tab.IndependentHide ? OwnHideState : Plugin.ChatLog.CurrentHideState;
        set
        {
            if (Tab.IndependentHide)
                OwnHideState = value;
            else
                Plugin.ChatLog.CurrentHideState = value;
        }
    }

    public Popout(Plugin plugin, Tab tab) : base(tab.IsTempTab ? $"{tab.Name}##popout" : $"{tab.Name}###popout-{tab.Identifier}")
    {
        Plugin = plugin;
        Tab = tab;

        InputHandler = new InputHandler(this, plugin, $"popout-{tab.Identifier}");

        Size = new Vector2(350, 350);
        SizeCondition = ImGuiCond.FirstUseEver;

        IsOpen = true;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
    }

    public override void PreOpenCheck()
    {
        if (!Tab.PopOut)
            IsOpen = false;
    }

    public override bool DrawConditions()
    {
        FrameTime = Environment.TickCount64;

        var isHidden = Tab.IndependentHide
            ? HideStateHelper.HideStateCheck(this, Tab.HideInBattle, Tab.HideDuringCutscenes, Tab.HideWhenNotLoggedIn, Plugin.ChatLog.ActivatedThisFrame || InputHandler.Activate)
            : Plugin.ChatLog.IsHidden;

        if (isHidden)
            return false;

        if (!Plugin.Config.HideWhenInactive || (!Plugin.Config.InactivityHideActiveDuringBattle && Plugin.InBattle) || !Tab.UnhideOnActivity)
        {
            LastActivityTime = FrameTime;
            return true;
        }

        var lastActivityTime = Math.Max(Tab.LastActivity, LastActivityTime);
        lastActivityTime = Math.Max(lastActivityTime, InputHandler.LastActivityTime);
        return FrameTime - lastActivityTime <= 1000 * Plugin.Config.InactivityHideTimeout;
    }

    public override void PreDraw()
    {
        if (Plugin.Config is { OverrideStyle: true, ChosenStyle: not null })
            StyleModel.GetConfiguredStyles()?.FirstOrDefault(style => style.Name == Plugin.Config.ChosenStyle)?.Push();

        Flags = ImGuiWindowFlags.NoFocusOnAppearing;
        if (!Plugin.Config.ShowPopOutTitleBar)
            Flags |= ImGuiWindowFlags.NoTitleBar;

        if (!Tab.CanMove)
            Flags |= ImGuiWindowFlags.NoMove;

        if (!Tab.CanResize)
            Flags |= ImGuiWindowFlags.NoResize;

        if (!Docked)
        {
            var alpha = Tab.IndependentOpacity ? Tab.Opacity : Plugin.Config.WindowAlpha;
            BgAlpha = alpha / 100f;
        }
    }

    public override void Draw()
    {
        using var id = ImRaii.PushId($"popout-{Tab.Identifier}");
        using var disabled = ImRaii.Disabled(SendHandler.AnyPending);

        LastWindowSize = ImGui.GetWindowSize();
        LastWindowPos = ImGui.GetWindowPos();

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows))
            LastActivityTime = FrameTime;

        if (!Plugin.Config.ShowPopOutTitleBar)
        {
            ImGui.TextUnformatted(Tab.Name);

            if (Tab.IsTempTab)
            {
                ImGui.SameLine();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Times))
                    IsOpen = false;
            }

            ImGui.Separator();
        }

        var ownsPreview = Tab.SupportsInput && Plugin.InputPreview.InputHandler == InputHandler;
        if (ownsPreview && Plugin.Config.PreviewPosition is PreviewPosition.Inside or PreviewPosition.Tooltip && Plugin.InputPreview.IsDrawable)
            Plugin.InputPreview.CalculatePreview();

        var remainingHeight = Tab.SupportsInput
            ? Plugin.ChatLog.GetRemainingHeightForMessageLog(ownsPreview)
            : ImGui.GetContentRegionAvail().Y;

        Plugin.ChatLog.DrawMessageLog(Tab, InputHandler.PayloadHandler, remainingHeight, false);

        if (!Tab.SupportsInput)
            return;

        if (ownsPreview && Plugin.Config.PreviewPosition is PreviewPosition.Inside && Plugin.InputPreview.IsDrawable)
            Plugin.InputPreview.DrawPreview();

        if (Tab.Channel is not null)
            Tab.CurrentChannel.SetChannel(Tab.Channel.Value);

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
            Plugin.ChatLog.DrawChannelName(Tab);

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Comment) && Tab.Channel is null)
        {
            PickerChannels = Plugin.ChatLog.GetValidChannels()
                .Where(pair => pair.Value != InputChannel.Tell)
                .Select(pair => (pair.Key, pair.Value))
                .ToList();
            ImGui.OpenPopup(ChatChannelPicker);
        }

        if (Tab.Channel is not null && ImGui.IsItemHovered())
            ImGuiUtil.Tooltip(Language.ChatLog_SwitcherDisabled);

        using (var popup = ImRaii.Popup(ChatChannelPicker))
        {
            if (popup)
            {
                foreach (var (name, channel) in PickerChannels)
                    if (ImGui.Selectable(name))
                        Tab.CurrentChannel.SetChannel(channel);
            }
        }

        ImGui.SameLine();

        var tellSpecial = false;
        InputHandler.DrawInputArea(Tab, ImGui.GetContentRegionAvail().X, ref tellSpecial);
        InputHandler.AutoCompleteHandler.DrawAutoComplete();
    }

    public override void PostDraw()
    {
        Docked = ImGui.IsWindowDocked();

        if (Plugin.Config is { OverrideStyle: true, ChosenStyle: not null })
            StyleModel.GetConfiguredStyles()?.FirstOrDefault(style => style.Name == Plugin.Config.ChosenStyle)?.Pop();
    }

    public override void OnClose()
    {
        Plugin.InputPreview.ReleaseSource(InputHandler);

        if (Tab.IsTempTab)
        {
            Plugin.TellWindows.Remove(Tab);
            Plugin.WindowSystem.RemoveWindow(this);
            return;
        }

        Plugin.ChatLog.PopOutWindows.Remove(Tab.Identifier);
        Plugin.WindowSystem.RemoveWindow(this);

        Tab.PopOut = false;
        Plugin.SaveConfig();
    }
}
