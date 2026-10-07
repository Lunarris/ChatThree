using System.Numerics;
using ChatThree.Resources;
using ChatThree.Ui.SettingsTabs;
using ChatThree.Util;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;

namespace ChatThree.Ui;

public sealed class SettingsWindow : Window
{
    private readonly Plugin Plugin;

    private Configuration Mutable { get; }
    private List<ISettingsTab> Tabs { get; }
    private int CurrentTab;

    public SettingsWindow(Plugin plugin) : base($"{Language.Settings_Title.Format(Plugin.PluginName)}###chat3-settings")
    {
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(475, 600),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        Mutable = new Configuration();

        Tabs =
        [
            new Display(Mutable),
            new ChatLogConfig(Plugin, Mutable),
            new Preview(Mutable),
            new Fonts(Mutable),
            new ChatColours(Plugin, Mutable),
            new Tabs(Plugin, Mutable),
            new Alerts(Mutable),
            new Database(Plugin, Mutable),
            new Miscellaneous(Mutable),
            new About()
        ];

        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        Initialise();

        Plugin.Commands.Register("/chat3", "Perform various actions with Chat 3.").Execute += Command;
        Plugin.Interface.UiBuilder.OpenConfigUi += Toggle;
    }

    public void Dispose()
    {
        Plugin.Interface.UiBuilder.OpenConfigUi -= Toggle;
        Plugin.Commands.Register("/chat3").Execute -= Command;
    }

    private void Command(string command, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            Toggle();
    }

    private void Initialise()
    {
        Mutable.UpdateFrom(Plugin.Config, false);
    }

    public void AddPlayerAlert(PlayerAlert alert)
    {
        Plugin.Config.PlayerAlerts.Add(alert);
        Plugin.SaveConfig();

        // Otherwise saving the open settings window would drop the new alert
        if (IsOpen)
            Mutable.PlayerAlerts.Add(alert.Clone());
    }

    public void RemovePlayerAlerts(string name, uint world)
    {
        Plugin.Config.PlayerAlerts.RemoveAll(a => a.IsFor(name, world));
        Plugin.SaveConfig();

        if (IsOpen)
            Mutable.PlayerAlerts.RemoveAll(a => a.IsFor(name, world));
    }

    public void SetPlayerAlertsEnabled(string name, uint world, bool enabled)
    {
        foreach (var alert in Plugin.Config.PlayerAlerts.Where(a => a.IsFor(name, world)))
            alert.Enabled = enabled;
        Plugin.SaveConfig();

        if (!IsOpen)
            return;

        foreach (var alert in Mutable.PlayerAlerts.Where(a => a.IsFor(name, world)))
            alert.Enabled = enabled;
    }

    public override void Draw()
    {
        if (ImGui.IsWindowAppearing())
            Initialise();

        using (var table = ImRaii.Table("##chat3-settings-table", 2))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("tab", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("settings", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextColumn();

                var changed = false;
                for (var i = 0; i < Tabs.Count; i++)
                {
                    if (!ImGui.Selectable($"{Tabs[i].Name}###tab-{i}", CurrentTab == i))
                        continue;

                    CurrentTab = i;
                    changed = true;
                }

                ImGui.TableNextColumn();

                var style = ImGui.GetStyle();
                var height = ImGui.GetContentRegionAvail().Y - style.FramePadding.Y * 2 - style.ItemSpacing.Y - style.ItemInnerSpacing.Y * 2 - ImGui.CalcTextSize("A").Y;

                using var child = ImRaii.Child("##chat3-settings", new Vector2(-1, height));
                if (child.Success)
                    Tabs[CurrentTab].Draw(changed);
            }
        }

        ImGui.Separator();

        var save = ImGui.Button(Language.Settings_Save);

        ImGui.SameLine();

        if (ImGui.Button(Language.Settings_SaveAndClose)) {
            save = true;
            IsOpen = false;
        }

        ImGui.SameLine();

        if (ImGui.Button(Language.Settings_Discard)) {
            IsOpen = false;
        }

        using (ImRaii.PushColor(ImGuiCol.Button, ColourUtil.RgbaToAbgr(0xFF5E5BFF)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, ColourUtil.RgbaToAbgr(0xFF7775FF)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, ColourUtil.RgbaToAbgr(0xFF4542FF)))

        if (!save)
            return;

        // calculate all conditions before updating config
        var hideChanged = !Mutable.HideChat && Mutable.HideChat != Plugin.Config.HideChat;
        var fontChanged = Mutable.GlobalFontV2 != Plugin.Config.GlobalFontV2
                          || Mutable.JapaneseFontV2 != Plugin.Config.JapaneseFontV2
                          || Mutable.ItalicFontV2 != Plugin.Config.ItalicFontV2
                          || Mutable.ExtraGlyphRanges != Plugin.Config.ExtraGlyphRanges;
        var fontSizeChanged = Math.Abs(Mutable.SymbolsFontSizeV2 - Plugin.Config.SymbolsFontSizeV2) > 0.001
                          || Math.Abs(Mutable.FontSizeV2 - Plugin.Config.FontSizeV2) > 0.001;
        var italicStateChanged = Mutable.ItalicEnabled != Plugin.Config.ItalicEnabled;

        Plugin.Config.UpdateFrom(Mutable, true);

        // save after 60 frames have passed, which should hopefully not
        // commit any changes that cause a crash
        Plugin.DeferredSaveFrames = 60;
        Plugin.MessageManager.ClearAllTabs();
        Plugin.MessageManager.FilterAllTabsAsync();

        if (fontChanged || fontSizeChanged || italicStateChanged)
            Plugin.FontManager.BuildFonts();

        if (hideChanged)
            GameFunctions.GameFunctions.SetChatInteractable(true);

        Initialise();
    }
}
