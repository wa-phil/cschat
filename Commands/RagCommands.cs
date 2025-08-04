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
            Name = "rag", Description = () => "Retrieval-Augmented Generation commands",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "add file", Description = () => "Add a single file's contents to the RAG store",
                    Action = async () =>
                    {
                        Console.Write("Enter file path: ");
                        var input = await User.ReadPathWithAutocompleteAsync(isDirectory: false);
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            if (!File.Exists(input))
                            {
                                Console.WriteLine($"File '{input}' does not exist.");
                                return Command.Result.Failed;
                            }
                            
                            await Engine.AddFileToVectorStore(input);
                            Console.WriteLine($"Added file '{input}' to RAG.");
                        }
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "add directory", Description = () => "Add a directory to the RAG store",
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
                    Name = "add zip contents", Description = () => "Add contents of a zip file to the RAG store",
                    Action = async () =>
                    {
                        Console.Write("Enter zip file path: ");
                        var input = await User.ReadPathWithAutocompleteAsync(isDirectory: false);
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            await Engine.AddZipFileToVectorStore(input);
                            Console.WriteLine($"Added contents of zip file '{input}' to RAG.");
                        }
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "display", Description = () => "Display an entry from the RAG store",
                    Action = () =>
                    {
                        if (Engine.VectorStore.IsEmpty)
                        {
                            Console.WriteLine("RAG store is empty. Please add files or directories first.");
                            return Task.FromResult(Command.Result.Success);
                        }

                        Console.WriteLine("Select an entry to display:");
                        var entries = Engine.VectorStore.GetEntries();
                        var choices = entries.Select((entry, index) => $"{index}: {entry.Reference}").ToList();

                        var selected = User.RenderMenu($"Select one of {Engine.VectorStore.Count} RAG Store Entries", choices);
                        if (selected == null)
                        {
                            Console.WriteLine("No entry selected.");
                            return Task.FromResult(Command.Result.Cancelled);
                        }

                        int selectedIndex = int.Parse(selected.Split(':')[0]);
                        if (selectedIndex < 0 || selectedIndex >= entries.Count)
                        {
                            Console.WriteLine("Invalid selection.");
                            return Task.FromResult(Command.Result.Failed);
                        }

                        var entry = entries[selectedIndex];
                        Console.WriteLine($"--- start {entry.Reference} ---\n{entry.Content}\n--- end {entry.Reference} ---");

                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "clear", Description = () => "Clear all RAG data",
                    Action = () =>
                    {
                        Engine.VectorStore.Clear();
                        Console.WriteLine("RAG store cleared.");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "search", Description = () =>"Search RAG store based on a query",
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
                }
            }
        };
    }
}
