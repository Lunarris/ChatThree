using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace ChatThree.Ui.SettingsTabs;

public sealed class Preview : ISettingsTab
{
    private Plugin Plugin { get; }
    private Configuration Mutable { get; }
    private volatile bool RefreshingNames;

    public string Name => $"{Language.Options_Preview_Tab}###tabs-preview";

    public Preview(Plugin plugin, Configuration mutable)
    {
        Plugin = plugin;
        Mutable = mutable;
    }

    public void Draw(bool changed)
    {
        using var wrap = ImRaii.TextWrapPos(0.0f);

        ImGuiUtil.OptionCheckbox(ref Mutable.SpellCheck, Language.Options_SpellCheck_Name, Language.Options_SpellCheck_Description);
        ImGui.Spacing();
        DrawRefreshNames();
        ImGui.Spacing();

        using (var combo = ImGuiUtil.BeginComboVertical(Language.Options_Preview_Name, Mutable.PreviewPosition.Name()))
        {
            if (combo)
            {
                foreach (var position in Enum.GetValues<PreviewPosition>())
                    if (ImGui.Selectable(position.Name(), Mutable.PreviewPosition == position))
                        Mutable.PreviewPosition = position;
            }
        }
        ImGuiUtil.HelpText(Language.Options_Preview_Description);
        if (Mutable.SpellCheck && Mutable.PreviewPosition is PreviewPosition.None or PreviewPosition.Tooltip)
            ImGuiUtil.WarningText(Language.Options_SpellCheck_ForcedPosition);
        ImGui.Spacing();

        using (ImRaii.Disabled(Mutable.SpellCheck))
        {
            if (ImGuiUtil.InputIntVertical(Language.Options_PreviewMinimum_Name, Language.Options_PreviewMinimum_Description, ref Mutable.PreviewMinimum))
                Mutable.PreviewMinimum = Math.Clamp(Mutable.PreviewMinimum, 1, 250);
            ImGui.Spacing();
            ImGuiUtil.OptionCheckbox(ref Mutable.OnlyPreviewIf, Language.Options_PreviewOnlyIf_Name, Language.Options_PreviewOnlyIf_Description);
        }
        if (Mutable.SpellCheck)
            ImGuiUtil.WarningText(Language.Options_SpellCheck_Overridden);

        ImGui.Spacing();
        DrawDictionary();
    }

    private void DrawRefreshNames()
    {
        using (ImRaii.Disabled(RefreshingNames))
        {
            if (ImGui.Button(Language.Options_SpellCheck_RefreshNames))
            {
                RefreshingNames = true;
                Task.Run(() =>
                {
                    try
                    {
                        Plugin.TellWindows.LoadPartnersFromHistory();
                        Plugin.InputPreview.RefreshNames();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error(ex, "Error loading tell partners for spell check");
                    }
                    finally
                    {
                        RefreshingNames = false;
                    }
                });
            }
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGuiUtil.HelpText(RefreshingNames
            ? Language.Options_SpellCheck_RefreshingNames
            : string.Format(Language.Options_SpellCheck_NameCount, Plugin.TellWindows.PartnerNames().Distinct(StringComparer.OrdinalIgnoreCase).Count()));
        ImGuiUtil.HelpText(Language.Options_SpellCheck_RefreshNames_Description);
    }

    // Works on the live config, since words are also added from the preview while this window is open
    private void DrawDictionary()
    {
        using var tree = ImRaii.TreeNode(Language.Options_SpellCheck_Words);
        if (!tree)
            return;

        var words = Plugin.Config.SpellCheckWords;
        if (words.Count == 0)
        {
            ImGuiUtil.HelpText(Language.Options_SpellCheck_WordsEmpty);
            return;
        }

        string? remove = null;
        foreach (var word in words.Order())
        {
            using var id = ImRaii.PushId(word);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash))
                remove = word;

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(word);
        }

        if (remove != null && words.Remove(remove))
            Plugin.SaveConfig();
    }
}
