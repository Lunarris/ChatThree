using Dalamud.Game.ClientState.Keys;

namespace ChatThree.GameFunctions.Types;

public class Keybind
{
    public VirtualKey Key1 { get; init; }
    public ModifierFlag Modifier1 { get; init; }

    public VirtualKey Key2 { get; init; }
    public ModifierFlag Modifier2 { get; init; }
}
