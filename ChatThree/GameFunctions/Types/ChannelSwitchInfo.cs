using ChatThree.Code;

namespace ChatThree.GameFunctions.Types;

public class ChannelSwitchInfo {
    public InputChannel? Channel { get; }
    public bool Permanent { get; }
    public RotateMode Rotate { get; }
    public string? Text { get; }

    public ChannelSwitchInfo(InputChannel? channel, bool permanent = false, RotateMode rotate = RotateMode.None, string? text = null)
    {
        Channel = channel;
        Permanent = permanent;
        Rotate = rotate;
        Text = text;
    }
}
