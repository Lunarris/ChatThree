namespace ChatThree.Ui.ChatLog;

public partial class ChatLog
{
    public void DrawChannelName(Tab activeTab)
    {
        var currentChannel = ReadChannelName(activeTab);
        InputHandler.ChunkHandler.DrawChunks(currentChannel);
    }
}