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
                    Action = () =>
                    {
                        var selected = Program.ui.RenderMenu("Use embeddings:", new List<string> { "true", "false" }, Program.config.RagSettings.UseEmbeddings ? 0 : 1);
                        if (selected == null)
                        {
                            Program.ui.WriteLine("No selection made.");
                            return Task.FromResult(Command.Result.Cancelled);
                        }
                        if (!bool.TryParse(selected, out var useEmbeddings))
                        {
                            Program.ui.WriteLine("Invalid selection. Please select 'true' or 'false'.");
                            return Task.FromResult(Command.Result.Failed);
                        }
                        Program.config.RagSettings.UseEmbeddings = useEmbeddings;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Program.ui.WriteLine($"Use embeddings set to {Program.config.RagSettings.UseEmbeddings}");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "embedding model", Description = () => $"Set the embedding model for RAG [currently: {Program.config.RagSettings.EmbeddingModel}]",
                    Action = () =>
                    {
                        Program.ui.WriteLine($"Current embedding model: {Program.config.RagSettings.EmbeddingModel}");
                        Program.ui.Write("Enter new embedding model (or press enter to keep current): ");
                        var modelInput = Program.ui.ReadLineWithHistory();
                        if (!string.IsNullOrWhiteSpace(modelInput))
                        {
                            Program.config.RagSettings.EmbeddingModel = modelInput.Trim();
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine("Embedding model updated.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "embedding concurrency", Description = () => $"Set the maximum concurrency for embedding generation [currently: {Program.config.RagSettings.MaxEmbeddingConcurrency}]",
                    Action = () =>
                    {
                        var maxValue = 100;
                        Program.ui.Write($"Current MaxEmbeddingConcurrency: {Program.config.RagSettings.MaxEmbeddingConcurrency}. Enter new value (1 to {maxValue}): ");
                        var concurrencyInput = Program.ui.ReadLineWithHistory();
                        if (int.TryParse(concurrencyInput, out var concurrency) && concurrency >= 1 && concurrency <= maxValue)
                        {
                            Program.config.RagSettings.MaxEmbeddingConcurrency = concurrency;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"MaxEmbeddingConcurrency set to {concurrency}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },
                new Command
                {
                    Name = "ingest concurrency", Description = () => $"Set the maximum concurrency for RAG ingestion [currently: {Program.config.RagSettings.MaxIngestConcurrency}]",
                    Action = () =>
                    {
                        var maxValue = 100;
                        Program.ui.Write($"Current MaxIngestConcurrency: {Program.config.RagSettings.MaxIngestConcurrency}. Enter new value (1 to {maxValue}): ");
                        var concurrencyInput = Program.ui.ReadLineWithHistory();
                        if (int.TryParse(concurrencyInput, out var concurrency) && concurrency >= 1 && concurrency <= maxValue)
                        {
                            Program.config.RagSettings.MaxIngestConcurrency = concurrency;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"MaxIngestConcurrency set to {concurrency}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
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
                            Program.ui.WriteLine($"Switched to chunker '{Program.config.RagSettings.ChunkingStrategy}'");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "chunk size", Description = () => $"Set the chunk size for RAG [currently: {Program.config.RagSettings.ChunkSize}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current chunk size: {Program.config.RagSettings.ChunkSize}. Enter new value (1 to 10000): ");
                        var sizeInput = Program.ui.ReadLineWithHistory();
                        if (int.TryParse(sizeInput, out var size) && size >= 1 && size <= 10000)
                        {
                            Program.config.RagSettings.ChunkSize = size;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Chunk size set to {size}");
                        }
                        else
                        {
                            Program.ui.WriteLine("Invalid chunk size value. Must be between 1 and 10000.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "max tokens per chunk", Description = () => $"Set the maximum tokens per chunk for RAG [currently: {Program.config.RagSettings.MaxTokensPerChunk}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current MaxTokensPerChunk: {Program.config.RagSettings.MaxTokensPerChunk}. Enter new value (1 to 32000): ");
                        var maxTokensInput = Program.ui.ReadLineWithHistory();
                        if (int.TryParse(maxTokensInput, out var maxTokens) && maxTokens >= 1 && maxTokens <= 32000)
                        {
                            Program.config.RagSettings.MaxTokensPerChunk = maxTokens;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"MaxTokensPerChunk set to {maxTokens}");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "max line length", Description = () => $"Set the maximum line length for RAG [currently: {Program.config.RagSettings.MaxLineLength}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current MaxLineLength: {Program.config.RagSettings.MaxLineLength}. Enter new value (1 to 32000): ");
                        var maxLineLengthInput = Program.ui.ReadLineWithHistory();
                        if (int.TryParse(maxLineLengthInput, out var maxLineLength) && maxLineLength >= 1 && maxLineLength <= 32000)
                        {
                            Program.config.RagSettings.MaxLineLength = maxLineLength;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"MaxLineLength set to {maxLineLength}");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "overlap", Description = () => $"Set the overlap size for RAG chunks [currently: {Program.config.RagSettings.Overlap}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current overlap size: {Program.config.RagSettings.Overlap}. Enter new value (0 to 100): ");
                        var overlapInput = Program.ui.ReadLineWithHistory();
                        if (int.TryParse(overlapInput, out var overlap) && overlap >= 0 && overlap <= 100)
                        {
                            Program.config.RagSettings.Overlap = overlap;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Overlap size set to {overlap}");
                        }
                        else
                        {
                            Program.ui.WriteLine("Invalid overlap size value. Must be between 0 and 100.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "TopK", Description = () => $"Set the number of top results to return from RAG queries [currently: {Program.config.RagSettings.TopK}]",
                    Action = () =>
                    {
                        const int maxK = 25;
                        Program.ui.Write($"Current TopK value: {Program.config.RagSettings.TopK}. Enter new value (1 to {maxK}): ");
                        var topKInput = Program.ui.ReadLineWithHistory();
                        if (int.TryParse(topKInput, out var topK) && topK >= 1 && topK <= maxK)
                        {
                            Program.config.RagSettings.TopK = topK;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"TopK set to {topK}");
                        }
                        else
                        {
                            Program.ui.WriteLine("Invalid TopK value. Must be between 1 and {maxK}.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "Use MMR", Description = () => $"Toggle the use of MMR for RAG [currently: {Program.config.RagSettings.UseMmr}]",
                    Action = () =>
                    {
                        Program.config.RagSettings.UseMmr = !Program.config.RagSettings.UseMmr;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Program.ui.WriteLine($"UseMmr set to {Program.config.RagSettings.UseMmr}");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "MMR Lambda", Description = () => $"Set the MMR lambda value for RAG [currently: {Program.config.RagSettings.MmrLambda}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current MMR Lambda: {Program.config.RagSettings.MmrLambda}. Enter new value (0.0 to 1.0): ");
                        var lambdaInput = Program.ui.ReadLineWithHistory();
                        if (float.TryParse(lambdaInput, out var lambda) && lambda >= 0.0f && lambda <= 1.0f)
                        {
                            Program.config.RagSettings.MmrLambda = lambda;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"MMR Lambda set to {lambda}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },
                new Command
                {
                    Name = "MMR Pool Multiplier" , Description = () => $"Set the MMR pool multiplier for RAG [currently: {Program.config.RagSettings.MmrPoolMultiplier}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current MMR Pool Multiplier: {Program.config.RagSettings.MmrPoolMultiplier}. Enter new value (0.0 to 10.0): ");
                        var multiplierInput = Program.ui.ReadLineWithHistory();
                        if (float.TryParse(multiplierInput, out var multiplier) && multiplier >= 0.0f && multiplier <= 10.0f)
                        {
                            Program.config.RagSettings.MmrPoolMultiplier = multiplier;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"MMR Pool Multiplier set to {multiplier}");
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                }
            }
        };
    }
}
