using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace ChatThree.Ui.SettingsTabs;

public sealed class ThemeSettings : ISettingsTab
{
    private Configuration Mutable { get; }

    public string Name => Language.Options_Theme_Tab + "###tabs-theme";

    public ThemeSettings(Configuration mutable)
    {
        Mutable = mutable;
    }

    public void Draw(bool changed)
    {
        using var wrap = ImRaii.TextWrapPos(0.0f);

        Header(Language.Options_Theme_Header);

        ImGuiUtil.OptionCheckbox(ref Mutable.UseTheme, Language.Options_Theme_Name, Language.Options_Theme_Desc);
        ImGui.Spacing();

        using (ImRaii.Disabled(!Mutable.UseTheme))
        using (var combo = ImRaii.Combo(Language.Options_ThemeScheme_Name, Theme.SchemeName(Mutable.ThemeScheme)))
        {
            if (combo)
            {
                foreach (var scheme in Enum.GetValues<ThemeScheme>())
                    if (ImGui.Selectable(Theme.SchemeName(scheme), Mutable.ThemeScheme == scheme))
                        Mutable.ThemeScheme = scheme;
            }
        }

        ImGui.Spacing();
        ImGui.Spacing();

        Header(Language.Options_DalamudStyle_Header);

        using (ImRaii.Disabled(Mutable.UseTheme))
            ImGuiUtil.OptionCheckbox(ref Mutable.OverrideStyle, Language.Options_DalamudStyle_Name, Language.Options_DalamudStyle_Desc);
        ImGui.Spacing();

        ImGuiUtil.HelpText(Language.Options_DalamudStyle_HowTo);
        ImGui.Spacing();

        if (Mutable.UseTheme)
            ImGuiUtil.WarningText(Language.Options_DalamudStyle_ThemeActive);
    }

    private static void Header(string text)
    {
        ImGui.TextUnformatted(text);
        ImGui.Separator();
        ImGui.Spacing();
    }
}
