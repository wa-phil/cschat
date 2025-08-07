using System;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Entity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Attributes { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty; // Reference to source chunk
    
    // Neighbor information for graph traversal
    public Dictionary<string, List<(Entity Entity, string RelationType, string Description)>> OutgoingRelationships { get; } = new();
    public Dictionary<string, List<(Entity Entity, string RelationType, string Description)>> IncomingRelationships { get; } = new();
    
    public override string ToString() => $"{Name} ({Type}) - {Attributes}";

    public Entity(string name, string type, string attributes, string sourceReference)
    {
        Name = name;
        Type = type;
        Attributes = attributes;
        SourceReference = sourceReference;
    }

    public Entity(string name, string type, string attributes)
        : this(name, type, attributes, string.Empty) {}
    
    // Add an outgoing relationship
    public void AddOutgoingRelationship(Entity target, string relationType, string description)
    {
        if (!OutgoingRelationships.ContainsKey(relationType))
            OutgoingRelationships[relationType] = new List<(Entity, string, string)>();
        
        // Avoid duplicates
        if (!OutgoingRelationships[relationType].Any(r => r.Entity.Name == target.Name))
        {
            OutgoingRelationships[relationType].Add((target, relationType, description));
        }
    }
    
    // Add an incoming relationship
    public void AddIncomingRelationship(Entity source, string relationType, string description)
    {
        if (!IncomingRelationships.ContainsKey(relationType))
            IncomingRelationships[relationType] = new List<(Entity, string, string)>();
        
        // Avoid duplicates
        if (!IncomingRelationships[relationType].Any(r => r.Entity.Name == source.Name))
        {
            IncomingRelationships[relationType].Add((source, relationType, description));
        }
    }
    
    // Get all neighbors (both incoming and outgoing)
    public List<Entity> GetAllNeighbors()
    {
        var outgoing = OutgoingRelationships.Values.SelectMany(list => list.Select(tuple => tuple.Entity));
        var incoming = IncomingRelationships.Values.SelectMany(list => list.Select(tuple => tuple.Entity));
        return outgoing.Concat(incoming).Distinct().ToList();
    }
    
    // Get only outgoing neighbors
    public List<Entity> GetOutgoingNeighbors()
    {
        return OutgoingRelationships.Values.SelectMany(list => list.Select(tuple => tuple.Entity)).Distinct().ToList();
    }
    
    // Get only incoming neighbors
    public List<Entity> GetIncomingNeighbors()
    {
        return IncomingRelationships.Values.SelectMany(list => list.Select(tuple => tuple.Entity)).Distinct().ToList();
    }
    
    // Get outgoing neighbors by relationship type
    public List<Entity> GetOutgoingNeighborsByType(string relationType)
    {
        return OutgoingRelationships.ContainsKey(relationType) 
            ? OutgoingRelationships[relationType].Select(tuple => tuple.Entity).ToList()
            : new List<Entity>();
    }
    
    // Get incoming neighbors by relationship type
    public List<Entity> GetIncomingNeighborsByType(string relationType)
    {
        return IncomingRelationships.ContainsKey(relationType) 
            ? IncomingRelationships[relationType].Select(tuple => tuple.Entity).ToList()
            : new List<Entity>();
    }
    
    // Get neighbors by relationship type (both directions)
    public List<Entity> GetNeighborsByType(string relationType)
    {
        var outgoing = GetOutgoingNeighborsByType(relationType);
        var incoming = GetIncomingNeighborsByType(relationType);
        return outgoing.Concat(incoming).Distinct().ToList();
    }
    
    // Get all outgoing relationship types
    public List<string> GetOutgoingRelationshipTypes()
    {
        return OutgoingRelationships.Keys.ToList();
    }
    
    // Get all incoming relationship types
    public List<string> GetIncomingRelationshipTypes()
    {
        return IncomingRelationships.Keys.ToList();
    }
    
    // Get all relationship types (both directions)
    public List<string> GetRelationshipTypes()
    {
        return OutgoingRelationships.Keys.Concat(IncomingRelationships.Keys).Distinct().ToList();
    }
    
    // Get relationship direction info
    public (int OutgoingCount, int IncomingCount, int TotalCount) GetRelationshipCounts()
    {
        var outgoingCount = OutgoingRelationships.Values.Sum(list => list.Count);
        var incomingCount = IncomingRelationships.Values.Sum(list => list.Count);
        return (outgoingCount, incomingCount, outgoingCount + incomingCount);
    }
    
    // Find shortest path to another entity (BFS)
    public List<Entity> FindPathTo(Entity target, int maxDepth = 5)
    {
        if (this.Name == target.Name) return new List<Entity> { this };
        
        var queue = new Queue<(Entity current, List<Entity> path)>();
        var visited = new HashSet<string>();
        
        queue.Enqueue((this, new List<Entity> { this }));
        visited.Add(this.Name);
        
        while (queue.Count > 0 && queue.Peek().path.Count <= maxDepth)
        {
            var (current, path) = queue.Dequeue();
            
            foreach (var neighbor in current.GetAllNeighbors())
            {
                if (neighbor.Name == target.Name)
                {
                    var result = new List<Entity>(path) { neighbor };
                    return result;
                }
                
                if (!visited.Contains(neighbor.Name))
                {
                    visited.Add(neighbor.Name);
                    var newPath = new List<Entity>(path) { neighbor };
                    queue.Enqueue((neighbor, newPath));
                }
            }
        }
        
        return new List<Entity>(); // No path found
    }
}

