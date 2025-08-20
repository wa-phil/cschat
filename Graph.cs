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

    public string GetGraphData0()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\nEntities with Directional Relationships:");
        foreach (var entity in Entities.Values)
        {
            var counts = entity.GetRelationshipCounts();
            sb.AppendLine($"- {entity} [Out: {counts.OutgoingCount}, In: {counts.IncomingCount}, Total: {counts.TotalCount}]");

            // Show outgoing relationships
            var outgoingTypes = entity.GetOutgoingRelationshipTypes();
            if (outgoingTypes.Count > 0)
            {
                sb.AppendLine($"  Outgoing Relationships ({counts.OutgoingCount}):");
                foreach (var relType in outgoingTypes)
                {
                    var targets = entity.GetOutgoingNeighborsByType(relType);
                    sb.AppendLine($"    --[{relType}]--> {string.Join(", ", targets.Select(n => n.Name))}");
                }
            }

            // Show incoming relationships
            var incomingTypes = entity.GetIncomingRelationshipTypes();
            if (incomingTypes.Count > 0)
            {
                sb.AppendLine($"  Incoming Relationships ({counts.IncomingCount}):");
                foreach (var relType in incomingTypes)
                {
                    var sources = entity.GetIncomingNeighborsByType(relType);
                    sb.AppendLine($"    <--[{relType}]-- {string.Join(", ", sources.Select(n => n.Name))}");
                }
            }

            if (counts.TotalCount == 0)
            {
                sb.AppendLine("  No relationships");
            }
        }

        sb.AppendLine("\nDirect Relationships:");
        foreach (var rel in Relationships)
            sb.AppendLine($"- {rel}");

        return sb.ToString();
    }    

    public string GetGraphData()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Knowledge Graph Summary:\n");

        foreach (var entity in Entities.Values)
        {
            sb.AppendLine($"**{entity.Name}** ({entity.Type})");

            // Optional: Add description if available
            if (!string.IsNullOrWhiteSpace(entity.Attributes))
            {
                sb.AppendLine($"{entity.Attributes}");
            }

            // Outgoing relationships
            if (entity.OutgoingRelationships.Any())
            {
                sb.AppendLine($"\n{entity.Name} has the following outgoing relationships:");
                foreach (var kvp in entity.OutgoingRelationships)
                {
                    string relationType = kvp.Key;
                    foreach (var (targetEntity, _, description) in kvp.Value)
                    {
                        sb.AppendLine($"- {entity.Name} {ToVerb(relationType)} {targetEntity.Name}: {description}");
                    }
                }
            }

            // Incoming relationships
            if (entity.IncomingRelationships.Any())
            {
                sb.AppendLine($"\n{entity.Name} is involved in the following incoming relationships:");
                foreach (var kvp in entity.IncomingRelationships)
                {
                    string relationType = kvp.Key;
                    foreach (var (sourceEntity, _, description) in kvp.Value)
                    {
                        sb.AppendLine($"- {sourceEntity.Name} {ToVerb(relationType)} {entity.Name}: {description}");
                    }
                }
            }

            sb.AppendLine("\n---\n");
        }

        return sb.ToString();
    }

    // Helper to convert relationship types to natural verbs
    private string ToVerb(string relationshipType)
    {
        return relationshipType switch
        {
            "works_for" => "works for",
            "located_in" => "is located in",
            "part_of" => "is part of",
            "uses" => "uses",
            "consumes" => "consumes",
            "orchestrates" => "orchestrates",
            "developed_in" => "is developed in",
            "packaged_as" => "is packaged as",
            "tests" => "tests",
            "depends_on" => "depends on",
            "permission_required_for" => "requires permission for",
            "created_by" => "is created by",
            "edited_in" => "is edited in",
            "supports" => "supports",
            "used_by" => "is used by",
            "used_for" => "is used for",
            _ => $"has a '{relationshipType}' relationship with"
        };
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

    // Add graph analytics capabilities
    public GraphAnalytics Analytics => new GraphAnalytics(this);

    // Detect and display communities using Louvain algorithm
    public void PrintCommunityAnalysis()
    {
        Console.WriteLine("\n=== COMMUNITY DETECTION (LOUVAIN ALGORITHM) ===");

        if (IsEmpty)
        {
            Console.WriteLine("Graph is empty. No communities to detect.");
            return;
        }

        var (communities, modularity) = Analytics.DetectCommunities();
        var clusters = Analytics.GenerateClusters(communities);

        Console.WriteLine($"Detected {clusters.Count} communities with modularity score: {modularity:F4}");
        Console.WriteLine($"(Higher modularity = better community structure)");

        foreach (var cluster in clusters)
        {
            Console.WriteLine($"\n--- COMMUNITY {cluster.Id} ---");
            Console.WriteLine($"Size: {cluster.Size} entities");
            Console.WriteLine($"Density: {cluster.Density:F3} (how tightly connected)");

            Console.WriteLine("\nEntity Types in this Community:");
            foreach (var typeCount in cluster.EntityTypes.OrderByDescending(kvp => kvp.Value))
            {
                Console.WriteLine($"  • {typeCount.Key}: {typeCount.Value} entities");
            }

            Console.WriteLine("\nTop Relationship Types:");
            foreach (var relType in cluster.TopRelationshipTypes)
            {
                Console.WriteLine($"  • {relType}");
            }

            Console.WriteLine("\nEntities in this Community:");
            foreach (var entityName in cluster.EntityNames.Take(10)) // Show first 10
            {
                if (Entities.ContainsKey(entityName))
                {
                    var entity = Entities[entityName];
                    Console.WriteLine($"  • {entity.Name} ({entity.Type})");
                }
            }

            if (cluster.EntityNames.Count > 10)
            {
                Console.WriteLine($"  ... and {cluster.EntityNames.Count - 10} more entities");
            }
        }

        Console.WriteLine($"\n=== END COMMUNITY ANALYSIS ===");
    }

    // Get entities in a specific community
    public List<Entity> GetEntitiesInCommunity(int communityId)
    {
        var (communities, _) = Analytics.DetectCommunities();
        var entitiesInCommunity = communities
            .Where(kvp => kvp.Value == communityId)
            .Select(kvp => kvp.Key)
            .Where(name => Entities.ContainsKey(name))
            .Select(name => Entities[name])
            .ToList();

        return entitiesInCommunity;
    }

    // Find which community an entity belongs to
    public int GetEntityCommunity(string entityName)
    {
        var (communities, _) = Analytics.DetectCommunities();
        return communities.GetValueOrDefault(entityName, -1);
    }
    
    // Print detailed community information
    public void PrintDetailedCommunityInfo(int? specificCommunityId = null)
    {
        Console.WriteLine("\n=== DETAILED COMMUNITY INFORMATION ===");

        if (IsEmpty)
        {
            Console.WriteLine("Graph is empty. No communities to analyze.");
            return;
        }

        var (communities, modularity) = Analytics.DetectCommunities();
        var clusters = Analytics.GenerateClusters(communities);

        Console.WriteLine($"Graph Overview:");
        Console.WriteLine($"  • Total Entities: {EntityCount}");
        Console.WriteLine($"  • Total Relationships: {RelationshipCount}");
        Console.WriteLine($"  • Communities Detected: {clusters.Count}");
        Console.WriteLine($"  • Modularity Score: {modularity:F4} (higher = better community structure)");

        // Filter to specific community if requested
        var clustersToShow = specificCommunityId.HasValue
            ? clusters.Where(c => c.Id == specificCommunityId.Value).ToList()
            : clusters;

        if (specificCommunityId.HasValue && clustersToShow.Count == 0)
        {
            Console.WriteLine($"\nCommunity {specificCommunityId.Value} not found.");
            return;
        }

        foreach (var cluster in clustersToShow)
        {
            Console.WriteLine($"\n╔══ COMMUNITY {cluster.Id} ══════════════════════════════════");
            Console.WriteLine($"║ Size: {cluster.Size} entities");
            Console.WriteLine($"║ Density: {cluster.Density:F3} (0=sparse, 1=fully connected)");

            // Entity type breakdown
            Console.WriteLine($"║");
            Console.WriteLine($"║ Entity Types:");
            var totalInCommunity = cluster.EntityTypes.Values.Sum();
            foreach (var typeCount in cluster.EntityTypes.OrderByDescending(kvp => kvp.Value))
            {
                var percentage = (double)typeCount.Value / totalInCommunity * 100;
                Console.WriteLine($"║   • {typeCount.Key}: {typeCount.Value} entities ({percentage:F1}%)");
            }

            // Relationship patterns
            Console.WriteLine($"║");
            Console.WriteLine($"║ Top Relationship Types in Community:");
            foreach (var relType in cluster.TopRelationshipTypes.Take(5))
            {
                Console.WriteLine($"║   • {relType}");
            }

            // Central entities (most connected within community)
            Console.WriteLine($"║");
            Console.WriteLine($"║ Most Connected Entities in Community:");
            var communityEntities = cluster.EntityNames
                .Where(name => Entities.ContainsKey(name))
                .Select(name => Entities[name])
                .OrderByDescending(e => e.GetAllNeighbors().Count(n => cluster.EntityNames.Contains(n.Name)))
                .Take(5);

            foreach (var entity in communityEntities)
            {
                var communityConnections = entity.GetAllNeighbors().Count(n => cluster.EntityNames.Contains(n.Name));
                var totalConnections = entity.GetAllNeighbors().Count;
                Console.WriteLine($"║   • {entity.Name} ({entity.Type}): {communityConnections}/{totalConnections} connections");
            }

            // Show all entities if small community or first 15 if large
            Console.WriteLine($"║");
            if (cluster.Size <= 15)
            {
                Console.WriteLine($"║ All Entities in Community:");
                foreach (var entityName in cluster.EntityNames.OrderBy(n => n))
                {
                    if (Entities.ContainsKey(entityName))
                    {
                        var entity = Entities[entityName];
                        var counts = entity.GetRelationshipCounts();
                        Console.WriteLine($"║   • {entity.Name} ({entity.Type}) - {counts.TotalCount} total connections");
                    }
                }
            }
            else
            {
                Console.WriteLine($"║ Sample Entities (showing 15 of {cluster.Size}):");
                foreach (var entityName in cluster.EntityNames.Take(15))
                {
                    if (Entities.ContainsKey(entityName))
                    {
                        var entity = Entities[entityName];
                        var counts = entity.GetRelationshipCounts();
                        Console.WriteLine($"║   • {entity.Name} ({entity.Type}) - {counts.TotalCount} total connections");
                    }
                }
                Console.WriteLine($"║   ... and {cluster.Size - 15} more entities");
            }

            // Cross-community connections
            Console.WriteLine($"║");
            Console.WriteLine($"║ Cross-Community Connections:");
            var crossConnections = new Dictionary<int, int>();
            foreach (var entityName in cluster.EntityNames)
            {
                if (Entities.ContainsKey(entityName))
                {
                    var entity = Entities[entityName];
                    var neighbors = entity.GetAllNeighbors();
                    foreach (var neighbor in neighbors)
                    {
                        var neighborCommunity = GetEntityCommunity(neighbor.Name);
                        if (neighborCommunity != cluster.Id && neighborCommunity != -1)
                        {
                            crossConnections[neighborCommunity] = crossConnections.GetValueOrDefault(neighborCommunity, 0) + 1;
                        }
                    }
                }
            }

            if (crossConnections.Any())
            {
                foreach (var cc in crossConnections.OrderByDescending(kvp => kvp.Value).Take(3))
                {
                    Console.WriteLine($"║   • {cc.Value} connections to Community {cc.Key}");
                }
            }
            else
            {
                Console.WriteLine($"║   • No cross-community connections (isolated community)");
            }

            Console.WriteLine($"╚════════════════════════════════════════════════════════");
        }

        // Summary statistics
        if (!specificCommunityId.HasValue)
        {
            Console.WriteLine($"\n=== COMMUNITY SUMMARY STATISTICS ===");
            var avgSize = clusters.Average(c => c.Size);
            var avgDensity = clusters.Average(c => c.Density);
            var largestCommunity = clusters.OrderByDescending(c => c.Size).First();
            var densestCommunity = clusters.OrderByDescending(c => c.Density).First();

            Console.WriteLine($"Average Community Size: {avgSize:F1} entities");
            Console.WriteLine($"Average Community Density: {avgDensity:F3}");
            Console.WriteLine($"Largest Community: #{largestCommunity.Id} with {largestCommunity.Size} entities");
            Console.WriteLine($"Densest Community: #{densestCommunity.Id} with {densestCommunity.Density:F3} density");

            // Size distribution
            var sizeGroups = clusters.GroupBy(c => c.Size switch
            {
                <= 5 => "Very Small (1-5)",
                <= 15 => "Small (6-15)",
                <= 50 => "Medium (16-50)",
                <= 100 => "Large (51-100)",
                _ => "Very Large (100+)"
            }).OrderBy(g => g.Key);

            Console.WriteLine($"\nCommunity Size Distribution:");
            foreach (var group in sizeGroups)
            {
                Console.WriteLine($"  • {group.Key}: {group.Count()} communities");
            }
        }

        Console.WriteLine($"\n=== END DETAILED COMMUNITY INFORMATION ===");
    }
    
    // Helper method to print just a summary table of all communities
    public void PrintCommunitySummaryTable()
    {
        Console.WriteLine("\n=== COMMUNITY SUMMARY TABLE ===");
        
        if (IsEmpty)
        {
            Console.WriteLine("Graph is empty. No communities to display.");
            return;
        }
        
        var (communities, modularity) = Analytics.DetectCommunities();
        var clusters = Analytics.GenerateClusters(communities);
        
        Console.WriteLine($"Total Communities: {clusters.Count} | Modularity: {modularity:F4}");
        Console.WriteLine();
        Console.WriteLine("┌──────────┬──────────┬──────────┬─────────────────────────────────────┐");
        Console.WriteLine("│ Comm. ID │   Size   │ Density  │ Dominant Entity Types               │");
        Console.WriteLine("├──────────┼──────────┼──────────┼─────────────────────────────────────┤");
        
        foreach (var cluster in clusters)
        {
            var dominantTypes = cluster.EntityTypes
                .OrderByDescending(kvp => kvp.Value)
                .Take(2)
                .Select(kvp => $"{kvp.Key}({kvp.Value})")
                .ToList();
            
            var typesStr = string.Join(", ", dominantTypes);
            if (typesStr.Length > 35) typesStr = typesStr.Substring(0, 32) + "...";
            
            Console.WriteLine($"│    {cluster.Id,2}    │    {cluster.Size,3}   │  {cluster.Density,6:F3} │ {typesStr,-35} │");
        }
        
        Console.WriteLine("└──────────┴──────────┴──────────┴─────────────────────────────────────┘");
        Console.WriteLine("\nUse PrintDetailedCommunityInfo(communityId) for detailed analysis of specific communities.");
        Console.WriteLine("=================================");
    }    
}

