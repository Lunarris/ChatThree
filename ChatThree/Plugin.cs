using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ChatThree.Ipc;
using ChatThree.Resources;
using ChatThree.Ui;
using ChatThree.Ui.ChatLog;
using ChatThree.Util;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace ChatThree;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class Plugin : IDalamudPlugin
{
    public const string PluginName = "Chat 3";

    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IDalamudPluginInterface Interface { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IKeyState KeyState { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IPartyList PartyList { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] public static INotificationManager Notification { get; private set; } = null!;
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] public static ISeStringEvaluator Evaluator { get; private set; } = null!;
    [PluginService] public static IContextMenu ContextMenu { get; private set; } = null!;

    public static Configuration Config = null!;
    public static readonly HttpClient HttpClient = new();

    public readonly WindowSystem WindowSystem = new(PluginName);
    public SettingsWindow SettingsWindow { get; }
    public ChatLog ChatLog { get; }
    public DbViewer DbViewer { get; }
    public TellConversationsWindow TellConversationsWindow { get; }
    public InputPreview InputPreview { get; }
    public CommandHelpWindow CommandHelpWindow { get; }
    public SeStringDebugger SeStringDebugger { get; }
    public DebuggerWindow DebuggerWindow { get; }

    public Commands Commands { get; }
    public GameFunctions.GameFunctions Functions { get; }
    public MessageManager MessageManager { get; }
    public TellWindows TellWindows { get; }
    public IpcManager Ipc { get; }
    public ExtraChat ExtraChat { get; }
    public TypingIpc TypingIpc { get; }
    public FontManager FontManager { get; }

    public int DeferredSaveFrames = -1;

    public DateTime GameStarted { get; }

    public Vector4 DefaultText = Vector4.Zero;

    // Tab management needs to happen outside the chatlog window class for access reasons
    public int LastTab { get; set; }
    public int? WantedTab { get; set; }
    public Tab CurrentTab
    {
        get
        {
            var i = LastTab;
            return i > -1 && i < Config.Tabs.Count ? Config.Tabs[i] : new Tab();
        }
    }

    public Plugin()
    {
        try
        {
            GameStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime();

            Config = Interface.GetPluginConfig() as Configuration ?? new Configuration();

            if (Config.Tabs.Count == 0)
                Config.Tabs.Add(TabsUtil.VanillaGeneral);

            ImGuiUtil.Initialize(this);

            Commands = new Commands();
            Functions = new GameFunctions.GameFunctions(this);
            Ipc = new IpcManager();
            TypingIpc = new TypingIpc(this);
            ExtraChat = new ExtraChat();
            FontManager = new FontManager();

            TellWindows = new TellWindows(this); // Needed by the message processing thread
            MessageManager = new MessageManager(this); // Does it require UI?

            ChatLog = new ChatLog(this);
            SettingsWindow = new SettingsWindow(this);
            DbViewer = new DbViewer(this);
            TellConversationsWindow = new TellConversationsWindow(this);
            InputPreview = new InputPreview(ChatLog.InputHandler);
            CommandHelpWindow = new CommandHelpWindow(ChatLog);
            SeStringDebugger = new SeStringDebugger(this);
            DebuggerWindow = new DebuggerWindow(this);

            WindowSystem.AddWindow(ChatLog);
            WindowSystem.AddWindow(SettingsWindow);
            WindowSystem.AddWindow(DbViewer);
            WindowSystem.AddWindow(TellConversationsWindow);
            WindowSystem.AddWindow(InputPreview);
            WindowSystem.AddWindow(CommandHelpWindow);
            WindowSystem.AddWindow(SeStringDebugger);
            WindowSystem.AddWindow(DebuggerWindow);

            FontManager.BuildFonts();

            Interface.UiBuilder.DisableCutsceneUiHide = true;
            Interface.UiBuilder.DisableGposeUiHide = true;

            // let all the other components register, then initialize commands
            Commands.Initialise();

            if (Interface.Reason is not PluginLoadReason.Boot)
                MessageManager.FilterAllTabsAsync();

            Framework.Update += FrameworkUpdate;
            Interface.UiBuilder.Draw += Draw;

            Interface.UiBuilder.OpenMainUi += ToggleMainWindowUi;

            #if !DEBUG
            // Avoid 300ms hitch when sending first message by preloading the
            // auto-translate cache. Don't do this in debug because it makes
            // profiling difficult.
            AutoTranslate.PreloadCache();
            #endif
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Plugin load threw an error, turning off plugin");
            Dispose();

            // Re-throw the exception to fail the plugin load.
            throw;
        }
    }

    // Suppressing this warning because Dispose() is called in Plugin() if the
    // load fails, so some values may not be initialized.
    [SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract")]
    public void Dispose()
    {
        Interface.UiBuilder.Draw -= Draw;
        Framework.Update -= FrameworkUpdate;
        GameFunctions.GameFunctions.SetChatInteractable(true);

        WindowSystem?.RemoveAllWindows();
        ChatLog?.Dispose();
        DbViewer?.Dispose();
        TellConversationsWindow?.Dispose();
        InputPreview?.Dispose();
        SettingsWindow?.Dispose();
        DebuggerWindow?.Dispose();
        SeStringDebugger?.Dispose();

        TypingIpc?.Dispose();
        ExtraChat?.Dispose();
        Ipc?.Dispose();
        MessageManager?.DisposeAsync().AsTask().Wait();
        TellWindows?.Dispose();
        Functions?.Dispose();
        Commands?.Dispose();

        HttpClient.Dispose();
    }

    private void Draw()
    {
        ChatLog.BeginFrame();

        if (Config.HideInLoadingScreens && Condition[ConditionFlag.BetweenAreas])
        {
            ChatLog.FinalizeFrame();
            TypingIpc.Update();
            return;
        }

        ChatLog.IsHidden = HideStateHelper.HideStateCheck(ChatLog, Config.HideInBattle, Config.HideDuringCutscenes, Config.HideWhenNotLoggedIn, ChatLog.InputHandler.Activate);

        Interface.UiBuilder.DisableUserUiHide = !Config.HideWhenUiHidden;
        DefaultText = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

        using ((Config.FontsEnabled ? FontManager.RegularFont : FontManager.Axis).Push())
            WindowSystem.Draw();

        ChatLog.FinalizeFrame();
        TypingIpc.Update();
    }

    public void SaveConfig()
    {
        Interface.SavePluginConfig(Config);
    }

    private static readonly string[] ChatAddonNames =
    [
        "ChatLog",
        "ChatLogPanel_0",
        "ChatLogPanel_1",
        "ChatLogPanel_2",
        "ChatLogPanel_3",
    ];

    private void FrameworkUpdate(IFramework framework)
    {
        if (DeferredSaveFrames >= 0 && DeferredSaveFrames-- == 0)
            SaveConfig();

        if (!Config.HideChat)
            return;

        foreach (var name in ChatAddonNames)
            if (GameFunctions.GameFunctions.IsAddonInteractable(name))
                GameFunctions.GameFunctions.SetAddonInteractable(name, false);
    }


    public static bool InBattle => Condition[ConditionFlag.InCombat];
    public static bool GposeActive => Condition[ConditionFlag.WatchingCutscene];
    public static bool CutsceneActive => Condition[ConditionFlag.OccupiedInCutSceneEvent] || Condition[ConditionFlag.WatchingCutscene78];
    public void ToggleMainWindowUi() => SettingsWindow.Toggle();
    public void ToggleDBViewerWindow() => DbViewer.Toggle();
}
