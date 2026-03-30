using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CSChat.Storage
{
    public class GraphSqliteStorage
    {
        private readonly string _connectionString;
        private readonly string _databasePath;

        public GraphSqliteStorage(string databasePath = "graph.db")
        {
            _databasePath = databasePath;
            // Simple connection string for SQLite
            _connectionString = $"Data Source={databasePath}";
        }

        /// <summary>
        /// Initialize the database with the required schema
        /// </summary>
        public async Task InitializeAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Create entities table
            var createEntitiesTable = @"
                CREATE TABLE IF NOT EXISTS entities (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    type TEXT NOT NULL,
                    attributes TEXT,
                    source_reference TEXT,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS idx_entities_name ON entities(name);
                CREATE INDEX IF NOT EXISTS idx_entities_type ON entities(type);
                CREATE INDEX IF NOT EXISTS idx_entities_updated ON entities(updated_at);
            ";

            // Create relationships table
            var createRelationshipsTable = @"
                CREATE TABLE IF NOT EXISTS relationships (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source_entity_id INTEGER NOT NULL,
                    target_entity_id INTEGER NOT NULL,
                    relationship_type TEXT NOT NULL,
                    description TEXT,
                    source_reference TEXT,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (source_entity_id) REFERENCES entities(id) ON DELETE CASCADE,
                    FOREIGN KEY (target_entity_id) REFERENCES entities(id) ON DELETE CASCADE,
                    UNIQUE(source_entity_id, target_entity_id, relationship_type)
                );

                CREATE INDEX IF NOT EXISTS idx_relationships_source ON relationships(source_entity_id);
                CREATE INDEX IF NOT EXISTS idx_relationships_target ON relationships(target_entity_id);
                CREATE INDEX IF NOT EXISTS idx_relationships_type ON relationships(relationship_type);
                CREATE INDEX IF NOT EXISTS idx_relationships_created ON relationships(created_at);
            ";

            // Create metadata table for storing graph-level information
            var createMetadataTable = @"
                CREATE TABLE IF NOT EXISTS graph_metadata (
                    key TEXT PRIMARY KEY,
                    value TEXT,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );
            ";

            // Create entity embeddings table (for future use with vector operations)
            var createEmbeddingsTable = @"
                CREATE TABLE IF NOT EXISTS entity_embeddings (
                    entity_id INTEGER PRIMARY KEY,
                    embedding BLOB,
                    embedding_model TEXT,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (entity_id) REFERENCES entities(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_embeddings_model ON entity_embeddings(embedding_model);
            ";

            await using var command = new SqliteCommand($"{createEntitiesTable}{createRelationshipsTable}{createMetadataTable}{createEmbeddingsTable}", connection);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Save a single entity to the database
        /// </summary>
        public async Task<long> SaveEntityAsync(Entity entity)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var insertSql = @"
                INSERT OR REPLACE INTO entities (name, type, attributes, source_reference, updated_at)
                VALUES (@name, @type, @attributes, @source_reference, CURRENT_TIMESTAMP)
                RETURNING id;
            ";

            await using var command = new SqliteCommand(insertSql, connection);
            command.Parameters.AddWithValue("@name", entity.Name);
            command.Parameters.AddWithValue("@type", entity.Type);
            command.Parameters.AddWithValue("@attributes", entity.Attributes ?? "");
            command.Parameters.AddWithValue("@source_reference", entity.SourceReference ?? "");

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }

        /// <summary>
        /// Save multiple entities in a batch transaction
        /// </summary>
        public async Task SaveEntitiesAsync(IEnumerable<Entity> entities)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            try
            {
                var insertSql = @"
                    INSERT OR REPLACE INTO entities (name, type, attributes, source_reference, updated_at)
                    VALUES (@name, @type, @attributes, @source_reference, CURRENT_TIMESTAMP);
                ";

                foreach (var entity in entities)
                {
                    await using var command = new SqliteCommand(insertSql, connection);
                    command.Transaction = transaction;
                    command.Parameters.AddWithValue("@name", entity.Name);
                    command.Parameters.AddWithValue("@type", entity.Type);
                    command.Parameters.AddWithValue("@attributes", entity.Attributes ?? "");
                    command.Parameters.AddWithValue("@source_reference", entity.SourceReference ?? "");
                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Save a single relationship to the database
        /// </summary>
        public async Task SaveRelationshipAsync(Relationship relationship)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // First, ensure both entities exist and get their IDs
            var getEntityIdSql = "SELECT id FROM entities WHERE name = @name";
            
            await using var sourceCommand = new SqliteCommand(getEntityIdSql, connection);
            sourceCommand.Parameters.AddWithValue("@name", relationship.Source.Name);
            var sourceId = await sourceCommand.ExecuteScalarAsync();

            await using var targetCommand = new SqliteCommand(getEntityIdSql, connection);
            targetCommand.Parameters.AddWithValue("@name", relationship.Target.Name);
            var targetId = await targetCommand.ExecuteScalarAsync();

            if (sourceId == null || targetId == null)
                throw new InvalidOperationException("Source or target entity not found in database");

            // Insert the relationship
            var insertSql = @"
                INSERT OR REPLACE INTO relationships (source_entity_id, target_entity_id, relationship_type, description, source_reference)
                VALUES (@source_id, @target_id, @type, @description, @source_reference);
            ";

            await using var command = new SqliteCommand(insertSql, connection);
            command.Parameters.AddWithValue("@source_id", sourceId);
            command.Parameters.AddWithValue("@target_id", targetId);
            command.Parameters.AddWithValue("@type", relationship.Type);
            command.Parameters.AddWithValue("@description", relationship.Description ?? "");
            command.Parameters.AddWithValue("@source_reference", relationship.SourceReference ?? "");

            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Save multiple relationships in a batch transaction
        /// </summary>
        public async Task SaveRelationshipsAsync(IEnumerable<Relationship> relationships)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            try
            {
                var getEntityIdSql = "SELECT id FROM entities WHERE name = @name";
                var insertRelationshipSql = @"
                    INSERT OR REPLACE INTO relationships (source_entity_id, target_entity_id, relationship_type, description, source_reference)
                    VALUES (@source_id, @target_id, @type, @description, @source_reference);
                ";

                foreach (var relationship in relationships)
                {
                    // Get source entity ID
                    await using var sourceCommand = new SqliteCommand(getEntityIdSql, connection);
                    sourceCommand.Transaction = transaction;
                    sourceCommand.Parameters.AddWithValue("@name", relationship.Source.Name);
                    var sourceId = await sourceCommand.ExecuteScalarAsync();

                    // Get target entity ID
                    await using var targetCommand = new SqliteCommand(getEntityIdSql, connection);
                    targetCommand.Transaction = transaction;
                    targetCommand.Parameters.AddWithValue("@name", relationship.Target.Name);
                    var targetId = await targetCommand.ExecuteScalarAsync();

                    if (sourceId == null || targetId == null)
                        continue; // Skip if entities don't exist

                    // Insert relationship
                    await using var relationshipCommand = new SqliteCommand(insertRelationshipSql, connection);
                    relationshipCommand.Transaction = transaction;
                    relationshipCommand.Parameters.AddWithValue("@source_id", sourceId);
                    relationshipCommand.Parameters.AddWithValue("@target_id", targetId);
                    relationshipCommand.Parameters.AddWithValue("@type", relationship.Type);
                    relationshipCommand.Parameters.AddWithValue("@description", relationship.Description ?? "");
                    relationshipCommand.Parameters.AddWithValue("@source_reference", relationship.SourceReference ?? "");
                    await relationshipCommand.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Save the entire graph store to the database
        /// </summary>
        public async Task SaveGraphStoreAsync(GraphStore graphStore)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            try
            {
                // Save all entities first using inline SQL
                var insertEntitySql = @"
                    INSERT OR REPLACE INTO entities (name, type, attributes, source_reference, updated_at)
                    VALUES (@name, @type, @attributes, @source_reference, CURRENT_TIMESTAMP);
                ";

                foreach (var entity in graphStore.Entities.Values)
                {
                    await using var command = new SqliteCommand(insertEntitySql, connection);
                    command.Transaction = transaction;
                    command.Parameters.AddWithValue("@name", entity.Name);
                    command.Parameters.AddWithValue("@type", entity.Type);
                    command.Parameters.AddWithValue("@attributes", entity.Attributes ?? "");
                    command.Parameters.AddWithValue("@source_reference", entity.SourceReference ?? "");
                    await command.ExecuteNonQueryAsync();
                }

                // Save all relationships using inline SQL
                var getEntityIdSql = "SELECT id FROM entities WHERE name = @name";
                var insertRelationshipSql = @"
                    INSERT OR REPLACE INTO relationships (source_entity_id, target_entity_id, relationship_type, description, source_reference)
                    VALUES (@source_id, @target_id, @type, @description, @source_reference);
                ";

                foreach (var relationship in graphStore.Relationships)
                {
                    // Get source entity ID
                    await using var sourceCommand = new SqliteCommand(getEntityIdSql, connection);
                    sourceCommand.Transaction = transaction;
                    sourceCommand.Parameters.AddWithValue("@name", relationship.Source.Name);
                    var sourceId = await sourceCommand.ExecuteScalarAsync();

                    // Get target entity ID
                    await using var targetCommand = new SqliteCommand(getEntityIdSql, connection);
                    targetCommand.Transaction = transaction;
                    targetCommand.Parameters.AddWithValue("@name", relationship.Target.Name);
                    var targetId = await targetCommand.ExecuteScalarAsync();

                    if (sourceId == null || targetId == null)
                        continue; // Skip if entities don't exist

                    // Insert relationship
                    await using var relationshipCommand = new SqliteCommand(insertRelationshipSql, connection);
                    relationshipCommand.Transaction = transaction;
                    relationshipCommand.Parameters.AddWithValue("@source_id", sourceId);
                    relationshipCommand.Parameters.AddWithValue("@target_id", targetId);
                    relationshipCommand.Parameters.AddWithValue("@type", relationship.Type);
                    relationshipCommand.Parameters.AddWithValue("@description", relationship.Description ?? "");
                    relationshipCommand.Parameters.AddWithValue("@source_reference", relationship.SourceReference ?? "");
                    await relationshipCommand.ExecuteNonQueryAsync();
                }

                // Update metadata
                var metadataSql = @"
                    INSERT OR REPLACE INTO graph_metadata (key, value, updated_at)
                    VALUES (@key, @value, CURRENT_TIMESTAMP);
                ";

                await using var metadataCommand = new SqliteCommand(metadataSql, connection);
                metadataCommand.Transaction = transaction;
                metadataCommand.Parameters.AddWithValue("@key", "last_saved");
                metadataCommand.Parameters.AddWithValue("@value", DateTime.UtcNow.ToString("O"));
                await metadataCommand.ExecuteNonQueryAsync();

                metadataCommand.Parameters["@key"].Value = "entity_count";
                metadataCommand.Parameters["@value"].Value = graphStore.EntityCount.ToString();
                await metadataCommand.ExecuteNonQueryAsync();

                metadataCommand.Parameters["@key"].Value = "relationship_count";
                metadataCommand.Parameters["@value"].Value = graphStore.RelationshipCount.ToString();
                await metadataCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Load all entities from the database
        /// </summary>
        public async Task<List<Entity>> LoadEntitiesAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT name, type, attributes, source_reference FROM entities ORDER BY name";
            await using var command = new SqliteCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            var entities = new List<Entity>();
            while (await reader.ReadAsync())
            {
                var entity = new Entity(
                    reader["name"].ToString() ?? "",
                    reader["type"].ToString() ?? "",
                    reader["attributes"].ToString() ?? "",
                    reader["source_reference"].ToString() ?? ""
                );
                entities.Add(entity);
            }

            return entities;
        }

        /// <summary>
        /// Load all relationships from the database
        /// </summary>
        public async Task<List<(string sourceName, string targetName, string type, string description, string sourceReference)>> LoadRelationshipsAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    se.name as source_name,
                    te.name as target_name,
                    r.relationship_type,
                    r.description,
                    r.source_reference
                FROM relationships r
                JOIN entities se ON r.source_entity_id = se.id
                JOIN entities te ON r.target_entity_id = te.id
                ORDER BY se.name, te.name
            ";

            await using var command = new SqliteCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            var relationships = new List<(string, string, string, string, string)>();
            while (await reader.ReadAsync())
            {
                relationships.Add((
                    reader["source_name"].ToString() ?? "",
                    reader["target_name"].ToString() ?? "",
                    reader["relationship_type"].ToString() ?? "",
                    reader["description"].ToString() ?? "",
                    reader["source_reference"].ToString() ?? ""
                ));
            }

            return relationships;
        }

        /// <summary>
        /// Load the entire graph store from the database
        /// </summary>
        public async Task<GraphStore> LoadGraphStoreAsync()
        {
            var graphStore = new GraphStore();

            // Load entities
            var entities = await LoadEntitiesAsync();
            foreach (var entity in entities)
            {
                graphStore.AddEntity(entity);
            }

            // Load relationships
            var relationships = await LoadRelationshipsAsync();
            foreach (var (sourceName, targetName, type, description, sourceReference) in relationships)
            {
                graphStore.AddRelationship(sourceName, targetName, type, description);
            }

            return graphStore;
        }

        /// <summary>
        /// Get database statistics
        /// </summary>
        public async Task<(int entityCount, int relationshipCount, DateTime? lastSaved)> GetStatisticsAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var entityCountSql = "SELECT COUNT(*) FROM entities";
            await using var entityCommand = new SqliteCommand(entityCountSql, connection);
            var entityCount = Convert.ToInt32(await entityCommand.ExecuteScalarAsync());

            var relationshipCountSql = "SELECT COUNT(*) FROM relationships";
            await using var relationshipCommand = new SqliteCommand(relationshipCountSql, connection);
            var relationshipCount = Convert.ToInt32(await relationshipCommand.ExecuteScalarAsync());

            var lastSavedSql = "SELECT value FROM graph_metadata WHERE key = 'last_saved'";
            await using var lastSavedCommand = new SqliteCommand(lastSavedSql, connection);
            var lastSavedResult = await lastSavedCommand.ExecuteScalarAsync();
            var lastSavedStr = lastSavedResult?.ToString();

            DateTime? lastSaved = null;
            if (!string.IsNullOrEmpty(lastSavedStr) && DateTime.TryParse(lastSavedStr, out var parsed))
            {
                lastSaved = parsed;
            }

            return (entityCount, relationshipCount, lastSaved);
        }

        /// <summary>
        /// Search entities by name or type
        /// </summary>
        public async Task<List<Entity>> SearchEntitiesAsync(string searchTerm, string? entityType = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT name, type, attributes, source_reference 
                FROM entities 
                WHERE name LIKE @search 
                    AND (@type IS NULL OR type = @type)
                ORDER BY name
            ";

            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@search", $"%{searchTerm}%");
            command.Parameters.AddWithValue("@type", entityType ?? (object)DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync();
            var entities = new List<Entity>();

            while (await reader.ReadAsync())
            {
                var entity = new Entity(
                    reader["name"].ToString() ?? "",
                    reader["type"].ToString() ?? "",
                    reader["attributes"].ToString() ?? "",
                    reader["source_reference"].ToString() ?? ""
                );
                entities.Add(entity);
            }

            return entities;
        }

        /// <summary>
        /// Get entities by type
        /// </summary>
        public async Task<List<Entity>> GetEntitiesByTypeAsync(string entityType)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT name, type, attributes, source_reference FROM entities WHERE type = @type ORDER BY name";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@type", entityType);

            await using var reader = await command.ExecuteReaderAsync();
            var entities = new List<Entity>();

            while (await reader.ReadAsync())
            {
                var entity = new Entity(
                    reader["name"].ToString() ?? "",
                    reader["type"].ToString() ?? "",
                    reader["attributes"].ToString() ?? "",
                    reader["source_reference"].ToString() ?? ""
                );
                entities.Add(entity);
            }

            return entities;
        }

        /// <summary>
        /// Delete all graph data
        /// </summary>
        public async Task ClearGraphAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            try
            {
                await using var deleteRelationships = new SqliteCommand("DELETE FROM relationships", connection);
                deleteRelationships.Transaction = transaction;
                await deleteRelationships.ExecuteNonQueryAsync();

                await using var deleteEntities = new SqliteCommand("DELETE FROM entities", connection);
                deleteEntities.Transaction = transaction;
                await deleteEntities.ExecuteNonQueryAsync();

                await using var deleteMetadata = new SqliteCommand("DELETE FROM graph_metadata", connection);
                deleteMetadata.Transaction = transaction;
                await deleteMetadata.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Check if database exists and is initialized
        /// </summary>
        public bool DatabaseExists()
        {
            return File.Exists(_databasePath);
        }

        /// <summary>
        /// Get the database file path
        /// </summary>
        public string GetDatabasePath()
        {
            return _databasePath;
        }
    }
}