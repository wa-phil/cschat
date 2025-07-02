using Azure;
using System;
using TinyJson;
using System.IO;
using System.Text;
using OpenAI.Chat;
using Azure.Identity;
using Azure.AI.OpenAI;
using System.Threading.Tasks;
using System.Collections.Generic;


[ProviderName("AzureAI")]
public class AzureAI : IChatProvider //, IEmbeddingProvider // TODO: uncomment and implement to support RAG scenario.
{
    private Config? config = null;
    private AzureOpenAIClient? azureClient = null;
    private ChatClient? chatClient = null;

    public AzureAI(Config cfg)
    {
        this.config = cfg ?? throw new ArgumentNullException(nameof(cfg));

        azureClient = new AzureOpenAIClient(new Uri(config.Host), new DefaultAzureCredential());
        chatClient = azureClient.GetChatClient(config.Model);
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        await Task.CompletedTask; // Simulate asynchronous behavior
        // There currently isn't a direct API to list models in Azure OpenAI, so we return a default model.
        // You can modify this to fetch models from a configuration or a known list.
        return new List<string>() { "esai-gpt4-32k" };
    }

    public async Task<string> PostChatAsync(Memory memory)
    {
        var chatHistory = memory.Messages.Select<ChatMessage, OpenAI.Chat.ChatMessage>(msg =>
            msg.Role switch
            {
                Roles.User => new UserChatMessage(msg.Content),
                Roles.Assistant => new AssistantChatMessage(msg.Content),
                Roles.System => new SystemChatMessage(msg.Content),
                _ => throw new ArgumentException("Unknown role in chat message")
            }).ToList(); // Explicitly specify type arguments

        string? ret = null;
        try
        {
            // Stream the response from the model
            var completionUpdates = chatClient!.CompleteChatStreaming(chatHistory);
            StringBuilder sb = new StringBuilder();

            foreach (var completionUpdate in completionUpdates) // Replace await foreach with regular foreach
            {
                foreach (var contentPart in completionUpdate.ContentUpdate)
                {
                    sb.Append(contentPart.Text);
                }
            }
            ret = sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}.\n Validate that you have access via: https://coreidentity.microsoft.com/manage/Entitlement/entitlement/wcdconsumer-ptx3");
        }
        await Task.CompletedTask; // Add await to simulate asynchronous behavior
        return ret ?? string.Empty; // Handle possible null reference return
    }

    // public async Task<float[]> GetEmbeddingAsync(string text)
    // {
    //     try
    //     {
    //         var client = azureClient.GetEmbeddingClient(config.Model);
    //         client.ThrowIfNull("Embedding client is not initialized. Ensure the model supports embeddings.");
    //         var response = await client.EmbedAsync(RequestContent.Create(text));

    //         var embedding = response.Value.Data[0].Embedding;
    //         return embedding.ToArray();
    //     }
    //     catch (Exception ex)
    //     {
    //         Console.WriteLine($"Failed to get embedding from AzureAI: {ex.Message}");
    //         return Array.Empty<float>();
    //     }
    // }
}
