using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace ChatThree.GameFunctions.Types;

[Serializable]
public class TellTarget
{
    public string Name { get; set; }
    public uint World { get; set; }
    public ulong ContentId { get; set; }
    public TellReason Reason { get; private set; }

    public TellTarget(string name, uint world, ulong contentId, TellReason reason)
    {
        Name = name;
        World = world;
        ContentId = contentId;
        Reason = reason;
    }

    public bool IsSet()
        => Name.Length > 0 && World > 0;

    public string ToWorldString()
        => Sheets.WorldSheet.TryGetRow(World, out var worldRow) ? worldRow.Name.ToString() : string.Empty;

    public string ToTargetString()
        => $"{Name}@{ToWorldString()}";

    public bool CompareNames(TellTarget other)
    {
        if (!other.IsSet() || !IsSet())
            return false;

        return ToTargetString().Equals(other.ToTargetString(), StringComparison.OrdinalIgnoreCase);
    }

    public bool FromCharacterLink(ReadOnlySePayload payload)
    {
        if (payload.Type != ReadOnlySePayloadType.Macro || payload.MacroCode != MacroCode.Link)
            return false;

        if (!payload.TryGetExpression(out var intExpr0, out _, out var uintExpr3, out _, out var strExpr5))
            return false;

        if (!intExpr0.TryGetInt(out var linkType) || (LinkMacroPayloadType)linkType != LinkMacroPayloadType.Character)
            return false;

        if (!uintExpr3.TryGetUInt(out var worldId) || !strExpr5.TryGetString(out var linkName))
            return false;

        Name = linkName.ToString();
        World = worldId;

        return true;
    }

    public unsafe void FromTarget(IPlayerCharacter target)
    {
        Name = target.Name.TextValue;
        World = target.HomeWorld.RowId;
        ContentId = ((Character*)target.Address)->ContentId;
    }

    public static TellTarget Empty() => new(string.Empty, 0, 0, TellReason.Direct);

    public TellTarget Clone()
        => new(Name, World, ContentId, Reason);
}
