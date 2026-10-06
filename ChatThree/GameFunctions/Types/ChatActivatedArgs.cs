namespace ChatThree.GameFunctions.Types;

public sealed class ChatActivatedArgs
{
    public string? AddIfNotPresent { get; init; }
    public string? Input { get; init; }
    public ChannelSwitchInfo ChannelSwitchInfo { get; }
    public TellReason? TellReason { get; init; }
    public TellTarget? TellTarget { get; init; }
    public bool TellSpecial { get; init; } //  specific to Eureka/Bozja/Zadnor

    public ChatActivatedArgs(ChannelSwitchInfo channelSwitchInfo)
    {
        ChannelSwitchInfo = channelSwitchInfo;
    }
}
