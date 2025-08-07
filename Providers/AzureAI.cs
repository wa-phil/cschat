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

[IsConfigurable("AzureAI")]
public class AzureAI : IChatProvider, IEmbeddingProvider, IGraphProvider
{
    private Config config = null!;
    private AzureOpenAIClient azureClient = null!;
    private ChatClient chatClient = null!;
    private EmbeddingClient embeddingClient = null!;


    public AzureAI(Config cfg) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        this.config = cfg ?? throw new ArgumentNullException(nameof(cfg));

        ctx.Append(Log.Data.Provider, "AzureAI");
        ctx.Append(Log.Data.Model, config.Model);
        ctx.Append(Log.Data.Message, $"Initializing Azure OpenAI client for {config.Host}");

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

    public async Task<string> PostChatAsync(Context Context, float _ /* TODO: figure out how temp plumbs through here*/) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        await Task.CompletedTask; // Ensure we don't block the thread unnecessarily
        var chatHistory = Context.Messages().Select<ChatMessage, OpenAI.Chat.ChatMessage>(msg =>
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


    public async Task GetEntitiesAndRelationshipsAsync(string content, string reference)
    {
        await Log.MethodAsync(async ctx =>
        {
            ctx.OnlyEmitOnFailure();
            ctx.Append(Log.Data.Reference, reference);

            try
            {
                var chatHistory = new List<OpenAI.Chat.ChatMessage>
                {
                    new SystemChatMessage(@"You are an expert at extracting entities and relationships from text. 
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
}"),
                    new UserChatMessage($"Source: {reference}\n\nText: {content}")
                };

                chatClient.ThrowIfNull("chatClient is not initialized.");

                // Use streaming completion like the existing PostChatAsync method
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

                // Print the raw JSON response
                Console.WriteLine($"\n=== Extracted from {reference} ===");
                Console.WriteLine("Raw JSON Response:");
                Console.WriteLine($"[Original length: {result.Length}]");
                Console.WriteLine(result);
                
                // Clean up the result - remove common JSON wrapper patterns
                var cleanResult = result.Trim();
                
                // Remove markdown code block markers
                if (cleanResult.StartsWith("```json"))
                {
                    cleanResult = cleanResult.Substring(7).Trim();
                }
                if (cleanResult.StartsWith("```"))
                {
                    cleanResult = cleanResult.Substring(3).Trim();
                }
                if (cleanResult.EndsWith("```"))
                {
                    cleanResult = cleanResult.Substring(0, cleanResult.Length - 3).Trim();
                }
                
                Console.WriteLine($"\n[Cleaned length: {cleanResult.Length}]");
                Console.WriteLine("Cleaned JSON:");
                Console.WriteLine(cleanResult);

                var graphDto = GraphStoreManager.JsonToGraphDto(cleanResult);
                if (graphDto != null) 
                { 
                    GraphStoreManager.ParseGraphFromJson(graphDto);
                    Console.WriteLine($"Successfully processed {graphDto.Entities?.Count ?? 0} entities and {graphDto.Relationships?.Count ?? 0} relationships");
                }
                else
                {
                    Console.WriteLine("Failed to parse JSON response into GraphDto");
                }

                Console.WriteLine("=================================\n");

                ctx.Append(Log.Data.Result, $"Extracted {GraphStoreManager.Graph.EntityCount} entities and {GraphStoreManager.Graph.RelationshipCount} relationships from {reference}");
                ctx.Succeeded();
            }
            catch (Exception ex)
            {
                ctx.Failed($"Failed to extract entities and relationships", ex);
                Console.WriteLine($"Failed to extract entities and relationships: {ex.Message}");
            }
        });
    }
}