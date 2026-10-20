using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ChatThree.Code;
using ChatThree.Resources;
using ChatThree.Ui.Handler;
using ChatThree.Util;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace ChatThree.Ui;

public partial class InputPreview : Window
{
    private readonly InputHandler MainInputHandler;
    public InputHandler InputHandler { get; private set; }

    private bool Drawing;
    private bool HasEvaluation;
    public float PreviewHeight;

    private string LastInput = string.Empty;
    private Message? PreviewMessage;

    private int CursorPosition;
    private bool NextChunkIsAutoTranslate;

    public int SelectedCursorPos = -1;

    private const string SpellPopupId = "##chat3-spellcheck";
    private readonly SpellChecker SpellChecker = new();
    private int SkipWords;
    private int WordIndex;
    private SpellTarget? SpellPopupTarget;
    private Vector2 SpellPopupPos;
    private Vector2 SpellPopupPivot;
    private Task<List<string>>? SpellSuggestions;

    private record SpellTarget(string Word, int Start);

    // Spell check lives in the preview, so it forces the preview on without changing the saved settings
    public static PreviewPosition EffectivePosition => Plugin.Config.SpellCheck && Plugin.Config.PreviewPosition is PreviewPosition.None or PreviewPosition.Tooltip
        ? PreviewPosition.Inside
        : Plugin.Config.PreviewPosition;
    private static int EffectiveMinimum => Plugin.Config.SpellCheck ? 1 : Plugin.Config.PreviewMinimum;
    private static bool EffectiveOnlyPreviewIf => !Plugin.Config.SpellCheck && Plugin.Config.OnlyPreviewIf;

    public InputPreview(InputHandler inputHandler) : base("##chat3-inputpreview")
    {
        Flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoScrollbar;

        MainInputHandler = inputHandler;
        InputHandler = inputHandler;

        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = true;

        Plugin.Framework.Update += UpdateConditionCheck;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= UpdateConditionCheck;
    }

    public void SetSource(InputHandler inputHandler)
    {
        if (InputHandler == inputHandler)
            return;

        InputHandler = inputHandler;
        LastInput = string.Empty;
        PreviewHeight = 0;
        PreviewMessage = null;
        HasEvaluation = false;
        SelectedCursorPos = -1;
    }

    public void ReleaseSource(InputHandler inputHandler)
    {
        if (InputHandler == inputHandler)
            SetSource(MainInputHandler);
    }

    private bool ValidDraw => !string.IsNullOrEmpty(InputHandler.ChatInput) && InputHandler.ChatInput.Length >= EffectiveMinimum;
    private void UpdateConditionCheck(IFramework framework)
    {
        if (Plugin.Config.SpellCheck)
            SpellChecker.EnsureLoaded();

        Drawing = ValidDraw;
        if (!Drawing)
        {
            LastInput = string.Empty;
            PreviewHeight = 0;
            PreviewMessage = null;
            HasEvaluation = false;

            return;
        }

        if (PreviewMessage == null || LastInput != InputHandler.ChatInput)
        {
            LastInput = InputHandler.ChatInput;

            // Commands and tell targets aren't words, so leave them out of the spell check
            var trimmed = InputHandler.ChatInput.Trim();
            var command = trimmed.StartsWith('/') ? trimmed.Split(' ')[0] : null;
            SkipWords = command is null ? 0 : command is "/t" or "/tell" ? 2 : 1;

            if (Plugin.Config.SpellCheck)
                SpellChecker.SetNames(PlayerNames());

            var bytes = Encoding.UTF8.GetBytes(trimmed);
            AutoTranslate.ReplaceWithPayload(ref bytes);

            var chunks = ChunkUtil.ToChunks(SeString.Parse(bytes), ChunkSource.Content, ChatType.Say).ToList();
            PreviewMessage = Message.FakeMessage(chunks, new ChatCode(XivChatType.Say, 0, 0));
            PreviewMessage.DecodeTextParam();
        }

        HasEvaluation = !EffectiveOnlyPreviewIf || PreviewMessage.Content.Count > 1;
    }

    public bool IsDrawable => ValidDraw && HasEvaluation;

    public bool IsDrawableFor(InputHandler inputHandler) => InputHandler == inputHandler && IsDrawable;

    private static bool IsWindowMode => EffectivePosition is PreviewPosition.Top or PreviewPosition.Bottom;
    public override bool DrawConditions()
    {
        return IsWindowMode && IsDrawable;
    }

