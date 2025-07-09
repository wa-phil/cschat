using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager
{
    private static Command CreateRagCommands()
    {
        return new Command
        {
            Name = "rag", Description = "Retrieval-Augmented Generation commands",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "file", Description = "Add a file to the RAG store",
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
                    Name = "directory", Description = "Add a directory to the RAG store",
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
                    Name = "clear", Description = "Clear all RAG data",
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

                            // Use the RagSearchTool to search the vector database
                            var ragTool = new RagSearchTool();
                            var results = await ragTool.SearchVectorDB(query);
                            
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
                            // Use the RagSearchTool to generate the RAG query
                            var ragTool = new RagSearchTool();
                            var response = await ragTool.GetRagQueryAsync(query);
                            Console.WriteLine($"RAG Query Response: \"{response}\"");
                        }
                        return Command.Result.Success;
                    }
                },
                CreateRagConfigCommands()
            }
        };
    }
}
