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
                        Program.ui.Write("Enter file path: ");
                        var input = await Program.ui.ReadPathWithAutocompleteAsync(isDirectory: false);
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            if (!File.Exists(input))
                            {
                                Program.ui.WriteLine($"File '{input}' does not exist.");
                                return Command.Result.Failed;
                            }
                            
                            await Engine.AddFileToVectorStore(input);
                            Program.ui.WriteLine($"Added file '{input}' to RAG.");
                        }
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "Graph Add File", Description = () => "Add a file to the Graph RAG store",
                    Action = async () =>
                    {
                        Program.ui.Write("Enter graph file path: ");
                        var input = await Program.ui.ReadPathWithAutocompleteAsync(isDirectory: false);
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            await Engine.AddFileToGraphStore(input);
                            Program.ui.WriteLine($"Added file '{input}' to RAG.");
                        }
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "add directory", Description = () => "Add a directory to the RAG store",
                    Action = async () =>
                    {
                        Program.ui.Write("Enter directory path: ");
                        var input = await Program.ui.ReadPathWithAutocompleteAsync(isDirectory: true);
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            await Engine.AddDirectoryToVectorStore(input);
                            Program.ui.WriteLine($"Added directory '{input}' to RAG.");
                        }
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "Graph Add Directory", Description = () => "Add a directory to the Graph RAG store",
                    Action = async () =>
                    {
                        Program.ui.Write("Enter directory path: ");
                        var input = await Program.ui.ReadPathWithAutocompleteAsync(isDirectory: true);
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            await Engine.AddDirectoryToGraphStore(input);
                            Program.ui.WriteLine($"Added directory '{input}' to RAG.");
                        }
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "add zip contents", Description = () => "Add contents of a zip file to the RAG store",
                    Action = async () =>
                    {
                        Program.ui.Write("Enter zip file path: ");
                        var input = await Program.ui.ReadPathWithAutocompleteAsync(isDirectory: false);
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            await Engine.AddZipFileToVectorStore(input);
                            Program.ui.WriteLine($"Added contents of zip file '{input}' to RAG.");
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
                            Program.ui.WriteLine("RAG store is empty. Please add files or directories first.");
                            return Task.FromResult(Command.Result.Success);
                        }

                        Program.ui.WriteLine("Select an entry to display:");
                        var entries = Engine.VectorStore.GetEntries();
                        var choices = entries.Select((entry, index) => $"{index}: {entry.Reference}").ToList();

                        var selected = Program.ui.RenderMenu($"Select one of {Engine.VectorStore.Count} RAG Store Entries", choices);
                        if (selected == null)
                        {
                            Program.ui.WriteLine("No entry selected.");
                            return Task.FromResult(Command.Result.Cancelled);
                        }

                        int selectedIndex = int.Parse(selected.Split(':')[0]);
                        if (selectedIndex < 0 || selectedIndex >= entries.Count)
                        {
                            Program.ui.WriteLine("Invalid selection.");
                            return Task.FromResult(Command.Result.Failed);
                        }

                        var entry = entries[selectedIndex];
                        Program.ui.WriteLine($"--- start {entry.Reference} ---\n{entry.Content}\n--- end {entry.Reference} ---");

                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "Graph Walk", Description = () => "do a n-hop node walk on the Knowledge Graph",
                    Action = () =>
                    {
                        Program.ui.Write("Enter search query: ");
                        var query = Program.ui.ReadLine();
                        
                        Program.ui.WriteLine("Enter the number of hops to walk (default is 2): ");
                        var hopsInput = Program.ui.ReadLine();

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
                    Name = "Graph Dump", Description = () => "display a range of entries from the Graph RAG store",
                    Action = () =>
                    {
                        GraphStoreManager.Graph.PrintGraph();
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "Graph Community Info", Description = () => "Show detailed information about graph communities",
                    Action = () =>
                    {
                        if (GraphStoreManager.Graph.IsEmpty)
                        {
                            Program.ui.WriteLine("Graph store is empty. Please add graph data first using 'rag fileForGraph'.");
                            return Task.FromResult(Command.Result.Failed);
                        }

                        Program.ui.Write("Enter community ID (leave blank for all communities): ");
                        var input = Program.ui.ReadLine();
                        
                        if (string.IsNullOrWhiteSpace(input))
                        {
                            GraphStoreManager.Graph.PrintDetailedCommunityInfo();
                        }
                        else if (int.TryParse(input, out int communityId))
                        {
                            GraphStoreManager.Graph.PrintDetailedCommunityInfo(communityId);
                        }
                        else
                        {
                            Program.ui.WriteLine("Invalid community ID. Please enter a number or leave blank for all communities.");
                            return Task.FromResult(Command.Result.Failed);
                        }
                        
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "Graph Community Summary", Description = () => "Show summary table of all graph communities",
                    Action = () =>
                    {
                        if (GraphStoreManager.Graph.IsEmpty)
                        {
                            Program.ui.WriteLine("Graph store is empty. Please add graph data first using 'rag fileForGraph'.");
                            return Task.FromResult(Command.Result.Failed);
                        }
                        
                        GraphStoreManager.Graph.PrintCommunitySummaryTable();
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "Graph Community Detection", Description = () => "Detect communities in the graph using Louvain algorithm",
                    Action = () =>
                    {
                        if (GraphStoreManager.Graph.IsEmpty)
                        {
                            Program.ui.WriteLine("Graph store is empty. Please add graph data first using 'rag fileForGraph'.");
                            return Task.FromResult(Command.Result.Failed);
                        }

                        GraphStoreManager.Graph.PrintCommunityAnalysis();
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command {
                    Name = "export summary",
                    Description = () => "Filter entries by reference and export a de-duped summary",
                    Action = () =>
                    {
                        Program.ui.Write("Enter filter (substring or prefix of reference): ");
                        var filter = Program.ui.ReadLineWithHistory();
                        if (string.IsNullOrEmpty(filter))
                        {
                            Program.ui.WriteLine("No filter provided. Export cancelled.");
                            return Task.FromResult(Command.Result.Cancelled);
                        }

                        var entries = Engine.VectorStore.GetEntries((refStr, content) =>
                            refStr.Source.Contains(filter, StringComparison.OrdinalIgnoreCase));

                        var merged = ContextManager.Flatten(entries);

                        var mdPath = $"summary_{filter.Replace(':','_').Replace('\\','_').Replace('/','_').Replace('.','_')}.md";
                        File.WriteAllText(
                            Path.Combine(Directory.GetCurrentDirectory(), mdPath),
                            string.Join("\n\n", merged.Select(e => $"--- start {e.Reference} ---\n{e.MergedContent}\n--- end {e.Reference} ---")));

                        Program.ui.WriteLine($"Wrote de-duped summary to: {mdPath}");
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
                            Program.ui.WriteLine("RAG store is empty. Please add files or directories first.");
                            return Command.Result.Failed;
                        }

                        Program.ui.Write("Enter search query: ");
                        var query = Program.ui.ReadLineWithHistory();
                        if (!string.IsNullOrWhiteSpace(query))
                        {
                            var embeddingProvider = Engine.Provider as IEmbeddingProvider;
                            if (embeddingProvider == null)
                            {
                                Program.ui.WriteLine("Current provider does not support embeddings.");
                                return Command.Result.Failed;
                            }
                            float[] queryEmbedding = await embeddingProvider.GetEmbeddingAsync(query);
                            if (queryEmbedding.Length == 0)
                            {
                                Program.ui.WriteLine("Failed to get embedding for test query.");
                                return Command.Result.Failed;
                            }

                            var results = await ContextManager.SearchVectorDB(query);
                            
                            if (results.Any())
                            {
                                Program.ui.WriteLine("Search Results:");
                                foreach (var result in results)
                                {
                                    Program.ui.WriteLine($"[{result.Score:F4}] {result.Reference}");
                                }

                                var scores = results.Select(x => x.Score).ToList();
                                float mean = scores.Average();
                                float stddev = (float)Math.Sqrt(scores.Average(x => Math.Pow(x - mean, 2)));

                                Program.ui.WriteLine($"\nEmbedding dimensions: {queryEmbedding.Length}");
                                Program.ui.WriteLine($"Total entries: {Engine.VectorStore.Count}");
                                Program.ui.WriteLine($"Average similarity score: {mean:F4}");
                                Program.ui.WriteLine($"Standard deviation: {stddev:F4}");
                            }
                            else
                            {
                                Program.ui.WriteLine("No results found.");
                            }
                        }
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "clear", Description = () => "Clear all RAG data",
                    Action = () =>
                    {
                        Engine.VectorStore.Clear();
                        Program.ui.WriteLine("RAG store cleared.");
                        return Task.FromResult(Command.Result.Success);
                    }
                }
            }
        };
    }
}
