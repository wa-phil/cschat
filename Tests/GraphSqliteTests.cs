using System;
using System.Threading.Tasks;
using CSChat.Storage;

namespace CSChat.Tests
{
    public static class GraphSqliteTests
    {
        public static async Task RunAllTestsAsync()
        {
            Console.WriteLine("=== TESTING SQLITE GRAPH STORAGE ===");
            
            try
            {
                await TestBasicSaveAndLoad();
                await TestSearchFunctionality();
                await TestLargeGraphSaveLoad();
                await TestDatabaseStatistics();
                
                Console.WriteLine("✓ All tests passed!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Tests failed: {ex.Message}");
                throw;
            }
            
            Console.WriteLine("=== TEST COMPLETE ===");
        }

        private static async Task TestBasicSaveAndLoad()
        {
            Console.WriteLine("\n--- Testing Basic Save and Load ---");
            
            var testDbPath = "test_graph.db";
            
            // Clean up any existing test database
            if (System.IO.File.Exists(testDbPath))
                System.IO.File.Delete(testDbPath);

            // Create a sample graph
            var originalGraph = new GraphStore();
            
            // Add some entities
            originalGraph.AddEntity(new Entity("Alice", "Person", "Software Engineer", "test-source"));
            originalGraph.AddEntity(new Entity("Bob", "Person", "Project Manager", "test-source"));
            originalGraph.AddEntity(new Entity("ACME Corp", "Organization", "Software Company", "test-source"));
            originalGraph.AddEntity(new Entity("ProjectX", "Project", "Machine Learning Project", "test-source"));

            // Add some relationships
            originalGraph.AddRelationship("Alice", "ACME Corp", "works_for", "Full-time employee");
            originalGraph.AddRelationship("Bob", "ACME Corp", "works_for", "Full-time employee");
            originalGraph.AddRelationship("Alice", "ProjectX", "works_on", "Lead developer");
            originalGraph.AddRelationship("Bob", "ProjectX", "manages", "Project manager");

            Console.WriteLine($"Original graph: {originalGraph.EntityCount} entities, {originalGraph.RelationshipCount} relationships");

            // Save to SQLite
            await originalGraph.SaveToSqliteAsync(testDbPath);
            Console.WriteLine($"✓ Graph saved to {testDbPath}");

            // Load from SQLite
            var loadedGraph = await GraphStore.LoadFromSqliteAsync(testDbPath);
            Console.WriteLine($"✓ Graph loaded from {testDbPath}");

            // Verify the data
            Console.WriteLine($"Loaded graph: {loadedGraph.EntityCount} entities, {loadedGraph.RelationshipCount} relationships");
            
            if (originalGraph.EntityCount != loadedGraph.EntityCount)
                throw new Exception($"Entity count mismatch: {originalGraph.EntityCount} vs {loadedGraph.EntityCount}");
                
            if (originalGraph.RelationshipCount != loadedGraph.RelationshipCount)
                throw new Exception($"Relationship count mismatch: {originalGraph.RelationshipCount} vs {loadedGraph.RelationshipCount}");

            // Verify specific entities exist
            if (!loadedGraph.Entities.ContainsKey("Alice"))
                throw new Exception("Alice entity not found in loaded graph");
                
            if (loadedGraph.Entities["Alice"].Type != "Person")
                throw new Exception("Alice entity type is incorrect");

            // Verify relationships
            var aliceConnections = loadedGraph.GetConnectedEntities("Alice");
            if (aliceConnections.Count != 2)
                throw new Exception($"Alice should have 2 connections, found {aliceConnections.Count}");

            Console.WriteLine("✓ Basic save and load test passed");

            // Clean up
            System.IO.File.Delete(testDbPath);
        }

