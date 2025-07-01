using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TinyJson;

[ProviderName("Ollama")]
public class Ollama : IChatProvider
{
    private Config config = null;
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
            var resp = await client.GetStringAsync($"{config.Host}/api/tags");
            dynamic parsed = resp.FromJson<dynamic>();
            var models = new List<string>();
            foreach (var model in parsed["models"])
            {
                models.Add((string)model["name"]);
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
            messages = memory.Messages.ConvertAll(msg => new
            {
                role = msg.Role.ToString().ToLower(),
                content = msg.Content
            }),
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
            dynamic respObj = respJson.FromJson<dynamic>();
            return respObj["choices"][0]["message"]["content"];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception occurred: {ex.Message}");
            throw;
        }
    }
}
