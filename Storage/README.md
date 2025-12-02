# SQLite Graph Storage Documentation

## Overview

The CSChat project now includes comprehensive SQLite storage support for the knowledge graph system. This allows you to persist entities, relationships, and metadata to a local SQLite database file.

## Features

- **Complete Graph Persistence**: Save and load entire graphs including entities, relationships, and metadata
- **Efficient Storage**: Uses normalized database schema with proper indexes for optimal performance
- **Search Capabilities**: Search entities by name, type, or other criteria without loading the entire graph
- **Transaction Safety**: All operations use database transactions for data integrity
- **Metadata Tracking**: Tracks creation times, update times, and graph statistics

## Database Schema

The storage system creates four main tables:

### entities
- `id` (Primary Key)
- `name` (Unique entity identifier)
- `type` (Entity type: Person, Organization, etc.)
- `attributes` (Entity description/attributes)
- `source_reference` (Reference to source document)
- `created_at` / `updated_at` (Timestamps)

### relationships
- `id` (Primary Key)
- `source_entity_id` / `target_entity_id` (Foreign keys to entities)
- `relationship_type` (Type of relationship)
- `description` (Relationship description)
- `source_reference` (Reference to source document)
- `created_at` (Timestamp)

### graph_metadata
- Stores graph-level information like last save time and counts

### entity_embeddings
- For future vector storage capabilities

## Basic Usage

### Saving a Graph

```csharp
// Save current graph to default database (graph.db)
await GraphStoreManager.Graph.SaveToSqliteAsync();

// Save to specific database file
await GraphStoreManager.Graph.SaveToSqliteAsync("my_graph.db");

// Save with status output
await GraphStoreManager.Graph.SaveWithStatusAsync("my_graph.db");
```

### Loading a Graph

```csharp
// Load from default database
var loadedGraph = await GraphStore.LoadFromSqliteAsync();

// Load from specific database file
var loadedGraph = await GraphStore.LoadFromSqliteAsync("my_graph.db");

// Load with status output
var loadedGraph = await GraphStore.LoadWithStatusAsync("my_graph.db");
```

### Working with the Current Graph Context

```csharp
// Save current graph context
await CSChat.Examples.SqliteGraphExample.SaveCurrentGraphAsync("current_graph.db");

// Load into current graph context
await CSChat.Examples.SqliteGraphExample.LoadIntoCurrentGraphAsync("current_graph.db");
```

## Advanced Operations

### Database Statistics

```csharp
// Get database statistics without loading the full graph
var (entityCount, relationshipCount, lastSaved) = 
    await GraphStore.GetDatabaseStatisticsAsync("my_graph.db");

Console.WriteLine($"Database contains {entityCount} entities, {relationshipCount} relationships");
Console.WriteLine($"Last saved: {lastSaved}");
```

### Searching

```csharp
// Search for entities by name
var results = await GraphStore.SearchEntitiesInDatabaseAsync("Alice", "my_graph.db");

// Search for entities by type
var people = await GraphStore.SearchEntitiesInDatabaseAsync("", "my_graph.db", "Person");
```

### Direct Storage Operations

```csharp
// Get storage instance for advanced operations
var storage = GraphStoreManager.Graph.GetStorage("my_graph.db");

// Initialize database
await storage.InitializeAsync();

// Save individual entities
await storage.SaveEntityAsync(new Entity("Test", "Type", "Attributes"));

// Clear all data
await storage.ClearGraphAsync();
```

## Example Usage

### Running the Complete Example

```csharp
// Run the comprehensive SQLite example
await CSChat.Examples.SqliteGraphExample.RunExampleAsync();
```

This example will:
1. Create a sample graph with people, organizations, projects, and technologies
2. Save it to SQLite database
3. Clear the in-memory graph
4. Load the graph back from SQLite
5. Demonstrate search functionality
6. Show database statistics
7. Run community analysis

### Running Tests

```csharp
// Run all SQLite integration tests
await CSChat.Tests.GraphSqliteTests.RunAllTestsAsync();
```

Tests include:
- Basic save and load functionality
- Search capabilities
- Large graph performance testing
- Database statistics

## File Locations

- **Default database location**: `graph.db` in the current working directory
- **Example database**: `example_graph.db` (created by the example)
- **Test databases**: `test_*.db` (temporary, cleaned up after tests)

## Performance Considerations

- **Batch Operations**: The storage system uses batch operations for saving multiple entities/relationships
- **Indexes**: Proper indexes are created for common query patterns
- **Transactions**: All multi-step operations use transactions for atomicity
- **Large Graphs**: Tested with 1000+ entities and relationships

## Error Handling

The storage system includes comprehensive error handling:
- Database connection failures
- Transaction rollbacks on errors
- File system permission issues
- Data integrity constraints

## Best Practices

1. **Regular Saves**: Save your graph periodically, especially after significant changes
2. **Backup**: Regular backup of your `.db` files
3. **Path Management**: Use absolute paths when working with database files
4. **Testing**: Always test save/load operations in development
5. **Cleanup**: Clean up temporary test databases

## Integration with Existing Workflow

The SQLite storage integrates seamlessly with the existing graph system:
- All existing graph operations work unchanged
- Storage is an optional add-on capability
- Compatible with community analysis and other graph features
- Preserves all entity and relationship metadata

## Troubleshooting

### Common Issues

1. **Permission Errors**: Ensure write permissions to the database directory
2. **File Locks**: Close other applications that might have the database file open
3. **Path Issues**: Use absolute paths for reliable database access
4. **Schema Updates**: Delete old database files if schema changes

### Verification

To verify your storage is working correctly:

```csharp
// Check if database exists and get basic stats
var storage = new CSChat.Storage.GraphSqliteStorage("my_graph.db");
if (storage.DatabaseExists())
{
    var (count, relCount, lastSaved) = await storage.GetStatisticsAsync();
    Console.WriteLine($"Database verified: {count} entities, {relCount} relationships");
}
```

## Future Enhancements

Planned improvements:
- Vector embedding storage and similarity search
- Graph versioning and change tracking
- Export/import capabilities (JSON, CSV)
- Multi-database synchronization
- Advanced querying capabilities

This storage system provides a robust foundation for persisting and working with knowledge graphs in the CSChat application.