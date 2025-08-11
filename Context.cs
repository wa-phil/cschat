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

// Add this class after the GraphAnalytics class (around line 500, after GraphDto class)

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
/*
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
/*
public class GraphDto
{
    public List<EntityDto> Entities { get; set; } = new List<EntityDto>();
    public List<RelationshipDto> Relationships { get; set; } = new List<RelationshipDto>();
}*/

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

    public IEnumerable<ChatMessage> Messages(bool InluceSystemMessage = true)
    {
        var result = new List<ChatMessage>();
        if (InluceSystemMessage) { result.Add(GetSystemMessage()); }
        result.AddRange(_messages);
        return result;
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
    
    public static List<(Reference Reference, string MergedContent)> Flatten(List<(Reference Reference, string Content)> entries)
    {
        var grouped = entries                           // grouped is Dictionary: source -> chunks, where chunks: List<(Start, End, Lines)>
            .GroupBy(entry => entry.Reference.Source)   // Group chunks by source file
            .ToDictionary(                              // use GroupBy + ToDictionary to correctly handle multiple source references
                g => g.Key,                             // key: source
                g => g.Select(entry => (
                    Start: entry.Reference.Start,       // nullable start line
                    End: entry.Reference.End,           // nullable end line
                    Lines: entry.Content.Split('\n')    // List<string>
                )).ToList()                             // Change from Enumerable -> List to avoid multiple reevaluations later
            );

        return grouped.Select(kvp => // flatten Dictionary: source -> chunks to List<(Reference, Content)>
        {
            var source = kvp.Key;
            var chunks = kvp.Value;

            var lineMap = chunks                                    // for files that have chunks
                .Where(c => c.Start.HasValue && c.End.HasValue)     // only consider line-ranged chunks
                .SelectMany(c => c.Lines                            // calculate line numbers for every line
                    .Select((line, idx) => (LineNumber: c.Start!.Value + idx, Content: line)))
                .GroupBy(x => x.LineNumber)                         // group by line number to deduplicate
                .ToDictionary(g => g.Key, g => g.First().Content);  // keep first occurrence of each line

            var fullChunks = chunks                                 // for whole files, or content w/ only one reference.
                .Where(c => !c.Start.HasValue || !c.End.HasValue)   // whole-content chunks (no line info)
                .SelectMany(c => c.Lines)                           // just extract their lines
                .ToList();

            var merged = lineMap                                    // put it all together...
                .OrderBy(kv => kv.Key)                              // order lines by line number
                .Select(kv => kv.Value)                             // get the content
                .Concat(fullChunks)                                 // append whole-content chunks at the end
                .ToList();                                          // convert to List to avoid multiple reevaluations

            var content = string.Join("\n", merged);                // join into final output
            var minLine = lineMap.Keys.DefaultIfEmpty().Min();      // min line number for ref
            var maxLine = lineMap.Keys.DefaultIfEmpty().Max();      // max line number for ref

            var reference = lineMap.Count > 0
                ? Reference.Partial(source, minLine, maxLine)       // ranged ref if we had any line-based chunks
                : Reference.Full(source);                           // otherwise fallback to whole-content

            return (reference, content);                            // return the combined entry
        }).ToList(); // gather all merged entries
    }

    public static async Task InvokeAsync(string input, Context Context) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        Context.ClearContext();

        // Try to add context to Context first
        var references = new List<string>();
        var content = await SearchVectorDB(input);
        if (content != null && content.Count > 0)
        {
            var results = Flatten(content.Select(r => (r.Reference, r.Content)).ToList());
            foreach (var result in results)
            {
                references.Add(result.Reference.ToString());
                Context.AddContext(result.Reference.ToString(), result.MergedContent);
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
        IEmbeddingProvider embeddingProvider = Engine.Provider as IEmbeddingProvider ?? throw new InvalidOperationException("Current configured provider does not support embeddings.");

        var GetEmbeddingAsync = Program.config.RagSettings.UseEmbeddings
            ? embeddingProvider!.GetEmbeddingAsync
            : (Func<string, Task<float[]>>) (text => Task.FromResult(new float[0])); // Return empty embedding if embeddings are not used

        ctx.Append(Log.Data.Reference, reference.Substring(0, Math.Min(reference.Length, 50)));
        var embeddings = new List<(Reference Reference, string Chunk, float[] Embedding)>();
        var chunks = Engine.TextChunker!.ChunkText(reference, content);
        ctx.Append(Log.Data.Count, chunks.Count);

        await Task.WhenAll(chunks.Select(async chunk =>
            embeddings.Add((
                Reference: chunk.Reference,
                Chunk: chunk.Content,
                Embedding: await GetEmbeddingAsync(chunk.Content)
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
            await graphProvider!.GetEntitiesAndRelationshipsAsync(chunk.Content, chunk.Reference.ToString());
            
            chunksProcessed++;
        }
        
        ctx.Append(Log.Data.Result, $"Processed {chunksProcessed} chunks and stored graph data");
        ctx.Succeeded(chunksProcessed > 0);
    });    

    public static async Task<List<SearchResult>> SearchReferences(string reference) => await Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var results = new List<SearchResult>();
        if (string.IsNullOrEmpty(reference) || null == Engine.VectorStore || Engine.VectorStore.IsEmpty)
        {
            return Task.FromResult(results);
        }

        results = Engine.VectorStore.SearchReferences(reference);
        ctx.Append(Log.Data.Result, results.Select(r => r.Reference).ToArray());
        ctx.Succeeded();
        return Task.FromResult(results);
    });

    public static async Task<List<SearchResult>> SearchVectorDB(string userMessage) => await Log.MethodAsync(async ctx =>
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

        var results = items.Where(i => i.Score >= average).ToList();
        if (results.Count == 0)
        {
            ctx.Append(Log.Data.Message, "No relevant results found in the vector store.");
        }
        else
        {
            ctx.Append(Log.Data.Result, results.Select(r => r.Reference).ToArray());
        }
        ctx.Succeeded();
        return results;
    });
}