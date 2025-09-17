using System;
using System.IO;
using System.Collections.Generic;

public class ChatThreadSettings
{
    public string RootDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, ".threads");
    public string DefaultNewThreadName { get; set; } = "New chat";
    public string? ActiveThreadName { get; set; }
}

public class FileFilterRules
{
    public List<string> Exclude { get; set; } = new();
    public List<string> Include { get; set; } = new(); // optional
}

public class RagSettings
{
    public string ChunkingStrategy { get; set; } = "SmartChunk";
    public int ChunkSize { get; set; } = 100;
    public int Overlap { get; set; } = 5;
    public bool NormalizeEmbeddings { get; set; } = true;
    public int TopK { get; set; } = 3; // as in k-nearest neighbors
    public bool UseEmbeddings { get; set; } = true; // whether to use embeddings for RAG
    public string EmbeddingModel { get; set; } = "nomic-embed-text"; // Default embedding model
    public int MaxTokensPerChunk { get; set; } = 8000;
    public int MaxEmbeddingConcurrency { get; set; } = 8; // Maximum number of concurrent embedding requests
    public int MaxIngestConcurrency { get; set; } = 10; // Maximum number of concurrent ingestion tasks
    public int MaxLineLength { get; set; } = 1600; // because there should be a limit, approximately 400 tokens in a line is a LOT.
    public bool UseMmr { get; set; } = true;      // toggle on/off
    public double MmrLambda { get; set; } = 0.55;  // relevance vs. diversity
    public float MmrPoolMultiplier { get; set; } = 4; // candidate pool = topK * multiplier
    public int MmrMinExtra { get; set; } = 4;       // at least +4 candidates
    public int TopKForParsing { get; set; } = 2; // how many top results to use for parsing

    public List<string> SupportedFileTypes { get; set; } = new List<string>
    {
        ".bash", ".bat",
        ".c", ".cpp", ".cs", ".csproj", ".csv",
        ".h", ".html",
        ".ignore",
        ".js",
        ".log",
        ".md",
        ".py",
        ".sh", ".sln",
        ".ts", ".txt",
        ".xml",
        ".yml"
    };
    public Dictionary<string, FileFilterRules> FileFilters { get; set; } = new();
}

public class UserManagedDataConfig
{
    public Dictionary<string, List<Dictionary<string, object>>> TypedData { get; set; } = new();
}

public class MailSettings
{
    public int MaxEmailsToProcess { get; set; } = 25; // Maximum number of emails to process in one go
    public int LookbackWindow { get; set; } = 30; // Default lookback window for fetching emails (in days)
    public int LookbackCount { get; set; } = 100; // Default maximum number of emails to fetch in one go
}

public class Config
{
    public string Provider { get; set; } = "Ollama";
    public string Model { get; set; } = string.Empty; // Ensure non-nullable property is initialized
    public string Host { get; set; } = "http://localhost:11434";
    public int MaxTokens { get; set; } = 32000;
    public float Temperature { get; set; } = 0.7f;
    public string SystemPrompt { get; set; } = "You are a helpful system agent.  When answering questions, if you do not know the answer, tell the user as much. Always strive to be honest and truthful.  You have access to an array of tools that you can use to get the information you need to help the user. These tools can list the contents of a directory, read metadata about files, read file contents, etc...";

    public string DefaultDirectory { get; set; } = Directory.GetCurrentDirectory();

    public RagSettings RagSettings { get; set; } = new RagSettings();

    public bool VerboseEventLoggingEnabled { get; set; } = false;
    public int MaxSteps { get; set; } = 25; // Maximum number of steps for planning
    public int MaxMenuItems { get; set; } = 10; // Maximum number of menu items to display at once

    public ChatThreadSettings ChatThreadSettings { get; set; } = new ChatThreadSettings();
    public MailSettings MailSettings { get; set; } = new MailSettings();

    public Dictionary<string, bool> EventSources { get; set; } = new Dictionary<string, bool>
    {
        { "Microsoft-Extensions-DependencyInjection", true },
        { "System.Diagnostics.Eventing.FrameworkEventSource", true },
        { "System.Threading.Tasks.TplEventSource", true },
        { "Microsoft-Diagnostics-DiagnosticSource", true },
        { "Private.InternalDiagnostics.System.Net.Sockets", true },
        { "Private.InternalDiagnostics.System.Net.Http", true },
        { "System.Net.NameResolution", true },
        { "System.Net.Http", true },
    };

    public Dictionary<string, bool> Subsystems { get; set; } = new Dictionary<string, bool>
    {
        { "Ado", false },
        { "Mcp", true },
        { "Kusto", true},
    };

    public AdoConfig Ado { get; set; } = new AdoConfig();
    public UserManagedDataConfig UserManagedData { get; set; } = new UserManagedDataConfig();

    public static Config Load(string configFilePath)
    {
        if (File.Exists(configFilePath))
        {
            Console.WriteLine($"Loading configuration from {configFilePath}");
            var json = File.ReadAllText(configFilePath);
            Console.WriteLine($"Configuration loaded: {json}");
            return json.FromJson<Config>() ?? new Config();
        }
        return new Config();
    }

    public static void Save(Config config, string configFilePath)
    {
        Console.WriteLine($"Saving configuration to {configFilePath}");
        var json = config.ToJson();
        File.WriteAllText(configFilePath, json);
    }
}