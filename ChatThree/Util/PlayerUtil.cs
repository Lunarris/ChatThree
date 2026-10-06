using Dalamud.Game.Config;

namespace ChatThree.Util;

public static class PlayerUtil
{
    public static bool ScreenshotMode;

    private static readonly string Salt = new Random().Next().ToString();

    private static long LastPlayerNameDisplayTypeRefresh;
    private static PlayerNameDisplayType CurrentPlayerNameDisplayType = PlayerNameDisplayType.FullName;

    private enum PlayerNameDisplayType : uint
    {
        FullName = 0,
        SurnameAbbreviated = 1,
        ForenameAbbreviated = 2,
        Initials = 3,
    }

    public static string HidePlayerInString(string str, string playerName, uint worldId)
    {
        var expected = AbbreviatePlayerName(playerName);
        var hash = HashPlayer(playerName, worldId);
        return str.Replace(playerName, expected).Replace(expected, hash);
    }

    public static string HashPlayer(string playerName, uint worldId)
    {
        var hashCode = $"{Salt}{playerName}{worldId}".GetHashCode();
        return $"Player {hashCode:X8}";
    }

    private static string AbbreviatePlayerName(string playerName)
    {
        if (LastPlayerNameDisplayTypeRefresh + 5_000 < Environment.TickCount64)
        {
            LastPlayerNameDisplayTypeRefresh = Environment.TickCount64;
            CurrentPlayerNameDisplayType = GetNameDisplayType();
        }

        if (CurrentPlayerNameDisplayType == PlayerNameDisplayType.FullName)
            return playerName;

        var split = playerName.Split(' ');
        if (split.Length != 2)
            return playerName;

        return CurrentPlayerNameDisplayType switch
        {
            PlayerNameDisplayType.SurnameAbbreviated => $"{split.First()} {split.Last().FirstOrDefault('A')}.",
            PlayerNameDisplayType.ForenameAbbreviated => $"{split.First().FirstOrDefault('A')}. {split.Last()}",
            PlayerNameDisplayType.Initials => $"{split.First().FirstOrDefault('A')}. {split.Last().FirstOrDefault('A')}.",
            _ => playerName,
        };
    }

    private static PlayerNameDisplayType GetNameDisplayType()
    {
        var ok = Plugin.GameConfig.TryGet(UiConfigOption.LogNameType, out uint type);
        if (!ok || !Enum.IsDefined((PlayerNameDisplayType)type))
            return PlayerNameDisplayType.FullName;

        return (PlayerNameDisplayType) type;
    }
}