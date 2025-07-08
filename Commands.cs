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
                        Name = "show",
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
                        Name = "clear",
                        Description = "Clear chat history",
                        Action = () =>
                        {
                            Program.memory.Clear();
                            Program.memory.AddSystemMessage(Program.config.SystemPrompt);
                            Console.WriteLine("Chat history cleared.");
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "load", Description = "Load chat history from a file",
                        Action = () =>
                        {
                            Console.Write("Enter file path to load chat history: ");
                            var filePath = Console.ReadLine();
                            if (!string.IsNullOrWhiteSpace(filePath))
                            {
                                try
                                {
                                    Program.memory.Load(filePath);
                                    Console.WriteLine($"Chat history loaded from '{filePath}'.");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to load chat history: {ex.Message}");
                                }
                            }
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "save", Description = "Save chat history to a file",
                        Action = () =>
                        {
                            Console.Write("Enter file path to save chat history: ");
                            var filePath = Console.ReadLine();
                            if (!string.IsNullOrWhiteSpace(filePath))
                            {
                                try
                                {
                                    Program.memory.Save(filePath);
                                    Console.WriteLine($"Chat history saved to '{filePath}'.");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to save chat history: {ex.Message}");
                                }
                            }
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
                    },
                    new Command
                    {
                        Name = "azure auth logging enabled",
                        Description = "Enable or disable verbose Azure logging",
                        Action = () =>
                        {
                            var options = new List<string> { "true", "false" };
                            var currentSetting = Program.config.AzureAuthVerboseLoggingEnabled ? "true" : "false";
                            var selected = User.RenderMenu("Enable Azure Authentication verbose logging:", options, options.IndexOf(currentSetting));

                            if (!string.IsNullOrWhiteSpace(selected) && bool.TryParse(selected, out var result))
                            {
                                Program.config.AzureAuthVerboseLoggingEnabled = result;
                                Console.WriteLine($"Azure Auth verbose logging set to {result}.");
                                Config.Save(Program.config, Program.ConfigFilePath);
                            }
                            else
                            {
                                Console.WriteLine("Invalid selection.");
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
                        Name = "status", Description = "Show RAG store status",
                        Action = () =>
                        {
                            if (Engine.VectorStore.IsEmpty)
                            {
                                Console.WriteLine("RAG store is empty.");
                            }
                            else
                            {
                                Console.WriteLine($"RAG store contains {Engine.VectorStore.Count} entries.");
                            }
                            return Task.FromResult(Command.Result.Success);
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
                    },
                    new Command
                    {
                        Name = "search", Description = "Search RAG store",
                        Action = async () =>
                        {
                            if (Engine.VectorStore.IsEmpty)
                            {
                                Console.WriteLine("RAG store is empty. Please add files or directories first.");
                                return Command.Result.Failed;
                            }

                            Console.Write("Enter search query: ");
                            var query = Console.ReadLine();
                            if (!string.IsNullOrWhiteSpace(query))
                            {
                                var embeddingProvider = Engine.Provider as IEmbeddingProvider;
                                if (embeddingProvider == null)
                                {
                                    Console.WriteLine("Current provider does not support embeddings.");
                                    return Command.Result.Failed;
                                }
                                float[] queryEmbedding = await embeddingProvider.GetEmbeddingAsync(query);
                                if (queryEmbedding.Length == 0)
                                {
                                    Console.WriteLine("Failed to get embedding for test query.");
                                    return Command.Result.Failed;
                                }

                                var results = await Engine.SearchVectorDB(query);
                                if (results.Any())
                                {
                                    Console.WriteLine("Search Results:");
                                    foreach (var result in results)
                                    {
                                        Console.WriteLine($"[{result.Score:F4}] {result.Reference}");
                                    }

                                    var scores = results.Select(x => x.Score).ToList();
                                    float mean = scores.Average();
                                    float stddev = (float)Math.Sqrt(scores.Average(x => Math.Pow(x - mean, 2)));

                                    Console.WriteLine($"\nEmbedding dimensions: {queryEmbedding.Length}");
                                    Console.WriteLine($"Total entries: {Engine.VectorStore.Count}");
                                    Console.WriteLine($"Average similarity score: {mean:F4}");
                                    Console.WriteLine($"Standard deviation: {stddev:F4}");
                                }
                                else
                                {
                                    Console.WriteLine("No results found.");
                                }
                            }
                            return Command.Result.Success;
                        }
                    },
                    new Command {
                        Name = "query", Description = "Generate a RAG query",
                        Action = async () =>
                        {
                            if (Engine.VectorStore.IsEmpty)
                            {
                                Console.WriteLine("RAG store is empty. Please add files or directories first.");
                                return Command.Result.Failed;
                            }

                            Console.Write("Enter query: ");
                            var query = Console.ReadLine();
                            if (!string.IsNullOrWhiteSpace(query))
                            {
                                var response = await Engine.GetRagQueryAsync(query);
                                Console.WriteLine($"RAG Query Response: \"{response}\"");
                            }
                            return Command.Result.Success;
                        }
                    },
                    new Command{
                        Name = "config", Description = "Configure RAG settings",
                        SubCommands = new List<Command>
                        {
                            new Command
                            {
                                Name = "embedding model", Description = "Set the embedding model for RAG",
                                Action = () =>
                                {
                                    Console.WriteLine($"Current embedding model: {Program.config.RagSettings.EmbeddingModel}");
                                    Console.Write("Enter new embedding model (or press enter to keep current): ");
                                    var modelInput = Console.ReadLine();
                                    if (!string.IsNullOrWhiteSpace(modelInput))
                                    {
                                        Program.config.RagSettings.EmbeddingModel = modelInput.Trim();
                                        Config.Save(Program.config, Program.ConfigFilePath);
                                        Console.WriteLine("Embedding model updated.");
                                    }
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "query", Description = "alter the prompt used to generate RAG queries",
                                Action = () =>
                                {
                                    Console.WriteLine($"Current RAG query prompt: {Program.config.RagSettings.QueryPrompt}");
                                    Console.Write("Enter new RAG query prompt (or press enter to keep current): ");
                                    var promptInput = Console.ReadLine();
                                    if (!string.IsNullOrWhiteSpace(promptInput))
                                    {
                                        Program.config.RagSettings.QueryPrompt = promptInput.Trim();
                                        Config.Save(Program.config, Program.ConfigFilePath);
                                        Console.WriteLine("RAG query prompt updated.");
                                    }
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "chunking method", Description = "Select the text chunker for RAG",
                                Action = () =>
                                {
                                    var chunkers = Program.Chunkers.Keys.ToList();
                                    var selected = User.RenderMenu("Select a text chunker:", chunkers, chunkers.IndexOf(Program.config.RagSettings.ChunkingStrategy));
                                    if (!string.IsNullOrWhiteSpace(selected) && !selected.Equals(Program.config.RagSettings.ChunkingStrategy, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Program.config.RagSettings.ChunkingStrategy = selected;
                                        Config.Save(Program.config, Program.ConfigFilePath);
                                        Console.WriteLine($"Switched to chunker '{Program.config.RagSettings.ChunkingStrategy}'");
                                    }
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "chunksize", Description = "Set the chunk size for RAG",
                                Action = () =>
                                {
                                    Console.Write($"Current chunk size: {Program.config.RagSettings.ChunkSize}. Enter new value (1 to 10000): ");
                                    var sizeInput = Console.ReadLine();
                                    if (int.TryParse(sizeInput, out var size) && size >= 1 && size <= 10000)
                                    {
                                        Program.config.RagSettings.ChunkSize = size;
                                        Config.Save(Program.config, Program.ConfigFilePath);
                                        Console.WriteLine($"Chunk size set to {size}");
                                    }
                                    else
                                    {
                                        Console.WriteLine("Invalid chunk size value. Must be between 1 and 10000.");
                                    }
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "overlap", Description = "Set the overlap size for RAG chunks",
                                Action = () =>
                                {
                                    Console.Write($"Current overlap size: {Program.config.RagSettings.Overlap}. Enter new value (0 to 100): ");
                                    var overlapInput = Console.ReadLine();
                                    if (int.TryParse(overlapInput, out var overlap) && overlap >= 0 && overlap <= 100)
                                    {
                                        Program.config.RagSettings.Overlap = overlap;
                                        Config.Save(Program.config, Program.ConfigFilePath);
                                        Console.WriteLine($"Overlap size set to {overlap}");
                                    }
                                    else
                                    {
                                        Console.WriteLine("Invalid overlap size value. Must be between 0 and 100.");
                                    }
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "TopK", Description = "Set the number of top results to return from RAG queries",
                                Action = () =>
                                {
                                    const int maxK = 10;
                                    Console.Write($"Current TopK value: {Program.config.RagSettings.TopK}. Enter new value (1 to {maxK}): ");
                                    var topKInput = Console.ReadLine();
                                    if (int.TryParse(topKInput, out var topK) && topK >= 1 && topK <= maxK)
                                    {
                                        Program.config.RagSettings.TopK = topK;
                                        Config.Save(Program.config, Program.ConfigFilePath);
                                        Console.WriteLine($"TopK set to {topK}");
                                    }
                                    else
                                    {
                                        Console.WriteLine("Invalid TopK value. Must be between 1 and {maxK}.");
                                    }
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "embedding threshold", Description = "Set the minimum similarity score for RAG results",
                                Action = () =>
                                {
                                    Console.Write($"Current embedding threshold: {Program.config.RagSettings.EmbeddingThreshold}. Enter new value (0.0 to 1.0): ");
                                    var thresholdInput = Console.ReadLine();
                                    if (float.TryParse(thresholdInput, out var threshold) && threshold >= 0.0f && threshold <= 1.0f)
                                    {
                                        Program.config.RagSettings.EmbeddingThreshold = threshold;
                                        Config.Save(Program.config, Program.ConfigFilePath);
                                        Console.WriteLine($"Embedding threshold set to {threshold}");
                                    }
                                    else
                                    {
                                        Console.WriteLine("Invalid embedding threshold value. Must be between 0.0 and 1.0.");
                                    }
                                    return Task.FromResult(Command.Result.Success);
                                }
                            }
                        }
                    },
                }
            },
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
                            var input = Console.ReadLine();
                            var result = await ToolRegistry.InvokeToolAsync(tool.Name, input ?? string.Empty) ?? string.Empty;
                            Console.WriteLine($"Tool result: {result}");
                            return Command.Result.Success;
                        }
                    }).ToList()
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
                            },
                            new Command
                            {
                                Name = "save", Description = "Save the contents of the log to a file",
                                Action = () =>
                                {
                                    Console.Write("Enter file path to save the log: ");
                                    var filePath = Console.ReadLine();
                                    if (!string.IsNullOrWhiteSpace(filePath))
                                    {
                                        try
                                        {
                                            var logEntries = Log.GetOutput();
                                            System.IO.File.WriteAllLines(filePath, logEntries);
                                            Console.WriteLine($"Log saved to '{filePath}'.");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Failed to save log: {ex.Message}");
                                        }
                                    }
                                    return Task.FromResult(Command.Result.Success);
                                }
                            }
                        }
                    },
                    new Command {
                        Name = "config", Description = "Configuration commands",
                        SubCommands = new List<Command>
                        {
                            new Command
                            {
                                Name = "show", Description = "Show current system configuration",
                                Action = () =>
                                {
                                    Console.WriteLine("Current Configuration:");
                                    Console.WriteLine(Program.config.ToJson());
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "save", Description = "Save the current configuration",
                                Action = () =>
                                {
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Console.WriteLine("Configuration saved.");
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "factory reset", Description = "Delete the current configuration and reset everything to defaults",
                                Action = () =>
                                {
                                    File.Delete(Program.ConfigFilePath);
                                    Program.config = new Config(); // Reset to default config
                                    Program.InitProgram();
                                    Console.WriteLine("Configuration reset to default.");
                                    return Task.FromResult(Command.Result.Success);
                                }
                            }
                        }
                    }
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