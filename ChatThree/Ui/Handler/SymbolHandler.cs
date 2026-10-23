using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace ChatThree.Ui.Handler;

public sealed record SymbolEntry(string Text, string Name, string? BuiltInShortcut);

public sealed class SymbolHandler
{
    private const string PopupId = "chat3-symbol-picker";
    private const int MaxRecent = 20;
    private const int Columns = 10;
    private const string OtherSymbolChars = "★☆♥♡♪♫✓√←→↑↓《》※☀☁☂℃℉°♀♂♠♣♦♤♧♢®©™€£¥♯♭◎◆◇■□〇●○△▽▼▲‹›≤≥«»“”─＼～∞";

    private static readonly Regex ShortcutPattern = new(@":([^\s:]{1,32}):$", RegexOptions.Compiled);
    private static readonly Dictionary<char, bool> SendableCache = [];
    private static List<SymbolEntry>? GameSymbols;
    private static List<SymbolEntry>? OtherSymbols;
    private static Dictionary<string, string>? BuiltInShortcuts;

    private InputHandler InputHandler { get; }
    private string Search = string.Empty;
    private bool WasOpen;
    private bool InsertedWhileOpen;

    public SymbolHandler(InputHandler inputHandler)
    {
        InputHandler = inputHandler;
    }

    public static float ButtonWidth => ImGuiUtil.CalcIconButtonSize().X + ImGui.GetStyle().ItemSpacing.X;