public class Relationship
{
    public Entity Source { get; set; }
    public Entity Target { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty; // Reference to source chunk

    public override string ToString() => $"{Source.Name} --[{Type}]--> {Target.Name}: {Description}";

    public Relationship(Entity source, Entity target, string type, string description, string sourceReference)
    {
        Source = source;
        Target = target;
        Type = type;
        Description = description;
        SourceReference = sourceReference;
    }

    public Relationship(Entity source, Entity target, string type, string description)
        : this(source, target, type, description, string.Empty) { }
}

public class GraphStore
{
    public Dictionary<string, Entity> Entities { get; } = new();
    public List<Relationship> Relationships { get; } = new();

    public int EntityCount => Entities.Count;
    public int RelationshipCount => Relationships.Count;
    public bool IsEmpty => Entities.Count == 0 && Relationships.Count == 0;

    public void AddEntity(Entity entity)
    {
        if (!Entities.ContainsKey(entity.Name))
            Entities[entity.Name] = entity;
    }

    public void AddRelationship(string sourceName, string targetName, string type, string description)
    {
        if (Entities.ContainsKey(sourceName) && Entities.ContainsKey(targetName))
        {
            var sourceEntity = Entities[sourceName];
            var targetEntity = Entities[targetName];
            
            var relationship = new Relationship(sourceEntity, targetEntity, type, description);
            Relationships.Add(relationship);
            
            // Update relationship information directionally
            sourceEntity.AddOutgoingRelationship(targetEntity, type, description);
            targetEntity.AddIncomingRelationship(sourceEntity, type, description);
        }
    }
    
    // Add relationship with Entity objects directly
    public void AddRelationship(Entity source, Entity target, string type, string description)
    {
        var relationship = new Relationship(source, target, type, description);
        Relationships.Add(relationship);
        
        // Update relationship information directionally
        source.AddOutgoingRelationship(target, type, description);
        target.AddIncomingRelationship(source, type, description);
    }
    
    // Get all entities connected to a specific entity (both directions)
    public List<Entity> GetConnectedEntities(string entityName)
    {
        return Entities.ContainsKey(entityName) 
            ? Entities[entityName].GetAllNeighbors()
            : new List<Entity>();
    }
    
    // Get entities this entity points to (outgoing relationships)
    public List<Entity> GetOutgoingConnectedEntities(string entityName)
    {
        return Entities.ContainsKey(entityName) 
            ? Entities[entityName].GetOutgoingNeighbors()
            : new List<Entity>();
    }
    
    // Get entities that point to this entity (incoming relationships)
    public List<Entity> GetIncomingConnectedEntities(string entityName)
    {
        return Entities.ContainsKey(entityName) 
            ? Entities[entityName].GetIncomingNeighbors()
            : new List<Entity>();
    }
    
    // Get entities connected by a specific relationship type (both directions)
    public List<Entity> GetConnectedEntitiesByType(string entityName, string relationType)
    {
        return Entities.ContainsKey(entityName) 
            ? Entities[entityName].GetNeighborsByType(relationType)
            : new List<Entity>();
    }
    
    // Get entities connected by outgoing relationships of a specific type
    public List<Entity> GetOutgoingConnectedEntitiesByType(string entityName, string relationType)
    {
        return Entities.ContainsKey(entityName) 
            ? Entities[entityName].GetOutgoingNeighborsByType(relationType)
            : new List<Entity>();
    }
    
