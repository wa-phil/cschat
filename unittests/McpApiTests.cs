using Xunit;
using Xunit.Abstractions;
using System;
using System.Reflection;
using System.Linq;

namespace unittests
{
    /// <summary>
    /// Tests to explore and validate the ModelContextProtocol NuGet package APIs
    /// </summary>
    public class McpApiTests
    {
        private readonly ITestOutputHelper _output;

        public McpApiTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Should_Find_ModelContextProtocol_Assemblies()
        {
            // Test that we can load the MCP assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName?.Contains("ModelContextProtocol") == true)
                .ToList();

            _output.WriteLine($"Found {assemblies.Count} ModelContextProtocol assemblies:");
            foreach (var assembly in assemblies)
            {
                _output.WriteLine($"  - {assembly.FullName}");
            }
        }

        [Fact]
        public void Should_Explore_ModelContextProtocol_Types()
        {
            // Get all types from ModelContextProtocol assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName?.Contains("ModelContextProtocol") == true);

            foreach (var assembly in assemblies)
            {
                _output.WriteLine($"\nTypes in {assembly.GetName().Name}: loaded @:{assembly.Location}");
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsPublic)
                        .OrderBy(t => t.FullName);

                    foreach (var type in types)
                    {
                        _output.WriteLine($"  {type.FullName}");
                        
                        // Show some methods for interesting types
                        if (type.Name.Contains("Client") || type.Name.Contains("Factory") || type.Name.Contains("Transport"))
                        {
                            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                                .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && 
                                           !m.Name.Equals("GetHashCode") && !m.Name.Equals("ToString") && 
                                           !m.Name.Equals("Equals") && !m.Name.Equals("GetType"))
                                .Take(5);
                            
                            foreach (var method in methods)
                            {
                                _output.WriteLine($"    → {method.Name}({string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"  Error loading types: {ex.Message}");
                }
            }
        }

        [Fact]
        public void Should_Explore_ModelContextProtocol_Core_Specifically()
        {
            _output.WriteLine("=== Exploring ModelContextProtocol.Core Assembly Specifically ===");
            
            try
            {
                // Force load the Core assembly by referencing a type we know exists
                var coreAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "ModelContextProtocol.Core");
                
                if (coreAssembly == null)
                {
                    _output.WriteLine("ModelContextProtocol.Core assembly not loaded. Attempting to load...");
                    // Try to load by referencing any type from Core
                    try
                    {
                        // This should force the assembly to load
                        var dummy = typeof(object).Assembly.GetType("ModelContextProtocol.Core.SomeType");
                        coreAssembly = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => a.GetName().Name == "ModelContextProtocol.Core");
                    }
                    catch
                    {
                        _output.WriteLine("Could not force load ModelContextProtocol.Core");
                    }
                }

                if (coreAssembly != null)
                {
                    _output.WriteLine($"Found ModelContextProtocol.Core assembly: {coreAssembly.FullName}");
                    _output.WriteLine($"Location: {coreAssembly.Location}");
                    
                    var allTypes = coreAssembly.GetTypes().Where(t => t.IsPublic).OrderBy(t => t.FullName);
                    
                    _output.WriteLine("\nAll public types in ModelContextProtocol.Core:");
                    foreach (var type in allTypes)
                    {
                        _output.WriteLine($"  {type.FullName}");
                        
                        // Show details for Client-related types
                        if (type.Namespace?.Contains("Client") == true || 
                            type.Name.Contains("Client") || 
                            type.Name.Contains("Transport") ||
                            type.Name.Contains("Factory"))
                        {
                            ShowTypeDetails(type);
                        }
                    }
                }
                else
                {
                    _output.WriteLine("ModelContextProtocol.Core assembly not found in current domain");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error exploring ModelContextProtocol.Core: {ex.Message}");
            }
        }

        [Fact]
        public void Should_Test_StdioClientTransport_Availability()
        {
            _output.WriteLine("Testing StdioClientTransport in both ModelContextProtocol and ModelContextProtocol.Core assemblies:");
            
            // Try ModelContextProtocol first
            try 
            {
                var transportType = Type.GetType("ModelContextProtocol.Client.StdioClientTransport, ModelContextProtocol");
                if (transportType != null)
                {
                    _output.WriteLine($"Found in ModelContextProtocol: {transportType.FullName}");
                    ShowTypeDetails(transportType);
                }
                else
                {
                    _output.WriteLine("StdioClientTransport not found in ModelContextProtocol assembly");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error checking ModelContextProtocol: {ex.Message}");
            }

            // Try ModelContextProtocol.Core
            try 
            {
                var transportType = Type.GetType("ModelContextProtocol.Client.StdioClientTransport, ModelContextProtocol.Core");
                if (transportType != null)
                {
                    _output.WriteLine($"Found in ModelContextProtocol.Core: {transportType.FullName}");
                    ShowTypeDetails(transportType);
                }
                else
                {
                    _output.WriteLine("StdioClientTransport not found in ModelContextProtocol.Core assembly");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error checking ModelContextProtocol.Core: {ex.Message}");
            }
        }

        private void ShowTypeDetails(Type type)
        {
            // Show constructors
            var constructors = type.GetConstructors();
            _output.WriteLine("Constructors:");
            foreach (var ctor in constructors)
            {
                var parameters = string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                _output.WriteLine($"  {type.Name}({parameters})");
            }
            
            // Show public methods
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && 
                           !m.Name.Equals("GetHashCode") && !m.Name.Equals("ToString") && 
                           !m.Name.Equals("Equals") && !m.Name.Equals("GetType"))
                .Take(10);
            
            if (methods.Any())
            {
                _output.WriteLine("Methods:");
                foreach (var method in methods)
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    _output.WriteLine($"  {method.ReturnType.Name} {method.Name}({parameters})");
                }
            }
        }

