using ChatThree.Code;
using Dalamud.Plugin.Ipc;

namespace ChatThree.Ipc;

using ChatInputState = (bool InputVisible, bool InputFocused, bool HasText, bool IsTyping, int TextLength, ChatType ChannelType);

public sealed class TypingIpc : IDisposable
{
    private Plugin Plugin { get; }

    private ICallGateProvider<ChatInputState> StateQueryGate { get; }
    private ICallGateProvider<ChatInputState, object?> StateChangedGate { get; }

    private ChatInputState LastState;
    private bool HasState;

    public TypingIpc(Plugin plugin)
    {
        Plugin = plugin;

        StateQueryGate = Plugin.Interface.GetIpcProvider<ChatInputState>("ChatTwo.GetChatInputState");
        StateChangedGate = Plugin.Interface.GetIpcProvider<ChatInputState, object?>("ChatTwo.ChatInputStateChanged");

        StateQueryGate.RegisterFunc(GetState);
    }

    private ChatInputState BuildState()
    {
        var log = Plugin.ChatLog;

        var usedChannel = Plugin.CurrentTab.CurrentChannel;
        var inputChannel = usedChannel.UseTempChannel ? usedChannel.TempChannel : usedChannel.Channel;
        var channelType = inputChannel.ToChatType();

        return (InputVisible: !log.IsHidden,
            log.InputHandler.InputFocused,
            HasText: log.InputHandler.ChatInput.Length > 0,
            IsTyping: log.InputHandler is { InputFocused: true, ChatInput.Length: > 0 },
            TextLength: log.InputHandler.ChatInput.Length,
            ChannelType: channelType);
    }

    private ChatInputState GetState()
        => BuildState();

    public void Update()
    {
        var state = BuildState();
        if (HasState && state.Equals(LastState))
            return;

        HasState = true;
        LastState = state;
        StateChangedGate.SendMessage(state);
    }

    public void Dispose()
    {
        StateQueryGate.UnregisterFunc();
    }
}
