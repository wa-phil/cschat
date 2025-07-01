using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Command
{
    public string Name { get; set; } = string.Empty; // Ensure non-nullable property is initialized
    public string Description { get; set; } = string.Empty; // Ensure non-nullable property is initialized
    public Func<Task> Action { get; set; } = () => Task.CompletedTask; // Ensure non-nullable property is initialized
}

public class CommandManager
{
    private readonly List<Command> _commands;

    public CommandManager(IEnumerable<Command>? commands = null) // Allow nullable parameter
    {
        _commands = commands?.ToList() ?? new List<Command>();
    }

    public void Register(Command command)
    {
        _commands.Add(command);
    }

    public Command Find(string input)
    {
        return _commands.FirstOrDefault(c => $"/{c.Name}".Equals(input, StringComparison.OrdinalIgnoreCase)) ?? new Command(); // Handle possible null reference return
    }

    public IEnumerable<Command> GetAll() => _commands;

    public IEnumerable<string> GetCompletions(string input) =>
        _commands.Select(c => $"/{c.Name}").Where(cmd => cmd.StartsWith(input, StringComparison.OrdinalIgnoreCase));

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
                    await Task.CompletedTask; // Simulate asynchronous behavior
                    Program.memory.Clear();
                    Program.memory.AddSystemMessage(Program.config.SystemPrompt);
                    Console.WriteLine("Chat history cleared.");
                }
            },
            new Command
            {
                Name = "history", Description = "Show chat history",
                Action = async () =>
                {
                    await Task.CompletedTask; // Simulate asynchronous behavior
                    Console.WriteLine("Chat History:");
                    foreach (var msg in Program.memory.Messages)
                    {
                        Console.WriteLine($"{msg.Role}: {msg.Content}");
                    }
                }
            },
            new Command
            {
                Name = "provider", Description = "Select the LLM Provider",
                Action = async () =>
                {
                    await Task.CompletedTask; // Simulate asynchronous behavior
                    var providers = Program.Providers.Keys.ToList();
                    var selected = User.RenderMenu(providers, providers.IndexOf(Program.config.Provider));
                    if (!string.IsNullOrWhiteSpace(selected) && !selected.Equals(Program.config.Provider, StringComparison.OrdinalIgnoreCase))
                    {
                        Program.SetProvider(selected);
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Console.WriteLine($"Switched to provider '{Program.config.Provider}'");
                    }
                }
            },
            new Command
            {
                Name = "model", Description = "List and select available models",
                Action = async () =>
                {
                    await Task.CompletedTask; // Simulate asynchronous behavior
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
                    await Task.CompletedTask; // Simulate asynchronous behavior
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
                Name = "system", Description = "Change system prompt",
                Action = async () =>
                {
                    await Task.CompletedTask; // Simulate asynchronous behavior
                    Console.WriteLine($"Current system prompt: {Program.config.SystemPrompt}");
                    Console.Write("Enter new system prompt (or press enter to keep current): ");
                    var promptInput = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(promptInput))
                    {
                        Program.config.SystemPrompt = promptInput.Trim();
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Console.WriteLine("System prompt updated.");
                    }
                }
            },
            new Command
            {
                Name = "temperature", Description = "Set response temperature",
                Action = async () =>
                {
                    await Task.CompletedTask; // Simulate asynchronous behavior
                    Console.Write($"Current temperature: {Program.config.Temperature}. Enter new value (0.0 to 1.0): ");
                    var tempInput = Console.ReadLine();
                    if (float.TryParse(tempInput, out var temp) && temp >= 0.0f && temp <= 1.0f)
                    {
                        Program.config.Temperature = temp;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Console.WriteLine($"Temperature set to {temp}");
                    }
                    else
                    {
                        Console.WriteLine("Invalid temperature value. Must be between 0.0 and 1.0.");
                    }
                }
            },
            new Command
            {
                Name = "max-tokens", Description = "Set maximum tokens for response",
                Action = async () =>
                {
                    await Task.CompletedTask; // Simulate asynchronous behavior
                    Console.Write($"Current max tokens: {Program.config.MaxTokens}. Enter new value (1 to 10000): ");
                    var tokensInput = Console.ReadLine();
                    if (int.TryParse(tokensInput, out var tokens) && tokens >= 1 && tokens <= 32000)
                    {
                        Program.config.MaxTokens = tokens;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Console.WriteLine($"Max tokens set to {tokens}");
                    }
                    else
                    {
                        Console.WriteLine("Invalid max tokens value. Must be between 1 and 32000.");
                    }
                }
            },
            new Command 
            {
                Name = "log-show", Description = "Show the contents of the log",
                Action = async () => 
                {
                    await Task.CompletedTask; // Simulate asynchronous behavior
                    var entries = Log.GetOutput().ToList();
                    Console.WriteLine($"Log Entries [{entries.Count}]:");
                    entries.ToList().ForEach(entry => Console.WriteLine(entry));
                }
            },
            new Command
            {
                Name = "log-clear", Description = "Clear the log entries",
                Action = async () => 
                {
                    await Task.CompletedTask; // Simulate asynchronous behavior
                    Log.ClearOutput();
                    Console.WriteLine("Log cleared.");
                }
            },
            new Command
            {
                Name = "exit", Description = "Quit the application",
                Action = () => { Environment.Exit(0); return Task.CompletedTask; }
            },
            new Command
            {
                Name = "quit", Description = "Quit the application",
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
