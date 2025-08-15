using System;
using System.Text;
using System.Linq;
using System.Reflection;
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

    public string GetFullPath()
    {
        var path = new Stack<string>();
        var current = this;
        while (current != null && !string.IsNullOrWhiteSpace(current.Name))
        {
            path.Push(current.Name);
            current = current.Parent;
        }
        return string.Join(">", path);
    }

    public string Name { get; set; } = string.Empty; // Ensure non-nullable property is initialized
    public Func<string> Description { get; set; } = () => string.Empty; // Ensure non-nullable property is initialized

    private async Task<Result> DefaultAction()
    {
        Result result = Result.Cancelled;

        while (Result.Cancelled == result)
        {
            // render the menu of subcommands, the text passed in should be a concatenation of the subcommand names with their descriptions,
            // formatted such that the descriptions are aligned with the command names
            var selected = User.RenderMenu($"{GetFullPath()} commands", SubCommands.Select(c => $"{c.Name} - {c.Description()}").ToList());
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

    public Command? Parent { get; set; }

    public Func<Task<Command.Result>> Action { get; set; }

    private List<Command> _subCommands = new();

    public List<Command> SubCommands
    {
        get => _subCommands;
        set
        {
            _subCommands = value;
            foreach (var sub in _subCommands)
            {
                sub.Parent = this;
            }
        }
    }

}

public partial class CommandManager : Command
{
    // Helper method to get input format guidance for a tool
    private static (string guidance, bool required) GetInputFormatGuidance(ITool tool)
    {
        var inputType = tool.InputType;

        // For NoInput, no input is needed
        if (inputType == typeof(NoInput))
        {
            return ("No input required - just press Enter.", false);
        }

        // For string input, explain it's plain text
        if (inputType == typeof(string))
        {
            return ("Enter plain text (no JSON formatting needed).", true);
        }

        // Check if the input type has an ExampleText attribute
        var exampleTextAttr = inputType.GetCustomAttribute<ExampleText>();
        if (exampleTextAttr != null)
        {
            return ($"Expected format ({inputType.Name}):\n{exampleTextAttr.Text}", true);
        }

        // For other types, provide basic JSON guidance
        return ($"Expected format: JSON object matching {inputType.Name} structure.", true);
    }

    // Helper method to parse tool input based on the tool's expected input type
    private static object ParseToolInput(ITool tool, string input)
    {
        // If the tool doesn't need input, return a default instance
        if (tool.InputType == typeof(NoInput))
        {
            return new NoInput();
        }
        
        // If the tool expects a string directly, return the string
        if (tool.InputType == typeof(string))
        {
            return input;
        }
        
        // For all other types, try to parse as JSON
        try
        {
            // If input is empty or whitespace for non-string types, try to create a default instance
            if (string.IsNullOrWhiteSpace(input))
            {
                return Activator.CreateInstance(tool.InputType) ?? new NoInput();
            }
            
            var parsedInput = JSONParser.ParseValue(tool.InputType, input);
            
            if (parsedInput == null)
            {
                throw new ArgumentException($"Failed to parse input for tool {tool.GetType().Name}");
            }
            return parsedInput;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing tool input: {ex.Message}");
            Console.WriteLine($"Expected type: {tool.InputType.Name}");
            Console.WriteLine($"Tool usage: {tool.Usage}");
            
            // Return a default instance if parsing fails
            return Activator.CreateInstance(tool.InputType) ?? new NoInput();
        }
    }

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
            CreateRagCommands(),            
            CreateSystemCommands(),
            new Command
            {
                Name = "restart", Description = () => "Reset chat history, RAG state, logs, and clear the console",
                Action = async () =>
                {
                    Console.ResetColor();
                    Console.Clear();
                    Program.Context.Clear();
                    Program.Context.AddSystemMessage(Program.config.SystemPrompt);
                    Engine.VectorStore.Clear();
                    GraphStoreManager.Graph.Clear();
                    Log.ClearOutput();
                    await Program.InitProgramAsync();
                    Console.WriteLine("Chat history, RAG state, and logs have been reset.");
                    Console.WriteLine("Current Configuration:");
                    Console.WriteLine(Program.config.ToJson());
                    return Command.Result.Success;
                }
            },
            new Command
            {
                Name = "help",
                Description = () => "Explain the program and generate summary of available commands",
                Action = async () =>
                {
                    Console.WriteLine("Menu structure and layout:");
                    var menuTree = Engine.BuildCommandTreeArt(Program.commandManager.SubCommands);
                    var summary = await ToolRegistry.InvokeToolAsync("summarize_text", new SummarizeText {
                        Prompt = "Confidently summarize and explain the cschat program's command and layout to a user.",
                        Text = menuTree });
                    if (string.IsNullOrEmpty(summary))
                    {
                        Console.WriteLine("Failed to generate summary.");
                        return Command.Result.Failed;
                    }
                    Console.WriteLine("Generated helptext:");
                    Console.WriteLine(summary);
                    return Command.Result.Success;
                }
            },
            new Command
            {
                Name = "exit", Description = () => "Quit the application",
                Action = () => { Environment.Exit(0); return Task.FromResult(Command.Result.Success); }
            }
        });

        return commands;
    }

    private static Command CreateToolsCommands()
    {
        var toolSubcommands = ToolRegistry.GetRegisteredTools().Select(tool =>
            new Command
            {
                Name = tool.Name,
                Description = () => tool.Description,
                Action = async () =>
                {
                    Console.WriteLine($"Using tool: {tool.Name}");
                    Console.WriteLine($"Description: {tool.Description}");
                    Console.WriteLine($"Usage: {tool.Usage}");
                    Console.WriteLine();

                    var toolInstance = ToolRegistry.GetTool(tool.Name);
                    if (toolInstance == null)
                    {
                        Console.WriteLine($"Tool '{tool.Name}' not found.");
                        return Command.Result.Failed;
                    }

                    // Show input format guidance
                    object toolInput = new NoInput(); // Default to NoInput
                    string? input = null;
                    var (guidance, isRequired) = GetInputFormatGuidance(toolInstance);
                    if (isRequired)
                    {
                        Console.WriteLine(guidance);
                        Console.WriteLine();
                        Console.Write("Enter input: ");
                                                            
                        // Get user input
                        input = User.ReadLineWithHistory();
                        try
                        {
                            // Parse input based on the tool's expected input type
                            toolInput = ParseToolInput(toolInstance, input ?? string.Empty);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing input: {ex.Message}");
                            return Command.Result.Failed;
                        }
                    }

                    var tempContext = new Context(string.Empty); // Create temporary Context for command execution
                    var result = await ToolRegistry.InvokeToolAsync(tool.Name, toolInput, tempContext) ?? string.Empty;
                    Console.WriteLine($"Tool result: {result}");
                    Console.WriteLine("Tool Context:");
                    User.RenderChatHistory(tempContext.Messages());
                    return Command.Result.Success;
                }
            }).ToList();

        return new Command
        {
            Name = "tools",
            Description = () => "Invoke/run tools",
            SubCommands = toolSubcommands,
            Action = async () =>
            {
                return await new Command
                {
                    Name = "tools",
                    Description = () => "Invoke/run tools",
                    SubCommands = toolSubcommands
                }.Action.Invoke();
            }
        };
    }
}