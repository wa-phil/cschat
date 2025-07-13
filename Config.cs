using System;
using System.IO;

public class RagSettings
{
    public string ChunkingStrategy { get; set; } = "LineChunk";
    public int ChunkSize { get; set; } = 100;
    public int Overlap { get; set; } = 5;
    public string QueryPrompt { get; set; } = "Extract a concise list of keywords that would appear in relevant documents to answer this question.";
    public bool NormalizeEmbeddings { get; set; } = true;
    public int TopK { get; set; } = 3; // as in k-nearest neighbors
    public string EmbeddingModel { get; set; } = "nomic-embed-text"; // Default embedding model
}

public class Config
{
    public string Provider { get; set; } = "Ollama";
    public string Model { get; set; } = string.Empty; // Ensure non-nullable property is initialized
    public string Host { get; set; } = "http://localhost:11434";
    public int MaxTokens { get; set; } = 32000;
    public float Temperature { get; set; } = 0.7f;
    public string SystemPrompt { get; set; } = "You are a helpful system agent.  When answering questions, if you do not know the answer, tell the user as much. Always strive to be honest and truthful.  You have access to an array of tools that you can use to get the information you need to help the user. These tools can list the contents of a directory, read metadata about files, read file contents, etc...";

    public RagSettings RagSettings { get; set; } = new RagSettings();
    public bool AzureAuthVerboseLoggingEnabled { get; set; } = false;
    public int MaxSteps { get; set; } = 25; // Maximum number of steps for planning

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