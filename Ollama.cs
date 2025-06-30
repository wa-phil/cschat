using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TinyJson;

[ProviderName("Ollama")]
public class Ollama : IChatProvider
{
    public async Task<List<string>> GetAvailableModelsAsync(Config config)
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

    public async Task<string> PostChatAsync(Config config, List<ChatMessage> history, string userInput)
    {
        history.Add(new ChatMessage { Role = "user", Content = userInput });
        using var client = new HttpClient();
        var requestBody = new
        {
            model = config.Model,
            messages = history.ToArray()
        };

        var content = new StringContent(requestBody.ToJson(), Encoding.UTF8, "application/json");
        try
        {
            var response = await client.PostAsync($"{config.Host}/v1/chat/completions", content);
            if (!response.IsSuccessStatusCode)
                return $"Error: {response.StatusCode}";

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
