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
                    Name = "fileForGraph", Description = "Add a file to the Graph RAG store",
                    Action = async () =>
                    {
                        Console.Write("Enter graph file path: ");
                        var input = await User.ReadPathWithAutocompleteAsync(isDirectory: false);
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            await Engine.AddFileToGraphStore(input);
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
                    Name = "dump", Description = "display a range of entries from the RAG store",
                    Action = () =>
                    {
                        if (Engine.VectorStore.IsEmpty)
                        {
                            Console.WriteLine("RAG store is empty. Please add files or directories first.");
                            return Task.FromResult(Command.Result.Failed);
                        }
                        Console.WriteLine($"Total entries: {Engine.VectorStore.Count}");
                        int startIndex = 0, endIndex = Engine.VectorStore.Count - 1;
                        if (Engine.VectorStore.Count > 10)
                        {
                            Console.WriteLine("Enter the range of entries to display:");
                            Console.Write("Enter start index: ");
                            startIndex = int.Parse(Console.ReadLine() ?? "0");
                            if (startIndex < 0 || startIndex >= Engine.VectorStore.Count)
                            {
                                Console.WriteLine("Invalid start index.");
                                return Task.FromResult(Command.Result.Failed);
                            }
                            Console.Write("Enter end index: ");
                            endIndex = int.Parse(Console.ReadLine() ?? "0");
                            if (endIndex < 0 || endIndex >= Engine.VectorStore.Count)
                            {
                                Console.WriteLine("Invalid end index.");
                                return Task.FromResult(Command.Result.Failed);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Displaying all entries in the RAG store:");
                            startIndex = 0;
                            endIndex = Engine.VectorStore.Count - 1;
                        }

                        var entries = Engine.VectorStore.GetEntries(start: startIndex, count: endIndex - startIndex + 1);
                        foreach (var entry in entries)
                        {
                            Console.WriteLine($"--- start {entry.Reference} ---\n{entry.Content}\n--- end {entry.Reference} ---");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "walkGraph", Description = "do a n-hop node walk on the Knowledge Graph",
                    Action = () =>
                    {
                        Console.Write("Enter search query: ");
                        var query = Console.ReadLine();
                        
                        Console.WriteLine("Enter the number of hops to walk (default is 2): ");
                        var hopsInput = Console.ReadLine();

                        int hops = 2;
                        if (int.TryParse(hopsInput, out int parsedHops))
                        {
                            hops = parsedHops;
                        }
                        GraphStoreManager.Graph.PrintEntitiesWithinHops(query ?? string.Empty, hops);
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "dumpGraph", Description = "display a range of entries from the Graph RAG store",
                    Action = () =>
                    {
                        GraphStoreManager.Graph.PrintGraph();
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
                    Name = "search", Description = "Search RAG store based on a query",
                    Action = async () =>
                    {
                        if (Engine.VectorStore.IsEmpty)
                        {
                            Console.WriteLine("RAG store is empty. Please add files or directories first.");
                            return Command.Result.Failed;
                        }

                        Console.Write("Enter search query: ");
                        var query = User.ReadLineWithHistory();
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

                            var results = await ContextManager.SearchVectorDB(query);
                            
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
                CreateRagConfigCommands()
            }
        };
    }
}
