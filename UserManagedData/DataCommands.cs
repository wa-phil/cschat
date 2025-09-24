using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Threading.Tasks;
using System.Linq.Expressions;
using System.Collections.Generic;

public static class DataCommands
{
    public static Command Commands()
    {
        return new Command
        {
            Name = "Data",
            Description = () => "Manage user data",
            SubCommands = GetDataTypeCommands()
        };
    }

    private static List<Command> GetDataTypeCommands()
    {
        var commands = new List<Command>();

        try
        {

            foreach (var type in Program.userManagedData.GetRegisteredTypes())
            {
                var metadata = Program.userManagedData.GetTypeMetadata(type);
                var typeCommands = CreateCommandsForType(type, metadata);
                commands.Add(typeCommands);
            }
        }
        catch (Exception ex)
        {
            Log.Method(ctx =>
            {
                ctx.Append(Log.Data.Message, $"Failed to create data commands: {ex.Message}");
                ctx.Failed("Command creation error", Error.InvalidInput);
            });
        }

        return commands;
    }

    private static Command CreateCommandsForType(Type type, UserManagedAttribute metadata)
    {
        return new Command
        {
            Name = metadata.Name,
            Description = () => metadata.Description,
            SubCommands = new List<Command>
            {
                CreateListCommand(type, metadata),
                CreateAddCommand(type, metadata),
                CreateUpdateCommand(type, metadata),
                CreateDeleteCommand(type, metadata)
            }
        };
    }

    private static Command CreateListCommand(Type type, UserManagedAttribute metadata)
    {
        return new Command
        {
            Name = "list",
            Description = () => $"List all {metadata.Name} items",
            Action = () =>
            {
                try
                {
                    var subsystem = Program.userManagedData;
                    var method = typeof(UserManagedData).GetMethod("GetItems")?.MakeGenericMethod(type);
                    var items = method?.Invoke(subsystem, null) as System.Collections.IEnumerable;

                    if (items == null)
                    {
                        Program.ui.WriteLine($"No {metadata.Name} items found.");
                        return Task.FromResult(Command.Result.Success);
                    }

                    var itemList = items.Cast<object>().ToList();
                    if (itemList.Count == 0)
                    {
                        Program.ui.WriteLine($"No {metadata.Name} items found.");
                        return Task.FromResult(Command.Result.Success);
                    }

                    Program.ui.WriteLine($"\n{metadata.Name} Items ({itemList.Count}):");
                    Program.ui.WriteLine(new string('-', 40));

                    for (int i = 0; i < itemList.Count; i++)
                    {
                        Program.ui.WriteLine($"{i + 1}. {itemList[i]}");
                    }

                    return Task.FromResult(Command.Result.Success);
                }
                catch (Exception ex)
                {
                    Program.ui.WriteLine($"Error listing {metadata.Name}: {ex.Message}");
                    return Task.FromResult(Command.Result.Failed);
                }
            }
        };
    }

    private static Command CreateAddCommand(Type type, UserManagedAttribute metadata)
    {
        return new Command
        {
            Name = "add",
            Description = () => $"Add a new {metadata.Name} item",
            Action = async () => await Log.MethodAsync(async ctx =>
            {
                ctx.Append(Log.Data.Name, metadata.Name);
                var instance = Activator.CreateInstance(type)!;
                ctx.Append(Log.Data.Type, type.FullName ?? type.Name);
                ctx.Append(Log.Data.Input, instance.ToJson() ?? "<null>");
                var form = UiFormBuilder.BuildForm(instance, type, $"Add {metadata.Name}");
                if (!await Program.ui.ShowFormAsync(form))
                {
                    ctx.Append(Log.Data.Message, "User cancelled form");
                    ctx.Succeeded();
                    return Command.Result.Cancelled;
                }
                // form.Model holds the cloned+edited object — reassign back:
                instance = (object?)form.Model ?? instance;
                ctx.Append(Log.Data.Output, instance.ToJson() ?? "<null>");

                // persist
                var (dataSubsystem, addMethod) = GetUserManagedAdd(type);
                addMethod.Invoke(dataSubsystem, new[] { instance });
                Config.Save(Program.config, Program.ConfigFilePath);
                ctx.Succeeded();
                return Command.Result.Success;
            })
        };
    }

