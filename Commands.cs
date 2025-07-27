using System;
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
    // Helper method to get input format guidance for a tool
    private static string GetInputFormatGuidance(ITool tool)
    {
        var inputType = tool.InputType;
        
        // For NoInput, no input is needed
        if (inputType == typeof(NoInput))
        {
            return "No input required - just press Enter.";
        }

        // For string input, explain it's plain text
        if (inputType == typeof(string))
        {
            return "Enter plain text (no JSON formatting needed).";
        }

        // Check if the input type has an ExampleText attribute
        var exampleTextAttr = inputType.GetCustomAttribute<ExampleText>();
        if (exampleTextAttr != null)
        {
            return $"Expected format ({inputType.Name}):\n{exampleTextAttr.Text}";
        }
                        
        // For other types, provide basic JSON guidance
        return $"Expected format: JSON object matching {inputType.Name} structure.";
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
            CreateProviderCommands(),
            CreateRagCommands(),
            CreateMcpCommands(),
            CreateToolsCommands(),
            CreateSystemCommands(),
            new Command
            {
                Name = "restart", Description = "Reset chat history, RAG state, logs, and clear the console",
                Action = () =>
                {
                    Program.Context.Clear();
                    Program.Context.AddSystemMessage(Program.config.SystemPrompt);   
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

    private static Command CreateToolsCommands()
    {
        return new Command
        {
            Name = "tools", 
            Description = "Tool commands",
            Action = async () =>
            {
                // Dynamically create subcommands based on current registry state
                var dynamicSubCommands = ToolRegistry.GetRegisteredTools().Select(tool => 
                    new Command
                    {
                        Name = tool.Name,
                        Description = tool.Description,
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
                            var inputGuidance = GetInputFormatGuidance(toolInstance);
                            Console.WriteLine(inputGuidance);
                            Console.WriteLine();
                            Console.Write("Enter input: ");
                            
                            // Get user input
                            var input = User.ReadLineWithHistory();
                            
                            // Parse input based on the tool's expected input type
                            object toolInput;
                            try
                            {
                                toolInput = ParseToolInput(toolInstance, input ?? string.Empty);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error parsing input: {ex.Message}");
                                return Command.Result.Failed;
                            }
                            
                            var tempContext = new Context(string.Empty); // Create temporary Context for command execution
                            var result = await ToolRegistry.InvokeToolAsync(tool.Name, toolInput, tempContext, input ?? string.Empty) ?? string.Empty;
                            Console.WriteLine($"Tool result: {result}");
                            Console.WriteLine($"Tool Context:");
                            User.RenderChatHistory(tempContext.Messages);
                            
                            return Command.Result.Success;
                        }
                    }).ToList();

                // Implement the menu logic directly (similar to DefaultAction but accessible)
                Command.Result result = Command.Result.Cancelled;
                while (Command.Result.Cancelled == result)
                {
                    var selected = User.RenderMenu("tools commands", dynamicSubCommands.Select(c => $"{c.Name} - {c.Description}").ToList());
                    if (string.IsNullOrEmpty(selected))
                    {
                        return Command.Result.Cancelled;
                    }
                    
                    // Strip the description part to get just the command name
                    selected = selected.Split('-')[0].Trim();
                    
                    // Find the command by name
                    var command = dynamicSubCommands.FirstOrDefault(c => c.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
                    if (command == null)
                    {
                        Console.WriteLine($"Command '{selected}' not found.");
                        return Command.Result.Failed;
                    }
                    
                    // Execute the command's action
                    result = await command.Action.Invoke();
                }
                return result;
            }
        };
    }
}