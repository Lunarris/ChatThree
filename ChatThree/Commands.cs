using ChatThree.Code;
using Dalamud.Game.Command;
using Lumina.Excel.Sheets;

namespace ChatThree;

public sealed class Commands : IDisposable
{
    private readonly Dictionary<string, CommandWrapper> Registered = [];

    public Dictionary<string, ChatType> TextCommandChannels { get; } = new();
    public Dictionary<string, TextCommand> AllCommands { get; } = [];

    public Commands()
    {
        SetUpTextCommandChannels();
        SetUpAllCommands();
    }

    public void Dispose()
    {
        foreach (var name in Registered.Keys)
            Plugin.CommandManager.RemoveHandler(name);
    }

    public void Initialise()
    {
        foreach (var wrapper in Registered.Values)
        {
            Plugin.CommandManager.AddHandler(wrapper.Name, new CommandInfo(Invoke)
            {
                HelpMessage = wrapper.Description ?? string.Empty,
                ShowInHelp = wrapper.ShowInHelp,
            });
        }
    }

    public CommandWrapper Register(string name, string? description = null, bool? showInHelp = null)
    {
        if (Registered.TryGetValue(name, out var wrapper))
        {
            if (description != null)
                wrapper.Description = description;

            if (showInHelp != null)
                wrapper.ShowInHelp = showInHelp.Value;

            return wrapper;
        }

        Registered[name] = new CommandWrapper(name, description, showInHelp ?? true);
        return Registered[name];
    }

    private void Invoke(string command, string arguments)
    {
        if (!Registered.TryGetValue(command, out var wrapper))
        {
            Plugin.Log.Warning($"Missing registration for command {command}");
            return;
        }

        try
        {
            wrapper.Invoke(command, arguments);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Error while executing command {command}");
        }
    }

    #region Ingame Commands
    private void SetUpTextCommandChannels()
    {
        TextCommandChannels.Clear();

        foreach (var input in Enum.GetValues<InputChannel>())
        {
            var commands = input.TextCommands();
            if (commands == null)
                continue;

            var type = input.ToChatType();
            foreach (var command in commands)
                AddTextCommandChannel(command, type);
        }

        if (Sheets.TextCommandSheet.TryGetRow(116, out var row))
            AddTextCommandChannel(row, ChatType.Echo);
    }

    private void AddTextCommandChannel(TextCommand command, ChatType type)
    {
        TextCommandChannels[command.Command.ToString()] = type;
        TextCommandChannels[command.ShortCommand.ToString()] = type;
        TextCommandChannels[command.Alias.ToString()] = type;
        TextCommandChannels[command.ShortAlias.ToString()] = type;
    }

    private void SetUpAllCommands()
    {
        foreach (var command in Sheets.TextCommandSheet)
        {
            if (!command.Command.IsEmpty)
                AllCommands.TryAdd(command.Command.ToString(), command);

            if (!command.ShortCommand.IsEmpty)
                AllCommands.TryAdd(command.ShortCommand.ToString(), command);

            if (!command.Alias.IsEmpty)
                AllCommands.TryAdd(command.Alias.ToString(), command);

            if (!command.ShortAlias.IsEmpty)
                AllCommands.TryAdd(command.ShortAlias.ToString(), command);
        }
    }
    #endregion
}

public sealed class CommandWrapper
{
    public string Name { get; }
    public string? Description { get; set; }
    public bool ShowInHelp { get; set; }

    public event Action<string, string>? Execute;

    public CommandWrapper(string name, string? description, bool showInHelp)
    {
        Name = name;
        Description = description;
        ShowInHelp = showInHelp;
    }

    public void Invoke(string command, string arguments)
    {
        Execute?.Invoke(command, arguments);
    }
}