    private static Command CreateUpdateCommand(Type type, UserManagedAttribute metadata)
    {
        return new Command
        {
            Name = "update",
            Description = () => $"Update an existing {metadata.Name} item",
            Action = async () => await Log.MethodAsync(async ctx =>
            {
                try
                {
                    ctx.Append(Log.Data.Name, metadata.Name);
                    ctx.Append(Log.Data.Type, type.FullName ?? type.Name);
                    var (dataSubsystem, getMethod) = GetUserManagedGet(type);
                    var items = (getMethod.Invoke(dataSubsystem, null) as IEnumerable)?.Cast<object>().ToList() ?? new();
                    if (items.Count == 0)
                    {
                        ctx.Append(Log.Data.Message, "No items found");
                        ctx.Succeeded();
                        return Command.Result.Success;
                    }

                    // pick
                    var selected = Program.ui.RenderMenu($"Select {metadata.Name} to update:", items.Select((o,i)=>$"{i}: {o}").ToList());
                    if (selected == null)
                    {
                        ctx.Append(Log.Data.Message, "User cancelled selection");
                        ctx.Succeeded();
                        return Command.Result.Cancelled;
                    }
                    var idx = int.Parse(selected.Split(':')[0]);
                    var original = items[idx];
                    ctx.Append(Log.Data.Input, original.ToJson() ?? "<null>");
                    var keyProp = FindKeyProperty(type);
                    ctx.Append(Log.Data.Key, keyProp?.Name ?? "<none>");

                    // CAPTURE BEFORE EDITS
                    object? keyValueBefore = keyProp != null ? keyProp.GetValue(original) : null;
                    var originalJsonBefore = keyProp == null ? original.ToJson() : null;

                    // build a form on a clone
                    var editable = CloneViaJson(original, type) ?? Activator.CreateInstance(type)!;
                    var form = UiFormBuilder.BuildForm(editable, type, $"Edit {metadata.Name}");

                    // show the form
                    if (!await Program.ui.ShowFormAsync(form))
                    {
                        ctx.Append(Log.Data.Message, "User cancelled form");
                        ctx.Succeeded();
                        return Command.Result.Cancelled;
                    }

                    // GET THE EDITED MODEL (don’t rely on ApplyEditsToOriginal)
                    ctx.Append(Log.Data.Output, form.Model?.ToJson() ?? "<null>");
                    var edited = (object?)form.Model ?? editable;

                    // (optional) keep this if you want in-place copy too, but it’s not required
                    // form.ApplyEditsToOriginal(original);

                    // build predicate using BEFORE state (unchanged)
                    Delegate predicate;
                    if (keyProp != null)
                    {
                        predicate = BuildEqualityPredicate(type, keyProp, keyValueBefore);
                    }
                    else
                    {
                        predicate = BuildJsonEqualityPredicate(type, originalJsonBefore!);
                    }

                    // persist the EDITED instance
                    var (subsystemForUpdate, updateMethod) = GetUserManagedUpdate(type);
                    updateMethod.Invoke(subsystemForUpdate, new[] { edited, predicate });

                    Config.Save(Program.config, Program.ConfigFilePath);
                    Program.ui.WriteLine($"Updated {metadata.Name}: {edited}");
                    return Command.Result.Success;
                }
                catch (Exception ex)
                {
                    Program.ui.WriteLine($"Error updating {metadata.Name}: {ex.Message}");
                    return Command.Result.Failed;
                }
            })
        };
    }

    /* ---------- parsing & utilities ---------- */

    private static string FormatValue(object? v) =>
        v == null ? "null" :
        v is DateTime dt ? dt.ToString("u") :
        v is IEnumerable e && v is not string
            ? "[" + string.Join(", ", e.Cast<object>().Select(FormatValue)) + "]"
            : v.ToString() ?? "";


    /* ---------- subsystem access via reflection (supports both classes present) ---------- */

    private static (object subsystem, MethodInfo getMethod) GetUserManagedGet(Type t)
    {
        var subsystem = Program.userManagedData as object;
        var mi = subsystem.GetType().GetMethod("GetItems")!.MakeGenericMethod(t);
        return (subsystem, mi);
    }

    private static (object subsystem, MethodInfo addMethod) GetUserManagedAdd(Type t)
    {
        var subsystem = Program.userManagedData as object;
        var mi = subsystem.GetType().GetMethod("AddItem")!.MakeGenericMethod(t);
        return (subsystem, mi);
    }

    private static (object subsystem, MethodInfo updateMethod) GetUserManagedUpdate(Type t)
    {
        var subsystem = Program.userManagedData as object;
        var mi = subsystem.GetType().GetMethod("UpdateItem")!.MakeGenericMethod(t);
        return (subsystem, mi);
    }

    /* ---------- key selection & predicates ---------- */

