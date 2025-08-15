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
        if (!_subsystems.ContainsKey(name))
        {
            ctx.Failed($"Subsystem '{name}' not found.", Error.InvalidInput);
            return;
        }

        if (!TryGetSubsystem(name, out var subsystem))
        {
            ctx.Failed($"Failed to set enabled state for subsystem '{name}'.", Error.InvalidInput);
            return;
        }

        subsystem!.IsEnabled = enabled;
        Program.config.Subsystems[name] = enabled; // Update the config
        ctx.Append(Log.Data.Message, $"Subsystem '{name}' is now {(enabled ? "enabled" : "disabled")}.");
        ctx.Succeeded();
        return;
    });

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
        foreach (var kv in Program.config.Subsystems)
        {
            ctx.Append(Log.Data.Message, $"Setting subsystem '{kv.Key}' enabled to {kv.Value}.");
            if (kv.Value)
            {
                Console.Write($"Connecting to subsystem '{kv.Key}'...");
            }

            Program.SubsystemManager.SetEnabled(kv.Key, kv.Value);

            if (kv.Value)
            {
                Console.WriteLine("connected.");
            }
        }
        Config.Save(Program.config, Program.ConfigFilePath); // Save the config
        ctx.Succeeded();
    });

    public Func<ISubsystem, bool> Enabled => s => null != s ? s.IsEnabled : false;
    public Func<ISubsystem, bool> ShouldBeEnabled => s => null != s && Program.config.Subsystems.TryGetValue(s.GetType().Name, out var enabled) && enabled;

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
