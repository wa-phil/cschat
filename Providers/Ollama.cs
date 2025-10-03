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

    public async Task<List<string>> GetAvailableModelsAsync() => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        using var client = new HttpClient();
        try
        {
            var models = new List<string>();
            var resp = await client.GetStringAsync($"{config.Host}/api/tags");
            resp.ThrowIfNull("Response from Ollama API is null.");
            dynamic? parsed = resp!.FromJson<dynamic>();
            if (parsed == null || parsed!["models"] == null)
            {
                ctx.Append(Log.Data.Message, "No models found in response.");
                return models;
            }
            foreach (var model in parsed!["models"]!)
            {
                if (model != null && model!["name"] != null)
                    models.Add((string)model!["name"]!);
            }
            ctx.Succeeded(models.Count > 0);
            return models;
        }
        catch (Exception ex)
        {
            ctx.Failed("Failed to fetch models from host.", ex);
            return new List<string>();
        }
    });

    public async Task<string> PostChatAsync(Context Context, float temperature) => await Log.MethodAsync(async ctx =>
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
            return Array.Empty<float>();
        }
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            ctx.Failed("Received empty response from embedding API", Error.EmptyResponse);
            return Array.Empty<float>();
        }
        ctx.Append(Log.Data.Response, json);
        dynamic? parsed = json!.FromJson<dynamic>();
        if (null == parsed || null == parsed!["embedding"])
        {
            ctx.Failed("No embedding found in response", Error.EmptyResponse);
            return Array.Empty<float>();
        }
        var embedding = parsed!["embedding"] as IEnumerable<object>;
        if (embedding == null)
        {
            ctx.Failed("Embedding is null in response", Error.EmptyResponse);
            return Array.Empty<float>();
        }
        ctx.Append(Log.Data.Count, embedding.Count());
        ctx.Succeeded();
        return embedding.Select(Convert.ToSingle).ToArray();
    });

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default
        ) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var items = texts?.Select((t, i) => (t, i)).ToList() ?? new();
        if (items.Count == 0) return Array.Empty<float[]>();

        int maxConc = Program.config.RagSettings.MaxEmbeddingConcurrency > 0
            ? Program.config.RagSettings.MaxEmbeddingConcurrency
            : 6;

        using var gate = new SemaphoreSlim(maxConc);
        var results = new float[items.Count][];

        await Task.WhenAll(items.Select(async x =>
        {
            await gate.WaitAsync();
            try
            {
                results[x.i] = await GetEmbeddingAsync(x.t); // uses your existing single-call method
            }
            finally
            {
                gate.Release();
            }
        }));

        ctx.Append(Log.Data.Count, results.Length);
        ctx.Succeeded();
        return results;
    });
    
}