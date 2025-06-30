using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class Command
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Func<Task> Action { get; set; }
}

public class CommandManager
{
    private readonly List<Command> _commands;

    public CommandManager(IEnumerable<Command> commands = null)
    {
        _commands = commands?.ToList() ?? new List<Command>();
    }

    public void Register(Command command)
    {
        _commands.Add(command);
    }

    public Command Find(string input)
    {
        return _commands.FirstOrDefault(c => $"/{c.Name}".Equals(input, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<Command> GetAll() => _commands;

    public IEnumerable<string> GetCompletions(string input)
    {
        return _commands
            .Select(c => $"/{c.Name}")
            .Where(cmd => cmd.StartsWith(input, StringComparison.OrdinalIgnoreCase));
    }

    public void ShowHelp()
    {
        Console.WriteLine("Commands:");
        foreach (var cmd in _commands)
        {
            Console.WriteLine($"  /{cmd.Name.PadRight(10)} - {cmd.Description}");
        }
    }

    public static CommandManager CreateDefaultCommands()
    {
        var commands = new CommandManager(new[]
        {
            new Command
            {
                Name = "clear", Description = "clear chat history",
                Action = async () =>
                {
                    Program.history.Clear();
                    Program.history.Add(new ChatMessage { Role = "system", Content = Program.config.SystemPrompt });
                    Console.WriteLine("Chat history cleared.");
                }
            },
            new Command
            {
                Name = "history", Description = "Show chat history",
                Action = async () =>
                {
                    Console.WriteLine("Chat History:");
                    foreach (var msg in Program.history)
                    {
                        Console.WriteLine($"{msg.Role}: {msg.Content}");
                    }
                }
            },
            new Command
            {
                Name = "model", Description = "List and select available models",
                Action = async () =>
                {
                    Console.WriteLine($"Current model: {Program.config.Model}");
                    var selected = await Program.SelectModelAsync();
                    if (selected != null)
                    {
                        Program.config.Model = selected;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Console.WriteLine($"Switched to model '{selected}'");
                    }
                }
            },
            new Command
            {
                Name = "host", Description = "Change Ollama host",
                Action = async () =>
                {
                    Console.Write("Enter new Ollama host: ");
                    var hostInput = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(hostInput))
                    {
                        Program.config.Host = hostInput.Trim();
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Console.WriteLine($"Switched to host '{Program.config.Host}'");
                    }
                }
            },
            new Command
            {
                Name = "exit", Description = "Quit the application",
                Action = () => { Environment.Exit(0); return Task.CompletedTask; }
            },
            new Command
            {
                Name = "?", Description = "Show this help message",
                Action = () => { Program.commandManager.ShowHelp(); return Task.CompletedTask; }
            },
            new Command
            {
                Name = "help", Description = "Show this help message",
                Action = () => { Program.commandManager.ShowHelp(); return Task.CompletedTask; }
            }
        });

        return commands; 
    }
}
