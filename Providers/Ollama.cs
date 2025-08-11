using System;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq; // Add using directive for LINQ

[IsConfigurable("Ollama")]
public class Ollama : IChatProvider, IEmbeddingProvider
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
            messages = Context.Messages().Select(msg => new
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
            var response = await client.PostAsync($"{config.Host}/api/chat", content);
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
            var result = respObj?["message"]?["content"]?.ToString() ?? string.Empty;
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

    public async Task<float[]> GetEmbeddingAsync(string text) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Input, text);
        ctx.Append(Log.Data.Host, config.Host);
        ctx.Append(Log.Data.Model, config.RagSettings.EmbeddingModel);

        var request = new
        {
            model = Program.config.RagSettings.EmbeddingModel,
            prompt = text
        };

        var content = new StringContent(request.ToJson(), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{config.Host}/api/embeddings", content);
        if (!response.IsSuccessStatusCode)
        {
            ctx.Failed($"Failed to get embedding: {response.StatusCode}", Error.ToolFailed);
            Console.WriteLine($"Failed to get embedding: {response.StatusCode}");
            return Array.Empty<float>();
        }
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            ctx.Failed("Received empty response from embedding API", Error.EmptyResponse);
            Console.WriteLine("Received empty response from embedding API.");
            return Array.Empty<float>();
        }
        ctx.Append(Log.Data.Response, json);
        dynamic? parsed = json!.FromJson<dynamic>();
        if (null == parsed || null == parsed!["embedding"])
        {
            Console.WriteLine("No embedding found in response.");
            ctx.Failed("No embedding found in response", Error.EmptyResponse);
            return Array.Empty<float>();
        }
        var embedding = parsed!["embedding"] as IEnumerable<object>;
        if (embedding == null)
        {
            ctx.Failed("Embedding is null in response", Error.EmptyResponse);
            Console.WriteLine("Embedding is null in response.");
            return Array.Empty<float>();
        }        
        ctx.Append(Log.Data.Count, embedding.Count());
        ctx.Succeeded();
        return embedding.Select(Convert.ToSingle).ToArray();
    });
}
