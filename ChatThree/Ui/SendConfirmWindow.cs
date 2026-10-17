using System.Numerics;
using ChatThree.Resources;
using ChatThree.Ui.Handler;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace ChatThree.Ui;

public class SendConfirmWindow : Window
{
    private const int PreviewLength = 200;

    private InputHandler? Owner;

    public SendConfirmWindow() : base($"{Plugin.PluginName}##chat3-send-confirm")
    {
        Flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.AlwaysAutoResize;

        DisableWindowSounds = true;
    }

    public void Show(InputHandler owner)
    {
        if (IsOpen && Owner == owner)
            return;

        Owner = owner;
        IsOpen = true;
        BringToFront();
    }

    public override void PreOpenCheck()
    {
        if (Owner?.SendHandler.Pending == null)
            IsOpen = false;
    }

    public override void PreDraw()
    {
        if (Owner == null)
            return;

        var center = Owner.MainWindow.LastWindowPos + Owner.MainWindow.LastWindowSize / 2;
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f));
    }

    public override void Draw()
    {
        if (Owner?.SendHandler.Pending is not var (_, send))
            return;

        // Stay above every chat window until answered, without taking keyboard focus
        ImGuiP.BringWindowToDisplayFront(ImGuiP.GetCurrentWindow());

        ImGui.TextUnformatted(string.Format(Language.ChannelGuard_Question, send.Destination));

        var preview = Owner.ChatInput.Trim();
        if (preview.Length > PreviewLength)
            preview = preview[..PreviewLength] + "…";

        using (ImRaii.TextWrapPos(400 * ImGuiHelpers.GlobalScale))
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int) ImGuiCol.TextDisabled]))
            ImGui.TextUnformatted(preview);

        ImGui.Spacing();

        // "Don't send" comes first, so a held or repeated Enter can only cancel
        if (ImGui.Button(Language.ChannelGuard_Cancel))
        {
            IsOpen = false;
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button(Language.ChannelGuard_Send))
        {
            Owner.SendHandler.SendPending(ref Owner.ChatInput);
            IsOpen = false;
        }
    }

    // Closing without sending (button, Esc or the close button) keeps the text in the input
    public override void OnClose()
    {
        if (Owner?.SendHandler.Pending == null)
            return;

        Owner.SendHandler.Pending = null;
        Owner.Activate = true;
    }
}
