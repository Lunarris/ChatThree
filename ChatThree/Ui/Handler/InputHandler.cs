using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ChatThree.Code;
using ChatThree.GameFunctions;
using ChatThree.GameFunctions.Types;
using ChatThree.Resources;
using ChatThree.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ChatThree.Ui.Handler;

public class InputHandler
{
    public const uint ChatOpenSfx = 35u;
    public const uint ChatCloseSfx = 3u;

    public const int MaxInputBytes = 500;
    private const int CounterWarningBytes = 400;
    private const int CounterCriticalBytes = 480;

    private const ImGuiInputTextFlags InputFlags = ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackCharFilter |
                                                   ImGuiInputTextFlags.CallbackCompletion | ImGuiInputTextFlags.CallbackHistory;

    public readonly Plugin Plugin;
    public readonly IChatWindow MainWindow;

    public readonly SendHandler SendHandler;
    public readonly AutoCompleteHandler AutoCompleteHandler;
    public readonly PayloadHandler PayloadHandler;
    public readonly ChunkHandler ChunkHandler;

    public string ChatInput = string.Empty;

    public bool FocusedPreview;
    public bool Activate;
    public bool InputFocused;

    public bool PlayedClosingSound = true;

    public long FrameTime; // set every frame
    public long LastActivityTime = Environment.TickCount64;

    public int CursorPos;
    public int ActivatePos = -1;

    public readonly string InputHandlerId;

    public InputHandler(IChatWindow mainWindow, Plugin plugin, string id)
    {
        MainWindow = mainWindow;
        Plugin = plugin;
        InputHandlerId = id;

        SendHandler = new SendHandler(plugin);
        ChunkHandler = new ChunkHandler(plugin);
        PayloadHandler = new PayloadHandler(this);
        AutoCompleteHandler = new AutoCompleteHandler(this);
    }

