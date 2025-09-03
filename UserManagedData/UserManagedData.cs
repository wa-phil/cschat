using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;

public class UserManagedData : IDisposable
{
    private readonly Dictionary<Type, UserManagedAttribute> _registeredTypes = new();
    private bool _connected = false;

    // Simple pub/sub for consumers to get notified when items change.
    public enum ChangeType { Added, Updated, Deleted }
    public delegate void ItemChangedHandler(Type itemType, ChangeType change, object? item);
    // Map of item type -> list of subscribers. Use typeof(object) for global subscribers.
    private readonly Dictionary<Type, List<ItemChangedHandler>> _subscribers = new();

    public void Dispose()
    {
        if (_connected)
        {
            Disconnect();
            _connected = false;
        }
        GC.SuppressFinalize(this);
    }

    public void Connect() => Log.Method(ctx =>
    {
        // Ensure types are discovered before registering commands
        DiscoverTypes();
        Program.commandManager.SubCommands.Add(DataCommands.Commands());
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Message, "Discovering UserManagedData types...");
        DiscoverTypes(ctx);
        ctx.Succeeded();
    });

    private void Disconnect()
    {
        Program.commandManager.SubCommands.RemoveAll(cmd => cmd.Name.Equals("Data", StringComparison.OrdinalIgnoreCase));
    }

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
            // Notify subscribers registered for this type and global subscribers
            var handlers = new List<ItemChangedHandler>();
            if (_subscribers.TryGetValue(typeof(T), out var listForType)) handlers.AddRange(listForType);
            foreach (var sub in handlers)
            {
                try { sub(typeof(T), ChangeType.Added, item); } catch { /* ignore subscriber errors */ }
            }
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
        // Notify subscribers that an item was updated (best-effort: pass the provided item)
        var updateHandlers = new List<ItemChangedHandler>();
        if (_subscribers.TryGetValue(typeof(T), out var listU)) updateHandlers.AddRange(listU);
        foreach (var sub in updateHandlers)
        {
            try { sub(typeof(T), ChangeType.Updated, item); } catch { /* ignore */ }
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

        // Remove matching items, capture the deleted objects so subscribers receive the exact item
        var deletedObjects = new List<T>();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            try
            {
                var json = items[i].ToJson();
                var obj = json.FromJson<T>();
                if (obj != null && predicate(obj))
                {
                    deletedObjects.Add(obj);
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

        // Notify subscribers for deletes, passing the exact deleted object
        var deleteHandlers = new List<ItemChangedHandler>();
        if (_subscribers.TryGetValue(typeof(T), out var listD)) deleteHandlers.AddRange(listD);
        foreach (var deleted in deletedObjects)
        {
            foreach (var sub in deleteHandlers)
            {
                try
                {
                    // Add a small trace so subscribers can see what's being passed
                    try { Log.Method(ctx => { ctx.Append(Log.Data.Message, $"UMD notifying delete for {typeName}"); ctx.Append(Log.Data.Name, deleted?.ToString() ?? "<null>"); ctx.Succeeded(); }); } catch { }
                    sub(typeof(T), ChangeType.Deleted, deleted);
                }
                catch { /* ignore */ }
            }
        }
    }

    // Subscribe to change notifications. Returns an IDisposable that can be disposed to unsubscribe.
    // - Subscribe(typeof(T), handler) registers for a specific type.
    // - Subscribe<T>(handler) is a generic convenience wrapper.
    public IDisposable Subscribe<T>(ItemChangedHandler handler) => Subscribe(typeof(T), handler);

    public IDisposable Subscribe(Type itemType, ItemChangedHandler handler)
    {
        if (itemType == null) throw new ArgumentNullException(nameof(itemType));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (!_subscribers.TryGetValue(itemType, out var list))
        {
            list = new List<ItemChangedHandler>();
            _subscribers[itemType] = list;
        }
        list.Add(handler);
        return new Unsubscriber(_subscribers, itemType, handler);
    }

    private class Unsubscriber : IDisposable
    {
        private readonly Dictionary<Type, List<ItemChangedHandler>> _subs;
        private readonly Type _type;
        private readonly ItemChangedHandler _handler;
        public Unsubscriber(Dictionary<Type, List<ItemChangedHandler>> subs, Type type, ItemChangedHandler handler)
        {
            _subs = subs;
            _type = type;
            _handler = handler;
        }
        public void Dispose()
        {
            try
            {
                if (_subs.TryGetValue(_type, out var list))
                {
                    list.Remove(_handler);
                    if (list.Count == 0) _subs.Remove(_type);
                }
            }
            catch { }
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