    public override void PreDraw()
    {
        var pos = InputHandler.MainWindow.LastWindowPos;
        var size = InputHandler.MainWindow.LastWindowSize;

        Size = size with { Y = PreviewHeight };

        var y = EffectivePosition switch
        {
            PreviewPosition.Top => pos.Y - PreviewHeight,
            PreviewPosition.Bottom => pos.Y + size.Y,
            _ => throw new ArgumentOutOfRangeException(nameof(EffectivePosition), EffectivePosition, null),
        };

        Position = pos with { Y = y };
        PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        CalculatePreview();
        DrawPreview();
    }

    public void CalculatePreview()
    {
        // We Pre-draw this once to get the actual height :HideThePain:
        PreviewHeight = 0;

        var pos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(-500, -500));
        var before = ImGui.GetCursorPosY();
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            ImGui.TextUnformatted(Language.Options_Preview_Header);
            DrawChunksPreview(PreviewMessage!.Content);
        }
        var after = ImGui.GetCursorPosY();
        ImGui.SetCursorPos(pos);

        PreviewHeight = after - before;
        PreviewHeight += IsWindowMode ? ImGui.GetStyle().WindowPadding.Y * 2 : 0;
    }

    public void DrawPreview()
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            ImGui.TextUnformatted(Language.Options_Preview_Header);

            DrawChunksPreview(PreviewMessage!.Content, InputHandler.PayloadHandler, unique: 10000);
        }

        DrawSpellPopup();
    }

    private void DrawChunksPreview(IReadOnlyList<Chunk> chunks, PayloadHandler? handler = null, float lineWidth = 0f, int unique = 0)
    {
        CursorPosition = 0;
        WordIndex = 0;

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        for (var i = 0; i < chunks.Count; i++)
        {
            if (chunks[i] is TextChunk text && string.IsNullOrEmpty(text.Content))
                continue;

            DrawChunkPreview(chunks[i], handler, lineWidth, unique);

            if (i < chunks.Count - 1)
            {
                ImGui.SameLine();
            }
        }
    }

    private void DrawChunkPreview(Chunk chunk, PayloadHandler? handler = null, float lineWidth = 0f, int unique = 0)
    {
        if (chunk is IconChunk icon)
        {
            InputHandler.ChunkHandler.DrawIcon(chunk, icon, handler);
            if (icon.Icon != BitmapFontIcon.AutoTranslateBegin)
                return;

            NextChunkIsAutoTranslate = true;
            var payload = (AutoTranslatePayload) chunk.Link!;
            CursorPosition += $"<at:{payload.Group},{payload.Key}>".Length;

            return;
        }

        if (chunk is not TextChunk text)
            return;

        if (NextChunkIsAutoTranslate)
        {
            NextChunkIsAutoTranslate = false;
            ImGuiUtil.WrapText(text.Content, chunk, handler, InputHandler.Plugin.DefaultText, lineWidth);
            return;
        }

        if (text.Link != null)
        {
            if (text.Link is ItemPayload)
                CursorPosition += "<item>".Length;
            else if (text.Link is MapLinkPayload)
                CursorPosition += "<flag>".Length;
            else if (text.Link is UriPayload)
                CursorPosition += text.Content.Length;

            ImGuiUtil.WrapText(text.Content, chunk, handler, InputHandler.Plugin.DefaultText, lineWidth);
            return;
        }

        // The measuring pass has no handler and shouldn't check or draw anything extra
        var spellCheck = handler != null && Plugin.Config.SpellCheck && SpellChecker.IsReady;
        foreach (var word in WhitespaceRegex().Split(text.Content).Where(s => s != string.Empty))
        {
            var wordSize = ImGui.CalcTextSize(word);
            if (ImGui.GetContentRegionAvail().X < wordSize.X)
                ImGui.NewLine();

            var misspelledStart = -1;
            var misspelledLength = 0;
            if (!char.IsWhiteSpace(word[0]) && WordIndex++ >= SkipWords && spellCheck
                && SpellChecker.TryGetWord(word, out var start, out var length) && SpellChecker.IsMisspelled(word.Substring(start, length)))
            {
                misspelledStart = start;
                misspelledLength = length;
            }

            var wordStart = CursorPosition;
            for (var i = 0; i < word.Length; i++)
            {
                var label = LetterString(word[i]);
                var letterSize = ImGui.CalcTextSize(label);

                CursorPosition++;
                using (ImRaii.PushId(CursorPosition + unique))
                {
                    if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, letterSize))
                    {
                        SelectedCursorPos = CursorPosition;
                        InputHandler.FocusedPreview = true;
                    }
                }

                if (i >= misspelledStart && i < misspelledStart + misspelledLength)
                {
                    DrawSquiggle(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().X, ImGui.GetItemRectMax().Y - ImGuiHelpers.GlobalScale);
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        OpenSpellPopup(word.Substring(misspelledStart, misspelledLength), wordStart + misspelledStart);
                }

                ImGui.SameLine();
            }
        }
        ImGui.NewLine();
    }

    // Party members, tell partners and whoever the current tab is sending tells to
    private IEnumerable<string> PlayerNames()
    {
        foreach (var member in Plugin.PartyList)
            yield return member.Name.TextValue;

        foreach (var name in InputHandler.Plugin.TellWindows.PartnerNames())
            yield return name;

        var tab = InputHandler.Plugin.CurrentTab;
        if (tab.TellTarget.IsSet())
            yield return tab.TellTarget.Name;
        if (tab.CurrentChannel.TellTarget?.IsSet() == true)
            yield return tab.CurrentChannel.TellTarget.Name;
        if (tab.CurrentChannel.TempTellTarget?.IsSet() == true)
            yield return tab.CurrentChannel.TempTellTarget.Name;
    }

    // A zigzag lined up to a screen grid, so the per letter pieces join up into one line
    private static void DrawSquiggle(float startX, float endX, float y)
    {
        var step = 2f * ImGuiHelpers.GlobalScale;
        var height = ImGuiHelpers.GlobalScale;
        var colour = ImGui.GetColorU32(ImGuiColors.DalamudRed);
        var drawList = ImGui.GetWindowDrawList();

        for (var i = (int) MathF.Floor(startX / step); i * step < endX; i++)
        {
            var up = i % 2 == 0;
            var from = new Vector2(MathF.Max(i * step, startX), y + (up ? height : -height));
            var to = new Vector2(MathF.Min((i + 1) * step, endX), y + (up ? -height : height));
            drawList.AddLine(from, to, colour, ImGuiHelpers.GlobalScale);
        }
    }

    private void OpenSpellPopup(string word, int start)
    {
        // CursorPosition counts from the trimmed input, so add back the leading whitespace
        var leading = InputHandler.ChatInput.Length - InputHandler.ChatInput.TrimStart().Length;
        SpellPopupTarget = new SpellTarget(word, start + leading);
        SpellSuggestions = SpellChecker.SuggestAsync(word);

        var viewport = ImGui.GetWindowViewport();
        SpellPopupPos = ImGui.GetMousePos();
        SpellPopupPivot = SpellPopupPos.Y > viewport.WorkPos.Y + viewport.WorkSize.Y / 2 ? new Vector2(0, 1) : Vector2.Zero;
        ImGui.OpenPopup(SpellPopupId);
    }

    private void DrawSpellPopup()
    {
        ImGui.SetNextWindowPos(SpellPopupPos, ImGuiCond.Always, SpellPopupPivot);
        using var popup = ImRaii.Popup(SpellPopupId);
        if (!popup || SpellPopupTarget is not { } target)
            return;

        var suggestions = SpellSuggestions;
        if (suggestions is not { IsCompleted: true })
            ImGui.TextDisabled(Language.SpellCheck_Searching);
        else if (!suggestions.IsCompletedSuccessfully || suggestions.Result.Count == 0)
            ImGui.TextDisabled(Language.SpellCheck_NoSuggestions);
        else
            foreach (var suggestion in suggestions.Result)
                if (ImGui.Selectable(suggestion))
                    ReplaceWord(target, suggestion);

        ImGui.Separator();
        if (ImGui.Selectable(string.Format(Language.SpellCheck_AddWord, target.Word)))
        {
            Plugin.Config.SpellCheckWords.Add(target.Word.ToLowerInvariant());
            InputHandler.Plugin.SaveConfig();
        }
    }

    private void ReplaceWord(SpellTarget target, string replacement)
    {
        // The preview can drift from the raw input when it evaluates a parameter, so check the spot before searching for the word
        var input = InputHandler.ChatInput;
        var start = target.Start;
        if (start < 0 || start + target.Word.Length > input.Length || string.CompareOrdinal(input, start, target.Word, 0, target.Word.Length) != 0)
            start = input.IndexOf(target.Word, StringComparison.Ordinal);

        if (start < 0)
            return;

        var updated = string.Concat(input.AsSpan(0, start), replacement, input.AsSpan(start + target.Word.Length));
        if (Encoding.UTF8.GetByteCount(updated) >= InputHandler.MaxInputBytes)
            return;

        InputHandler.ChatInput = updated;
        InputHandler.ActivatePos = start + replacement.Length;
        InputHandler.Activate = true;
    }

    private static readonly Dictionary<char, string> LetterStrings = [];

    private static string LetterString(char letter)
    {
        if (!LetterStrings.TryGetValue(letter, out var str))
            LetterStrings[letter] = str = letter.ToString();

        return str;
    }

    [GeneratedRegex(@"(\s)")]
    private static partial Regex WhitespaceRegex();
}