    public void DrawInputArea(Tab activeTab, float inputWidth, ref bool tellSpecial)
    {
        var inputType = activeTab.CurrentChannel.UseTempChannel
            ? activeTab.CurrentChannel.TempChannel.ToChatType()
            : activeTab.CurrentChannel.Channel.ToChatType();

        var isCommand = ChatInput.Trim().StartsWith('/');
        if (isCommand)
        {
            var command = ChatInput.Split(' ')[0];
            if (Plugin.Commands.TextCommandChannels.TryGetValue(command, out var channel))
                inputType = channel;

            if (!IsValidCommand(command))
                inputType = ChatType.Error;
        }

        var normalColor = ImGui.GetColorU32(ImGuiCol.Text);
        var inputColour = Plugin.Config.ChatColours.TryGetValue(inputType, out var inputCol) ? inputCol : inputType.DefaultColor();

        if (!isCommand && Plugin.ExtraChat.ChannelOverride is var (_, overrideColour))
            inputColour = overrideColour;

        if (isCommand && Plugin.ExtraChat.ChannelCommandColours.TryGetValue(ChatInput.Split(' ')[0], out var ecColour))
            inputColour = ecColour;

        // Show a byte counter next to the input once the message gets close to the limit
        var inputBytes = Encoding.UTF8.GetByteCount(ChatInput);
        var showCounter = inputBytes >= CounterWarningBytes;
        if (showCounter)
            inputWidth -= ImGui.CalcTextSize($"{MaxInputBytes}/{MaxInputBytes}").X + ImGui.GetStyle().ItemSpacing.X;

        var push = inputColour != null;
        using (ImRaii.PushColor(ImGuiCol.Text, push ? ColourUtil.RgbaToAbgr(inputColour!.Value) : 0, push))
        {
            var isChatEnabled = activeTab is { InputDisabled: false };
            if (isChatEnabled && (Activate || FocusedPreview))
            {
                FocusedPreview = false;
                ImGui.SetKeyboardFocusHere();
            }

            var chatCopy = ChatInput;
            using (ImRaii.Disabled(!isChatEnabled))
            {
                var flags = InputFlags | (!isChatEnabled ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None);
                ImGui.SetNextItemWidth(inputWidth);
                ImGui.InputTextWithHint("##chat3-input", isChatEnabled ? "": Language.ChatLog_DisabledInput, ref ChatInput, MaxInputBytes, flags, Callback);
            }
            var inputActive = ImGui.IsItemActive();
            InputFocused = isChatEnabled && inputActive;

            var tooltipDraw = Plugin.Config.PreviewPosition is PreviewPosition.Tooltip && Plugin.InputPreview.IsDrawable;
            if (tooltipDraw && ImGui.IsItemHovered())
            {
                ImGui.SetNextWindowSize(new Vector2(500 * ImGuiHelpers.GlobalScale, -1));
                using var tooltip = ImRaii.Tooltip();
                Plugin.InputPreview.DrawPreview();
            }

            if (ImGui.IsItemDeactivated())
            {
                if (ImGui.IsKeyDown(ImGuiKey.Escape))
                {
                    ChatInput = chatCopy;

                    if (activeTab.CurrentChannel.UseTempChannel)
                    {
                        activeTab.CurrentChannel.ResetTempChannel();
                        Plugin.Functions.Chat.SetChannelWithExtraChat(activeTab.CurrentChannel.Channel);
                    }
                }

                if (ImGui.IsKeyDown(ImGuiKey.Enter) || ImGui.IsKeyDown(ImGuiKey.KeypadEnter))
                {
                    Plugin.CommandHelpWindow.IsOpen = false;
                    SendHandler.SendChatBox(activeTab, ref ChatInput, ref tellSpecial);

                    if (activeTab.CurrentChannel.UseTempChannel)
                    {
                        activeTab.CurrentChannel.ResetTempChannel();
                        Plugin.Functions.Chat.SetChannelWithExtraChat(activeTab.CurrentChannel.Channel);
                    }
                }
            }

            // Process keybinds that have modifiers while the chat is focused.
            if (inputActive)
            {
                Plugin.Functions.KeybindManager.HandleKeybinds(KeyboardSource.ImGui, true, true);
                LastActivityTime = FrameTime;
            }

            // Only trigger unfocused if we are currently not calling the auto complete
            if (!Activate && !inputActive && AutoCompleteHandler.AutoCompleteInfo == null)
            {
                if (Plugin.Config.PlaySounds && !PlayedClosingSound)
                {
                    PlayedClosingSound = true;
                    unsafe { UIGlobals.PlaySoundEffect(ChatCloseSfx); }
                }

                if (activeTab.CurrentChannel.UseTempChannel)
                {
                    activeTab.CurrentChannel.ResetTempChannel();
                    Plugin.Functions.Chat.SetChannelWithExtraChat(Plugin.CurrentTab.CurrentChannel.Channel);
                }
            }

            using (var context = ImRaii.ContextPopupItem("ChatInputContext"))
            {
                if (context)
                {
                    using var pushedColor = ImRaii.PushColor(ImGuiCol.Text, normalColor);
                    if (ImGui.Selectable(Language.ChatLog_HideChat))
                        MainWindow.CurrentHideState = HideState.User;
                }
            }
        }

        // Drawn last so all the item checks above still refer to the input
        if (showCounter)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(inputBytes >= CounterCriticalBytes ? ImGuiColors.DalamudRed : ImGuiColors.DalamudYellow, $"{inputBytes}/{MaxInputBytes}");
        }
    }

    private bool IsValidCommand(string command)
    {
        return Plugin.CommandManager.Commands.ContainsKey(command) || Plugin.Commands.AllCommands.ContainsKey(command);
    }

    private unsafe int Callback(scoped ref ImGuiInputTextCallbackData data)
    {
        // We play the opening sound here only if closing sound has been played before
        if (Plugin.Config.PlaySounds && PlayedClosingSound)
        {
            PlayedClosingSound = false;
            UIGlobals.PlaySoundEffect(ChatOpenSfx);
        }

        // Set the cursor pos to the user selected
        if (Plugin.InputPreview.SelectedCursorPos != -1)
        {
            data.CursorPos = Plugin.InputPreview.SelectedCursorPos;
            Plugin.InputPreview.SelectedCursorPos = -1;
        }

        CursorPos = data.CursorPos;
        if (data.EventFlag == ImGuiInputTextFlags.CallbackCompletion)
        {
            if (data.CursorPos == 0)
            {
                AutoCompleteHandler.AutoCompleteInfo = new AutoCompleteInfo(string.Empty, data.CursorPos, data.CursorPos);
                AutoCompleteHandler.AutoCompleteOpen = true;
                AutoCompleteHandler.AutoCompleteSelection = 0;

                return 0;
            }

            int white;
            for (white = data.CursorPos - 1; white >= 0; white--)
                if (data.Buf[white] == ' ')
                    break;

            var start = data.Buf + white + 1;
            var end = data.CursorPos - white - 1;
            var utf8Message = Marshal.PtrToStringUTF8((nint)start, end);
            var correctedCursor = data.CursorPos - (end - utf8Message.Length);
            AutoCompleteHandler.AutoCompleteInfo = new AutoCompleteInfo(utf8Message, white + 1, correctedCursor);
            AutoCompleteHandler.AutoCompleteOpen = true;
            AutoCompleteHandler.AutoCompleteSelection = 0;
            return 0;
        }

        if (data.EventFlag == ImGuiInputTextFlags.CallbackCharFilter)
            if (!Plugin.Functions.Chat.IsCharValid((char) data.EventChar))
                return 1;

        if (Activate)
        {
            Activate = false;
            data.CursorPos = ActivatePos > -1 ? ActivatePos : ChatInput.Length;
            data.SelectionStart = data.SelectionEnd = data.CursorPos;
            ActivatePos = -1;
        }

        Plugin.CommandHelpWindow.IsOpen = false;
        var text = MemoryHelper.ReadString((nint) data.Buf, data.BufTextLen);
        if (text.StartsWith('/'))
        {
            var command = text.Split(' ')[0];
            if (Plugin.Commands.AllCommands.TryGetValue(command, out var textCommand))
                Plugin.CommandHelpWindow.UpdateContent(textCommand.Description);
            else if (Plugin.CommandManager.Commands.TryGetValue(command, out var info) && info.ShowInHelp)
                Plugin.CommandHelpWindow.UpdateContent(info.HelpMessage);
        }

        if (data.EventFlag != ImGuiInputTextFlags.CallbackHistory)
            return 0;

        var prevPos = SendHandler.InputBacklogIdx;
        switch (data.EventKey)
        {
            case ImGuiKey.UpArrow:
                switch (SendHandler.InputBacklogIdx)
                {
                    case -1:
                        var offset = 0;

                        if (!string.IsNullOrWhiteSpace(ChatInput))
                        {
                            SendHandler.AddBacklog(ChatInput);
                            offset = 1;
                        }

                        SendHandler.InputBacklogIdx = SendHandler.InputBacklog.Count - 1 - offset;
                        break;
                    case > 0:
                        SendHandler.InputBacklogIdx--;
                        break;
                }
                break;
            case ImGuiKey.DownArrow:
                if (SendHandler.InputBacklogIdx != -1)
                    if (++SendHandler.InputBacklogIdx >= SendHandler.InputBacklog.Count)
                        SendHandler.InputBacklogIdx = -1;
                break;
        }

        if (prevPos == SendHandler.InputBacklogIdx)
            return 0;

        var historyStr = SendHandler.InputBacklogIdx >= 0 ? SendHandler.InputBacklog[SendHandler.InputBacklogIdx] : string.Empty;
        data.DeleteChars(0, data.BufTextLen);
        data.InsertChars(0, historyStr);

        return 0;
    }
}