public class GraphAnalytics
{
    private readonly GraphStore graph;
    
    public GraphAnalytics(GraphStore graphStore)
    {
        graph = graphStore;
    }
    
    // Louvain algorithm for community detection
    public (Dictionary<string, int> communities, double modularity) DetectCommunities()
    {
        var entities = graph.Entities.Keys.ToList();
        var communities = new Dictionary<string, int>();
        
        // Initialize each node in its own community
        for (int i = 0; i < entities.Count; i++)
        {
            communities[entities[i]] = i;
        }
        
        double totalWeight = graph.Relationships.Count;
        if (totalWeight == 0) return (communities, 0.0);
        
        bool improvement = true;
        
        while (improvement)
        {
            improvement = false;
            
            foreach (var entity in entities)
            {
                int currentCommunity = communities[entity];
                var neighborCommunities = new Dictionary<int, double>();
                
                // Calculate the weight of connections to each neighboring community
                var neighbors = GetNeighbors(entity);
                foreach (var neighbor in neighbors)
                {
                    int neighborCommunity = communities[neighbor];
                    if (!neighborCommunities.ContainsKey(neighborCommunity))
                        neighborCommunities[neighborCommunity] = 0;
                    neighborCommunities[neighborCommunity] += 1.0; // Weight of edge
                }
                
                // Find the community that gives the best modularity gain
                double bestGain = 0;
                int bestCommunity = currentCommunity;
                
                foreach (var kvp in neighborCommunities)
                {
                    int targetCommunity = kvp.Key;
                    if (targetCommunity == currentCommunity) continue;
                    
                    double gain = CalculateModularityGain(entity, currentCommunity, targetCommunity, communities, totalWeight);
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestCommunity = targetCommunity;
                    }
                }
                
                // Move to the best community if there's an improvement
                if (bestCommunity != currentCommunity)
                {
                    communities[entity] = bestCommunity;
                    improvement = true;
                }
            }
        }
        
