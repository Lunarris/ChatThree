using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;

namespace ChatThree.Ui.SettingsTabs;

public sealed class Alerts(Configuration mutable) : ISettingsTab
{
    private Configuration Mutable { get; } = mutable;
    public string Name => Language.Options_Alerts_Tab + "###tabs-alerts";

    private List<World> Worlds { get; } = Sheets.WorldSheet
        .Where(world => world.IsPublic)
        .OrderBy(world => world.Name.ToString())
        .ToList();

    public void Draw(bool changed)
    {
        ImGuiUtil.HelpText(Language.Options_Alerts_Description);
        ImGui.Spacing();

        if (ImGui.Button(Language.Options_Alerts_Add))
            Mutable.PlayerAlerts.Add(new PlayerAlert());

        ImGui.SameLine();

        var target = (Plugin.TargetManager.SoftTarget ?? Plugin.TargetManager.Target) as IPlayerCharacter;
        using (ImRaii.Disabled(target == null))
        {
            if (ImGui.Button(Language.Options_Alerts_AddTarget) && target != null)
                Mutable.PlayerAlerts.Add(new PlayerAlert { Name = target.Name.TextValue, World = target.HomeWorld.RowId });
        }

        ImGui.Spacing();

        if (Mutable.PlayerAlerts.Count == 0)
        {
            ImGuiUtil.HelpText(Language.Options_Alerts_Empty);
            return;
        }

        using var table = ImRaii.Table("##player-alerts", 5, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
        if (!table.Success)
            return;

        var buttonWidth = ImGuiUtil.CalcIconButtonSize().X;
        ImGui.TableSetupColumn("##enabled", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn(Language.Options_Alerts_Column_Name, ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn(Language.Options_Alerts_Column_World, ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn(Language.Options_Alerts_Column_Sound, ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, buttonWidth * 2 + ImGui.GetStyle().ItemSpacing.X);
        ImGui.TableHeadersRow();

        var toRemove = -1;
        foreach (var (idx, alert) in Mutable.PlayerAlerts.Index())
        {
            using var id = ImRaii.PushId(idx);

            ImGui.TableNextColumn();
            ImGui.Checkbox("##enabled", ref alert.Enabled);
            if (ImGui.IsItemHovered())
                ImGuiUtil.Tooltip(Language.Options_Alerts_Enabled_Tooltip);

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##name", Language.Options_Alerts_NameHint, ref alert.Name, 32);

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            using (var combo = ImRaii.Combo("##world", WorldName(alert.World)))
            {
                if (combo.Success)
                {
                    if (ImGui.Selectable(Language.Options_Alerts_AnyWorld, alert.World == 0))
                        alert.World = 0;

                    ImGui.Separator();

                    foreach (var world in Worlds)
                        if (ImGui.Selectable(world.Name.ToString(), alert.World == world.RowId))
                            alert.World = world.RowId;
                }
            }

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            using (var combo = ImRaii.Combo("##sound", SoundName(alert.Sound)))
            {
                if (combo.Success)
                {
                    for (var sound = 0u; sound <= PlayerAlert.MaxSound; sound++)
                    {
                        if (!ImGui.Selectable(SoundName(sound), alert.Sound == sound))
                            continue;

                        alert.Sound = sound;
                        PlaySound(sound);
                    }
                }
            }

            ImGui.TableNextColumn();
            using (ImRaii.Disabled(alert.Sound == 0))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, "preview", Language.Options_Alerts_Preview_Tooltip))
                    PlaySound(alert.Sound);
            }

            ImGui.SameLine();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "remove", Language.Options_Alerts_Remove_Tooltip))
                toRemove = idx;
        }

        if (toRemove > -1)
            Mutable.PlayerAlerts.RemoveAt(toRemove);
    }

    private static string WorldName(uint world)
    {
        if (world == 0)
            return Language.Options_Alerts_AnyWorld;

        return Sheets.WorldSheet.TryGetRow(world, out var worldRow) ? worldRow.Name.ToString() : world.ToString();
    }

    private static string SoundName(uint sound)
    {
        return sound == 0 ? Language.Options_Alerts_NoSound : string.Format(Language.Options_Alerts_SoundEffect, sound);
    }

    private static unsafe void PlaySound(uint sound)
    {
        if (sound is > 0 and <= PlayerAlert.MaxSound)
            UIGlobals.PlayChatSoundEffect(sound);
    }
}
