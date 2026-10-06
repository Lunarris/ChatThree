using System.Numerics;
using ChatThree.Code;
using ChatThree.GameFunctions.Types;
using ChatThree.Resources;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Lumina.Text.ReadOnly;

namespace ChatThree.Ui;

public class TellWindows : IDisposable
{
    private readonly Plugin Plugin;

    private readonly object TabsLock = new();
    private volatile Tab[] Tabs = [];
    private readonly HashSet<Guid> Windows = [];

    // The best known way to reach each partner, keyed by "Name@World". A tab
    // built from a chat message alone has no content ID and falls back to a
    // plain /tell, so we keep what the game or an incoming tell told us.
    private readonly Dictionary<string, TellTarget> Contexts = new(StringComparer.OrdinalIgnoreCase);

    public TellWindows(Plugin plugin)
    {
        Plugin = plugin;

        Plugin.ClientState.Logout += Logout;
        Plugin.ContextMenu.OnMenuOpened += MenuOpened;
    }

    public void Dispose()
    {
        Plugin.ContextMenu.OnMenuOpened -= MenuOpened;
        Plugin.ClientState.Logout -= Logout;
    }

    private void Logout(int _, int __)
    {
        lock (TabsLock)
        {
            foreach (var tab in Tabs)
                tab.PopOut = false;

            Tabs = [];
            Contexts.Clear();
        }
    }

    /// <summary>
    /// Records a tell target handed over by the game, e.g. "Send Tell" on a
    /// Party Finder listing, so the partner's tell window sends with the same
    /// content ID and reason as the main chat does.
    /// </summary>
    public void SetContext(TellTarget target)
    {
        if (!target.IsSet() || target.ContentId == 0)
            return;

        lock (TabsLock)
        {
            Contexts[target.ToTargetString()] = target.Clone();

            var tab = Tabs.FirstOrDefault(t => t.TellTarget.CompareNames(target));
            if (tab != null)
                tab.TellTarget = target.Clone();
        }
    }

    public void ProcessMessage(Message message)
    {
        if (!TryGetPartner(message, out var partner))
            return;

        var tab = FindOrCreateTab(partner, Plugin.Config.TellWindows);
        if (tab == null)
            return;

        // Once they have sent us a tell we can always reply, which outlives
        // e.g. a Party Finder listing.
        if (message.Code.Type == ChatType.TellIncoming && message.ContentId != 0)
            SetContext(new TellTarget(partner.Name, partner.World, message.ContentId, TellReason.Reply));

        tab.AddMessage(message);
    }

    /// <param name="fullHistory">Load every stored tell with this player, not only what the history setting allows.</param>
    public Tab? Open(string name, uint world, bool fullHistory = false)
    {
        // ContentId stays 0 so the first message goes through /tell, it gets
        // filled in once the player answers.
        var partner = new TellTarget(name, world, 0, TellReason.Direct);
        return partner.IsSet() ? FindOrCreateTab(partner, true, fullHistory) : null;
    }

    private Tab? FindOrCreateTab(TellTarget partner, bool create, bool fullHistory = false)
    {
        Tab? tab;

        // Tell tabs are created from the message processing thread and the UI
        lock (TabsLock)
        {
            tab = Tabs.FirstOrDefault(t => t.TellTarget.CompareNames(partner));
            if (tab != null)
            {
                // An open window may only hold this session's tells.
                if (!fullHistory)
                    return tab;
            }
            else
            {
                if (!create)
                    return null;

                if (Contexts.TryGetValue(partner.ToTargetString(), out var context))
                    partner = context.Clone();

                tab = new Tab
                {
                    Name = partner.ToTargetString(),
                    Channel = InputChannel.Tell,
                    SupportsInput = true,
                    PopOut = true,
                    IsTempTab = true,
                    UnreadMode = UnreadMode.None,
                    TellTarget = partner,
                };

                Tabs = [..Tabs, tab];
            }
        }

        LoadHistory(tab, fullHistory);
        return tab;
    }