        // Relabel communities to be consecutive integers starting from 0
        var communityMapping = new Dictionary<int, int>();
        int newCommunityId = 0;
        var finalCommunities = new Dictionary<string, int>();
        
        foreach (var entity in entities)
        {
            int oldCommunity = communities[entity];
            if (!communityMapping.ContainsKey(oldCommunity))
            {
                communityMapping[oldCommunity] = newCommunityId++;
            }
            finalCommunities[entity] = communityMapping[oldCommunity];
        }
        
        double modularity = CalculateModularity(finalCommunities, totalWeight);
        return (finalCommunities, modularity);
    }
    
    // Generate clusters based on community detection
    public List<GraphCluster> GenerateClusters(Dictionary<string, int> communities)
    {
        var clusters = new List<GraphCluster>();
        var communityGroups = communities.GroupBy(kvp => kvp.Value);
        
        foreach (var group in communityGroups)
        {
            var cluster = new GraphCluster
            {
                Id = group.Key,
                EntityNames = group.Select(kvp => kvp.Key).ToList()
            };
            
            // Calculate entity type distribution
            cluster.EntityTypes = new Dictionary<string, int>();
            foreach (var entityName in cluster.EntityNames)
            {
                if (graph.Entities.ContainsKey(entityName))
                {
                    var entityType = graph.Entities[entityName].Type;
                    cluster.EntityTypes[entityType] = cluster.EntityTypes.GetValueOrDefault(entityType, 0) + 1;
                }
            }
            
            // Calculate cluster density
            cluster.Density = CalculateClusterDensity(cluster.EntityNames);
            
            // Find top relationship types within the cluster
            cluster.TopRelationshipTypes = GetTopRelationshipTypesInCluster(cluster.EntityNames);
            
            clusters.Add(cluster);
        }
        
        return clusters.OrderByDescending(c => c.Size).ToList();
    }
    
    // Helper methods
    private List<string> GetNeighbors(string entityName)
    {
        var neighbors = new HashSet<string>();
        
        if (graph.Entities.ContainsKey(entityName))
        {
            var entity = graph.Entities[entityName];
            
            foreach (var relationshipList in entity.OutgoingRelationships.Values)
            {
                foreach (var (neighborEntity, _, _) in relationshipList)
                {
                    neighbors.Add(neighborEntity.Name);
                }
            }
            
            foreach (var relationshipList in entity.IncomingRelationships.Values)
            {
                foreach (var (neighborEntity, _, _) in relationshipList)
                {
                    neighbors.Add(neighborEntity.Name);
                }
            }
        }
        
        return neighbors.ToList();
    }
    
    private double CalculateModularityGain(string entity, int currentCommunity, int targetCommunity, 
        Dictionary<string, int> communities, double totalWeight)
    {
        // Simplified modularity gain calculation
        var neighbors = GetNeighbors(entity);
        
        double currentConnections = neighbors.Count(n => communities[n] == currentCommunity);
        double targetConnections = neighbors.Count(n => communities[n] == targetCommunity);
        
        return (targetConnections - currentConnections) / totalWeight;
    }
    
    private double CalculateModularity(Dictionary<string, int> communities, double totalWeight)
    {
        if (totalWeight == 0) return 0;
        
        double modularity = 0;
        var communityGroups = communities.GroupBy(kvp => kvp.Value);
        
        foreach (var group in communityGroups)
        {
            var communityNodes = group.Select(kvp => kvp.Key).ToList();
            double internalEdges = 0;
            double totalDegree = 0;
            
            foreach (var node in communityNodes)
            {
                var neighbors = GetNeighbors(node);
                totalDegree += neighbors.Count;
                internalEdges += neighbors.Count(n => communityNodes.Contains(n));
            }
            
            internalEdges /= 2; // Each edge counted twice
            double expectedInternalEdges = (totalDegree * totalDegree) / (4 * totalWeight);
            
            modularity += (internalEdges / totalWeight) - (expectedInternalEdges / totalWeight);
        }
        
        return modularity;
    }
    
    private double CalculateClusterDensity(List<string> clusterNodes)
    {
        if (clusterNodes.Count < 2) return 0;
        
        int actualEdges = 0;
        int possibleEdges = clusterNodes.Count * (clusterNodes.Count - 1) / 2;
        
        foreach (var node in clusterNodes)
        {
            var neighbors = GetNeighbors(node);
            actualEdges += neighbors.Count(n => clusterNodes.Contains(n));
        }
        
        actualEdges /= 2; // Each edge counted twice
        return possibleEdges > 0 ? (double)actualEdges / possibleEdges : 0;
    }
    
    private List<string> GetTopRelationshipTypesInCluster(List<string> clusterNodes)
    {
        var relationshipTypes = new Dictionary<string, int>();
        
        foreach (var relationship in graph.Relationships)
        {
            if (clusterNodes.Contains(relationship.Source.Name) && clusterNodes.Contains(relationship.Target.Name))
            {
                relationshipTypes[relationship.Type] = relationshipTypes.GetValueOrDefault(relationship.Type, 0) + 1;
            }
        }
        
        return relationshipTypes
            .OrderByDescending(kvp => kvp.Value)
            .Take(3)
            .Select(kvp => $"{kvp.Key} ({kvp.Value})")
            .ToList();
    }
}