    // Get entities connected by incoming relationships of a specific type
    public List<Entity> GetIncomingConnectedEntitiesByType(string entityName, string relationType)
    {
        return Entities.ContainsKey(entityName) 
            ? Entities[entityName].GetIncomingNeighborsByType(relationType)
            : new List<Entity>();
    }
    
    // Find shortest path between two entities
    public List<Entity> FindPath(string fromEntityName, string toEntityName, int maxDepth = 5)
    {
        if (!Entities.ContainsKey(fromEntityName) || !Entities.ContainsKey(toEntityName))
            return new List<Entity>();
            
        return Entities[fromEntityName].FindPathTo(Entities[toEntityName], maxDepth);
    }
    
    // Get all entities within N hops of a given entity
    public List<Entity> GetEntitiesWithinHops(string entityName, int maxHops)
    {
        if (!Entities.ContainsKey(entityName)) return new List<Entity>();
        
        var result = new HashSet<Entity>();
        var queue = new Queue<(Entity entity, int depth)>();
        var visited = new HashSet<string>();
        
        queue.Enqueue((Entities[entityName], 0));
        visited.Add(entityName);
        
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            result.Add(current);
            
            if (depth < maxHops)
            {
                foreach (var neighbor in current.GetAllNeighbors())
                {
                    if (!visited.Contains(neighbor.Name))
                    {
                        visited.Add(neighbor.Name);
                        queue.Enqueue((neighbor, depth + 1));
                    }
                }
            }
        }
        
        return result.ToList();
    }

    public void Clear()
    {
        Entities.Clear();
        Relationships.Clear();
    }

    public void PrintGraph()
    {
        Console.WriteLine($"\n=== GRAPH STORE SUMMARY ===");
        Console.WriteLine($"Total Entities: {EntityCount}");
        Console.WriteLine($"Total Relationships: {RelationshipCount}");

        Console.WriteLine("\nEntities with Directional Relationships:");
        foreach (var entity in Entities.Values)
        {
            var counts = entity.GetRelationshipCounts();
            Console.WriteLine($"- {entity} [Out: {counts.OutgoingCount}, In: {counts.IncomingCount}, Total: {counts.TotalCount}]");
            
            // Show outgoing relationships
            var outgoingTypes = entity.GetOutgoingRelationshipTypes();
            if (outgoingTypes.Count > 0)
            {
                Console.WriteLine($"  Outgoing Relationships ({counts.OutgoingCount}):");
                foreach (var relType in outgoingTypes)
                {
                    var targets = entity.GetOutgoingNeighborsByType(relType);
                    Console.WriteLine($"    --[{relType}]--> {string.Join(", ", targets.Select(n => n.Name))}");
                }
            }
            
            // Show incoming relationships
            var incomingTypes = entity.GetIncomingRelationshipTypes();
            if (incomingTypes.Count > 0)
            {
                Console.WriteLine($"  Incoming Relationships ({counts.IncomingCount}):");
                foreach (var relType in incomingTypes)
                {
                    var sources = entity.GetIncomingNeighborsByType(relType);
                    Console.WriteLine($"    <--[{relType}]-- {string.Join(", ", sources.Select(n => n.Name))}");
                }
            }
            
            if (counts.TotalCount == 0)
            {
                Console.WriteLine("  No relationships");
            }
        }

        Console.WriteLine("\nDirect Relationships:");
        foreach (var rel in Relationships)
            Console.WriteLine($"- {rel}");

        Console.WriteLine("============================\n");
    }
    
    // Print detailed graph analysis
    public void PrintGraphAnalysis()
    {
        Console.WriteLine($"\n=== GRAPH ANALYSIS ===");
        Console.WriteLine($"Total Entities: {EntityCount}");
        Console.WriteLine($"Total Relationships: {RelationshipCount}");
        
        // Find most connected entities
        var sortedByConnections = Entities.Values
            .OrderByDescending(e => e.GetAllNeighbors().Count)
            .Take(5);
            
        Console.WriteLine("\nMost Connected Entities:");
        foreach (var entity in sortedByConnections)
        {
            var neighborCount = entity.GetAllNeighbors().Count;
            Console.WriteLine($"- {entity.Name}: {neighborCount} connections");
        }
        
        // Show relationship type distribution
        var relationshipTypes = Relationships.GroupBy(r => r.Type)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
            
        Console.WriteLine("\nRelationship Types:");
        foreach (var kvp in relationshipTypes)
        {
            Console.WriteLine($"- {kvp.Key}: {kvp.Value} instances");
        }
        
        // Find isolated entities (no connections)
        var isolatedEntities = Entities.Values.Where(e => e.GetAllNeighbors().Count == 0).ToList();
        if (isolatedEntities.Count > 0)
        {
            Console.WriteLine($"\nIsolated Entities ({isolatedEntities.Count}):");
            foreach (var entity in isolatedEntities)
            {
                Console.WriteLine($"- {entity.Name}");
            }
        }
        
        Console.WriteLine("======================\n");
    }
    
