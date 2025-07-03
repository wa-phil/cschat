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

public class CommandManager : Command
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
            new Command
            {
                Name = "chat", Description = "chat-related commands",
                SubCommands = new List<Command>
                {
                    new Command
                    {
                        Name = "show history",
                        Description = "Show chat history",
                        Action = () =>
                        {
                            Console.WriteLine("Chat History:");
                            foreach (var msg in Program.memory.Messages)
                            {
                                Console.WriteLine($"{msg.Role}: {msg.Content}");
                            }
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "clear history",
                        Description = "Clear chat history",
                        Action = () =>
                        {
                            Program.memory.Clear();
                            Program.memory.AddSystemMessage(Program.config.SystemPrompt);
                            Console.WriteLine("Chat history cleared.");
                            return Task.FromResult(Command.Result.Success);
                        }
                    }                    
                }
            },
            new Command
            {
                Name = "provider", Description = "Provider-related commands",
                SubCommands = new List<Command>
                {
                    new Command
                    {
                        Name = "select", Description = "Select the LLM Provider",
                        Action = () =>
                        {
                            var providers = Program.Providers.Keys.ToList();
                            var selected = User.RenderMenu("Select a provider:", providers, providers.IndexOf(Program.config.Provider));
                            if (!string.IsNullOrWhiteSpace(selected) && !selected.Equals(Program.config.Provider, StringComparison.OrdinalIgnoreCase))
                            {
                                Engine.SetProvider(selected);
                                Config.Save(Program.config, Program.ConfigFilePath);
                                Console.WriteLine($"Switched to provider '{Program.config.Provider}'");
                            }
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "model", Description = "List and select available models",
                        Action = async () =>
                        {
                            Console.WriteLine($"Current model: {Program.config.Model}");
                            var selected = await Engine.SelectModelAsync();
                            if (selected != null)
                            {
                                Program.config.Model = selected;
                                Config.Save(Program.config, Program.ConfigFilePath);
                                Console.WriteLine($"Switched to model '{selected}'");
                            }
                            return Command.Result.Success;
                        }
                    },
                    new Command
                    {
                        Name = "host", Description = "Change Ollama host",
                        Action = () =>
                        {
                            Console.Write("Enter new Ollama host: ");
                            var hostInput = Console.ReadLine();
                            if (!string.IsNullOrWhiteSpace(hostInput))
                            {
                                Program.config.Host = hostInput.Trim();
                                Config.Save(Program.config, Program.ConfigFilePath);
                                Console.WriteLine($"Switched to host '{Program.config.Host}'");
                            }
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "system", Description = "Change system prompt",
                        Action = () =>
                        {
                            Console.WriteLine($"Current system prompt: {Program.config.SystemPrompt}");
                            Console.Write("Enter new system prompt (or press enter to keep current): ");
                            var promptInput = Console.ReadLine();
                            if (!string.IsNullOrWhiteSpace(promptInput))
                            {
                                Program.config.SystemPrompt = promptInput.Trim();
                                Config.Save(Program.config, Program.ConfigFilePath);
                                Console.WriteLine("System prompt updated.");
                            }
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "temp", Description = "Set response temperature",
                        Action = () =>
                        {
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
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "max-tokens", Description = "Set maximum tokens for response",
                        Action = () =>
                        {
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
                            return Task.FromResult(Command.Result.Success);
                        }
                    }
                }
            },
            new Command
            {
                Name = "rag", Description = "Retrieval-Augmented Generation commands",
                SubCommands = new List<Command>
                {
                    new Command
                    {
                        Name = "file",
                        Description = "Add a file to the RAG store",
                        Action = async () =>
                        {
                            Console.Write("Enter file path: ");
                            var input = await User.ReadPathWithAutocompleteAsync(isDirectory: false);
                            if (!string.IsNullOrWhiteSpace(input))
                            {
                                await Engine.AddFileToVectorStore(input);
                                Console.WriteLine($"Added file '{input}' to RAG.");
                            }
                            return Command.Result.Success;
                        }
                    },
                    new Command
                    {
                        Name = "directory",
                        Description = "Add a directory to the RAG store",
                        Action = async () =>
                        {
                            Console.Write("Enter directory path: ");
                            var input = await User.ReadPathWithAutocompleteAsync(isDirectory: true);
                            if (!string.IsNullOrWhiteSpace(input))
                            {
                                await Engine.AddDirectoryToVectorStore(input);
                                Console.WriteLine($"Added directory '{input}' to RAG.");
                            }
                            return Command.Result.Success;
                        }
                    },
                    new Command
                    {
                        Name = "clear",
                        Description = "Clear all RAG data",
                        Action = () =>
                        {
                            Engine.VectorStore.Clear();
                            Console.WriteLine("RAG store cleared.");
                            return Task.FromResult(Command.Result.Success);
                        }
                    }
                }
            },
            new Command
            {
                Name = "system", Description = "System commands",
                SubCommands = new List<Command>
                {
                    new Command
                    {
                        Name = "log", Description = "Logging commands",
                        SubCommands = new List<Command>
                        {
                            new Command
                            {
                                Name = "show", Description = "Show the contents of the log",
                                Action = () =>
                                {
                                    var entries = Log.GetOutput().ToList();
                                    Console.WriteLine($"Log Entries [{entries.Count}]:");
                                    entries.ToList().ForEach(entry => Console.WriteLine(entry));
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "clear", Description = "Clear the log entries",
                                Action = () =>
                                {
                                    Log.ClearOutput();
                                    Console.WriteLine("Log cleared.");
                                    return Task.FromResult(Command.Result.Success);
                                }
                            }
                        }
                    },
                    new Command
                    {
                        Name = "exit", Description = "Quit the application",
                        Action = () => { Environment.Exit(0); return Task.FromResult(Command.Result.Success); }
                    }                 
                }
            }
        });

        return commands; 
    }
}
