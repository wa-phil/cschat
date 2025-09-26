using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

class ExportFilterModel { public string Filter { get; set; } = string.Empty; }
class RagSearchModel { public string Query { get; set; } = string.Empty; }
class GraphWalkModel { public string Query { get; set; } = string.Empty; public int Hops { get; set; } = 2; }
class CommunityInfoModel { public bool All { get; set; } = true; public int CommunityId { get; set; } = 0; }

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
                    Action = async () =>
                    {
                        var form = UiForm.Create("Graph Walk", new GraphWalkModel());
                        form.AddString<GraphWalkModel>("Query", m => m.Query, (m,v)=> m.Query = v).WithHelp("Node search text.");
                        form.AddInt<GraphWalkModel>("Hops", m => m.Hops, (m,v)=> m.Hops = v).IntBounds(1,10).WithHelp("Number of hops (1-10).");
                        if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                        var model = (GraphWalkModel)form.Model!;
                        GraphStoreManager.Graph.PrintEntitiesWithinHops(model.Query ?? string.Empty, model.Hops);
                        return Command.Result.Success;
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
                    Action = async () =>
                    {
                        if (GraphStoreManager.Graph.IsEmpty)
                        {
                            Program.ui.WriteLine("Graph store is empty. Please add graph data first using 'rag fileForGraph'.");
                            return Command.Result.Failed;
                        }
                        var form = UiForm.Create("Community Info", new CommunityInfoModel());
                        form.AddBool<CommunityInfoModel>("All", m => m.All, (m,v)=> m.All = v).WithHelp("Show all communities?");
                        form.AddInt<CommunityInfoModel>("CommunityId", m => m.CommunityId, (m,v)=> m.CommunityId = v).MakeOptional();
                        if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                        var model = (CommunityInfoModel)form.Model!;
                        if (model.All)
                        {
                            GraphStoreManager.Graph.PrintDetailedCommunityInfo();
                        } else {
                            GraphStoreManager.Graph.PrintDetailedCommunityInfo(model.CommunityId);
                        }
                        return Command.Result.Success;
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
                        var form = UiForm.Create("Export summary", new ExportFilterModel { Filter = string.Empty });
                        form.AddString<ExportFilterModel>("Filter", m => m.Filter, (m,v)=> m.Filter = v)
                            .WithHelp("Substring or prefix of reference; required.");
                        return Program.ui.ShowFormAsync(form).ContinueWith(t => {
                            if (!t.Result) { return Command.Result.Cancelled; }
                            var filter = ((ExportFilterModel)form.Model!).Filter;
                            if (string.IsNullOrWhiteSpace(filter)) { return Command.Result.Cancelled; }

                            var entries = Engine.VectorStore.GetEntries((refStr, content) =>
                                refStr.Source.Contains(filter, StringComparison.OrdinalIgnoreCase));

                            var merged = ContextManager.Flatten(entries);

                            var safe = filter.Replace(':','_').Replace('/', '_').Replace('.', '_').Replace('\\', '_');
                            var mdPath = $"summary_{safe}.md";
                            File.WriteAllText(
                                Path.Combine(Directory.GetCurrentDirectory(), mdPath),
                                string.Join("\n\n", merged.Select(e => $"--- start {e.Reference} ---\n{e.MergedContent}\n--- end {e.Reference} ---")));

                            Program.ui.WriteLine($"Wrote de-duped summary to: {mdPath}");
                            return Command.Result.Success;
                        });
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

                        var qform = UiForm.Create("RAG search", new RagSearchModel { Query = string.Empty });
                        qform.AddString<RagSearchModel>("Query", m => m.Query, (m,v)=> m.Query = v)
                             .WithHelp("Enter search text; required.");
                        if (await Program.ui.ShowFormAsync(qform))
                        {
                            var query = ((RagSearchModel)qform.Model!).Query;
                            if (string.IsNullOrWhiteSpace(query)) { return Command.Result.Cancelled; }
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
