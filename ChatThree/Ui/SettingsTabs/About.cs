using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace ChatThree.Ui.SettingsTabs;

public sealed class About : ISettingsTab
{
    public string Name => string.Format(Language.Options_About_Tab, Plugin.PluginName) + "###tabs-about";

    public void Draw(bool changed)
    {
        using var wrap = ImRaii.TextWrapPos(0.0f);

        ImGui.TextUnformatted(string.Format(Language.Options_About_BasedOn, Plugin.PluginName));

        ImGuiHelpers.ScaledDummy(10.0f);

        ImGui.TextUnformatted(Language.Options_About_Authors);
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.ParsedGold, Plugin.Interface.Manifest.Author);

        ImGui.TextUnformatted(Language.Options_About_Version);
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.ParsedOrange, Plugin.Interface.Manifest.AssemblyVersion.ToString(3));

        ImGuiHelpers.ScaledDummy(10.0f);

        ImGui.Spacing();

        ImGui.TextUnformatted(Language.Options_About_Github_Issues);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "githubIssues"))
            Dalamud.Utility.Util.OpenLink("https://github.com/Lunarris/ChatThree/issues");

        ImGui.Spacing();

        ImGuiHelpers.ScaledDummy(10.0f);

        ImGui.TextUnformatted(Language.Options_About_ChatTwo);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "githubUpstream"))
            Dalamud.Utility.Util.OpenLink("https://github.com/Infiziert90/ChatTwo");
    }
}
