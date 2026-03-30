using System;
using System.Threading.Tasks;
using CSChat.Storage;

public static class SimpleSqliteTest 
{
    public static async Task RunTestAsync()
    {
        Console.WriteLine("=== SIMPLE SQLITE TEST ===");
        
        try
        {
            // Create a simple graph
            var graph = new GraphStore();
            graph.AddEntity(new Entity("Test1", "TestType", "Test entity 1"));
            graph.AddEntity(new Entity("Test2", "TestType", "Test entity 2"));
            graph.AddRelationship("Test1", "Test2", "connects_to", "Test connection");
            
            Console.WriteLine($"Created test graph: {graph.EntityCount} entities, {graph.RelationshipCount} relationships");
            
            // Test simple save
            var dbPath = "simple_test.db";
            var storage = new GraphSqliteStorage(dbPath);
            await storage.InitializeAsync();
            await storage.SaveGraphStoreAsync(graph);
            
            Console.WriteLine("✓ Graph saved successfully");
            
            // Test simple load
            var loadedGraph = await storage.LoadGraphStoreAsync();
            Console.WriteLine($"✓ Graph loaded: {loadedGraph.EntityCount} entities, {loadedGraph.RelationshipCount} relationships");
            
            // Verify data
            if (loadedGraph.EntityCount == graph.EntityCount && loadedGraph.RelationshipCount == graph.RelationshipCount)
            {
                Console.WriteLine("✓ Test PASSED - Data integrity verified");
            }
            else
            {
                Console.WriteLine("✗ Test FAILED - Data integrity check failed");
            }
            
            // Cleanup
            if (System.IO.File.Exists(dbPath))
                System.IO.File.Delete(dbPath);
                
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Test FAILED: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        
        Console.WriteLine("=== TEST COMPLETE ===");
    }
}