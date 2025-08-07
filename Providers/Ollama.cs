using System;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq; // Add using directive for LINQ

[IsConfigurable("Ollama")]
public class Ollama : IChatProvider, IEmbeddingProvider, IGraphProvider
{
    private Config config = new Config(); // Initialize non-nullable field to avoid null reference
    private readonly HttpClient client = new HttpClient();
    public Ollama(Config cfg)
    {
        this.config = cfg ?? throw new ArgumentNullException(nameof(cfg));
        client.BaseAddress = new Uri(config.Host);
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        using var client = new HttpClient();
        try
        {
            var models = new List<string>();
            var resp = await client.GetStringAsync($"{config.Host}/api/tags");
            resp.ThrowIfNull("Response from Ollama API is null.");
            dynamic? parsed = resp!.FromJson<dynamic>();
            if (parsed == null || parsed!["models"] == null)
            {
                Console.WriteLine("No models found in response.");
                return models;
            }
            foreach (var model in parsed!["models"]!)
            {
                if (model != null && model!["name"] != null)
                    models.Add((string)model!["name"]!);
            }
            return models;
        }
        catch
        {
            Console.WriteLine("Failed to fetch models from host.");
            return new List<string>();
        }
    }

    public async Task<string> PostChatAsync(Context Context, float temperature) => await Log.MethodAsync(async ctx=>
    {
        ctx.OnlyEmitOnFailure();
        string respJson = string.Empty;
        var requestBody = new
        {
            model = config.Model,
            messages = Context.Messages?.Select(msg => new
            {
                role = msg.Role.ToString().ToLower(),
                content = msg.Content
            }).ToList(),
            stream = false,
            temperature = temperature,
            max_tokens = config.MaxTokens,
        };
        var content = new StringContent(requestBody.ToJson(), Encoding.UTF8, "application/json");
        try
        {
            var response = await client.PostAsync($"{config.Host}/v1/chat/completions", content);
            if (!response.IsSuccessStatusCode)
            {
                ctx.Append(Log.Data.Response, response.StatusCode.ToString());
                return $"Error: {response.StatusCode}";
            }

            respJson = await response.Content.ReadAsStringAsync();
            respJson.ThrowIfNull("Response from Ollama API is null.");
            ctx.Append(Log.Data.Input, requestBody.ToJson());
            ctx.Append(Log.Data.Result, respJson);
            dynamic? respObj = respJson!.FromJson<dynamic>();
            var result = respObj?["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
            ctx.Succeeded();
            return result;
        }
        catch (Exception ex)
        {
            ctx.Failed("Exception during PostChatAsync", ex);
            Console.WriteLine($"Exception occurred: {ex.Message}");
            throw;
        }
    });
    
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var request = new
        {
            model = Program.config.RagSettings.EmbeddingModel,
            prompt = text
        };

        var content = new StringContent(request.ToJson(), Encoding.UTF8, "application/json");
        try
        {
            var response = await client.PostAsync($"{config.Host}/api/embeddings", content);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to get embedding: {response.StatusCode}");
                return Array.Empty<float>();
            }
            var json = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                Console.WriteLine("Received empty response from embedding API.");
                return Array.Empty<float>();
            }
            dynamic? parsed = json!.FromJson<dynamic>();
            if (null == parsed || null == parsed!["embedding"])
            {
                Console.WriteLine("No embedding found in response.");
                return Array.Empty<float>();
            }
            var embedding = parsed!["embedding"] as IEnumerable<object>;
            if (embedding == null)
            {
                Console.WriteLine("Embedding is null in response.");
                return Array.Empty<float>();
            }

            return embedding.Select(Convert.ToSingle).ToArray();
           
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception during embedding request: {ex.Message}");
            return Array.Empty<float>();
        }
    }

    public async Task GetEntitiesAndRelationshipsAsync(string content, string reference) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Reference, reference);

        try
        {
            var systemPrompt = @"You are an expert at extracting entities and relationships from text. 
Extract all important entities (people, places, organizations, concepts, etc.) and their relationships from the provided text.

For each entity, identify:
- Entity name
- Entity type (Person, Organization, Location, Concept, etc.)
- Key attributes or descriptions

For each relationship, identify:
- Source entity
- Target entity  
- Relationship type (works_for, located_in, part_of, etc.)
- Relationship description

Format your response as JSON with 'entities' and 'relationships' arrays.

Example:
{
  ""entities"": [
    {""name"": ""John Smith"", ""type"": ""Person"", ""attributes"": ""Senior Developer""},
    {""name"": ""Acme Corp"", ""type"": ""Organization"", ""attributes"": ""Technology company""}
  ],
  ""relationships"": [
    {""source"": ""John Smith"", ""target"": ""Acme Corp"", ""type"": ""works_for"", ""description"": ""employed as Senior Developer""}
  ]
}";

            var tempContext = new Context(systemPrompt);
            tempContext.AddUserMessage($"Source: {reference}\n\nText: {content}");
            
            var result = await PostChatAsync(tempContext, 0.1f); // Low temperature for consistent extraction
            
            // Print the raw JSON response
            Console.WriteLine($"\n=== Extracted from {reference} ===");
            Console.WriteLine("Raw JSON Response:");
            Console.WriteLine(result);
            
            var graphDto = GraphStoreManager.JsonToGraphDto(result);
            if (graphDto != null) { GraphStoreManager.ParseGraphFromJson(graphDto); }

            Console.WriteLine("=================================\n");

            ctx.Append(Log.Data.Result, $"Extracted {GraphStoreManager.Graph.EntityCount} entities and {GraphStoreManager.Graph.RelationshipCount} relationships from {reference}");
            ctx.Succeeded();
            
            Console.WriteLine("=================================\n");
        }
        catch (Exception ex)
        {
            ctx.Failed($"Failed to extract entities and relationships", ex);
            Console.WriteLine($"Failed to extract entities and relationships: {ex.Message}");
        }
    });

}
