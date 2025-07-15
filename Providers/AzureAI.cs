using Azure;
using System;
using System.IO;
using System.Linq;
using System.Text;
using OpenAI.Chat;
using Azure.Identity;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using System.Threading.Tasks;
using Azure.Core.Diagnostics;
using System.Diagnostics.Tracing;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

// Custom EventSourceListener that bridges Azure SDK events to our Log class
public class AzureLogEventListener : EventListener
{
    protected override void OnEventSourceCreated(EventSource eventSource) => Log.Method(ctx =>
    {
        ctx.Append(Log.Data.Name, eventSource.Name);
        bool enabled = false;
        if (Program.config.AzureAuthVerboseLoggingEnabled && eventSource.Name.StartsWith("Azure-"))
        {
            EnableEvents(eventSource, EventLevel.Verbose);
            enabled = true;
        }
        ctx.Append(Log.Data.Enabled, enabled);
        ctx.Succeeded();
    });

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {        
        if (eventData.EventSource.Name.StartsWith("Azure-"))
        {
            var level = eventData.Level == EventLevel.Error ? Log.Level.Error : Log.Level.Information;

            using var ctx = new Log.Context(level);
            ctx.OnlyEmitOnFailure();
            ctx.Append(Log.Data.Level, level);
            ctx.Append(Log.Data.Source, $"Azure.{eventData.EventSource.Name}");
            ctx.Append(Log.Data.Message, $"[{eventData.EventName}] {eventData.Message}");
            if (eventData.Payload != null && eventData.Payload.Count > 0)
            {
                var payload = string.Join(", ", eventData.Payload);
                ctx.Append(Log.Data.Message, $"[{eventData.EventName}] {eventData.Message} - Payload: {payload}");
            }
            ctx.Succeeded(eventData.Level != EventLevel.Error);
        }
    }
}

[IsConfigurable("AzureAI")]
public class AzureAI : IChatProvider, IEmbeddingProvider
{
    private Config config = null!;
    private AzureOpenAIClient azureClient = null!;
    private ChatClient chatClient = null!;
    private EmbeddingClient embeddingClient = null!;
    private static AzureLogEventListener? _eventSourceListener;

    public AzureAI(Config cfg) => Log.Method(ctx =>
    {
        this.config = cfg ?? throw new ArgumentNullException(nameof(cfg));

        ctx.Append(Log.Data.Provider, "AzureAI");
        ctx.Append(Log.Data.Model, config.Model);
        ctx.Append(Log.Data.Message, $"Initializing Azure OpenAI client for {config.Host}");
        
        // Create and configure our custom event listener to capture Azure SDK events
        if (_eventSourceListener == null)
        {
            _eventSourceListener = new AzureLogEventListener();
            ctx.Append(Log.Data.Message, "Azure event source listener initialized");
        }

        // Enable Azure Core diagnostics logging based on configuration
        using (AzureEventSourceListener.CreateConsoleLogger(EventLevel.Verbose))
        {
            // Configure credential options with verbose logging
            var credentialOptions = new DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = false,
                ExcludeAzureCliCredential = false,
                ExcludeAzurePowerShellCredential = false,
                ExcludeVisualStudioCredential = false,
                ExcludeManagedIdentityCredential = false,
                //ExcludeVisualStudioCodeCredential = false, // apparently is deprecated.
                ExcludeInteractiveBrowserCredential = false, // Disable interactive for headless scenarios
            };

            ctx.Append(Log.Data.Message, "Creating Azure OpenAI client with DefaultAzureCredential");
            azureClient = new AzureOpenAIClient(new Uri(config.Host), new DefaultAzureCredential(credentialOptions));
            chatClient = azureClient.GetChatClient(config.Model);
            embeddingClient = azureClient.GetEmbeddingClient(config.RagSettings.EmbeddingModel);
            ctx.Append(Log.Data.Message, "Azure OpenAI client initialized successfully");
        }
        
        ctx.Succeeded();
    });

    public Task<List<string>> GetAvailableModelsAsync()
    {
        // There currently isn't a direct API to list models in Azure OpenAI, so we return a default model.
        // You can modify this to fetch models from a configuration or a known list.
        return Task.FromResult(new List<string>() { "esai-gpt4-32k" });
    }

    public async Task<string> PostChatAsync(Memory memory, float _ /* TODO: figure out how temp plumbs through here*/) => await Log.MethodAsync(async ctx =>
    {
        await Task.CompletedTask; // Ensure we don't block the thread unnecessarily
        var chatHistory = memory.Messages.Select<ChatMessage, OpenAI.Chat.ChatMessage>(msg =>
            msg.Role switch
            {
                Roles.User => new UserChatMessage(msg.Content),
                Roles.Assistant => new AssistantChatMessage(msg.Content),
                Roles.System => new SystemChatMessage(msg.Content),
                Roles.Tool => new AssistantChatMessage(msg.Content),
                _ => throw new NotSupportedException($"Role '{msg.Role}' is not supported in Azure OpenAI.")
            }).ToList(); // Explicitly specify type arguments

        ctx.Append(Log.Data.Model, config.Model ?? "unknown");
        chatClient.ThrowIfNull("chatClient is not initialized.");
        ctx.Append(Log.Data.Count, chatHistory.Count);

        try
        {
            // Stream the response from the model            
            var completionUpdates = chatClient!.CompleteChatStreaming(chatHistory);
            StringBuilder sb = new StringBuilder();

            foreach (var completionUpdate in completionUpdates)
            {
                foreach (var contentPart in completionUpdate.ContentUpdate)
                {
                    sb.Append(contentPart.Text);
                }
            }
            
            var result = sb.ToString();
            ctx.Append(Log.Data.Result, $"Response length: {result.Length} characters");
            ctx.Succeeded();
            return result;
        }
        catch (Exception ex)
        {
            ctx.Failed($"Azure OpenAI request failed", ex);
            Console.WriteLine($"Error: {ex.Message}.\n Validate that you have access to the Azure OpenAI service and that the model '{config.Model}' is available.");
            return string.Empty;
        }
    });

    public async Task<float[]> GetEmbeddingAsync(string text) => await Log.MethodAsync(
        retryCount: 2,
        shouldRetry: e => e is TimeoutException,
        func: async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        embeddingClient.ThrowIfNull("Embedding client could not be retrieved.");
        try
        {
            ctx.Append(Log.Data.Model, Program.config.RagSettings.EmbeddingModel);
            var response = await embeddingClient!.GenerateEmbeddingAsync(text);
            ctx.Succeeded();
            return response.Value.ToFloats().ToArray();
        }
        catch (Exception ex)
        {
            ctx.Failed($"Failed to get embedding from Azure OpenAI", ex);
            Console.WriteLine($"Failed to get embedding from AzureAI: {ex.Message}");
            return Array.Empty<float>();
        }
    });
}
