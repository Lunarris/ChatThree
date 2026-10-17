using ChatThree.Code;
using ChatThree.GameFunctions;
using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace ChatThree.Ui.SettingsTabs;

public sealed class Miscellaneous(Configuration mutable) : ISettingsTab
{
    private Configuration Mutable { get; } = mutable;
    public string Name => Language.Options_Miscellaneous_Tab + "###tabs-miscellaneous";

    public void Draw(bool changed)
    {
        using (var combo = ImGuiUtil.BeginComboVertical(Language.Options_CommandHelpSide_Name, Mutable.CommandHelpSide.Name()))
        {
            if (combo.Success)
            {
                foreach (var side in Enum.GetValues<CommandHelpSide>())
                    if (ImGui.Selectable(side.Name(), Mutable.CommandHelpSide == side))
                        Mutable.CommandHelpSide = side;
            }
        }

        ImGuiUtil.HelpText(string.Format(Language.Options_CommandHelpSide_Description, Plugin.PluginName));
        ImGui.Spacing();

        using (var combo = ImGuiUtil.BeginComboVertical(Language.Options_KeybindMode_Name, Mutable.KeybindMode.Name()))
        {
            if (combo.Success)
            {
                foreach (var mode in Enum.GetValues<KeybindMode>())
                {
                    if (ImGui.Selectable(mode.Name(), Mutable.KeybindMode == mode))
                        Mutable.KeybindMode = mode;

                    if (ImGui.IsItemHovered())
                        ImGuiUtil.Tooltip(mode.Tooltip() ?? "");
                }
            }
        }

        ImGuiUtil.HelpText(string.Format(Language.Options_KeybindMode_Description, Plugin.PluginName));
        ImGui.Spacing();

        ImGui.Checkbox(Language.Options_SortAutoTranslate_Name, ref Mutable.SortAutoTranslate);
        ImGuiUtil.HelpText(Language.Options_SortAutoTranslate_Description);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref Mutable.WarnPublicChannels, Language.Options_WarnPublicChannels_Name, Language.Options_WarnPublicChannels_Description);
        if (Mutable.WarnPublicChannels)
        {
            using var _ = ImRaii.PushIndent(10.0f);
            ImGui.Checkbox(Language.Options_WarnAllowParty_Name, ref Mutable.WarnAllowParty);
            DrawAllowedShells();
        }

        ImGui.Spacing();
    }

    private void DrawAllowedShells()
    {
        ImGui.TextUnformatted(Language.Options_WarnAllowedShells);
        using var _ = ImRaii.PushIndent(10.0f);

        var any = false;
        foreach (var channel in Enum.GetValues<InputChannel>())
        {
            var idx = channel.LinkshellIndex();
            if (channel.IsLinkshell() && ChannelGuard.LinkshellId(idx) is var id and not 0)
                any |= AllowCheckbox(channel, Mutable.WarnAllowedLinkshells, id);
            else if (channel.IsCrossLinkshell() && ChannelGuard.CrossLinkshellKey(idx) is { } key)
                any |= AllowCheckbox(channel, Mutable.WarnAllowedCrossLinkshells, key);
            else if (channel.IsExtraChatLinkshell() && Plugin.CommandManager.Commands.ContainsKey(channel.Prefix()))
                any |= AllowCheckbox(channel, Mutable.WarnAllowedExtraChatLinkshells, idx);
        }

        if (!any)
            ImGuiUtil.HelpText(Language.Options_WarnNoShells);
    }

    private static bool AllowCheckbox<T>(InputChannel channel, HashSet<T> allowed, T key)
    {
        var isAllowed = allowed.Contains(key);
        if (ImGui.Checkbox($"{ChannelGuard.ChannelName(channel)}##warn-allow-{channel}", ref isAllowed))
        {
            if (isAllowed)
                allowed.Add(key);
            else
                allowed.Remove(key);
        }

        return true;
    }
}
