using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using ChatThree.Code;
using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ImGuiNotification;

namespace ChatThree.Ui;

public class DbViewer : Window
{
    public const float RowPerPage = 1000.0f;

    private readonly Plugin Plugin;

    private static readonly DateTime MinimalDate = new(2021, 1, 1);

    private DateTime AfterDate;
    private DateTime BeforeDate;

    private int CurrentPage = 1;
    private string SimpleSearchTerm = "";
    private string SenderFilter = "";
    private bool OnlyCurrentCharacter = true;
    private readonly Dictionary<ChatType, (ChatSource, ChatSource)> SelectedChannels;

    private bool IsProcessing;
    private long ProcessingStart = Environment.TickCount64;
    private (DateTime Min, DateTime Max, int Page, bool Local, int ChannelCount) LastProcessed;

    private string MinDateString = "";
    private string MaxDateString = "";

    private readonly string DateFormat;
    private readonly string DateTimeFormat;

    private long Count;
    private Message[] Messages = [];  // Messages are only touched while processing is false
    private ConcurrentStack<Message> Filtered = [];  // Is used every frame, so ConcurrentStack for safety

    private bool NeedsScrollReset;

    private readonly FileDialogManager FileDialog = new();
    private bool IsExporting;

    public DbViewer(Plugin plugin) : base("DBViewer###chat3-dbviewer")
    {
        Plugin = plugin;
        SelectedChannels = TabsUtil.MostlyPlayer;

        DateFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
        DateTimeFormat = "ddd, dd MMM yyy HH:mm:ss";

        LastProcessed = (AfterDate, BeforeDate, CurrentPage, OnlyCurrentCharacter, SelectedChannels.Count);
        DateReset();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(475, 600),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        Plugin.Commands.Register("/chat3db", "Get access to your message history, with simple filter options.", true).Execute += Toggle;
    }

    public void Dispose()
    {
        Plugin.Commands.Register("/chat3db", "Get access to your message history, with simple filter options.", true).Execute -= Toggle;
    }

    private void Toggle(string _, string __) => Toggle();

    public override void Draw()
    {
        var totalPages = (int)Math.Ceiling(Count / RowPerPage);
        if (totalPages < 1)
            totalPages = 1;

        if (CurrentPage > totalPages)
            CurrentPage = 1;

        // First row

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudViolet, Language.DbViewer_DatePicker_FromTo);
        ImGui.SameLine();

        var spacing = 3.0f * ImGuiHelpers.GlobalScale;
        DateWidget.DatePickerWithInput("##FromDate", 1, ref MinDateString, ref AfterDate, DateFormat);
        DateWidget.DatePickerWithInput("##ToDate", 2, ref MaxDateString, ref BeforeDate, DateFormat, true);