public class GraphCluster
{
    public int Id { get; set; }
    public List<string> EntityNames { get; set; } = new List<string>();
    public Dictionary<string, int> EntityTypes { get; set; } = new Dictionary<string, int>();
    public double Density { get; set; }
    public List<string> TopRelationshipTypes { get; set; } = new List<string>();
    
    public int Size => EntityNames.Count;
    
    public override string ToString() => $"Cluster {Id}: {Size} entities, Density: {Density:F3}";
}

public class GraphMetrics
{
    public Dictionary<string, double> DegreeCentrality { get; set; } = new Dictionary<string, double>();
    public Dictionary<string, double> BetweennessCentrality { get; set; } = new Dictionary<string, double>();
    public Dictionary<string, double> ClosenessCentrality { get; set; } = new Dictionary<string, double>();
    public List<GraphCluster> Communities { get; set; } = new List<GraphCluster>();
    public double Modularity { get; set; }
    
    public override string ToString() => $"Graph Metrics: {Communities.Count} communities, Modularity: {Modularity:F4}";
}

public static class GraphStoreManager
{
    /// <summary>
    /// Generates reference documentation for the current knowledge graph, following strict reference guide principles.
    /// Output is Markdown, neutral, factual, and structured according to the graph's machinery.
    /// </summary>
    public static async Task GenerateReferenceDocumentationAsync(/*string content*/) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        //ctx.Append(Log.Data.Reference, content);

