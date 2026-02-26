# Providers

Located in `/Providers/`. Each provider implements `IChatProvider` and optionally `IEmbeddingProvider`. Providers are discovered via `[IsConfigurable("name")]` and registered in the DI container at startup.

## Interfaces

### IChatProvider

```csharp
public interface IChatProvider
{
    Task<List<string>> GetAvailableModelsAsync();
    Task<string> PostChatAsync(Context history, float temperature);
}
```

`PostChatAsync` receives the full conversation context (system message + RAG chunks + message history) and returns the model's response as a string.

### IEmbeddingProvider

```csharp
public interface IEmbeddingProvider
{
    Task<float[]> GetEmbeddingAsync(string text);
    Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct);
}
```

Both providers implement this interface. The active provider is cast to `IEmbeddingProvider` at RAG ingest time and query time.

## Ollama

**File:** `Providers/Ollama.cs`
**Registration name:** `"Ollama"` (`[IsConfigurable("Ollama")]`)
**Default host:** `http://localhost:11434`

### Chat

Sends a POST to `/api/chat` with `stream = false`. The request body includes `model`, `messages`, `temperature`, and `max_tokens`. Messages are mapped from `ChatMessage` to plain `{role, content}` objects. The response is parsed from the `message.content` field of the JSON response.

### Embeddings

Sends a POST to `/api/embeddings` with `model` (from `config.RagSettings.EmbeddingModel`) and `prompt`. Returns the `embedding` array from the JSON response.

Batch embeddings (`GetEmbeddingsAsync`) fan out individual calls with a `SemaphoreSlim` limited to `config.RagSettings.MaxEmbeddingConcurrency`.

### Authentication

None. Ollama is a local server; no credentials are required.

## AzureAI

**File:** `Providers/AzureAI.cs`
**Registration name:** `"AzureAI"` (`[IsConfigurable("AzureAI")]`)
**Auth:** `DefaultAzureCredential`

### Construction

The `AzureAI` constructor creates an `AzureOpenAIClient` using `DefaultAzureCredential` with all standard credential sources enabled (environment, Azure CLI, PowerShell, Visual Studio, Managed Identity, interactive browser). A `ChatClient` and `EmbeddingClient` are obtained from the parent client for the configured model and embedding model respectively.

### Chat

Uses `chatClient.CompleteChatStreaming(chatHistory)` for streaming token delivery. Role mapping:

| CSChat Role | Azure OpenAI type |
|-------------|-------------------|
| `User` | `UserChatMessage` |
| `Assistant` | `AssistantChatMessage` |
| `System` | `SystemChatMessage` |
| `Tool` | `AssistantChatMessage` |

### Embeddings

`GetEmbeddingAsync` calls `embeddingClient.GenerateEmbeddingAsync(text)` with up to 2 retries on HTTP 429 (rate limit, 2 s backoff) and `TimeoutException` (parses retry-after seconds).

`GetEmbeddingsAsync` batches requests using `MaxEmbeddingConcurrency` as the slice size per API call. Each slice gets a 90-second per-slice timeout. On 429 or 5xx, exponential backoff is applied (250 ms × 2^attempt, up to 4 attempts).

### Model Listing

`GetAvailableModelsAsync` returns a static list `["esai-gpt4-32k"]`. Azure OpenAI does not expose a public list-models API.

## Switching Providers

Call `Engine.SetProvider("Ollama")` or `Engine.SetProvider("AzureAI")` at runtime. This resolves the provider from the DI container and saves the name to `config.Provider`. The `ProviderCommands` group provides a menu command for switching providers interactively.

The `config.Host` value must point to the correct endpoint for the selected provider (Ollama: local URL; AzureAI: Azure OpenAI endpoint URL).