    // Print all entities within N hops of a given entity
    public void PrintEntitiesWithinHops(string entityName, int maxHops)
    {
        if (!Entities.ContainsKey(entityName))
        {
            Console.WriteLine($"Entity '{entityName}' not found in the graph.");
            return;
        }
        
        var entitiesWithinHops = GetEntitiesWithinHops(entityName, maxHops);
        
        Console.WriteLine($"\n=== ENTITIES WITHIN {maxHops} HOP(S) OF '{entityName}' ===");
        Console.WriteLine($"Found {entitiesWithinHops.Count} entities:");
        
        // Group by distance/hop count
        var entitiesByDistance = new Dictionary<int, List<Entity>>();
        var queue = new Queue<(Entity entity, int distance)>();
        var visited = new HashSet<string>();
        
        queue.Enqueue((Entities[entityName], 0));
        visited.Add(entityName);
        
        while (queue.Count > 0)
        {
            var (current, distance) = queue.Dequeue();
            
            if (!entitiesByDistance.ContainsKey(distance))
                entitiesByDistance[distance] = new List<Entity>();
            entitiesByDistance[distance].Add(current);
            
            if (distance < maxHops)
            {
                foreach (var neighbor in current.GetAllNeighbors())
                {
                    if (!visited.Contains(neighbor.Name))
                    {
                        visited.Add(neighbor.Name);
                        queue.Enqueue((neighbor, distance + 1));
                    }
                }
            }
        }
        
        // Print entities grouped by distance
        for (int hop = 0; hop <= maxHops; hop++)
        {
            if (entitiesByDistance.ContainsKey(hop))
            {
                var hopEntities = entitiesByDistance[hop];
                if (hop == 0)
                    Console.WriteLine($"\nStarting Entity (0 hops):");
                else
                    Console.WriteLine($"\nEntities at {hop} hop(s) away ({hopEntities.Count}):");
                
                foreach (var entity in hopEntities)
                {
                    var counts = entity.GetRelationshipCounts();
                    Console.WriteLine($"  - {entity.Name} ({entity.Type}) [Connections: {counts.TotalCount}]");
                    
                    // Show how this entity connects back to previous hop (except for starting entity)
                    if (hop > 0)
                    {
                        var connectingRelationships = new List<string>();
                        
                        // Check outgoing relationships to previous hop entities
                        foreach (var relType in entity.GetOutgoingRelationshipTypes())
                        {
                            var targets = entity.GetOutgoingNeighborsByType(relType);
                            var previousHopTargets = targets.Where(t => entitiesByDistance.ContainsKey(hop - 1) && 
                                                                      entitiesByDistance[hop - 1].Any(e => e.Name == t.Name));
                            foreach (var target in previousHopTargets)
                                connectingRelationships.Add($"--[{relType}]--> {target.Name}");
                        }
                        
                        // Check incoming relationships from previous hop entities
                        foreach (var relType in entity.GetIncomingRelationshipTypes())
                        {
                            var sources = entity.GetIncomingNeighborsByType(relType);
                            var previousHopSources = sources.Where(s => entitiesByDistance.ContainsKey(hop - 1) && 
                                                                       entitiesByDistance[hop - 1].Any(e => e.Name == s.Name));
                            foreach (var source in previousHopSources)
                                connectingRelationships.Add($"<--[{relType}]-- {source.Name}");
                        }
                        
                        if (connectingRelationships.Count > 0)
                            Console.WriteLine($"    Connected via: {string.Join(", ", connectingRelationships)}");
                    }
                }
            }
        }
        
        Console.WriteLine($"\n=== END {maxHops} HOP(S) ANALYSIS ===\n");
    }
}

public static class GraphStoreManager
{
    public static GraphStore Graph { get; } = new GraphStore();

    public static void ParseGraphFromJson(GraphDto graphDto)
    {
        foreach (var entityDto in graphDto.Entities)
            Graph.AddEntity(new Entity(entityDto.Name, entityDto.Type, entityDto.Attributes));

        foreach (var relDto in graphDto.Relationships)
            Graph.AddRelationship(relDto.Source, relDto.Target, relDto.Type, relDto.Description);
    }

