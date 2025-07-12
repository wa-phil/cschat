using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Command
{
    public enum Result
    {
        Success,
        Failed,
        Cancelled
    }
    public string Name { get; set; } = string.Empty; // Ensure non-nullable property is initialized
    public string Description { get; set; } = string.Empty; // Ensure non-nullable property is initialized

    private async Task<Result> DefaultAction()
    {
        Result result = Result.Cancelled;

        while (Result.Cancelled == result)
        {
            // render the menu of subcommands, the text passed in should be a concatenation of the subcommand names with their descriptions,
            // formatted such that the descriptions are aligned with the command names
            var selected = User.RenderMenu($"{Name} commands", SubCommands.Select(c => $"{c.Name} - {c.Description}").ToList());
            if (string.IsNullOrEmpty(selected))
            {
                return Result.Cancelled;
            }
            // strip the description part to get just the command name
            selected = selected.Split('-')[0].Trim();
            // find the command by name or alias
            var command = SubCommands.FirstOrDefault(c => c.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
            if (command == null)
            {
                Console.WriteLine($"Command '{selected}' not found.");
                return Result.Failed;
            }
            // Execute the command's action
            result = await command.Action.Invoke();
        }
        return result;
    }

    public Command()
    {
        Action = DefaultAction; // Set the default action to the method defined above
    }

    public Func<Task<Command.Result>> Action { get; set; }

    public List<Command> SubCommands { get; set; } = new List<Command>();
}

public partial class CommandManager : Command
{
    public new string Name { get; set; } = "Menu";
    public new string Description { get; set; } = "Commands";
    public CommandManager(IEnumerable<Command>? commands = null) // Allow nullable parameter
    {
        SubCommands = commands?.ToList() ?? new List<Command>();
    }

    public static CommandManager CreateDefaultCommands()
    {
        var commands = new CommandManager(new[]
        {
            CreateChatCommands(),
            CreateProviderCommands(),
            CreateRagCommands(),
            new Command
            {
                Name = "tools", Description = "Tool commands",
                SubCommands = ToolRegistry.GetRegisteredTools().Select(tool => 
                    new Command
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        Action = async () =>
                        {
                            Console.Write($"Using tool: {tool.Name}.\n{tool.Usage}\n Enter input: ");
                            // Tools may not require input, so we should handle empty input gracefully
                            var input = User.ReadLineWithHistory();
                            var tempMemory = new Memory(string.Empty); // Create temporary memory for command execution
                            var result = await ToolRegistry.InvokeToolAsync(tool.Name, input ?? string.Empty, tempMemory, input ?? string.Empty) ?? string.Empty;
                            Console.WriteLine($"Tool result: {result}");
                            Console.WriteLine($"Tool Memory:");
                            User.RenderChatHistory(tempMemory.Messages);
                            
                            return Command.Result.Success;
                        }
                    }).ToList()
            },
            CreateSystemCommands(),
            new Command
            {
                Name = "restart", Description = "Reset chat history, RAG state, logs, and clear the console",
                Action = () =>
                {
                    Program.memory.Clear();
                    Program.memory.AddSystemMessage(Program.config.SystemPrompt);   
                    Engine.VectorStore.Clear();
                    Log.ClearOutput();
                    Console.Clear();
                    Console.WriteLine("Chat history, RAG state, and logs have been reset.");
                    Console.WriteLine("Current Configuration:");
                    Console.WriteLine(Program.config.ToJson());
                    return Task.FromResult(Command.Result.Success);
                }
            },
            new Command
            {
                Name = "exit", Description = "Quit the application",
                Action = () => { Environment.Exit(0); return Task.FromResult(Command.Result.Success); }
            }
        });

        return commands; 
    }
}