    //List for the conversations window to keep list users to open message engagements with.
    public List<TellConversation> GetConversations()
    {
        var conversations = new Dictionary<string, TellConversation>(StringComparer.OrdinalIgnoreCase);

        // Newest first, so the first tell seen for a player is their latest.
        foreach (var (date, senderSource) in Plugin.MessageManager.Store.GetTellSenders(Plugin.MessageManager.CurrentContentId))
        {
            if (!TryGetPartner(senderSource, out var partner))
                continue;

            var key = partner.ToTargetString();
            if (conversations.TryGetValue(key, out var conversation))
                conversation.Count++;
            else
                conversations[key] = new TellConversation(partner.Name, partner.World, key, date.ToLocalTime().ToString("g"));
        }

        // Dictionaries keep insertion order as long as nothing is removed.
        return conversations.Values.ToList();
    }

    private void MenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetDefault target)
            return;

        if (target.TargetName.Length == 0 || target.TargetContentId == Plugin.PlayerState.ContentId)
            return;

        // Validae only players
        if (target.TargetHomeWorld.ValueNullable is not { IsPublic: true })
            return;

        var name = target.TargetName;
        var world = target.TargetHomeWorld.RowId;
        args.AddMenuItem(new MenuItem
        {
            Name = Language.Context_OpenMessenger,
            PrefixChar = 'C',
            OnClicked = _ => Open(name, world),
        });
    }

    public void AddWindowsToDraw()
    {
        foreach (var tab in Tabs)
        {
            if (!Windows.Add(tab.Identifier))
                continue;

            var window = new Popout(Plugin, tab, -1)
            {
                Position = new Vector2(100, 100) + new Vector2(30, 30) * (Windows.Count - 1),
                PositionCondition = ImGuiCond.FirstUseEver,
            };

            Plugin.WindowSystem.AddWindow(window);
        }
    }

    public void Remove(Tab tab)
    {
        lock (TabsLock)
            Tabs = Tabs.Where(t => t != tab).ToArray();

        Windows.Remove(tab.Identifier);
    }

    private void LoadHistory(Tab tab, bool fullHistory = false)
    {
        Task.Run(() =>
        {
            try
            {
                DateTimeOffset? since = null;
                if (!fullHistory && !Plugin.Config.FilterIncludePreviousSessions)
                    since = Plugin.GameStarted;

                using var messages = Plugin.MessageManager.Store.GetMostRecentMessages(Plugin.MessageManager.CurrentContentId, since, chatTypes: [ChatType.TellIncoming, ChatType.TellOutgoing]);
                var pendingMessages = messages.Where(message => TryGetPartner(message, out var partner) && partner.CompareNames(tab.TellTarget)).ToList();
                tab.Messages.AddSortPrune(pendingMessages, MessageManager.MessageDisplayLimit);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Error loading tell history");
            }
        });
    }

    private static bool TryGetPartner(Message message, out TellTarget partner)
    {
        partner = TellTarget.Empty();
        if (message.Code.Type is not (ChatType.TellIncoming or ChatType.TellOutgoing))
            return false;

        return TryGetPartner(message.SenderSource, out partner);
    }

    private static bool TryGetPartner(SeString senderSource, out TellTarget partner)
    {
        partner = TellTarget.Empty();
        foreach (var payload in new ReadOnlySeString(senderSource.Encode()))
        {
            if (partner.FromCharacterLink(payload))
                break; // Character link found
        }

        return partner.IsSet();
    }
}

/// <summary>
/// One player the current character has exchanged tells with.
/// </summary>
public sealed class TellConversation(string name, uint world, string display, string lastTell)
{
    public string Name { get; } = name;
    public uint World { get; } = world;
    public string Display { get; } = display;
    public string LastTell { get; } = lastTell;
    public int Count { get; set; } = 1;

    // Drawn every frame, so only formatted once the count has settled.
    private string? CachedCountText;
    public string CountText => CachedCountText ??= Count.ToString();
}
