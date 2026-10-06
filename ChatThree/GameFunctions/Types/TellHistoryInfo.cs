namespace ChatThree.GameFunctions.Types;

public sealed class TellHistoryInfo
{
    public string Name { get; }
    public uint World { get; }
    public ulong ContentId { get; }

    public TellHistoryInfo(string name, uint world, ulong contentId)
    {
        Name = name;
        World = world;
        ContentId = contentId;
    }
}
