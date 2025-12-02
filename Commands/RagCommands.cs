using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

class ExportFilterModel { public string Filter { get; set; } = string.Empty; }
class RagSearchModel { public string Query { get; set; } = string.Empty; }
class GraphWalkModel { public string Query { get; set; } = string.Empty; public int Hops { get; set; } = 2; }
class CommunityInfoModel { public bool All { get; set; } = true; public int CommunityId { get; set; } = 0; }
class PathModel { public string Path { get; set; } = string.Empty; }

public partial class CommandManager
{
    private static Command CreateRagCommands()
    {
        return new Command
        {
            Name = "rag", Description = () => "Retrieval-Augmented Generation commands",
            SubCommands = new List<Command>
            {
                // Consolidated Knowledge Graph commands under nested 'graph'
                new Command
                {
                    Name = "Graph", Description = () => "Experimental Knowledge Graph (Graph RAG) Commands",
                    SubCommands = new List<Command>
                    {
                        new Command
                        {
                            Name = "add file", Description = () => "Add a file to the Graph RAG store",
                            Action = async () =>
                            {
                                // Use UiForm instead of direct path prompt
                                var form = UiForm.Create("Add Graph File", new PathModel());
                                form.AddPath<PathModel>("File", m => m.Path, (m,v)=> m.Path = v)
                                    .WithHelp("Select a file whose contents will be ingested into the Graph RAG store.")
                                    .WithPathMode(PathPickerMode.OpenExisting);

                                if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                                var path = ((PathModel)form.Model!).Path?.Trim();
                                if (string.IsNullOrWhiteSpace(path)) return Command.Result.Cancelled;
                                await Engine.AddFileToGraphStore(path);
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "add directory", Description = () => "Add a directory (recursively) to the Graph RAG store",
                            Action = async () =>
                            {
                                var form = UiForm.Create("Add Graph Directory", new PathModel());
                                form.AddPath<PathModel>("Directory", m => m.Path, (m,v)=> m.Path = v)
                                    .WithHelp("Select a directory whose files will be ingested into the Graph RAG store (recursively).")
                                    .WithPathMode(PathPickerMode.OpenExisting);
                                if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                                var dir = ((PathModel)form.Model!).Path?.Trim();
                                if (string.IsNullOrWhiteSpace(dir)) return Command.Result.Cancelled;
                                await Engine.AddDirectoryToGraphStore(dir);
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "Generate docs", Description = () => "Generate code + graph documentation for a directory",
                            Action = async () =>
                            {
                                var form = UiForm.Create("Generate Graph + Code Docs", new PathModel());
                                form.AddPath<PathModel>("Code directory", m => m.Path, (m,v)=> m.Path = v)
                                    .WithHelp("Select the root directory of the codebase to document and ingest into the graph.")
                                    .WithPathMode(PathPickerMode.OpenExisting);
                                if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                                var dir = ((PathModel)form.Model!).Path?.Trim();
                                if (string.IsNullOrWhiteSpace(dir)) return Command.Result.Cancelled;
                                await Engine.AddDirectoryToGraphStore(dir);
                                await Engine.GenerateCodeAndGraphDocumentationAsync(dir);
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "walk", Description = () => "Perform an N-hop node walk in the graph",
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
                            Name = "save", Description = () => "Save all nodes/edges in the graph in a sqlite database",
                            Action = async () =>
                            {
                                // Use UiForm to get save location
                                var form = UiForm.Create("Save Graph to SQLite Database", new PathModel());
                                form.AddPath<PathModel>("Database File", m => m.Path, (m,v)=> m.Path = v)
                                    .WithHelp("Select location to save the graph database (.db file).")
                                    .WithPathMode(PathPickerMode.SaveFile);

                                if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                                var path = ((PathModel)form.Model!).Path?.Trim();
                                if (string.IsNullOrWhiteSpace(path)) return Command.Result.Cancelled;
                                
                                // Ensure .db extension
                                if (!path.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                                {
                                    path += ".db";
                                }
                                
                                await GraphStoreManager.SaveCurrentGraphAsync(path);
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "load", Description = () => "Load all nodes/edges in the graph from a sqlite database",
                            Action = async () =>
                            {
                                // Use UiForm instead of direct path prompt
                                var form = UiForm.Create("Load Graph from File", new PathModel());
                                form.AddPath<PathModel>("File", m => m.Path, (m,v)=> m.Path = v)
                                    .WithHelp("Select a file whose contents will be loaded into the Graph.")
                                    .WithPathMode(PathPickerMode.OpenExisting);

                                if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                                var path = ((PathModel)form.Model!).Path?.Trim();
                                if (string.IsNullOrWhiteSpace(path)) return Command.Result.Cancelled;
                                await GraphStoreManager.LoadIntoCurrentGraphAsync(path);
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "dump", Description = () => "Dump all nodes/edges in the graph",
                            Action = async () =>
                            {
                                GraphStoreManager.Graph.PrintGraph();
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "community info", Description = () => "Detailed info about one or all communities",
                            Action = async () =>
                            {
                                using var output = Program.ui.BeginRealtime("Community Info");
                                if (GraphStoreManager.Graph.IsEmpty)
                                {
                                    output.WriteLine("Graph store is empty. Please add graph data first using 'rag graph add file'.");
                                    return Command.Result.Failed;
                                }
                                var form = UiForm.Create("Community Info", new CommunityInfoModel());
                                form.AddBool<CommunityInfoModel>("All", m => m.All, (m,v)=> m.All = v).WithHelp("Show all communities?");
                                form.AddInt<CommunityInfoModel>("CommunityId", m => m.CommunityId, (m,v)=> m.CommunityId = v).MakeOptional();
                                if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                                var model = (CommunityInfoModel)form.Model!;
                                if (model.All)
                                {
                                    GraphStoreManager.Graph.PrintDetailedCommunityInfo(output);
                                } else {
                                    GraphStoreManager.Graph.PrintDetailedCommunityInfo(output, model.CommunityId);
                                }
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "community summary", Description = () => "Summary table of all communities",
                            Action = () =>
                            {
                                using var output = Program.ui.BeginRealtime("Community Summary");
                                if (GraphStoreManager.Graph.IsEmpty)
                                {
                                    output.WriteLine("Graph store is empty. Please add graph data first using 'rag graph add file'.");
                                    return Task.FromResult(Command.Result.Failed);
                                }
                                GraphStoreManager.Graph.PrintCommunitySummaryTable(output);
                                return Task.FromResult(Command.Result.Success);
                            }
                        },
                        new Command
                        {
                            Name = "community detection", Description = () => "Run Louvain community detection and summary",
                            Action = () =>
                            {
                                using var output = Program.ui.BeginRealtime("Community Detection");
                                if (GraphStoreManager.Graph.IsEmpty)
                                {
                                    output.WriteLine("Graph store is empty. Please add graph data first using 'rag graph add file'.");
                                    return Task.FromResult(Command.Result.Failed);
                                }
                                GraphStoreManager.Graph.PrintCommunityAnalysis(output);
                                return Task.FromResult(Command.Result.Success);
                            }
                        }
/*                    new Command
                        {
                            Name = "Generate Documentation", Description = () => "Generate documentation for the Knowledge Graph",
                            Action = async () =>
                            {
                                await Engine.GetDocumentation();
                                return Command.Result.Success;
                            }
                        },
*/
                    }
                },
                new Command
                {
                    Name = "add file", Description = () => "Add a single file's contents to the RAG store",
                    Action = async () => await Log.MethodAsync(async ctx =>
                    {
                        ctx.OnlyEmitOnFailure();
                        var form = UiForm.Create("Add RAG File", new PathModel());
                        form.AddPath<PathModel>("File", m => m.Path, (m,v)=> m.Path = v)
                            .WithHelp("Select a file to ingest into the vector RAG store.")
                            .WithPathMode(PathPickerMode.OpenExisting);
                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            ctx.Append(Log.Data.Message, "User cancelled form.");
                            return Command.Result.Cancelled;
                        }
                        var path = ((PathModel)form.Model!).Path?.Trim();
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            ctx.Append(Log.Data.Message, "No file path provided.");
                            return Command.Result.Cancelled;
                        }
                        if (!File.Exists(path))
                        {
                            ctx.Append(Log.Data.Message, $"File '{path}' does not exist.");
                            return Command.Result.Failed;
                        }
                        await Engine.AddFileToVectorStore(path);
                        ctx.Succeeded();
                        return Command.Result.Success;
                    })
                },
                new Command
                {
                    Name = "add directory", Description = () => "Add a directory to the RAG store",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Add RAG Directory", new PathModel());
                        form.AddPath<PathModel>("Directory", m => m.Path, (m,v)=> m.Path = v)
                            .WithHelp("Select a directory whose files will be ingested into the vector RAG store (recursively).")
                            .WithPathMode(PathPickerMode.OpenExisting);
                        if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                        var dir = ((PathModel)form.Model!).Path?.Trim();
                        if (string.IsNullOrWhiteSpace(dir)) return Command.Result.Cancelled;
                        if (!Directory.Exists(dir))
                        {
                            Log.Method(ctx=>ctx.Append(Log.Data.Message, $"Directory '{dir}' does not exist."));
                            return Command.Result.Failed;
                        }
                        await Engine.AddDirectoryToVectorStore(dir);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "add zip contents", Description = () => "Add contents of a zip file to the RAG store",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Add Zip Contents", new PathModel());
                        form.AddPath<PathModel>("Zip file", m => m.Path, (m,v)=> m.Path = v)
                            .WithHelp("Select a .zip file whose contents will be extracted and ingested into the vector RAG store.")
                            .WithPathMode(PathPickerMode.OpenExisting);
                        if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }
                        var zip = ((PathModel)form.Model!).Path?.Trim();
                        if (string.IsNullOrWhiteSpace(zip)) return Command.Result.Cancelled;
                        if (!File.Exists(zip))
                        {
                            Log.Method(ctx=>ctx.Append(Log.Data.Message, $"Zip file '{zip}' does not exist."));
                            return Command.Result.Failed;
                        }
                        await Engine.AddZipFileToVectorStore(zip);
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
                            Log.Method(ctx=>ctx.Append(Log.Data.Message, "RAG store is empty. Please add files or directories first."));
                            return Task.FromResult(Command.Result.Success);
                        }

                        var entries = Engine.VectorStore.GetEntries();
                        var choices = entries.Select((entry, index) => $"{index}: {entry.Reference}").ToList();

                        var selected = Program.ui.RenderMenu($"Select one of {Engine.VectorStore.Count} RAG Store Entries", choices);
                        if (selected == null)
                        {
                            Log.Method(ctx=>ctx.Append(Log.Data.Message, "No entry selected."));
                            return Task.FromResult(Command.Result.Cancelled);
                        }

                        int selectedIndex = int.Parse(selected.Split(':')[0]);
                        if (selectedIndex < 0 || selectedIndex >= entries.Count)
                        {
                            Log.Method(ctx=>ctx.Append(Log.Data.Message, "Invalid selection."));
                            return Task.FromResult(Command.Result.Failed);
                        }

                        var entry = entries[selectedIndex];
                        using var output = Program.ui.BeginRealtime($"RAG Entry: {entry.Reference}");
                        output.WriteLine(entry.Content);

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
                            using var output = Program.ui.BeginRealtime("Export Summary");
                            output.WriteLine($"Total entries matching '{filter}': {entries.Count}");
                            output.WriteLine($"Total de-duped sections: {merged.Count}");
                            output.WriteLine($"Wrote de-duped summary to: {mdPath}");
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
                            Log.Method(ctx=>ctx.Append(Log.Data.Message, "RAG store is empty. Please add files or directories first."));
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
                                Log.Method(ctx=>ctx.Append(Log.Data.Message, "Current provider does not support embeddings."));
                                return Command.Result.Failed;
                            }
                            float[] queryEmbedding = await embeddingProvider.GetEmbeddingAsync(query);
                            if (queryEmbedding.Length == 0)
                            {
                                Log.Method(ctx=>ctx.Append(Log.Data.Message, "Failed to get embedding for test query."));
                                return Command.Result.Failed;
                            }

                            var results = await ContextManager.SearchVectorDB(query);
                            using var output = Program.ui.BeginRealtime($"RAG Search Results for: {query}");
                            if (results.Any())
                            {
                                output.WriteLine("Search Results:");
                                foreach (var result in results)
                                {
                                    output.WriteLine($"[{result.Score:F4}] {result.Reference}");
                                }

                                var scores = results.Select(x => x.Score).ToList();
                                float mean = scores.Average();
                                float stddev = (float)Math.Sqrt(scores.Average(x => Math.Pow(x - mean, 2)));

                                output.WriteLine($"\nEmbedding dimensions: {queryEmbedding.Length}");
                                output.WriteLine($"Total entries: {Engine.VectorStore.Count}");
                                output.WriteLine($"Average similarity score: {mean:F4}");
                                output.WriteLine($"Standard deviation: {stddev:F4}");
                            }
                            else
                            {
                                output.WriteLine("No results found.");
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
                        using var output = Program.ui.BeginRealtime("Clear RAG Store");
                        output.WriteLine("RAG store cleared.");
                        return Task.FromResult(Command.Result.Success);
                    }
                }
            }
        };
    }
}