        ImGui.SameLine(0, spacing);

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Recycle))
            DateReset();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Language.DbViewer_Date_Reset_Tooltip);

        ImGui.SameLine(0, spacing);

        ChannelSelection();

        ImGui.SameLine(0, spacing);

        using (ImRaii.Disabled(IsExporting))
        {
            if (ImGui.Button(IsExporting ? Language.DbViewer_Export_Running : Language.DbViewer_Export_Button))
                OpenExportDialog();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiUtil.Tooltip(Language.DbViewer_Export_Tooltip);

        FileDialog.Draw();

        var skipText = Language.DbViewer_CharacterOption;
        var textLength = ImGui.GetTextLineHeight() + ImGui.CalcTextSize(skipText).X + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SameLine(ImGui.GetContentRegionMax().X - textLength);
        ImGui.Checkbox(skipText, ref OnlyCurrentCharacter);

        // Second row

        var width = 350 * ImGuiHelpers.GlobalScale;
        var loadingIndicator = IsProcessing && ProcessingStart < Environment.TickCount64;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(string.Format(Language.DbViewer_Page, CurrentPage, totalPages, Count, loadingIndicator ? Language.DbViewer_LoadingIndicator : ""));
        ImGuiUtil.DrawArrows(ref CurrentPage, 1, totalPages, spacing, tooltipLeft: Language.Page_ArrowLeft_Tooltip, tooltipRight: Language.Page_ArrowRight_Tooltip);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - width);
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint("##searchbar", Language.DbViewer_SearcHint, ref SimpleSearchTerm, 30))
            Filtered = Filter(Messages);

        // Third row

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##senderbar", Language.DbViewer_SenderHint, ref SenderFilter, 256))
            Filtered = Filter(Messages);

        if (ImGui.IsItemHovered())
            ImGuiUtil.Tooltip(Language.DbViewer_Sender_Tooltip);

        // Fourth row

        if (DateWidget.Validate(MinimalDate, ref AfterDate, ref BeforeDate))
            DateRefresh();

        if (!IsProcessing && LastProcessed != (AfterDate, BeforeDate, CurrentPage, OnlyCurrentCharacter, SelectedChannels.Count))
        {

            if (LastProcessed.Page == CurrentPage)
                CurrentPage = 1;

            AdjustDates();
            IsProcessing = true;
            ProcessingStart = Environment.TickCount64 + 1_000;
            LastProcessed = (AfterDate, BeforeDate, CurrentPage, OnlyCurrentCharacter, SelectedChannels.Count);
            Task.Run(() =>
            {
                try
                {
                    ulong? character = OnlyCurrentCharacter ? Plugin.PlayerState.ContentId : null;
                    var channels = SelectedChannels.Select(pair => (byte) pair.Key).ToArray();

                    if (CurrentPage == 1)
                        Count = Plugin.MessageManager.Store.CountDateRange(AfterDate, BeforeDate, channels, character);

                    using var rangeMessageEnumerator = Plugin.MessageManager.Store.GetPagedDateRange(AfterDate, BeforeDate, channels, character, CurrentPage - 1);
                    Messages = rangeMessageEnumerator.ToArray();

                    Filtered = Filter(Messages);
                    NeedsScrollReset = true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "Failed reading messages from database");
                }
                finally
                {
                    IsProcessing = false;
                }
            });
        }

        ImGuiHelpers.ScaledDummy(5.0f);

        if (Filtered.IsEmpty)
        {
            ImGui.TextUnformatted(SimpleSearchTerm == "" && ParseSenders(SenderFilter).Length == 0 ? Language.DbViewer_Status_NothingFound : Language.DbViewer_Status_NoSearchResult);
            return;
        }

        using var child = ImRaii.Child("##tableChild");
        if (!child.Success)
            return;

        if (NeedsScrollReset)
        {
            NeedsScrollReset = false;
            ImGui.SetScrollY(0.0f);
        }

        using var table = ImRaii.Table("##messageHistory", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
        if (!table.Success)
            return;

        var columnWidth = ImGui.CalcTextSize(Language.DbViewer_TableField_Type);
        ImGui.TableSetupColumn(Language.DbViewer_TableField_Date, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn(Language.DbViewer_TableField_Type, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, columnWidth.X);
        ImGui.TableSetupColumn(Language.DbViewer_TableField_Sender);
        ImGui.TableSetupColumn(Language.DbViewer_TableField_Content);

        ImGui.TableHeadersRow();
        foreach (var message in Filtered)
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(message.Date.ToLocalTime().ToString(DateTimeFormat));

            ImGui.TableNextColumn();
            var pos = ImGui.GetCursorPos();
            ImGuiUtil.CenterText($"{(byte)message.Code.Type}");
            ImGui.SetCursorPos(pos);
            ImGui.Dummy(columnWidth);
            if (ImGui.IsItemHovered())
                ImGuiUtil.Tooltip(message.Code.Type.Name());

            ImGui.TableNextColumn();
            Plugin.ChatLog.InputHandler.ChunkHandler.DrawChunks(message.Sender);

            ImGui.TableNextColumn();
            Plugin.ChatLog.InputHandler.ChunkHandler.DrawChunks(message.Content);
        }
    }

    private void ChannelSelection()
    {
        const string addTabPopup = "add-channel-popup";
        var spacing = 3.0f * ImGuiHelpers.GlobalScale;

        if (ImGui.Button("Channels"))
            ImGui.OpenPopup(addTabPopup);

        using var popup = ImRaii.Popup(addTabPopup);
        if (!popup.Success)
            return;

        using var channelNode = ImRaii.TreeNode(Language.Options_Tabs_Channels);
        if (!channelNode.Success)
            return;

        foreach (var (header, types) in ChatTypeExt.SortOrder)
        {
            using var pushedId = ImRaii.PushId(header);

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Check))
            {
                foreach (var type in types)
                    SelectedChannels.TryAdd(type, (ChatSourceExt.All, ChatSourceExt.All));
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Select all");

            ImGui.SameLine(0, spacing);

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            {
                foreach (var type in types)
                    SelectedChannels.Remove(type);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Unselect all");

            ImGui.SameLine(0, spacing);

            using var headerNode = ImRaii.TreeNode(header);
            if (!headerNode.Success)
                continue;

            foreach (var type in types)
            {
                if (type.IsGm())
                    continue;

                var enabled = SelectedChannels.ContainsKey(type);
                if (ImGui.Checkbox($"##{type.Name()}", ref enabled))
                {
                    if (enabled)
                        SelectedChannels[type] = (ChatSourceExt.All, ChatSourceExt.All);
                    else
                        SelectedChannels.Remove(type);
                }

                ImGui.SameLine();
                ImGui.TextUnformatted(type.Name());
            }
        }
    }

    private void OpenExportDialog()
    {
        AdjustDates();

        // to-do test filter ranges for CSV export. Addtion of /tells pop up windows should allow filtering of tell between logged user and filtered player. 
        var after = AfterDate;
        var before = BeforeDate;
        var searchTerm = SimpleSearchTerm;
        var senders = ParseSenders(SenderFilter);
        var channels = SelectedChannels.Select(pair => (byte) pair.Key).ToArray();
        ulong? character = OnlyCurrentCharacter ? Plugin.PlayerState.ContentId : null;

        var fileName = $"chat3-{after:yyyy-MM-dd}_to_{before:yyyy-MM-dd}";
        FileDialog.SaveFileDialog(Language.DbViewer_Export_DialogTitle, ".csv", fileName, ".csv", (ok, path) =>
        {
            if (!ok || string.IsNullOrWhiteSpace(path) || IsExporting)
                return;

            IsExporting = true;
            Task.Run(() =>
            {
                try
                {
                    var rows = ExportCsv(path, after, before, channels, character, senders, searchTerm);
                    WrapperUtil.AddNotification(string.Format(Language.DbViewer_Export_Success, rows, path), NotificationType.Success);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "Failed exporting messages to CSV");
                    WrapperUtil.AddNotification(Language.DbViewer_Export_Error, NotificationType.Error);
                }
                finally
                {
                    IsExporting = false;
                }
            });
        });
    }

    private int ExportCsv(string path, DateTime after, DateTime before, byte[] channels, ulong? character, string[] senders, string searchTerm)
    {
        // Excel to read UTF-8 encoding
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.Write(string.Join(',', Language.DbViewer_TableField_Date, Language.DbViewer_TableField_Type, Language.DbViewer_TableField_Sender, Language.DbViewer_TableField_Content));
        writer.Write("\r\n");

        var rows = 0;
        using var messages = Plugin.MessageManager.Store.GetDateRange(after, before, channels, character);
        foreach (var message in messages)
        {
            var sender = ChunkUtil.ToRawString(message.Sender.Where(chunk => chunk.Source == ChunkSource.Sender).ToList());
            if (sender == "")
                sender = ChunkUtil.ToRawString(message.Sender).Trim();

            var content = ChunkUtil.ToRawString(message.Content);
            if (!MatchesFilters(sender, content, senders, searchTerm))
                continue;

            writer.Write(CsvField(message.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            writer.Write(',');
            writer.Write(CsvField(message.Code.Type.Name()));
            writer.Write(',');
            writer.Write(CsvField(sender));
            writer.Write(',');
            writer.Write(CsvField(content));
            writer.Write("\r\n");
            rows++;
        }

        return rows;
    }

    private static string CsvField(string value)
    {
        // test bug fix: prevent csv from seeing formulas, keep as plain text
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = $"'{value}";

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private ConcurrentStack<Message> Filter(Message[] messages)
    {
        var senders = ParseSenders(SenderFilter);
        if (SimpleSearchTerm == "" && senders.Length == 0)
            return new ConcurrentStack<Message>(messages.Reverse().OrderByDescending(m => m.Date));

        return new ConcurrentStack<Message>(
            messages.Reverse().Where(m =>
                MatchesFilters(ChunkUtil.ToRawString(m.Sender), ChunkUtil.ToRawString(m.Content), senders, SimpleSearchTerm)
                ).OrderByDescending(m => m.Date));
    }

    private static string[] ParseSenders(string filter)
    {
        return filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool MatchesFilters(string sender, string content, string[] senders, string searchTerm)
    {
        if (senders.Length > 0 && !senders.Any(name => sender.Contains(name, StringComparison.InvariantCultureIgnoreCase)))
            return false;

        return searchTerm == ""
               || sender.Contains(searchTerm, StringComparison.InvariantCultureIgnoreCase)
               || content.Contains(searchTerm, StringComparison.InvariantCultureIgnoreCase);
    }

    private void DateRefresh()
    {
        MinDateString = AfterDate.ToString(DateFormat);
        MaxDateString = BeforeDate.ToString(DateFormat);
    }

    private void AdjustDates()
    {
        AfterDate = new DateTime(AfterDate.Year, AfterDate.Month, AfterDate.Day, 0, 0, 0);
        BeforeDate = new DateTime(BeforeDate.Year, BeforeDate.Month, BeforeDate.Day, 23, 59, 59);
    }

    private void DateReset()
    {
        AfterDate = DateTime.Now.AddDays(-5);
        BeforeDate = DateTime.Now;

        AdjustDates();
        DateRefresh();
    }
}