    public static GraphDto? JsonToGraphDto(string jsonString)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                Console.WriteLine("JSON string is null or empty");
                return null;
            }
            
            Console.WriteLine($"Attempting to parse JSON string of length: {jsonString.Length}");
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var result = JsonSerializer.Deserialize<GraphDto>(jsonString, options);
            if (result == null)
            {
                Console.WriteLine("JsonSerializer.Deserialize returned null");
                return null;
            }
            
            Console.WriteLine($"Successfully parsed GraphDto with {result.Entities?.Count ?? 0} entities and {result.Relationships?.Count ?? 0} relationships");
            return result;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse JSON: {ex.Message}");
            Console.WriteLine($"JSON content: {jsonString}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error parsing JSON: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return null;
        }
    }
}

public class EntityDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Attributes { get; set; } = string.Empty;
}

public class RelationshipDto
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class GraphDto
{
    public List<EntityDto> Entities { get; set; } = new List<EntityDto>();
    public List<RelationshipDto> Relationships { get; set; } = new List<RelationshipDto>();
}

public class Context
{
    protected ChatMessage _systemMessage = new ChatMessage { Role = Roles.System, Content = string.Empty };
    protected List<ChatMessage> _messages = new List<ChatMessage>();
    protected List<(string Reference, string Chunk)> _context = new List<(string Reference, string Chunk)>();
    private DateTime _conversationStartTime = DateTime.Now;

    public Context(string? systemPrompt = null)
    {
        _conversationStartTime = DateTime.Now;
        AddSystemMessage(systemPrompt ?? Program.config.SystemPrompt);
    }

    public Context(IEnumerable<ChatMessage> messages)
    {
        _conversationStartTime = DateTime.Now;
        foreach (var msg in messages)
        {
            if (msg.Role == Roles.System)
                AddSystemMessage(msg.Content);
            else
                _messages.Add(msg);
        }
    }

    public IEnumerable<ChatMessage> Messages
    {
        get
        {
            var result = new List<ChatMessage>();
            result.Add(GetSystemMessage());
            result.AddRange(_messages);
            return result;
        }
    }

    public void Clear()
    {
        _systemMessage.Content = string.Empty;
        _context.Clear();
        _messages.Clear();
        _conversationStartTime = DateTime.Now;
    }

    public void AddContext(string reference, string chunk) => _context.Add((reference, chunk));
    public void ClearContext() => _context.Clear();
    public List<(string Reference, string Chunk)> GetContext() => new List<(string Reference, string Chunk)>(_context);

    public void AddUserMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.User, Content = content });
    public void AddAssistantMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.Assistant, Content = content });
    public void AddToolMessage(string content) => _messages.Add(new ChatMessage { Role = Roles.Tool, Content = content });
    public void AddSystemMessage(string content)
    {
        _systemMessage.Content = _systemMessage.Content.Length > 0
            ? $"{_systemMessage.Content}\n{content}"
            : content;
        // Ensure system message has the conversation start time, not current time
        _systemMessage.CreatedAt = _conversationStartTime;
    }

    public void SetSystemMessage(string content)
    {
        _systemMessage = new ChatMessage
        {
            Role = Roles.System,
            Content = content,
            CreatedAt = _conversationStartTime
        };
    }

    public ChatMessage GetSystemMessage()
    {
        var result = new ChatMessage { Role = Roles.System, Content = _systemMessage.Content };
        if (_context.Count > 0)
        {
            result.Content += "\nWhat follows is content to help answer your next question.\n" + string.Join("\n", _context.Select(c => $"--- BEGIN CONTEXT: {c.Reference} ---\n{c.Chunk}\n--- END CONTEXT ---"));
            result.Content += "\nWhen referring to the provided context in your answer, explicitly state which content you are referencing in the form 'as per [reference], [your answer]'.";
        }
        return result;
    }

    public void Save(string filePath)
    {
        var data = new ContextData
        {
            SystemMessage = _systemMessage,
            Messages = _messages,
            Context = _context
        };

        var json = data.ToJson();
        System.IO.File.WriteAllText(filePath, json);
    }

    public void Load(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var json = System.IO.File.ReadAllText(filePath);
        var data = json.FromJson<ContextData>();

        if (data == null)
        {
            throw new InvalidOperationException("Failed to deserialize Context data.");
        }

        _systemMessage = data.SystemMessage ?? new ChatMessage { Role = Roles.System, Content = string.Empty };
        _messages = data.Messages ?? new List<ChatMessage>();
        _context = data.Context ?? new List<(string Reference, string Chunk)>();
    }

    public Context Clone()
    {
        return new Context
        {
            _systemMessage = new ChatMessage
            {
                Role = Roles.System,
                Content = $"{_systemMessage.Content}", // Ensure a deep copy of the content for system message -- SUPER IMPORTANT
                CreatedAt = _systemMessage.CreatedAt
            },
            _messages = new List<ChatMessage>(_messages),
            _context = new List<(string Reference, string Chunk)>(_context),
            _conversationStartTime = _conversationStartTime
        };
    }

    private class ContextData
    {
        public ChatMessage? SystemMessage { get; set; }
        public List<ChatMessage>? Messages { get; set; }
        public List<(string Reference, string Chunk)>? Context { get; set; }
    }
}