    public void DrawButton(bool enabled)
    {
        using (ImRaii.Disabled(!enabled))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Star, "symbol-picker", Language.Symbols_ButtonTooltip))
            {
                Search = string.Empty;
                InsertedWhileOpen = false;
                ImGui.OpenPopup(PopupId);
            }
        }

        DrawPopup();
    }

    // Called from the input callback right after a ':' was typed
    public unsafe void ExpandShortcut(ref ImGuiInputTextCallbackData data)
    {
        if (!Plugin.Config.SymbolShortcuts)
            return;

        var before = Marshal.PtrToStringUTF8((nint) data.Buf, data.CursorPos);
        var match = ShortcutPattern.Match(before);
        if (!match.Success)
            return;

        var replacement = FindShortcut(InputHandler.Plugin, match.Groups[1].Value);
        if (replacement == null || !IsSendable(InputHandler.Plugin, replacement))
            return;

        var matchBytes = Encoding.UTF8.GetByteCount(match.Value);
        var replacementBytes = Encoding.UTF8.GetByteCount(replacement);
        if (data.BufTextLen - matchBytes + replacementBytes >= data.BufSize)
            return;

        var start = data.CursorPos - matchBytes;
        data.DeleteChars(start, matchBytes);
        data.InsertChars(start, replacement);
    }

    public static string? FindShortcut(Plugin plugin, string name)
    {
        name = name.Trim();
        var custom = Plugin.Config.CustomSymbolShortcuts?
            .FirstOrDefault(s => !string.IsNullOrEmpty(s.Text) && s.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        if (custom != null)
            return custom.Text;

        EnsureCatalog(plugin);
        return BuiltInShortcuts!.GetValueOrDefault(name.ToLowerInvariant());
    }

    public static bool IsSendable(Plugin plugin, string text)
    {
        foreach (var c in text)
        {
            if (!SendableCache.TryGetValue(c, out var valid))
                SendableCache[c] = valid = plugin.Functions.Chat.IsCharValid(c);

            if (!valid)
                return false;
        }

        return text.Length > 0;
    }

    public static IReadOnlyList<SymbolEntry> AllGameSymbols(Plugin plugin)
    {
        EnsureCatalog(plugin);
        return GameSymbols!;
    }

    private static void EnsureCatalog(Plugin plugin)
    {
        if (GameSymbols != null)
            return;

        GameSymbols = [];
        BuiltInShortcuts = [];
        foreach (var icon in Enum.GetValues<SeIconChar>())
        {
            var text = icon.ToIconString();
            if (!IsSendable(plugin, text))
                continue;

            var shortcut = icon.ToString().ToLowerInvariant();
            BuiltInShortcuts.TryAdd(shortcut, text);
            GameSymbols.Add(new SymbolEntry(text, SplitWords(icon.ToString()), shortcut));
        }

        OtherSymbols = OtherSymbolChars
            .Select(c => c.ToString())
            .Distinct()
            .Where(s => IsSendable(plugin, s))
            .Select(s => new SymbolEntry(s, string.Empty, null))
            .ToList();
    }

    private void DrawPopup()
    {
        using var popup = ImRaii.Popup(PopupId);
        if (!popup)
        {
            // Give the cursor back to the input once the picker closes
            if (WasOpen && InsertedWhileOpen)
            {
                InputHandler.Activate = true;
                InputHandler.ActivatePos = InputHandler.CursorPos;
            }

            WasOpen = false;
            return;
        }

        WasOpen = true;
        EnsureCatalog(InputHandler.Plugin);

        var cell = new Vector2(ImGui.GetFrameHeight() * 1.25f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var gridWidth = Columns * cell.X + (Columns - 1) * spacing;

        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(gridWidth);
        ImGui.InputTextWithHint("##symbol-search", Language.Symbols_SearchHint, ref Search, 64);

        using var child = ImRaii.Child("##symbol-grid", new Vector2(gridWidth + ImGui.GetStyle().ScrollbarSize, cell.Y * 8));
        if (!child)
            return;

        var search = Search.Trim();
        if (search.Length == 0 && Plugin.Config.RecentSymbols.Count > 0)
        {
            var recent = Plugin.Config.RecentSymbols
                .Select(text => GameSymbols!.FirstOrDefault(e => e.Text == text) ?? new SymbolEntry(text, string.Empty, null))
                .ToList();
            DrawSection(Language.Symbols_Recent, recent, cell, "recent");
        }

        DrawSection(Language.Symbols_Game, Filter(GameSymbols!, search), cell, "game");
        DrawSection(Language.Symbols_Other, Filter(OtherSymbols!, search), cell, "other");
    }

    private static List<SymbolEntry> Filter(List<SymbolEntry> entries, string search)
    {
        if (search.Length == 0)
            return entries;

        return entries.Where(e =>
            e.Text.Contains(search, StringComparison.Ordinal)
            || e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (e.BuiltInShortcut?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || CustomNamesFor(e.Text).Any(n => n.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private void DrawSection(string title, List<SymbolEntry> entries, Vector2 cell, string id)
    {
        if (entries.Count == 0)
            return;

        ImGui.TextDisabled(title);
        using var pushId = ImRaii.PushId(id);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (i % Columns != 0)
                ImGui.SameLine();

            if (ImGui.Button($"{entry.Text}##{i}", cell))
                Insert(entry.Text);

            if (ImGui.IsItemHovered())
                DrawTooltip(entry);
        }

        ImGui.Spacing();
    }

    private static void DrawTooltip(SymbolEntry entry)
    {
        using var tooltip = ImRaii.Tooltip();
        if (entry.Name.Length > 0)
            ImGui.TextUnformatted(entry.Name);

        var shortcuts = CustomNamesFor(entry.Text).ToList();
        if (entry.BuiltInShortcut != null)
            shortcuts.Add(entry.BuiltInShortcut);

        if (Plugin.Config.SymbolShortcuts && shortcuts.Count > 0)
            ImGui.TextDisabled(string.Join("  ", shortcuts.Distinct().Select(s => $":{s}:")));
        else if (entry.Name.Length == 0)
            ImGui.TextUnformatted(entry.Text);
    }

    private static IEnumerable<string> CustomNamesFor(string text)
        => Plugin.Config.CustomSymbolShortcuts?
               .Where(s => s.Text == text && !string.IsNullOrWhiteSpace(s.Name))
               .Select(s => s.Name.Trim())
           ?? [];

    private void Insert(string text)
    {
        var input = InputHandler.ChatInput;
        var bytes = Encoding.UTF8.GetBytes(input);
        var bytePos = Math.Clamp(InputHandler.CursorPos, 0, bytes.Length);
        var charPos = Encoding.UTF8.GetCharCount(bytes, 0, bytePos);

        var updated = input.Insert(charPos, text);
        if (Encoding.UTF8.GetByteCount(updated) >= InputHandler.MaxInputBytes)
            return;

        InputHandler.ChatInput = updated;
        InputHandler.CursorPos = bytePos + Encoding.UTF8.GetByteCount(text);
        InsertedWhileOpen = true;

        var recent = Plugin.Config.RecentSymbols;
        recent.Remove(text);
        recent.Insert(0, text);
        if (recent.Count > MaxRecent)
            recent.RemoveRange(MaxRecent, recent.Count - MaxRecent);
        InputHandler.Plugin.SaveConfig();
    }

    private static string SplitWords(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var boundary = i > 0 && (char.IsUpper(c) && !char.IsUpper(name[i - 1]) || char.IsDigit(c) && !char.IsDigit(name[i - 1]));
            if (boundary)
                builder.Append(' ');
            builder.Append(c);
        }

        return builder.ToString();
    }
}
