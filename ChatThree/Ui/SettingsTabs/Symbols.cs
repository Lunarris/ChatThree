using ChatThree.Resources;
using ChatThree.Ui.Handler;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace ChatThree.Ui.SettingsTabs;

public sealed class Symbols : ISettingsTab
{
    private Plugin Plugin { get; }
    private Configuration Mutable { get; }

    public string Name => $"{Language.Options_Symbols_Tab}###tabs-symbols";

    public Symbols(Plugin plugin, Configuration mutable)
    {
        Plugin = plugin;
        Mutable = mutable;
    }

    public void Draw(bool changed)
    {
        using var wrap = ImRaii.TextWrapPos(0.0f);

        ImGuiUtil.OptionCheckbox(ref Mutable.ShowSymbolPicker, Language.Options_SymbolPicker_Name, Language.Options_SymbolPicker_Description);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref Mutable.SymbolShortcuts, Language.Options_SymbolShortcuts_Name, Language.Options_SymbolShortcuts_Description);
        ImGui.Spacing();

        using (ImRaii.Disabled(!Mutable.SymbolShortcuts))
        {
            DrawCustomShortcuts();
            ImGui.Spacing();
            DrawBuiltIn();
        }

        ImGui.Spacing();
        using (ImRaii.Disabled(Plugin.Config.RecentSymbols.Count == 0))
        {
            // Recent symbols are live state, so this clears them right away
            if (ImGui.Button(Language.Options_SymbolShortcuts_ClearRecent))
            {
                Plugin.Config.RecentSymbols.Clear();
                Plugin.SaveConfig();
            }
        }
    }

    private void DrawCustomShortcuts()
    {
        using var tree = ImRaii.TreeNode(Language.Options_SymbolShortcuts_Custom, ImGuiTreeNodeFlags.DefaultOpen);
        if (!tree)
            return;

        ImGuiUtil.HelpText(Language.Options_SymbolShortcuts_CustomHelp);
        var shortcuts = Mutable.CustomSymbolShortcuts ??= [];
        var nameWidth = 120 * ImGuiHelpers.GlobalScale;
        var textWidth = 160 * ImGuiHelpers.GlobalScale;

        int? remove = null;
        for (var i = 0; i < shortcuts.Count; i++)
        {
            var shortcut = shortcuts[i];
            using var id = ImRaii.PushId(i);

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash))
                remove = i;

            ImGui.SameLine();
            ImGui.SetNextItemWidth(nameWidth);
            ImGui.InputTextWithHint("##name", Language.Options_SymbolShortcuts_Name_Hint, ref shortcut.Name, 32);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(textWidth);
            ImGui.InputTextWithHint("##text", Language.Options_SymbolShortcuts_Text_Hint, ref shortcut.Text, 64);

            if (!string.IsNullOrWhiteSpace(shortcut.Name))
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled($":{shortcut.Name.Trim()}:");
            }

            if (shortcut.Text.Length > 0 && !SymbolHandler.IsSendable(Plugin, shortcut.Text))
                ImGuiUtil.WarningText(Language.Options_SymbolShortcuts_Unsendable);
        }

        if (remove is { } index)
            shortcuts.RemoveAt(index);

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "add-shortcut", Language.Options_SymbolShortcuts_Add))
            shortcuts.Add(new SymbolShortcut());
    }

    private void DrawBuiltIn()
    {
        using var tree = ImRaii.TreeNode(Language.Options_SymbolShortcuts_BuiltIn);
        if (!tree)
            return;

        // The tab wraps text, which squeezes fit-to-content columns down to one character
        using var noWrap = ImRaii.TextWrapPos(-1.0f);
        using var table = ImRaii.Table("##built-in-symbols", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        foreach (var entry in SymbolHandler.AllGameSymbols(Plugin))
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Text);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($":{entry.BuiltInShortcut}:");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(entry.Name);
        }
    }
}
