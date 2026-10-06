using System.Numerics;
using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace ChatThree.Ui.Handler;

public class AutoCompleteHandler
{
    private const string AutoCompleteId = "##chat3-autocomplete";

    public InputHandler InputHandler;

    public AutoCompleteInfo? AutoCompleteInfo;
    public bool AutoCompleteOpen;
    public int AutoCompleteSelection;
    private List<AutoTranslateEntry>? AutoCompleteList;
    private bool FixCursor;
    private bool AutoCompleteShouldScroll;

    public AutoCompleteHandler(InputHandler inputHandler)
    {
        InputHandler = inputHandler;
    }

    public void DrawAutoComplete()
    {
        if (AutoCompleteInfo == null)
            return;

        AutoCompleteList ??= AutoTranslate.Matching(AutoCompleteInfo.ToComplete, Plugin.Config.SortAutoTranslate);
        if (AutoCompleteOpen)
        {
            ImGui.OpenPopup(AutoCompleteId);
            AutoCompleteOpen = false;
        }

        ImGui.SetNextWindowSize(new Vector2(400, 300) * ImGuiHelpers.GlobalScale);
        using var popup = ImRaii.Popup(AutoCompleteId);
        if (!popup.Success)
        {
            if (InputHandler.ActivatePos == -1)
                InputHandler.ActivatePos = AutoCompleteInfo.EndPos;

            AutoCompleteInfo = null;
            AutoCompleteList = null;
            InputHandler.Activate = true;
            return;
        }

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##auto-complete-filter", Language.AutoTranslate_Search_Hint, ref AutoCompleteInfo.ToComplete, 256, ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackHistory, AutoCompleteCallback))
        {
            AutoCompleteList = AutoTranslate.Matching(AutoCompleteInfo.ToComplete, Plugin.Config.SortAutoTranslate);
            AutoCompleteSelection = 0;
            AutoCompleteShouldScroll = true;
        }

        var selected = -1;
        if (ImGui.IsItemActive() && ImGui.GetIO().KeyCtrl)
        {
            for (var i = 0; i < 10 && i < AutoCompleteList.Count; i++)
            {
                var num = (i + 1) % 10;
                var key = ImGuiKey.Key0 + num;
                var key2 = ImGuiKey.Keypad0 + num;
                if (ImGui.IsKeyDown(key) || ImGui.IsKeyDown(key2))
                    selected = i;
            }
        }

        if (ImGui.IsItemDeactivated())
        {
            if (ImGui.IsKeyDown(ImGuiKey.Escape))
            {
                ImGui.CloseCurrentPopup();
                return;
            }

            var enter = ImGui.IsKeyDown(ImGuiKey.Enter) || ImGui.IsKeyDown(ImGuiKey.KeypadEnter);
            if (AutoCompleteList.Count > 0 && enter)
                selected = AutoCompleteSelection;
        }

        if (ImGui.IsWindowAppearing())
        {
            FixCursor = true;
            ImGui.SetKeyboardFocusHere(-1);
        }

        using var child = ImRaii.Child("##auto-complete-list", Vector2.Zero, false, ImGuiWindowFlags.HorizontalScrollbar);
        if (!child.Success)
            return;

        using var clipper = new ListClipper(AutoCompleteList.Count);
        foreach (var i in clipper.Rows)
        {
            var entry = AutoCompleteList[i];

            var highlight = AutoCompleteSelection == i;
            var clicked = ImGui.Selectable($"{entry.Text}##{entry.Group}/{entry.Row}", highlight) || selected == i;
            if (i < 10)
            {
                var button = (i + 1) % 10;
                var text = string.Format(Language.AutoTranslate_Completion_Key, button);
                var size = ImGui.CalcTextSize(text);

                ImGui.SameLine(ImGui.GetContentRegionAvail().X - size.X);

                using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
                    ImGui.TextUnformatted(text);
            }

            if (!clicked)
                continue;

            var before = InputHandler.ChatInput[..AutoCompleteInfo.StartPos];
            var after = InputHandler.ChatInput[AutoCompleteInfo.EndPos..];
            var replacement = entry.PlainText ? entry.Text : $"<at:{entry.Group},{entry.Row}>";
            InputHandler.ChatInput = $"{before}{replacement}{after}";
            ImGui.CloseCurrentPopup();
            InputHandler.Activate = true;
            InputHandler.ActivatePos = AutoCompleteInfo.StartPos + replacement.Length;
        }

        if (!AutoCompleteShouldScroll)
            return;

        AutoCompleteShouldScroll = false;
        var selectedPos = clipper.StartPosY + clipper.ItemsHeight * (AutoCompleteSelection * 1f);
        ImGui.SetScrollFromPosY(selectedPos - ImGui.GetWindowPos().Y);
    }

    private int AutoCompleteCallback(scoped ref ImGuiInputTextCallbackData data)
    {
        if (FixCursor && AutoCompleteInfo != null)
        {
            FixCursor = false;
            data.CursorPos = AutoCompleteInfo.ToComplete.Length;
            data.SelectionStart = data.SelectionEnd = data.CursorPos;
        }

        if (AutoCompleteList == null)
            return 0;

        switch (data.EventKey)
        {
            case ImGuiKey.UpArrow:
                if (AutoCompleteSelection == 0)
                    AutoCompleteSelection = AutoCompleteList.Count - 1;
                else
                    AutoCompleteSelection--;

                AutoCompleteShouldScroll = true;
                return 1;
            case ImGuiKey.DownArrow:
                if (AutoCompleteSelection == AutoCompleteList.Count - 1)
                    AutoCompleteSelection = 0;
                else
                    AutoCompleteSelection++;

                AutoCompleteShouldScroll = true;
                return 1;
            default:
                if (ImGui.IsKeyPressed(ImGuiKey.Tab))
                {
                    if (AutoCompleteSelection == AutoCompleteList.Count - 1)
                        AutoCompleteSelection = 0;
                    else
                        AutoCompleteSelection++;

                    AutoCompleteShouldScroll = true;
                    return 1;
                }

                break;
        }

        return 0;
    }
}