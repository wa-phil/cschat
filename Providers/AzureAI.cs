using Azure;
using System;

using System.IO;
using System.Linq;
using System.Text;
using OpenAI.Chat;
using Azure.Identity;
using Azure.AI.OpenAI;
using System.Threading.Tasks;
using System.Collections.Generic;


[IsConfigurable("AzureAI")]
public class AzureAI : IChatProvider, IEmbeddingProvider
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
            if (chatClient == null)
                throw new InvalidOperationException("chatClient is not initialized.");
            var completionUpdates = chatClient.CompleteChatStreaming(chatHistory);
            StringBuilder sb = new StringBuilder();

            foreach (var completionUpdate in completionUpdates)
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
            Console.WriteLine($"Error: {ex.Message}.\n Validate that you have access to the Azure OpenAI service and that the model '{config?.Model}' is available.");
        }
        await Task.CompletedTask;
        return ret ?? string.Empty;
    }

    public async Task<float[]> GetEmbeddingAsync(string text) => await Log.MethodAsync(async ctx =>
    {
        try
        {
            var embeddingClient = azureClient?.GetEmbeddingClient("text-embedding-3-large");
            embeddingClient.ThrowIfNull("Embedding client could not be retrieved.");

            var response = await embeddingClient!.GenerateEmbeddingAsync(text);
            return response.Value.ToFloats().ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get embedding from AzureAI: {ex.Message}");
            return Array.Empty<float>();
        }
    });
}
