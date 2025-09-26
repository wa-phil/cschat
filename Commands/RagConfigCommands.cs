using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public partial class CommandManager
{

    private static Command CreateRagConfigCommands()
    {
        return new Command
        {
            Name = "RAG",
            Description = () => "RAG (Retrieval-Augmented Generation) configuration settings",
            SubCommands = new List<Command>
            {
                // GROUP: Core Embedding Settings
                new Command
                {
                    Name = "Core Settings", Description = () => "Edit core embedding + retrieval settings (form)",
                    Action = async () =>
                    {
                        var form = UiForm.Create("RAG – Core Settings", Program.config.RagSettings);
                        form.AddBool<RagSettings>("Use Embeddings", m => m.UseEmbeddings, (m,v)=> m.UseEmbeddings = v, nameof(RagSettings.UseEmbeddings))
                            .WithHelp("Enable/disable use of embeddings during retrieval.");
                        form.AddString<RagSettings>("Embedding Model", m => m.EmbeddingModel, (m,v)=> m.EmbeddingModel = v, nameof(RagSettings.EmbeddingModel))
                            .WithHelp("Embedding model identifier (provider specific).");
                        form.AddInt<RagSettings>("TopK", m => m.TopK, (m,v)=> m.TopK = v, nameof(RagSettings.TopK))
                            .IntBounds(1,25)
                            .WithHelp("Number of similar chunks to retrieve.");
                        form.AddInt<RagSettings>("TopK For Parsing", m => m.TopKForParsing, (m,v)=> m.TopKForParsing = v, nameof(RagSettings.TopKForParsing))
                            .IntBounds(1,10)
                            .WithHelp("Number of results to include for parsing context.");
                        form.AddInt<RagSettings>("Embedding Concurrency", m => m.MaxEmbeddingConcurrency, (m,v)=> m.MaxEmbeddingConcurrency = v, nameof(RagSettings.MaxEmbeddingConcurrency))
                            .IntBounds(1,100)
                            .WithHelp("Max parallel embedding requests.");
                        form.AddInt<RagSettings>("Ingest Concurrency", m => m.MaxIngestConcurrency, (m,v)=> m.MaxIngestConcurrency = v, nameof(RagSettings.MaxIngestConcurrency))
                            .IntBounds(1,100)
                            .WithHelp("Max parallel ingest tasks.");
                        if (!await Program.ui.ShowFormAsync(form)) return Command.Result.Cancelled;
                        Program.config.RagSettings = (RagSettings)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                },
                // GROUP: Chunking Settings
                new Command
                {
                    Name = "Chunking", Description = () => "Edit chunking strategy + sizes (form)",
                    Action = async () => await Log.MethodAsync(async ctx =>
                    {
                        var form = UiForm.Create("RAG – Chunking", Program.config.RagSettings);
                        // Provide a choice list for strategies
                        var strategies = Program.Chunkers.Keys.OrderBy(k=>k).ToArray();
                        ctx.Append(Log.Data.Choices, string.Join(", ", strategies));
                        form.AddChoice<RagSettings>("Chunking Strategy", strategies, m=> m.ChunkingStrategy, (m,v)=> m.ChunkingStrategy = v, nameof(RagSettings.ChunkingStrategy))
                            .WithHelp("Select which registered chunker to use.");
                        form.AddInt<RagSettings>("Chunk Size", m=> m.ChunkSize, (m,v)=> m.ChunkSize = v, nameof(RagSettings.ChunkSize))
                            .IntBounds(1,10000)
                            .WithHelp("Target characters per chunk.");
                        form.AddInt<RagSettings>("Max Tokens / Chunk", m=> m.MaxTokensPerChunk, (m,v)=> m.MaxTokensPerChunk = v, nameof(RagSettings.MaxTokensPerChunk))
                            .IntBounds(1,32000)
                            .WithHelp("Token count ceiling for a chunk.");
                        form.AddInt<RagSettings>("Max Line Length", m=> m.MaxLineLength, (m,v)=> m.MaxLineLength = v, nameof(RagSettings.MaxLineLength))
                            .IntBounds(1,32000)
                            .WithHelp("Maximum characters permitted per line before wrapping.");
                        form.AddInt<RagSettings>("Overlap", m=> m.Overlap, (m,v)=> m.Overlap = v, nameof(RagSettings.Overlap))
                            .IntBounds(0,100)
                            .WithHelp("Characters overlapping between consecutive chunks.");
                        if (!await Program.ui.ShowFormAsync(form))
                        {
                            ctx.Append(Log.Data.Message, "User cancelled chunking config.");
                            ctx.Succeeded();
                            return Command.Result.Cancelled;
                        }
                        Program.config.RagSettings = (RagSettings)form.Model!;
                        ctx.Append(Log.Data.Result, $"Selected chunking strategy: {Program.config.RagSettings.ChunkingStrategy}");
                        Engine.SetTextChunker(Program.config.RagSettings.ChunkingStrategy);
                        Config.Save(Program.config, Program.ConfigFilePath);
                        ctx.Succeeded();
                        return Command.Result.Success;
                    })
                },
                // GROUP: MMR Settings
                new Command
                {
                    Name = "MMR", Description = () => "Edit Maximal Marginal Relevance (form)",
                    Action = async () =>
                    {
                        var form = UiForm.Create("RAG – MMR", Program.config.RagSettings);
                        form.AddBool<RagSettings>("Use MMR", m=> m.UseMmr, (m,v)=> m.UseMmr = v, nameof(RagSettings.UseMmr))
                            .WithHelp("Toggle diversity-based re-ranking.");
                        form.AddDouble<RagSettings>("Lambda (0-1)", m=> m.MmrLambda, (m,v)=> m.MmrLambda = v, nameof(RagSettings.MmrLambda))
                            .IntBounds(0,1)
                            .WithHelp("Balance relevance (near 1) vs diversity (near 0).");
                        form.AddFloat<RagSettings>("Pool Multiplier", m=> m.MmrPoolMultiplier, (m,v)=> m.MmrPoolMultiplier = v, nameof(RagSettings.MmrPoolMultiplier))
                            .IntBounds(0,10)
                            .WithHelp("Candidate pool = TopK * multiplier.");
                        form.AddInt<RagSettings>("Min Extra Candidates", m=> m.MmrMinExtra, (m,v)=> m.MmrMinExtra = v, nameof(RagSettings.MmrMinExtra))
                            .IntBounds(0,100)
                            .WithHelp("Minimum extra candidates beyond TopK.");
                        if (!await Program.ui.ShowFormAsync(form)) return Command.Result.Cancelled;
                        Program.config.RagSettings = (RagSettings)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                }
            }
        };
    }
}