        try
        {
            var working = new Context(@"""
You are a senior developer writing internal documentation for new team members.

Using the following knowledge graph, write a developer-friendly guide that introduces each tool, repository, and process in natural language.

Avoid technical jargon unless necessary, and don't use ontology-style formatting like arrows or relationship labels.

Instead, explain how each entity fits into the overall build and deployment workflow, what it’s used for, and how it interacts with other components.

Use a conversational tone, like you're walking a teammate through the system.

Here's an example of the style I want:

Forge is our main build orchestration tool. It handles over 30 actions during the Click-to-Run (C2R) deployment process, including packaging and telemetry. It pulls configuration from DeployTools and relies on RepoMan to create Unity packages. We distribute Forge as a NuGet package, and all changes go through CloudBuild for testing before release.

Now, write similar descriptions for the rest of the entities in the graph consisting of entities and relationships.
Each entity contains:
- Entity name
- Entity type (Person, Organization, Location, Concept, etc.)
- Key attributes or descriptions
- List of incoming and outgoing relationships

Each relationship contains:
- Source entity
- Target entity  
- Relationship type (works_for, located_in, part_of, etc.)
- Relationship description

Given the context of the knowledge graph, generate comprehensive reference documentation that includes:

1. An overview of the graph structure, including key entities and their relationships.
2. Detailed descriptions of each entity, including attributes, types, and relationships.
3. Explanations of the significance of each relationship within the graph.

The output should be well-structured, easy to navigate, and suitable for both technical audiences. Generate it in markdown format.

Use the following philosphy:
Reference material provides accurate, neutral, and structured descriptions of a product—especially software—serving as an authoritative source users consult for clarity and certainty in their work. Unlike tutorials or how-to guides, it avoids explanation or instruction, focusing solely on describing the product’s components (e.g., APIs, functions) in alignment with its internal structure. The tone should be objective and austere, using standard patterns for consistency, and may include concise examples to illustrate usage without drifting into teaching. Good reference material is essential for confident, informed use of a system.
""");
            string content = GraphStoreManager.Graph.GetGraphData();
            working.AddUserMessage(content);

            var response = await TypeParser.GetAsync(working, typeof(string));
            if (response == null)// || response is not GraphDto graphDtoObject)
            {
                Console.WriteLine($"JSON processing issue");
                return;
            }
            Console.WriteLine($"\n=== Extracted from {response} ===");

            /*if (graphDtoObject != null)
            {
                GraphStoreManager.ParseGraphFromJson(graphDtoObject);
                await GenerateEntityEmbeddingsAsync(graphDtoObject.Entities);
                Console.WriteLine($"Successfully processed {graphDtoObject.Entities?.Count ?? 0} entities and {graphDtoObject.Relationships?.Count ?? 0} relationships");
            }
            else
            {
                Console.WriteLine("Failed to parse JSON response into GraphDto");
            }*/

            Console.WriteLine("=================================\n");

            //ctx.Append(Log.Data.Result, $"Extracted {GraphStoreManager.Graph.EntityCount} entities and {GraphStoreManager.Graph.RelationshipCount} relationships from {reference}");
            ctx.Succeeded();
        }
        catch (Exception ex)
        {
            ctx.Failed($"Failed to extract entities and relationships", ex);
            Console.WriteLine($"Failed to extract entities and relationships: {ex.Message}");
        }
    });

    public static GraphStore Graph { get; } = new GraphStore();

    // Add embeddings storage for entities
    private static readonly Dictionary<string, float[]> EntityEmbeddings = new Dictionary<string, float[]>();


    public static void ParseGraphFromJson(GraphDto graphDto)
    {
        foreach (var entityDto in graphDto.Entities)
            Graph.AddEntity(new Entity(entityDto.Name, entityDto.Type, entityDto.Attributes));

        foreach (var relDto in graphDto.Relationships)
            Graph.AddRelationship(relDto.Source, relDto.Target, relDto.Type, relDto.Description);
    }

    public static async Task ExtractAndStoreAsync(string content, string reference) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Reference, reference);

        try
        {
            var working = new Context(@"""
You are an expert at extracting entities and relationships from text and code.
Extract all important entities (people, places, organizations, concepts, functions, variables etc.) and their relationships from the provided text.

For each entity, identify:
- Entity name
- Entity type (Person, Organization, Location, Concept, etc.)
- Key attributes or descriptions

For each relationship, identify:
- Source entity
- Target entity  
- Relationship type (works_for, located_in, part_of, etc.)
- Relationship description
""");

            working.AddUserMessage(content);
            var response = await TypeParser.GetAsync(working, typeof(GraphDto));
            if (response == null || response is not GraphDto graphDtoObject)
            {
                Console.WriteLine($"JSON processing issue");
                return;
            }
            Console.WriteLine($"\n=== Extracted from {response} ===");

            if (graphDtoObject != null)
            {
                GraphStoreManager.ParseGraphFromJson(graphDtoObject);
                await GenerateEntityEmbeddingsAsync(graphDtoObject.Entities);
                Console.WriteLine($"Successfully processed {graphDtoObject.Entities?.Count ?? 0} entities and {graphDtoObject.Relationships?.Count ?? 0} relationships");
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

    /// <summary>
    /// THIS IS THE KEY METHOD THAT POPULATES EntityEmbeddings
    /// Called automatically when new entities are extracted from documents
    /// </summary>
    private static async Task GenerateEntityEmbeddingsAsync(List<EntityDto> entities)
    {
        if (Engine.Provider is not IEmbeddingProvider embeddingProvider)
        {
            Console.WriteLine("Warning: No embedding provider available for entity embedding generation");
            return;
        }

        Console.WriteLine($"Generating embeddings for {entities.Count} new entities...");
        int generated = 0;

        foreach (var entityDto in entities)
        {
            // Skip if we already have an embedding for this entity
            if (EntityEmbeddings.ContainsKey(entityDto.Name))
            {
                Console.WriteLine($"  Skipping {entityDto.Name} - embedding already exists");
                continue;
            }

            try
            {
                // Create a comprehensive text representation of the entity
                // This combines all the entity's information into one string for embedding
                var entityText = $"{entityDto.Name} {entityDto.Type} {entityDto.Attributes}";

                Console.WriteLine($"  Generating embedding for: {entityDto.Name} ({entityDto.Type})");

                // Call the embedding provider (Azure OpenAI, Ollama, etc.)
                var embedding = await embeddingProvider.GetEmbeddingAsync(entityText);

                if (embedding != null && embedding.Length > 0)
                {
                    // Normalize the embedding vector and store it
                    EntityEmbeddings[entityDto.Name] = Normalize(embedding);
                    generated++;
                    //Console.WriteLine($"    ✅ Generated embedding (dimension: {embedding.Length})");
                }
                else
                {
                    Console.WriteLine($"    ❌ Failed to generate embedding - null or empty response");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ❌ Failed to generate embedding for entity {entityDto.Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"Generated {generated} new embeddings. Total embeddings: {EntityEmbeddings.Count}");
    }


    // Helper methods for vector operations
    private static float[] Normalize(float[] vector)
    {
        float norm = (float)Math.Sqrt(vector.Sum(v => v * v));
        if (norm < 1e-8) return vector; // avoid divide-by-zero
        return vector.Select(v => v / norm).ToArray();
    }

}