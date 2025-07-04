using System;
using System.IO;

public class RagSettings
{
    public string ChunkingStrategy { get; set; } = "LineChunk";
    public int ChunkSize { get; set; } = 100;
    public int Overlap { get; set; } = 5;
    public string QueryPrompt { get; set; } = "Generate a concise natural language query that captures the user's intent for retrieval.";
}

public class Config
{
    public string Provider { get; set; } = "Ollama";
    public string Model { get; set; } = string.Empty; // Ensure non-nullable property is initialized
    public string Host { get; set; } = "http://localhost:11434";
    public int MaxTokens { get; set; } = 4000;
    public float Temperature { get; set; } = 0.7f;
    public string SystemPrompt { get; set; } = "You are a helpful assistant.";

    public RagSettings RagSettings { get; set; } = new RagSettings();

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