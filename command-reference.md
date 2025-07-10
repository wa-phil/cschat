---

# cschat Command Reference

This guide introduces the key commands in **cschat** and organizes them to help you quickly understand their purpose and usage. The commands are grouped by functionality (chat, providers, system management, RAG, etc.) and explained step-by-step for ease of use.

---

## **1. Managing the Chat**

These commands let you interact with and manage your chat history.

### Commands:
- **`chat show`**: Display the current chat conversation history.
- **`chat clear`**: Wipe the chat history and reset the session.
- **`chat save`**: Save the chat history to a file for later use.
- **`chat load`**: Load a previously saved chat history from a file.

### Usage Example:
1. **View the chat:** Run `chat show`.
2. **Save the conversation:** Use `chat save` to save the chat to your desired file path.
3. **Clear the chat:** Start fresh with `chat clear`.
4. **Reload a saved conversation:** Use `chat load` to import a saved chat history.

---

## **2. Configuring and Managing Providers**

Providers control *how* the language model generates responses. Use these commands to switch between providers, select models, and adjust their behavior.

### Commands:
- **`provider select`**: Choose the underlying language model provider (e.g., Azure, Ollama).
- **`provider model`**: Choose a specific model available for the selected provider.
- **`provider host`**: Update the host for the **Ollama** provider.
- **`provider system`**: Customize the **system prompt** that guides the model's behavior.
- **`provider temp`**: Adjust the model’s **response creativity** (`0.0` for deterministic, `1.0` for creative).
- **`provider max-tokens`**: Set the maximum token limit (controls length) for model responses.
- **`provider azure auth logging enabled`**: Enable or disable verbose logging for Azure authentication (useful for debugging).

### Usage Example:
1. **Switch providers:** Use `provider select` to change providers, e.g., from Azure to Ollama.
2. **Customize the system prompt:** Run `provider system` to guide the AI's tone and responses.
3. **Adjust response length or creativity:**
   - Use `provider max-tokens` to set the length.
   - Use `provider temp` to modify creativity.

---

## **3. System Management**

These commands let you manage system settings, logs, and configurations.

### Commands:
1. **Log Management:**
   - **`system log show`**: Display the current log entries.
   - **`system log clear`**: Wipe all log entries.
   - **`system log save`**: Save the log to a specified file.

2. **Configuration Management:**
   - **`system config show`**: Display the current system configuration.
   - **`system config save`**: Save the current configuration to a file.
   - **`system config factory reset`**: Reset the configuration to factory defaults.

3. **Utility:**
   - **`system clear`**: Clear the console screen.

### Usage Example:
1. **View logs:** Run `system log show` to inspect program logs.
2. **Reset configuration:** Use `system config factory reset` to restore defaults.
3. **Clear the console:** Keep your console clean while working with `system clear`.

---

## **4. Retrieval Augmented Generation (RAG)**

RAG enhances language model responses by retrieving information from a knowledge base or document set. Use these commands to manage a RAG store (e.g., a vector store backed by documents).

### Commands:
**Managing the RAG Store:**
- **`rag file`**: Add a single file to the RAG store.
- **`rag directory`**: Add all files in a directory to the RAG store.
- **`rag status`**: View the current RAG store status (e.g., number of entries).
- **`rag clear`**: Clear all data from the RAG store.
- **`rag search`**: Query the RAG store with a search string to retrieve relevant matched chunks.

**Configuring RAG:**
- **`rag config embedding model`**: Set the embedding model (used to generate vector representations of text).
- **`rag config query`**: Customize the query prompt for searching the RAG store.
- **`rag config chunking method`**: Select a text chunking method for breaking documents into smaller pieces.
- **`rag config chunksize`**: Set the number of tokens in each chunk (to control the size of document fragments).
- **`rag config overlap`**: Define the overlap between chunks (to ensure smooth transitions in content).
- **`rag config TopK`**: Specify the number of top results to return from searches.

### Usage Example:
1. **Add data to the store:**
   - Use `rag file` to add a file.
   - Or `rag directory` to bulk add files from a folder.
2. **Search for information:** Use `rag search` and enter your query.
3. **Configure RAG behavior:**
   - Set the chunk size using `rag config chunksize`.
   - Choose the embedding model with `rag config embedding model`.

---

## **Command Summary Table**

| **Category**            | **Command**                        | **Description**                              |
|--------------------------|------------------------------------|----------------------------------------------|
| **Chat Management**      | `chat show`                       | Display the chat history.                    |
|                          | `chat clear`                      | Clear the chat history.                      |
|                          | `chat save`                       | Save chat history to a file.                 |
|                          | `chat load`                       | Load chat history from a file.               |
| **Provider Management**  | `provider select`                 | Select the AI/ML provider.                   |
|                          | `provider model`                  | Choose a model for the current provider.     |
|                          | `provider host`                   | Update the Ollama host.                      |
|                          | `provider system`                 | Change the system prompt.                    |
|                          | `provider temp`                   | Adjust the response creativity.              |
|                          | `provider max-tokens`             | Set the maximum tokens for responses.        |
|                          | `provider azure auth logging enabled` | Enable or disable Azure auth logging.       |
| **System Management**    | `system log show`                 | Show the application logs.                   |
|                          | `system log clear`                | Clear the application logs.                  |
|                          | `system log save`                 | Save the logs to a file.                     |
|                          | `system config show`              | Show the current configuration.              |
|                          | `system config save`              | Save current configuration to a file.        |
|                          | `system config factory reset`     | Reset configuration to defaults.             |
|                          | `system clear`                    | Clear the console screen.                    |
| **RAG (Retrieval)**      | `rag file`                        | Add a file to the RAG store.                 |
|                          | `rag directory`                   | Add a directory to the RAG store.            |
|                          | `rag status`                      | Show the RAG store's status.                 |
|                          | `rag clear`                       | Clear the RAG store.                         |
|                          | `rag search`                      | Search the RAG store with a query.           |
|                          | `rag config embedding model`      | Set the embedding model.                     |
|                          | `rag config query`                | Customize the query prompt.                  |
|                          | `rag config chunking method`      | Choose a chunking strategy.                  |
|                          | `rag config chunksize`            | Set the chunk size in tokens.                |
|                          | `rag config overlap`              | Set overlap between chunks.                  |
|                          | `rag config TopK`                 | Specify the number of top results to return. |

---