        [Fact]
        public void Should_Test_McpClientFactory_Availability()
        {
            _output.WriteLine("Testing McpClientFactory in both ModelContextProtocol and ModelContextProtocol.Core assemblies:");
            
            // Try ModelContextProtocol first
            try 
            {
                var factoryType = Type.GetType("ModelContextProtocol.Client.McpClientFactory, ModelContextProtocol");
                if (factoryType != null)
                {
                    _output.WriteLine($"Found in ModelContextProtocol: {factoryType.FullName}");
                    ShowTypeDetails(factoryType);
                }
                else
                {
                    _output.WriteLine("McpClientFactory not found in ModelContextProtocol assembly");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error checking ModelContextProtocol: {ex.Message}");
            }

            // Try ModelContextProtocol.Core
            try 
            {
                var factoryType = Type.GetType("ModelContextProtocol.Client.McpClientFactory, ModelContextProtocol.Core");
                if (factoryType != null)
                {
                    _output.WriteLine($"Found in ModelContextProtocol.Core: {factoryType.FullName}");
                    ShowTypeDetails(factoryType);
                }
                else
                {
                    _output.WriteLine("McpClientFactory not found in ModelContextProtocol.Core assembly");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error checking ModelContextProtocol.Core: {ex.Message}");
            }
        }

        [Fact]
        public void Should_List_All_Client_Related_Types()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName?.Contains("ModelContextProtocol") == true);

            _output.WriteLine("All Client-related types:");
            
            foreach (var assembly in assemblies)
            {
                try
                {
                    var clientTypes = assembly.GetTypes()
                        .Where(t => t.IsPublic && (
                            t.Name.Contains("Client") || 
                            t.Name.Contains("Factory") || 
                            t.Name.Contains("Transport") ||
                            t.Namespace?.Contains("Client") == true
                        ))
                        .OrderBy(t => t.FullName);

                    foreach (var type in clientTypes)
                    {
                        _output.WriteLine($"  {type.FullName}");
                        
                        if (type.IsClass && !type.IsAbstract)
                        {
                            _output.WriteLine($"    → Can be instantiated: {type.IsClass && !type.IsAbstract}");
                        }
                        
                        if (type.IsInterface)
                        {
                            _output.WriteLine($"    → Interface");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Error in assembly {assembly.GetName().Name}: {ex.Message}");
                }
            }

            if (!assemblies.Any())
            {
                _output.WriteLine("No ModelContextProtocol assemblies found - this is expected during unit tests.");
            }
        }

        [Fact]
        public void Explore_McpClientTool_Properties()
        {
            // Let's understand what properties are available on McpClientTool
            var coreAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "ModelContextProtocol.Core");

            Assert.NotNull(coreAssembly);
            _output.WriteLine($"Exploring McpClientTool from {coreAssembly.GetName().Name}");

            var mcpClientToolType = coreAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == "McpClientTool");

            if (mcpClientToolType != null)
            {
                _output.WriteLine($"\n🔍 McpClientTool Properties:");
                var properties = mcpClientToolType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var prop in properties)
                {
                    _output.WriteLine($"  • {prop.Name}: {prop.PropertyType.Name}");
                    if (prop.PropertyType.Name.Contains("Schema") || prop.Name.Contains("Schema"))
                    {
                        _output.WriteLine($"    ↳ 🎯 SCHEMA PROPERTY FOUND!");
                    }
                }

                _output.WriteLine($"\n🔍 McpClientTool Methods:");
                var methods = mcpClientToolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && 
                               !m.Name.Equals("GetHashCode") && !m.Name.Equals("ToString") && 
                               !m.Name.Equals("Equals") && !m.Name.Equals("GetType"));

                foreach (var method in methods)
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    _output.WriteLine($"  • {method.Name}({parameters})");
                }
            }
            else
            {
                _output.WriteLine("❌ McpClientTool type not found");
            }
        }
    }
}