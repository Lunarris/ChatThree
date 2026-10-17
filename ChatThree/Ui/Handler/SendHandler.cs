using System.Text;
using ChatThree.Code;
using ChatThree.GameFunctions;
using ChatThree.GameFunctions.Types;
using ChatThree.Util;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ChatThree.Ui.Handler;

public class SendHandler
{
    private readonly Plugin Plugin;

    public List<string> InputBacklog => Plugin.InputBacklog;
    public int InputBacklogIdx = -1;

    public const int ConfirmDelayMs = 500;
    private const uint WarnSound = 11;

    public sealed class PendingSend(Tab tab, GuardedSend send, string text, InputChannel channel)
    {
        public Tab Tab { get; } = tab;
        public GuardedSend Send { get; } = send;
        public string Text { get; } = text;
        public InputChannel Channel { get; } = channel;
        public long Since { get; } = Environment.TickCount64;
        public bool EnterReleased { get; set; }

        // A held or bounced Enter must not count as the second press
        public bool Ready => EnterReleased && Environment.TickCount64 - Since >= ConfirmDelayMs;
    }

    public PendingSend? Pending;

    private string Message = string.Empty;
    private bool TellSpecialUnused;

    public SendHandler(Plugin plugin)
    {
        Plugin = plugin;
    }

    public void SendWithoutContext(string message)
    {
        Message = message;
        SendChatBox(Plugin.CurrentTab, ref Message, ref TellSpecialUnused);
    }

    public void SendChatBox(Tab activeTab, ref string chatInput, ref bool tellSpecial)
    {
        if (!string.IsNullOrWhiteSpace(chatInput))
        {
            var trimmed = chatInput.Trim();
            if (Pending is { } pending && pending.Tab == activeTab && pending.Text == trimmed)
            {
                if (pending.Ready)
                    SendPending(pending, ref chatInput);

                return;
            }

            Pending = null;
            if (!tellSpecial && ChannelGuard.Check(Plugin, activeTab, trimmed) is { } guarded)
            {
                Pending = new PendingSend(activeTab, guarded, trimmed, activeTab.CurrentChannel.Channel);
                UIGlobals.PlayChatSoundEffect(WarnSound);
                return;
            }

            AddBacklog(trimmed);
            InputBacklogIdx = -1;

            if (HasTranslationCommand(trimmed))
            {
                activeTab.CurrentChannel.ResetTempChannel();
                chatInput = string.Empty;
                return;
            }

            if (tellSpecial)
            {
                var tellBytes = Encoding.UTF8.GetBytes(trimmed);
                AutoTranslate.ReplaceWithPayload(ref tellBytes);

                Plugin.Functions.Chat.SendTellUsingCommandInner(tellBytes);
                tellSpecial = false;

                activeTab.CurrentChannel.ResetTempChannel();
                chatInput = string.Empty;
                return;
            }

            if (!trimmed.StartsWith('/'))
            {
                var target = activeTab.TellTarget.IsSet() ? activeTab.TellTarget : activeTab.CurrentChannel.TempTellTarget ?? activeTab.CurrentChannel.TellTarget;
                if (target != null)
                {
                    // ContentId 0 is a case where we can't directly send messages, so we send a /tell formatted message and let the game handle it
                    if (target.ContentId == 0)
                    {
                        trimmed = $"/tell {target.ToTargetString()} {trimmed}";
                        var tellBytes = Encoding.UTF8.GetBytes(trimmed);
                        AutoTranslate.ReplaceWithPayload(ref tellBytes);

                        ChatBox.SendMessageUnsafe(tellBytes);

                        activeTab.CurrentChannel.ResetTempChannel();
                        chatInput = string.Empty;
                        return;
                    }

                    var reason = target.Reason;
                    var world = Sheets.WorldSheet.GetRow(target.World);
                    if (world is { IsPublic: true })
                    {
                        if (reason == TellReason.Reply && GameFunctions.GameFunctions.GetFriends().Any(friend => friend.ContentId == target.ContentId))
                            reason = TellReason.Friend;

                        var tellBytes = Encoding.UTF8.GetBytes(trimmed);
                        AutoTranslate.ReplaceWithPayload(ref tellBytes);

                        Plugin.Functions.Chat.SendTell(reason, target.ContentId, target.Name, (ushort) world.RowId, tellBytes, trimmed);
                    }

                    activeTab.CurrentChannel.ResetTempChannel();
                    chatInput = string.Empty;
                    return;
                }

                if (activeTab.CurrentChannel.UseTempChannel)
                    trimmed = $"{activeTab.CurrentChannel.TempChannel.Prefix()} {trimmed}";
                else
                    trimmed = $"{activeTab.CurrentChannel.Channel.Prefix()} {trimmed}";
            }

            var bytes = Encoding.UTF8.GetBytes(trimmed);
            AutoTranslate.ReplaceWithPayload(ref bytes);

            ChatBox.SendMessageUnsafe(bytes);
        }

        activeTab.CurrentChannel.ResetTempChannel();
        chatInput = string.Empty;
    }

    private void SendPending(PendingSend pending, ref string chatInput)
    {
        Pending = null;
        AddBacklog(pending.Text);
        InputBacklogIdx = -1;

        var bytes = Encoding.UTF8.GetBytes(pending.Send.Message);
        AutoTranslate.ReplaceWithPayload(ref bytes);
        ChatBox.SendMessageUnsafe(bytes);

        pending.Tab.CurrentChannel.ResetTempChannel();
        chatInput = string.Empty;
    }

    private bool HasTranslationCommand(string trimmed)
    {
        var messageBytes = Encoding.UTF8.GetBytes(trimmed);
        if (AutoTranslate.StartsWithCommand(ref messageBytes))
        {
            ChatBox.SendMessageUnsafe(messageBytes);
            return true;
        }

        return false;
    }

    public void AddBacklog(string message)
    {
        for (var i = 0; i < InputBacklog.Count; i++)
        {
            if (InputBacklog[i] != message)
                continue;

            InputBacklog.RemoveAt(i);
            break;
        }

        InputBacklog.Add(message);
    }
}