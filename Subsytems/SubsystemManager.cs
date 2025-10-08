using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Common;

public class SubsystemManager
{
    private readonly Dictionary<string, Type> _subsystems = new();

    public T Get<T>() where T : ISubsystem
    {
        if (null == Program.serviceProvider) throw new InvalidOperationException("Service provider is not initialized.");
        var subsystem = (T?)Program.serviceProvider.GetService(typeof(T));
        if (subsystem == null)
        {
            throw new InvalidOperationException($"Subsystem of type {typeof(T).Name} is not registered.");
        }
        return subsystem;
    }

    public IEnumerable<string> Names => _subsystems.Keys;
    public IEnumerable<Type> Types => _subsystems.Values;
    public bool IsEnabled(string name) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Subsystem name cannot be null or empty.", nameof(name));
        if (!_subsystems.ContainsKey(name))
        {
            ctx.Failed($"Subsystem '{name}' not found.", Error.InvalidInput);
            return false;
        }

        ctx.Succeeded(TryGetSubsystem(name, out var subsystem));
        ctx.Append(Log.Data.Output, null != subsystem);
        ctx.Append(Log.Data.Message, $"TryGetSubsystem '{name}' returned {(null == subsystem ? "<NULL>" : "a valid object")}.");
        return subsystem?.IsEnabled ?? false;
    });

    public Type GetType(string name)
    {
        if (string.IsNullOrEmpty(name) || !_subsystems.ContainsKey(name))
        {
            throw new ArgumentException($"Subsystem '{name}' not found.");
        }
        return _subsystems[name];
    }

    public bool TryGetSubsystem(string name, out ISubsystem? subsystem)
    {
        if (null == Program.serviceProvider) throw new InvalidOperationException("Service provider is not initialized.");
        subsystem = (ISubsystem?)Program.serviceProvider.GetService(_subsystems[name]);
        return subsystem != null;
    }
    
    public void SetEnabled(string name, bool enabled) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Subsystem name cannot be null or empty.", nameof(name));
        // Allow callers to pass either the configured logical name (from IsConfigurable) or the concrete
        // type name (older config files may use type names). Map type names to the registered logical name.
        var key = name;
        if (!_subsystems.ContainsKey(key))
        {
            // Try to find a registered subsystem whose Type.Name matches the provided name
            var pair = _subsystems.FirstOrDefault(kvp => string.Equals(kvp.Value.Name, name, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(pair.Key))
            {
                ctx.Failed($"Subsystem '{name}' not found.", Error.InvalidInput);
                return;
            }
            key = pair.Key;
        }

        if (!TryGetSubsystem(key, out var subsystem))
        {
            ctx.Failed($"Failed to set enabled state for subsystem '{key}'.", Error.InvalidInput);
            return;
        }

        // If enabling, ensure dependencies are enabled first
        if (enabled)
        {
            var deps = GetDependenciesForSubsystem(subsystem!);
            foreach (var dep in deps)
            {
                if (!IsEnabled(dep))
                {
                    ctx.Append(Log.Data.Message, $"Enabling dependency '{dep}' for subsystem '{key}'.");
                    SetEnabled(dep, true);
                }
            }
        }

        subsystem!.IsEnabled = enabled;

        // If disabling, cascade to any subsystems that depend on this one
        if (!enabled)
        {
            var dependents = GetDependentsForSubsystem(key).ToList();
            if (dependents.Count > 0)
            {
                using var output = Program.ui.BeginRealtime("Disabling dependencies");   
                foreach (var d in dependents)
                {
                    if (IsEnabled(d))
                    {
                        output.WriteLine($"Disabling dependent subsystem '{d}' because '{key}' was disabled.");
                        SetEnabled(d, false);
                    }
                }
            }
        }

        // Persist using the logical configurable name when available
        var cfgName = subsystem.GetType().GetCustomAttribute<IsConfigurable>()?.Name ?? key;
        Program.config.Subsystems[cfgName] = enabled; // Update the config
        ctx.Append(Log.Data.Message, $"Subsystem '{cfgName}' is now {(enabled ? "enabled" : "disabled")}.");
        ctx.Succeeded();
        return;
    });

    private IEnumerable<string> GetDependenciesForSubsystem(ISubsystem subsystem)
    {
        var attrs = subsystem.GetType().GetCustomAttributes<DependsOnAttribute>();
        foreach (var a in attrs)
            yield return a.Name;
    }

    private IEnumerable<string> GetDependentsForSubsystem(string subsystemName)
    {
        // A dependent is any registered subsystem whose DependsOnAttribute lists subsystemName
        foreach (var kv in _subsystems)
        {
            var t = kv.Value;
            var deps = t.GetCustomAttributes<DependsOnAttribute>();
            foreach (var d in deps)
            {
                if (string.Equals(d.Name, subsystemName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return kv.Key;
                    break;
                }
            }
        }
    }

    public void Register(Dictionary<string, Type> subsystemTypes) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        foreach (var kvp in subsystemTypes)
        {
            ctx.Append(Log.Data.Message, $"Registering subsystem '{kvp.Key}' of type '{kvp.Value.Name}'.");
            _subsystems.Add(kvp.Key, kvp.Value);
        }
        ctx.Append(Log.Data.Message, "All subsystems registered and enabled/disabled as appropriate successfully.");
        ctx.Succeeded();
    });

    public void Connect() => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        // Iterate a snapshot of the configured subsystems to avoid
        // modifying the collection (SetEnabled updates Program.config.Subsystems)
        var subsystems = Program.config.Subsystems.ToList();
        using var output = Program.ui.BeginRealtime("Connecting to subsystems...");
        foreach (var kv in subsystems)
        {
            ctx.Append(Log.Data.Message, $"Setting subsystem '{kv.Key}' enabled to {kv.Value}.");
            if (kv.Value)
            {
                output.Write($"Connecting to subsystem '{kv.Key}'...");
            }

            Program.SubsystemManager.SetEnabled(kv.Key, kv.Value);

            if (kv.Value)
            {
                output.WriteLine("connected.");
            }
        }
        Config.Save(Program.config, Program.ConfigFilePath); // Save the config
        ctx.Succeeded();
    });

    public Func<ISubsystem, bool> Enabled => s => null != s ? s.IsEnabled : false;
    public Func<ISubsystem, bool> ShouldBeEnabled => s =>
    {
        if (null == s) return false;
        var cfgName = s.GetType().GetCustomAttribute<IsConfigurable>()?.Name ?? s.GetType().Name;
        return Program.config.Subsystems.TryGetValue(cfgName, out var enabled) && enabled;
    };

    public IEnumerable<(string, ISubsystem)> All(Func<ISubsystem, bool>? filter = null)
    {
        filter = filter ?? (s => null != s);
        foreach (var kvp in _subsystems)
        {
            if (TryGetSubsystem(kvp.Key, out var subsystem) && filter(subsystem!))
            {
                yield return (kvp.Key, subsystem!);
            }
        }
    }
}
