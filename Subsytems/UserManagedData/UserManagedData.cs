using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;

[IsConfigurable("UserManagedData")]
public class UserManagedData : ISubsystem
{
    private readonly Dictionary<Type, UserManagedAttribute> _registeredTypes = new();
    private bool _connected = false;

    public Type ConfigType => typeof(UserManagedDataConfig);
    public bool IsAvailable { get; } = true;
    public bool IsEnabled
    {
        get => _connected;
        set
        {
            if (value && !_connected)
            {
                _connected = true;
                Connect();
                Register();
            }
            else if (!value && _connected)
            {
                Unregister();
                _connected = false;
            }
        }
    }

    public void Register()
    {
        // Ensure types are discovered before registering commands
        DiscoverTypes();
        Program.commandManager.SubCommands.Add(DataCommands.Commands());
    }

    public void Unregister()
    {
        Program.commandManager.SubCommands.RemoveAll(cmd => cmd.Name.Equals("Data", StringComparison.OrdinalIgnoreCase));
    }

    private void Connect() => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Message, "Discovering UserManagedData types...");
        DiscoverTypes(ctx);
        ctx.Succeeded();
    });

    private void DiscoverTypes(Log.Context? ctx = null)
    {
        // Discover all types with UserManagedAttribute attribute
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetCustomAttribute<UserManagedAttribute>() != null)
            .ToList();

        foreach (var type in types)
        {
            var attr = type.GetCustomAttribute<UserManagedAttribute>()!;
            
            // Validate type has parameterless constructor
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                ctx?.Append(Log.Data.Message, $"Skipping type {type.Name}: no parameterless constructor");
                continue;
            }

            _registeredTypes[type] = attr;
            ctx?.Append(Log.Data.Message, $"Registered type: {type.Name} as '{attr.Name}'");
        }

        ctx?.Append(Log.Data.Count, _registeredTypes.Count);
    }

    public List<T> GetItems<T>() where T : new()
    {
        var type = typeof(T);
        if (!_registeredTypes.ContainsKey(type))
        {
            throw new InvalidOperationException($"Type {type.Name} is not registered with UserManagedData subsystem");
        }

        var typeName = type.Name;
        var config = Program.config.UserManagedData;
        
        if (!config.TypedData.TryGetValue(typeName, out var items))
        {
            return new List<T>();
        }

        var result = new List<T>();
        foreach (var item in items)
        {
            try
            {
                var json = item.ToJson();
                var obj = json.FromJson<T>();
                if (obj != null)
                {
                    result.Add(obj);
                }
            }
            catch (Exception ex)
            {
                Log.Method(ctx =>
                {
                    ctx.Append(Log.Data.Message, $"Failed to deserialize {typeName}: {ex.Message}");
                    ctx.Failed("Deserialization error", Error.InvalidInput);
                });
            }
        }

        return result;
    }

    public void AddItem<T>(T item) where T : new()
    {
        var type = typeof(T);
        if (!_registeredTypes.ContainsKey(type))
        {
            throw new InvalidOperationException($"Type {type.Name} is not registered with UserManagedData subsystem");
        }

        var typeName = type.Name;
        var config = Program.config.UserManagedData;
        
        if (!config.TypedData.ContainsKey(typeName))
        {
            config.TypedData[typeName] = new List<Dictionary<string, object>>();
        }

        // Convert to dictionary for storage
        var json = item!.ToJson();
        var dict = json.FromJson<Dictionary<string, object>>();
        if (dict != null)
        {
            config.TypedData[typeName].Add(dict);
        }
    }

    public void UpdateItem<T>(T item, Func<T, bool> predicate) where T : new()
    {
        var type = typeof(T);
        if (!_registeredTypes.ContainsKey(type))
        {
            throw new InvalidOperationException($"Type {type.Name} is not registered with UserManagedData subsystem");
        }

        var typeName = type.Name;
        var config = Program.config.UserManagedData;
        
        if (!config.TypedData.TryGetValue(typeName, out var items))
        {
            return;
        }

        // Find and replace the item
        for (int i = 0; i < items.Count; i++)
        {
            try
            {
                var json = items[i].ToJson();
                var obj = json.FromJson<T>();
                if (obj != null && predicate(obj))
                {
                    // Replace with updated item
                    var updatedJson = item!.ToJson();
                    var updatedDict = updatedJson.FromJson<Dictionary<string, object>>();
                    if (updatedDict != null)
                    {
                        items[i] = updatedDict;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Method(ctx =>
                {
                    ctx.Append(Log.Data.Message, $"Failed to update {typeName}: {ex.Message}");
                    ctx.Failed("Update error", Error.InvalidInput);
                });
            }
        }
    }

    public void DeleteItem<T>(Func<T, bool> predicate) where T : new()
    {
        var type = typeof(T);
        if (!_registeredTypes.ContainsKey(type))
        {
            throw new InvalidOperationException($"Type {type.Name} is not registered with UserManagedData subsystem");
        }

        var typeName = type.Name;
        var config = Program.config.UserManagedData;
        
        if (!config.TypedData.TryGetValue(typeName, out var items))
        {
            return;
        }

        // Remove matching items
        for (int i = items.Count - 1; i >= 0; i--)
        {
            try
            {
                var json = items[i].ToJson();
                var obj = json.FromJson<T>();
                if (obj != null && predicate(obj))
                {
                    items.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                Log.Method(ctx =>
                {
                    ctx.Append(Log.Data.Message, $"Failed to delete {typeName}: {ex.Message}");
                    ctx.Failed("Delete error", Error.InvalidInput);
                });
            }
        }
    }

    public IEnumerable<Type> GetRegisteredTypes() => _registeredTypes.Keys;
    
    public UserManagedAttribute GetTypeMetadata(Type type) 
    {
        if (!_registeredTypes.TryGetValue(type, out var attr))
        {
            throw new InvalidOperationException($"Type {type.Name} is not registered with UserManagedData subsystem");
        }
        return attr;
    }
}
