using System.Text;
using ChatThree.Code;
using ChatThree.Util;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Extensions;

namespace ChatThree.GameFunctions;

public readonly record struct GuardedSend(string Destination, string Message);

public static class ChannelGuard
{
    private const uint EmoteCommandRow = 401;

    public static GuardedSend? Check(Plugin plugin, Tab tab, string trimmed)
    {
        if (!Plugin.Config.WarnPublicChannels)
            return null;

        if (trimmed.StartsWith('/'))
        {
            var command = trimmed.Split(' ')[0].ToLowerInvariant();

            // A channel command on its own only switches channel
            if (trimmed[command.Length..].Trim().Length == 0)
                return null;

            if (IsEmoteCommand(command))
                return new GuardedSend(ChatTypeName(ChatType.CustomEmote), trimmed);

            return CommandChannel(plugin, command) is { } commandChannel && IsGuarded(commandChannel)
                ? new GuardedSend(ChannelName(commandChannel), trimmed)
                : null;
        }

        var bytes = Encoding.UTF8.GetBytes(trimmed);
        if (AutoTranslate.StartsWithCommand(ref bytes))
            return null;

        // Same check as SendHandler: with a tell target the message is sent as a tell
        var target = tab.TellTarget.IsSet() ? tab.TellTarget : tab.CurrentChannel.TempTellTarget ?? tab.CurrentChannel.TellTarget;
        if (target != null)
            return null;

        var channel = tab.CurrentChannel.UseTempChannel ? tab.CurrentChannel.TempChannel : tab.CurrentChannel.Channel;
        return IsGuarded(channel)
            ? new GuardedSend(ChannelName(channel), $"{channel.Prefix()} {trimmed}")
            : null;
    }

    public static string ChannelName(InputChannel channel)
    {
        var name = ChatTypeName(channel.ToChatType());

        string? shellName = null;
        if (channel.IsLinkshell())
            shellName = Chat.GetLinkshellName(channel.LinkshellIndex());
        else if (channel.IsCrossLinkshell())
            shellName = Chat.GetCrossLinkshellName(channel.LinkshellIndex());

        return string.IsNullOrWhiteSpace(shellName) ? name : $"{name}: {shellName}";
    }

    public static unsafe ulong LinkshellId(uint idx)
    {
        var shells = InfoProxyLinkshell.Instance()->LinkShells;
        return idx < shells.Length ? shells[(int) idx].Id : 0;
    }

    public static unsafe string? CrossLinkshellKey(uint idx)
    {
        var shells = InfoProxyCrossWorldLinkshell.Instance()->CrossWorldLinkshells;
        if (idx >= shells.Length)
            return null;

        ref var shell = ref shells[(int) idx];
        var name = shell.Name.ToString();
        return name.Length == 0 ? null : $"{name}|{shell.FoundationTime}";
    }

    private static bool IsGuarded(InputChannel channel)
    {
        var config = Plugin.Config;
        if (channel.IsLinkshell())
            return !config.WarnAllowedLinkshells.Contains(LinkshellId(channel.LinkshellIndex()));

        if (channel.IsCrossLinkshell())
            return CrossLinkshellKey(channel.LinkshellIndex()) is not { } key || !config.WarnAllowedCrossLinkshells.Contains(key);

        if (channel.IsExtraChatLinkshell())
            return !config.WarnAllowedExtraChatLinkshells.Contains(channel.LinkshellIndex());

        return channel switch
        {
            InputChannel.Party => !config.WarnAllowParty,
            InputChannel.Say or InputChannel.Yell or InputChannel.Shout or InputChannel.NoviceNetwork
                or InputChannel.Alliance or InputChannel.FreeCompany => true,
            _ => false,
        };
    }

    private static InputChannel? CommandChannel(Plugin plugin, string command)
    {
        if (plugin.Commands.TextCommandChannels.TryGetValue(command, out var type))
            return type.ToInputChannel();

        foreach (var channel in Enum.GetValues<InputChannel>())
            if (channel.IsExtraChatLinkshell() && channel.Prefix() == command)
                return channel;

        return null;
    }

    private static bool IsEmoteCommand(string command)
    {
        if (!Sheets.TextCommandSheet.TryGetRow(EmoteCommandRow, out var row))
            return false;

        return new[] { row.Command, row.ShortCommand, row.Alias, row.ShortAlias }
            .Select(text => text.ToString())
            .Any(text => text.Length > 0 && text == command);
    }

    private static string ChatTypeName(ChatType type)
        => Sheets.LogFilterSheet.FirstOrNull(row => row.LogKind == (byte) type)?.Name.ToString() ?? type.Name();
}
