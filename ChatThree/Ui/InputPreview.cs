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
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace ChatThree.Ui;

public partial class InputPreview : Window
{
    private readonly InputHandler InputHandler;

    private bool Drawing;
    private bool HasEvaluation;
    public float PreviewHeight;

    private int LastLength;
    private Message? PreviewMessage;

    private int CursorPosition;
    private bool NextChunkIsAutoTranslate;

    public int SelectedCursorPos = -1;

    public InputPreview(InputHandler inputHandler) : base("##chat3-inputpreview")
    {
        Flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoScrollbar;

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

    private bool ValidDraw => !string.IsNullOrEmpty(InputHandler.ChatInput) && InputHandler.ChatInput.Length >= Plugin.Config.PreviewMinimum;
    private void UpdateConditionCheck(IFramework framework)
    {
        Drawing = ValidDraw;
        if (!Drawing)
        {
            LastLength = 0;
            PreviewHeight = 0;
            PreviewMessage = null;
            HasEvaluation = false;

            return;
        }

        if (PreviewMessage == null || LastLength != InputHandler.ChatInput.Length)
        {
            LastLength = InputHandler.ChatInput.Length;

            var bytes = Encoding.UTF8.GetBytes(InputHandler.ChatInput.Trim());
            AutoTranslate.ReplaceWithPayload(ref bytes);

            var chunks = ChunkUtil.ToChunks(SeString.Parse(bytes), ChunkSource.Content, ChatType.Say).ToList();
            PreviewMessage = Message.FakeMessage(chunks, new ChatCode(XivChatType.Say, 0, 0));
            PreviewMessage.DecodeTextParam();
        }

        HasEvaluation = !Plugin.Config.OnlyPreviewIf || PreviewMessage.Content.Count > 1;
    }

    public bool IsDrawable => ValidDraw && HasEvaluation;

    private static bool IsWindowMode => Plugin.Config.PreviewPosition is PreviewPosition.Top or PreviewPosition.Bottom;
    public override bool DrawConditions()
    {
        return IsWindowMode && IsDrawable;
    }

    public override void PreDraw()
    {
        var pos = InputHandler.MainWindow.LastWindowPos;
        var size = InputHandler.MainWindow.LastWindowSize;

        Size = size with { Y = PreviewHeight };

        var y = Plugin.Config.PreviewPosition switch
        {
            PreviewPosition.Top => pos.Y - PreviewHeight,
            PreviewPosition.Bottom => pos.Y + size.Y,
            _ => throw new ArgumentOutOfRangeException(nameof(Plugin.Config.PreviewPosition), Plugin.Config.PreviewPosition, null),
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
    }

    private void DrawChunksPreview(IReadOnlyList<Chunk> chunks, PayloadHandler? handler = null, float lineWidth = 0f, int unique = 0)
    {
        CursorPosition = 0;

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

        foreach (var word in WhitespaceRegex().Split(text.Content).Where(s => s != string.Empty))
        {
            var wordSize = ImGui.CalcTextSize(word);
            if (ImGui.GetContentRegionAvail().X < wordSize.X)
                ImGui.NewLine();

            foreach (var letter in word)
            {
                var label = LetterString(letter);
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
                ImGui.SameLine();
            }
        }
        ImGui.NewLine();
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
