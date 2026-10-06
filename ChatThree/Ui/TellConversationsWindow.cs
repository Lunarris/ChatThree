using System.Numerics;
using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace ChatThree.Ui;

public class TellConversationsWindow : Window
{
    private const string Command = "/chat3tells";

    private readonly Plugin Plugin;

    private string SearchTerm = "";
    private volatile bool IsLoading;

    // Swapped as a whole by the loading task, read every frame.
    private volatile List<TellConversation> Conversations = [];
    private List<TellConversation> Filtered = [];
    private List<TellConversation>? FilteredFrom;
    private string FilteredTerm = "";

    public TellConversationsWindow(Plugin plugin) : base($"{Language.TellConversations_Title}###chat3-tellconversations")
    {
        Plugin = plugin;

        Size = new Vector2(400, 450);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        Plugin.Commands.Register(Command, Language.TellConversations_Command).Execute += Toggle;
    }

    public void Dispose()
    {
        Plugin.Commands.Register(Command).Execute -= Toggle;
    }

    private void Toggle(string _, string __) => Toggle();

    public override void OnOpen()
    {
        Reload();
    }

    private void Reload()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        Task.Run(() =>
        {
            try
            {
                Conversations = Plugin.TellWindows.GetConversations();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Error loading tell conversations");
            }
            finally
            {
                IsLoading = false;
            }
        });
    }

    public override void Draw()
    {
        var spacing = 3.0f * ImGuiHelpers.GlobalScale;

        ImGuiUtil.HelpText(Language.TellConversations_WindowHelpText);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGuiUtil.CalcIconButtonSize().X - spacing);
        ImGui.InputTextWithHint("##search", Language.TellConversations_SearchHint, ref SearchTerm, 64);

        ImGui.SameLine(0, spacing);

        using (ImRaii.Disabled(IsLoading))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Sync, tooltip: Language.TellConversations_Refresh))
                Reload();
        }

        ImGuiHelpers.ScaledDummy(5.0f);

        // Only refilter when the list or the search term changed.
        var conversations = Conversations;
        if (!ReferenceEquals(FilteredFrom, conversations) || FilteredTerm != SearchTerm)
        {
            FilteredFrom = conversations;
            FilteredTerm = SearchTerm;
            Filtered = SearchTerm.Length == 0
                ? conversations
                : conversations.Where(c => c.Display.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (Filtered.Count == 0)
        {
            ImGui.TextUnformatted(IsLoading ? Language.DbViewer_LoadingIndicator : conversations.Count == 0 ? Language.TellConversations_Empty : Language.TellConversations_NoMatch);
            return;
        }

        using var child = ImRaii.Child("##conversations");
        if (!child.Success)
            return;

        using var table = ImRaii.Table("##conversationTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn(Language.TellConversations_Player);
        ImGui.TableSetupColumn(Language.TellConversations_LastTell, ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Language.TellConversations_Count, ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        for (var i = 0; i < Filtered.Count; i++)
        {
            var conversation = Filtered[i];
            using var id = ImRaii.PushId(i);

            ImGui.TableNextColumn();
            if (ImGui.Selectable(conversation.Display, false, ImGuiSelectableFlags.SpanAllColumns))
                Open(conversation);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(conversation.LastTell);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(conversation.CountText);
        }
    }

    private void Open(TellConversation conversation)
    {
        var tab = Plugin.TellWindows.Open(conversation.Name, conversation.World, fullHistory: true);

        // Bring the window forward if it was already open.
        if (tab != null)
            ImGui.SetWindowFocus($"{tab.Name}##popout");
    }
}
