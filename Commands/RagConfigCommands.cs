using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager
{
    private static Command CreateRagConfigCommands()
    {
        return new Command
        {
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
                }
            }
        };
    }
}