public class ContextManager
{
    public static GraphStore GraphStore { get; } = new GraphStore();

    public static async Task InvokeAsync(string input, Context Context) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Context.ClearContext();

        // Try to add context to Context first
        var references = new List<string>();
        var results = await SearchVectorDB(input);
        if (results != null && results.Count > 0)
        {
            foreach (var result in results)
            {
                references.Add(result.Reference);
                Context.AddContext(result.Reference, result.Content);
            }
            // Context was added, no summary response required, returning modified Context back to caller.
            ctx.Append(Log.Data.Result, references.ToArray());
            ctx.Succeeded();
            return;
        }

        // If no results found, return a message
        Context.AddContext("Context", "No special or relevant information about current context.");
        ctx.Append(Log.Data.Message, "Nothing relevant in the knowledge base.");
        ctx.Succeeded();
        return;
    });

    public static async Task AddContent(string content, string reference = "content") => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Engine.TextChunker.ThrowIfNull("Text chunker is not set. Please configure a text chunker before adding files to the vector store.");
        IEmbeddingProvider? embeddingProvider = Engine.Provider as IEmbeddingProvider;
        embeddingProvider.ThrowIfNull("Current configured provider does not support embeddings.");

        ctx.Append(Log.Data.Reference, reference);
        var embeddings = new List<(string Reference, string Chunk, float[] Embedding)>();
        var chunks = Engine.TextChunker!.ChunkText(reference, content);
        ctx.Append(Log.Data.Count, chunks.Count);

        await Task.WhenAll(chunks.Select(async chunk =>
            embeddings.Add((
                Reference: chunk.Reference,
                Chunk: chunk.Content,
                Embedding: await embeddingProvider!.GetEmbeddingAsync(chunk.Content)
            ))
        ));

        Engine.VectorStore.Add(embeddings);
        ctx.Succeeded(embeddings.Count > 0);
    });

    public static async Task AddGraphContent(string content, string reference = "content") => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Engine.TextChunker.ThrowIfNull("Text chunker is not set. Please configure a text chunker before adding files to the vector store.");
        IGraphProvider? graphProvider = Engine.Provider as IGraphProvider;
        graphProvider.ThrowIfNull("Current configured provider does not support graph operations.");

        ctx.Append(Log.Data.Reference, reference);
        var chunks = Engine.TextChunker!.ChunkText(reference, content);
        ctx.Append(Log.Data.Count, chunks.Count);

        var chunksProcessed = 0;
        
        foreach (var chunk in chunks)
        {
            await graphProvider!.GetEntitiesAndRelationshipsAsync(chunk.Content, chunk.Reference);
            
            chunksProcessed++;
        }
        
        ctx.Append(Log.Data.Result, $"Processed {chunksProcessed} chunks and stored graph data");
        ctx.Succeeded(chunksProcessed > 0);
    });    

    public static async Task<List<SearchResult>> SearchVectorDB(string userMessage)
    {
        var empty = new List<SearchResult>();
        if (string.IsNullOrEmpty(userMessage) || null == Engine.VectorStore || Engine.VectorStore.IsEmpty) { return empty; }

        var embeddingProvider = Engine.Provider as IEmbeddingProvider;
        if (embeddingProvider == null) { return empty; }

        float[]? query = await embeddingProvider!.GetEmbeddingAsync(userMessage);
        if (query == null) { return empty; }

        var items = Engine.VectorStore.Search(query, Program.config.RagSettings.TopK);
        // filter out below average results
        var average = items.Average(i => i.Score);

        return items.Where(i => i.Score >= average).ToList();
    }
}