        private static async Task TestSearchFunctionality()
        {
            Console.WriteLine("\n--- Testing Search Functionality ---");
            
            var testDbPath = "test_search.db";
            
            // Clean up any existing test database
            if (System.IO.File.Exists(testDbPath))
                System.IO.File.Delete(testDbPath);

            // Create a graph with searchable entities
            var graph = new GraphStore();
            
            graph.AddEntity(new Entity("John Doe", "Person", "Software Developer", "test-source"));
            graph.AddEntity(new Entity("Jane Smith", "Person", "Data Scientist", "test-source"));
            graph.AddEntity(new Entity("TechCorp Inc", "Organization", "Technology Company", "test-source"));
            graph.AddEntity(new Entity("DataMining", "Project", "Big data analysis", "test-source"));

            // Save to database
            await graph.SaveToSqliteAsync(testDbPath);

            // Test searching
            var searchResults = await GraphStore.SearchEntitiesInDatabaseAsync("John", testDbPath);
            if (searchResults.Count != 1 || searchResults[0].Name != "John Doe")
                throw new Exception("Search for 'John' failed");

            var personResults = await GraphStore.SearchEntitiesInDatabaseAsync("", testDbPath, "Person");
            if (personResults.Count != 2)
                throw new Exception($"Search for Person type should return 2 results, got {personResults.Count}");

            Console.WriteLine("✓ Search functionality test passed");

            // Clean up
            System.IO.File.Delete(testDbPath);
        }

        private static async Task TestLargeGraphSaveLoad()
        {
            Console.WriteLine("\n--- Testing Large Graph Performance ---");
            
            var testDbPath = "test_large.db";
            
            // Clean up any existing test database
            if (System.IO.File.Exists(testDbPath))
                System.IO.File.Delete(testDbPath);

            // Create a larger graph
            var graph = new GraphStore();
            
            // Add 1000 entities
            for (int i = 0; i < 1000; i++)
            {
                graph.AddEntity(new Entity($"Entity{i}", "TestType", $"Test entity number {i}", "bulk-test"));
            }
            
            // Add relationships between consecutive entities
            for (int i = 0; i < 999; i++)
            {
                graph.AddRelationship($"Entity{i}", $"Entity{i + 1}", "connects_to", $"Connection {i}");
            }

            Console.WriteLine($"Created large graph: {graph.EntityCount} entities, {graph.RelationshipCount} relationships");

            // Measure save time
            var startTime = DateTime.Now;
            await graph.SaveToSqliteAsync(testDbPath);
            var saveTime = DateTime.Now - startTime;
            Console.WriteLine($"✓ Save completed in {saveTime.TotalMilliseconds:F1}ms");

            // Measure load time
            startTime = DateTime.Now;
            var loadedGraph = await GraphStore.LoadFromSqliteAsync(testDbPath);
            var loadTime = DateTime.Now - startTime;
            Console.WriteLine($"✓ Load completed in {loadTime.TotalMilliseconds:F1}ms");

            // Verify counts
            if (graph.EntityCount != loadedGraph.EntityCount || graph.RelationshipCount != loadedGraph.RelationshipCount)
                throw new Exception("Large graph save/load count mismatch");

            Console.WriteLine("✓ Large graph performance test passed");

            // Clean up
            System.IO.File.Delete(testDbPath);
        }

        private static async Task TestDatabaseStatistics()
        {
            Console.WriteLine("\n--- Testing Database Statistics ---");
            
            var testDbPath = "test_stats.db";
            
            // Clean up any existing test database
            if (System.IO.File.Exists(testDbPath))
                System.IO.File.Delete(testDbPath);

            // Test statistics on non-existent database
            var (entityCount, relationshipCount, lastSaved) = await GraphStore.GetDatabaseStatisticsAsync(testDbPath);
            if (entityCount != 0 || relationshipCount != 0 || lastSaved.HasValue)
                throw new Exception("Statistics should be empty for non-existent database");

            // Create and save a small graph
            var graph = new GraphStore();
            graph.AddEntity(new Entity("Test1", "Type1", "Attr1", "source1"));
            graph.AddEntity(new Entity("Test2", "Type2", "Attr2", "source2"));
            graph.AddRelationship("Test1", "Test2", "relates_to", "Test relationship");

            await graph.SaveToSqliteAsync(testDbPath);

            // Test statistics on saved database
            (entityCount, relationshipCount, lastSaved) = await GraphStore.GetDatabaseStatisticsAsync(testDbPath);
            if (entityCount != 2 || relationshipCount != 1 || !lastSaved.HasValue)
                throw new Exception($"Statistics incorrect: {entityCount} entities, {relationshipCount} relationships, lastSaved: {lastSaved}");

            Console.WriteLine($"✓ Database statistics: {entityCount} entities, {relationshipCount} relationships");
            Console.WriteLine($"✓ Last saved: {lastSaved:yyyy-MM-dd HH:mm:ss}");

            Console.WriteLine("✓ Database statistics test passed");

            // Clean up
            System.IO.File.Delete(testDbPath);
        }
    }
}