    private static PropertyInfo? FindKeyProperty(Type t)
    {
        var key = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.GetCustomAttribute<UserKeyAttribute>() != null);
        if (key != null) return key;

        return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase));
    }

    private static Delegate BuildEqualityPredicate(Type t, PropertyInfo keyProp, object? keyValue)
    {
        var param = Expression.Parameter(t, "x");
        var left = Expression.Property(param, keyProp);
        var right = Expression.Constant(keyValue, keyProp.PropertyType);
        var body = Expression.Equal(left, right);
        var funcType = typeof(Func<,>).MakeGenericType(t, typeof(bool));
        return Expression.Lambda(funcType, body, param).Compile();
    }

    private static Delegate BuildJsonEqualityPredicate(Type t, string json)
    {
        var param = Expression.Parameter(t, "x");
        var toJsonMi = typeof(DataCommands).GetMethod(nameof(ToJson), BindingFlags.Static | BindingFlags.NonPublic)!;
        var call = Expression.Call(toJsonMi, param);
        var eq = Expression.Equal(call, Expression.Constant(json));
        var funcType = typeof(Func<,>).MakeGenericType(t, typeof(bool));
        return Expression.Lambda(funcType, eq, param).Compile();
    }

    private static string ToJson(object obj) => obj.ToJson();

    private static object? CloneViaJson(object obj, Type type)
    {
        try
        {
            var json = obj.ToJson();
            var fromJsonMi = typeof(string).GetMethod("FromJson", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
            // If extension is in scope, call via dynamic dispatch:
            return json.FromJson(type);
        }
        catch
        {
            return null;
        }
    }

    private static Command CreateDeleteCommand(Type type, UserManagedAttribute metadata)
    {
        return new Command
        {
            Name = "delete",
            Description = () => $"Delete a {metadata.Name} item",
            Action = () =>
            {
                try
                {
                    var subsystem = Program.userManagedData;
                    var method = typeof(UserManagedData).GetMethod("GetItems")?.MakeGenericMethod(type);
                    var items = method?.Invoke(subsystem, null) as System.Collections.IEnumerable;

                    if (items == null)
                    {
                        Program.ui.WriteLine($"No {metadata.Name} items found to delete.");
                        return Task.FromResult(Command.Result.Success);
                    }

                    var itemList = items.Cast<object>().ToList();
                    if (itemList.Count == 0)
                    {
                        Program.ui.WriteLine($"No {metadata.Name} items found to delete.");
                        return Task.FromResult(Command.Result.Success);
                    }

                    // Use the menu system to select an item to delete
                    var choices = itemList.Select((item, index) => $"{index}: {item}").ToList();
                    var selected = Program.ui.RenderMenu(
                        $"Select {metadata.Name} to delete:",
                        choices
                    );

                    if (selected == null)
                    {
                        Program.ui.WriteLine("Delete cancelled.");
                        return Task.FromResult(Command.Result.Cancelled);
                    }

                    var selectedIndex = int.Parse(selected.Split(':')[0]);

                    // Confirm deletion
                    Program.ui.Write($"Are you sure you want to delete '{itemList[selectedIndex]}'? (y/N): ");
                    var confirm = Program.ui.ReadLine();
                    if (!string.Equals(confirm, "y", StringComparison.OrdinalIgnoreCase))
                    {
                        Program.ui.WriteLine("Delete cancelled.");
                        return Task.FromResult(Command.Result.Cancelled);
                    }

                    // Delete the item - build a predicate and invoke DeleteItem<T> via reflection
                    var original = itemList[selectedIndex];

                    // Build predicate: prefer [UserKey] or Id property; otherwise fallback to JSON equality
                    Delegate predicate;
                    var keyProp = FindKeyProperty(type);
                    if (keyProp != null)
                    {
                        var keyValue = keyProp.GetValue(original);
                        predicate = BuildEqualityPredicate(type, keyProp, keyValue);
                    }
                    else
                    {
                        var originalJson = ToJson(original);
                        predicate = BuildJsonEqualityPredicate(type, originalJson);
                    }

                    var deleteMi = subsystem.GetType().GetMethod("DeleteItem")!.MakeGenericMethod(type);
                    deleteMi.Invoke(subsystem, new[] { predicate });

                    Config.Save(Program.config, Program.ConfigFilePath);
                    Program.ui.WriteLine($"Deleted: {original}");

                    return Task.FromResult(Command.Result.Success);
                }
                catch (Exception ex)
                {
                    Program.ui.WriteLine($"Error deleting {metadata.Name}: {ex.Message}");
                    return Task.FromResult(Command.Result.Failed);
                }
            }
        };
    }
}