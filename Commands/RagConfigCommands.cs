using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public partial class CommandManager
{
    private static Command CreateRagFileTypeCommands()
    {
        return new Command
        {
            Name = "supported file types",
            Description = () => "manage and configure RAG related settings for supported file types",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "list", Description = () => "List all supported file types",
                    Action = () =>
                    {
                        var fileTypes = Program.config.RagSettings.SupportedFileTypes;
                        if (fileTypes.Count == 0)
                        {
                            Program.ui.WriteLine("No supported file types configured.");
                        }
                        else
                        {
                            Program.ui.WriteLine("Supported File Types:");
                            foreach (var type in fileTypes)
                            {
                                Program.ui.WriteLine($"- {type}");
                            }
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "add", Description = () => "Add a new supported file type",
                    Action = () =>
                    {
                        Program.ui.Write("Enter new file type (e.g., .txt, .md): ");
                        var input = Program.ui.ReadLineWithHistory();
                        if (!string.IsNullOrWhiteSpace(input) && !Program.config.RagSettings.SupportedFileTypes.Contains(input.Trim(), StringComparer.OrdinalIgnoreCase))
                        {
                            Program.config.RagSettings.SupportedFileTypes.Add(input.Trim());
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Added file type '{input.Trim()}' to supported types.");
                        }
                        else
                        {
                            Program.ui.WriteLine("Invalid or duplicate file type.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "remove", Description = () => "Remove a supported file type",
                    Action = () =>
                    {
                        Program.ui.Write("Enter file type to remove: ");
                        var input = Program.ui.ReadLineWithHistory();
                        if (!string.IsNullOrWhiteSpace(input) && Program.config.RagSettings.SupportedFileTypes.Remove(input.Trim()))
                        {
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Removed file type '{input.Trim()}' from supported types.");
                        }
                        else
                        {
                            Program.ui.WriteLine("Invalid or non-existent file type.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "clear", Description = () => "Clear all supported file types",
                    Action = () =>
                    {
                        Program.config.RagSettings.SupportedFileTypes.Clear();
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Program.ui.WriteLine("Cleared all supported file types.");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "set", Description = () => "Set the supported file types from a comma-separated list",
                    Action = () =>
                    {
                        Program.ui.Write("Enter comma-separated file types (e.g., .txt, .md): ");
                        var input = Program.ui.ReadLineWithHistory();
                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            var types = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(t => t.Trim())
                                             .Where(t => !string.IsNullOrWhiteSpace(t))
                                             .ToHashSet(StringComparer.OrdinalIgnoreCase)
                                             .ToList();
                            Program.config.RagSettings.SupportedFileTypes = types;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine("Updated supported file types.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "rules", Description = () => "Manage file filter rules for supported file types",
                    SubCommands = new List<Command>
                    {
                        new Command
                        {
                            Name = "add exclude rule", Description = () => "Add an exclude rule for a file type",
                            Action = () =>
                            {
                                // use menu to select file type to add exclude rule for
                                var fileTypes = Program.config.RagSettings.SupportedFileTypes.ToList();
                                if (fileTypes.Count == 0)
                                {
                                    Program.ui.WriteLine("No supported file types configured. Please add some first.");
                                    return Task.FromResult(Command.Result.Failed);
                                }
                                var selectedType = Program.ui.RenderMenu("Select file type to add exclude rule for:", fileTypes);
                                if (string.IsNullOrWhiteSpace(selectedType))
                                {
                                    Program.ui.WriteLine("No file type selected.");
                                    return Task.FromResult(Command.Result.Cancelled);
                                }
                                var type = selectedType.Trim();
                                if (!Program.config.RagSettings.FileFilters.TryGetValue(type, out var rules))
                                {
                                    rules = new FileFilterRules();
                                    Program.config.RagSettings.FileFilters[type] = rules;
                                }
                                Program.ui.Write("Enter exclude regex pattern: ");
                                var pattern = Program.ui.ReadLineWithHistory();
                                if (!string.IsNullOrWhiteSpace(pattern))
                                {
                                    // validate that the pattern is a valid regex
                                    try
                                    {
                                        new Regex(pattern);
                                    }
                                    catch (ArgumentException)
                                    {
                                        Program.ui.WriteLine("Invalid regex pattern.");
                                        return Task.FromResult(Command.Result.Failed);
                                    }
                                    rules.Exclude.Add(pattern);
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Added exclude rule '{pattern}' for file type '{type}'.");
                                }
                                else
                                {
                                    Program.ui.WriteLine("Invalid pattern.");
                                }
                                return Task.FromResult(Command.Result.Success);
                            }
                        },
                        new Command
                        {
                            Name = "add include rule", Description = () => "Add an include rule for a file type",
                            Action = () =>
                            {
                                // use menu to select file type to add include rule for
                                var fileTypes = Program.config.RagSettings.SupportedFileTypes.ToList();
                                if (fileTypes.Count == 0)
                                {
                                    Program.ui.WriteLine("No supported file types configured. Please add some first.");
                                    return Task.FromResult(Command.Result.Failed);
                                }
                                var selectedType = Program.ui.RenderMenu("Select file type to add include rule for:", fileTypes);
                                if (string.IsNullOrWhiteSpace(selectedType))
                                {
                                    Program.ui.WriteLine("No file type selected.");
                                    return Task.FromResult(Command.Result.Cancelled);
                                }
                                var type = selectedType.Trim();
                                if (!Program.config.RagSettings.FileFilters.TryGetValue(type, out var rules))
                                {
                                    rules = new FileFilterRules();
                                    Program.config.RagSettings.FileFilters[type] = rules;
                                }
                                Program.ui.Write("Enter include regex pattern: ");
                                var pattern = Program.ui.ReadLineWithHistory();
                                if (!string.IsNullOrWhiteSpace(pattern))
                                {
                                    // validate that the pattern is a valid regex
                                    try
                                    {
                                        new Regex(pattern);
                                    }
                                    catch (ArgumentException)
                                    {
                                        Program.ui.WriteLine("Invalid regex pattern.");
                                        return Task.FromResult(Command.Result.Failed);
                                    }
                                    rules.Include.Add(pattern);
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Added include rule '{pattern}' for file type '{type}'.");
                                }
                                else
                                {
                                    Program.ui.WriteLine("Invalid pattern.");
                                }
                                return Task.FromResult(Command.Result.Success);
                            }
                        },
                        new Command
                        {
                            Name = "list rules", Description = () => "List all rules for a file type",
                            Action = () =>
                            {
                                // use menu to select file type to list rules for
                                var fileTypes = Program.config.RagSettings.SupportedFileTypes
                                    .Where(ft=>
                                        Program.config.RagSettings.FileFilters.ContainsKey(ft) &&
                                        (Program.config.RagSettings.FileFilters[ft].Include.Count +
                                         Program.config.RagSettings.FileFilters[ft].Exclude.Count) > 0)
                                    .ToList();
                                if (fileTypes.Count == 0)
                                {
                                    Program.ui.WriteLine("No rules configured for supported file types. Please add some first.");
                                    return Task.FromResult(Command.Result.Failed);
                                }
                                var selectedType = Program.ui.RenderMenu("Select file type to list rules for:", fileTypes);
                                if (string.IsNullOrWhiteSpace(selectedType))
                                {
                                    Program.ui.WriteLine("No file type selected.");
                                    return Task.FromResult(Command.Result.Cancelled);
                                }
                                var type = selectedType.Trim();
                                if (Program.config.RagSettings.FileFilters.TryGetValue(type, out var rules))
                                {
                                    Program.ui.WriteLine($"Rules for file type '{type}':");
                                    Program.ui.WriteLine("Include Patterns:");
                                    foreach (var include in rules.Include)
                                    {
                                        Program.ui.WriteLine($"- {include}");
                                    }
                                    Program.ui.WriteLine("Exclude Patterns:");
                                    foreach (var exclude in rules.Exclude)
                                    {
                                        Program.ui.WriteLine($"- {exclude}");
                                    }
                                }
                                else
                                {
                                    Program.ui.WriteLine($"No rules found for file type '{type}'.");
                                }
                                return Task.FromResult(Command.Result.Success);
                            }
                        },
                        new Command
                        {
                            Name = "remove rule", Description = () => "Remove a rule from a file type",
                            Action = () =>
                            {
                                // use menu to select file type to remove rule from
                                var fileTypes = Program.config.RagSettings.SupportedFileTypes
                                    .Where(ft=>
                                        Program.config.RagSettings.FileFilters.ContainsKey(ft) &&
                                        (Program.config.RagSettings.FileFilters[ft].Include.Count +
                                         Program.config.RagSettings.FileFilters[ft].Exclude.Count) > 0)
                                    .ToList();
                                if (fileTypes.Count == 0)
                                {
                                    Program.ui.WriteLine("No supported file types configured. Please add some first.");
                                    return Task.FromResult(Command.Result.Failed);
                                }
                                var selectedType = Program.ui.RenderMenu("Select file type to remove rule from:", fileTypes);
                                if (string.IsNullOrWhiteSpace(selectedType))
                                {
                                    Program.ui.WriteLine("No file type selected.");
                                    return Task.FromResult(Command.Result.Cancelled);
                                }
                                var type = selectedType.Trim();
                                if (!Program.config.RagSettings.FileFilters.TryGetValue(type, out var rules))
                                {
                                    Program.ui.WriteLine($"No rules found for file type '{type}'.");
                                    return Task.FromResult(Command.Result.Failed);
                                }
                                Program.ui.WriteLine("Select a rule to remove:");
                                var choices = new List<string>();
                                choices.AddRange(rules.Include.Where(p=>!string.IsNullOrWhiteSpace(p)).Select(p => $"Include: {p}"));
                                choices.AddRange(rules.Exclude.Where(p=>!string.IsNullOrWhiteSpace(p)).Select(p => $"Exclude: {p}"));
                                if (choices.Count == 0)
                                {
                                    Program.config.RagSettings.FileFilters.Remove(type);
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"No rules found for file type '{type}'. Removed file type from configuration.");
                                    return Task.FromResult(Command.Result.Success);
                                }
                                var selectedRule = Program.ui.RenderMenu("Select a rule to remove:", choices);
                                if (string.IsNullOrWhiteSpace(selectedRule))
                                {
                                    Program.ui.WriteLine("No rule selected.");
                                    return Task.FromResult(Command.Result.Cancelled);
                                }
                                choices.Remove(selectedRule);
                                if (selectedRule.StartsWith("Include: "))
                                {
                                    var rule = selectedRule.Substring("Include: ".Length);
                                    if (rules.Include.Remove(rule))
                                    {
                                        Program.ui.WriteLine($"Removed include rule '{rule}' from file type '{type}'.");
                                    }
                                    else
                                    {
                                        Program.ui.WriteLine($"Rule '{rule}' not found in include rules for file type '{type}'.");
                                    }
                                }
                                else if (selectedRule.StartsWith("Exclude: "))
                                {
                                    var rule = selectedRule.Substring("Exclude: ".Length);
                                    if (rules.Exclude.Remove(rule))
                                    {
                                        Program.ui.WriteLine($"Removed exclude rule '{rule}' from file type '{type}'.");
                                    }
                                    else
                                    {
                                        Program.ui.WriteLine($"Rule '{rule}' not found in exclude rules for file type '{type}'.");
                                    }
                                }
                                else
                                {
                                    Program.ui.WriteLine("Invalid rule selected.");
                                    return Task.FromResult(Command.Result.Failed);
                                }
                                if (0 == choices.Count)
                                {
                                    Program.config.RagSettings.FileFilters.Remove(type);
                                    Program.ui.WriteLine($"No rules left for file type '{type}'. Removed file type from configuration.");
                                }
                                Config.Save(Program.config, Program.ConfigFilePath);
                                return Task.FromResult(Command.Result.Success);
                            }
                        }
                    }
                }
            }
        };
    }

    private static Command CreateRagConfigCommands()
    {
        return new Command
        {
            Name = "RAG",
            Description = () => "RAG (Retrieval-Augmented Generation) configuration settings",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "use embeddings", Description = () => $"Toggle the use of embeddings for RAG [currently: {(Program.config.RagSettings.UseEmbeddings ? "Enabled" : "Disabled")}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Use Embeddings", Program.config.RagSettings.UseEmbeddings);
                        form.AddBool("Enable")
                            .WithHelp("Enable or disable the use of embeddings.");

                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.UseEmbeddings = (bool)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "embedding model", Description = () => $"Set the embedding model for RAG [currently: {Program.config.RagSettings.EmbeddingModel}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Embedding Model", Program.config.RagSettings.EmbeddingModel);
                        form.AddString("Model")
                            .WithHelp("Enter the embedding model to use.");

                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.EmbeddingModel = (string)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "embedding concurrency", Description = () => $"Set the maximum concurrency for embedding generation [currently: {Program.config.RagSettings.MaxEmbeddingConcurrency}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Embedding Concurrency", Program.config.RagSettings.MaxEmbeddingConcurrency);
                        form.AddInt("Max Concurrency")
                            .IntBounds(min: 1, max: 100)
                            .WithHelp("Enter the maximum concurrency for embedding generation (1 to 100).");

                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.MaxEmbeddingConcurrency = (int)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "ingest concurrency", Description = () => $"Set the maximum concurrency for RAG ingestion [currently: {Program.config.RagSettings.MaxIngestConcurrency}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Ingest Concurrency", Program.config.RagSettings.MaxIngestConcurrency);
                        form.AddInt("Max Concurrency")       
                            .IntBounds(min: 1, max: 100)
                            .WithHelp("Enter the maximum concurrency for RAG ingestion (1 to 100).");

                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.MaxIngestConcurrency = (int)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "chunking method", Description = () => $"Select the text chunker for RAG [currently: {Program.config.RagSettings.ChunkingStrategy}]",
                    Action = () =>
                    {
                        var chunkers = Program.Chunkers.Keys.ToList();
                        var selected = Program.ui.RenderMenu("Select a text chunker:", chunkers, chunkers.IndexOf(Program.config.RagSettings.ChunkingStrategy));
                        if (!string.IsNullOrWhiteSpace(selected) && !selected.Equals(Program.config.RagSettings.ChunkingStrategy, StringComparison.OrdinalIgnoreCase))
                        {
                            Program.config.RagSettings.ChunkingStrategy = selected;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },
                new Command
                {
                    Name = "chunk size", Description = () => $"Set the chunk size for RAG [currently: {Program.config.RagSettings.ChunkSize}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Chunk Size", Program.config.RagSettings.ChunkSize);
                        form.AddInt("Chunk Size")
                            .IntBounds(min: 1, max: 10000)
                            .WithHelp("Enter the chunk size (1 to 10000).");

                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.ChunkSize = (int)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "max tokens per chunk", Description = () => $"Set the maximum tokens per chunk for RAG [currently: {Program.config.RagSettings.MaxTokensPerChunk}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Max Tokens Per Chunk", Program.config.RagSettings.MaxTokensPerChunk);
                        form.AddInt("Max Tokens")
                            .IntBounds(min: 1, max: 32000)
                            .WithHelp("Enter the maximum tokens per chunk (1 to 32000).");

                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.MaxTokensPerChunk = (int)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "max line length", Description = () => $"Set the maximum line length for RAG [currently: {Program.config.RagSettings.MaxLineLength}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Max Line Length", Program.config.RagSettings.MaxLineLength);
                        form.AddInt("Max Line Length")
                            .IntBounds(min: 1, max: 32000)
                            .WithHelp("Enter the maximum line length (1 to 32000).");

                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.MaxLineLength = (int)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "overlap", Description = () => $"Set the overlap size for RAG chunks [currently: {Program.config.RagSettings.Overlap}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Overlap Size", Program.config.RagSettings.Overlap);
                        form.AddInt("Overlap Size")
                            .IntBounds(min: 0, max: 100)
                            .WithHelp("Enter the overlap size (0 to 100).");
                        
                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.Overlap = (int)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "TopK", Description = () => $"Set the number of top results to return from RAG queries [currently: {Program.config.RagSettings.TopK}]",
                    Action = async () =>
                    {
                        const int maxK = 25;
                        var form = UiForm.Create("TopK", Program.config.RagSettings);
                        form.AddInt<RagSettings>("TopK", v => v.TopK, (v, i) => v.TopK = i)
                            .IntBounds(min: 1, max: maxK)
                            .WithHelp($"Enter the number of top results to return (1 to {maxK}).");

                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.TopK = ((RagSettings)form.Model!).TopK;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "Use MMR", Description = () => $"Toggle the use of MMR for RAG [currently: {Program.config.RagSettings.UseMmr}]",
                    Action = () =>
                    {
                        Program.config.RagSettings.UseMmr = !Program.config.RagSettings.UseMmr;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "MMR Lambda", Description = () => $"Set the MMR lambda value for RAG [currently: {Program.config.RagSettings.MmrLambda}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("MMR Lambda", Program.config.RagSettings.MmrLambda);
                        form.AddDouble("Lambda (0 to 1)")
                            .IntBounds(min: 0, max: 1)
                            .WithHelp("Enter the MMR lambda floating-point value (0 to 1).");

                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.MmrLambda = (double)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "MMR Pool Multiplier" , Description = () => $"Set the MMR pool multiplier for RAG [currently: {Program.config.RagSettings.MmrPoolMultiplier}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("MMR Pool Multiplier", Program.config.RagSettings.MmrPoolMultiplier);
                        form.AddFloat("Multiplier (0.0 to 10.0)")
                            .IntBounds(min: 0, max: 10)
                            .WithHelp("Enter the MMR pool multiplier (0.0 to 10.0).");
                        
                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings.MmrPoolMultiplier = (float)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                }
            }
        };
    }
}
