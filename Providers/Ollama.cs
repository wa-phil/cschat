using System;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq; // Add using directive for LINQ

[ProviderName("Ollama")]
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

    public async Task<string> PostChatAsync(Memory memory)
    {
        var requestBody = new
        {
            model = config.Model,
            messages = memory.Messages?.Select(msg => new
            {
                role = msg.Role.ToString().ToLower(),
                content = msg.Content
            }).ToList(),
            stream = false,
            temperature = config.Temperature,
            max_tokens = config.MaxTokens,
        };
        var content = new StringContent(requestBody.ToJson(), Encoding.UTF8, "application/json");
        try
        {
            var response = await client.PostAsync($"{config.Host}/v1/chat/completions", content);
            if (!response.IsSuccessStatusCode)
            {
                return $"Error: {response.StatusCode}";
            }

            var respJson = await response.Content.ReadAsStringAsync();
            respJson.ThrowIfNull("Response from Ollama API is null.");
            dynamic? respObj = respJson!.FromJson<dynamic>();
            return respObj?["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception occurred: {ex.Message}");
            throw;
        }
    }
    
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var request = new
        {
            model = config.Model,
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

}
