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
            Action = async () =>
            {
                try
                {
                    var (dataSubsystem, getMethod) = GetUserManagedGet(type);
                    var itemsEnumerable = getMethod.Invoke(dataSubsystem, null) as IEnumerable;
                    var items = itemsEnumerable?.Cast<object>().ToList() ?? new List<object>();
                    if (items.Count == 0)
                    {
                        Program.ui.WriteLine($"No {metadata.Name} items found.");
                        return Command.Result.Success;
                    }

                    // Let user choose the item to edit
                    var choices = items.Select((o, i) => $"{i}: {o}").ToList();
                    var selected = Program.ui.RenderMenu($"Select {metadata.Name} to update:", choices);
                    if (selected == null)
                    {
                        Program.ui.WriteLine("Update cancelled.");
                        return Command.Result.Cancelled;
                    }
                    var selectedIndex = int.Parse(selected.Split(':')[0]);
                    var original = items[selectedIndex];

                    // Clone original (json roundtrip) so we can edit without mutating predicate data
                    // If ToJson/FromJson exist as extensions (they do elsewhere), use them when available.
                    var editable = CloneViaJson(original, type) ?? Activator.CreateInstance(type)!;

                    if (!await FillObjectInteractively(editable, type, isUpdate: true))
                    {
                        Program.ui.WriteLine("Update cancelled.");
                        return Command.Result.Cancelled;
                    }

                    // Build a predicate: prefer [UserKey], else property named "Id"
                    var keyProp = FindKeyProperty(type);
                    Delegate predicate;
                    if (keyProp != null)
                    {
                        var keyValue = keyProp.GetValue(original);
                        predicate = BuildEqualityPredicate(type, keyProp, keyValue);
                    }
                    else
                    {
                        // Fallback: match by JSON snapshot (less ideal but works generically)
                        var originalJson = original.ToJson();
                        predicate = BuildJsonEqualityPredicate(type, originalJson);
                    }

                    var (subsystemForUpdate, updateMethod) = GetUserManagedUpdate(type);
                    updateMethod.Invoke(subsystemForUpdate, new[] { editable, predicate });

                    Config.Save(Program.config, Program.ConfigFilePath);
                    Program.ui.WriteLine($"Updated {metadata.Name}: {editable}");
                    return Command.Result.Success;
                }
                catch (Exception ex)
                {
                    Program.ui.WriteLine($"Error updating {metadata.Name}: {ex.Message}");
                    return Command.Result.Failed;
                }
            }
        };
    }

    /* --------------------------- helpers --------------------------- */

    private static async Task<bool> FillObjectInteractively(object obj, Type type, bool isUpdate)
    {
        foreach (var prop in GetEditableProperties(type))
        {
            var uf = prop.GetCustomAttribute<UserFieldAttribute>();
            var required = uf?.Required ?? false;
            var display = uf?.Display ?? prop.Name;
            var hint = uf?.Hint;

            var propType = prop.PropertyType;
            var current = prop.GetValue(obj);

            if (IsSimple(propType))
            {
                var ok = PromptForSimple(ref current, propType, display, required, isUpdate, hint);
                if (!ok) return false;
                prop.SetValue(obj, current);
            }
            else if (IsList(propType, out var elemType))
            {
                var listInstance = current as IList;
                if (listInstance == null)
                {
                    listInstance = (IList)(Activator.CreateInstance(typeof(List<>).MakeGenericType(elemType))!);
                }
                if (!await EditListInteractively(listInstance, elemType, display, isUpdate))
                    return false;
                prop.SetValue(obj, listInstance);
            }
            else // complex object
            {
                Program.ui.WriteLine($"\n-- {display} {(required ? "(required)" : "(optional)")} --");
                var child = current ?? Activator.CreateInstance(propType)!;

                // allow skipping optional complex properties
                if (!required)
                {
                    Program.ui.Write($"Edit {display}? (y/N): ");
                    var ans = (Program.ui.ReadLine() ?? "").Trim();
                    if (!ans.Equals("y", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (!await FillObjectInteractively(child, propType, isUpdate))
                    return false;

                prop.SetValue(obj, child);
            }
        }
        return true;
    }

    private static IEnumerable<PropertyInfo> GetEditableProperties(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

    private static bool PromptForSimple(ref object? value, Type type, string display, bool required, bool isUpdate, string? hint)
    {
        while (true)
        {
            var currentText = isUpdate && value != null ? $" [{FormatValue(value)}]" : "";
            var requiredText = required ? " (required)" : " (optional)";
            if (!string.IsNullOrWhiteSpace(hint)) Program.ui.WriteLine(hint);
            Program.ui.Write($"{display}{requiredText}:{currentText} ");

            var input = ReadLineAllowEsc();
            if (input == null)
            {
                // ESC pressed -> cancel the whole interactive fill
                return false;
            }
            if (string.IsNullOrWhiteSpace(input))
            {
                if (isUpdate && value != null) return true;         // keep existing
                if (!required) { value = GetDefault(type); return true; }
                // else required and empty -> re-prompt
                Program.ui.WriteLine("Value is required.");
                continue;
            }

            if (TryParse(type, input!, out var parsed))
            {
                value = parsed;
                return true;
            }
            Program.ui.WriteLine($"Could not parse '{input}' as {PrettyType(type)}. Try again.");
        }
    }

    private static string? ReadLineAllowEsc()
    {
        // Read first key to detect ESC without consuming input from Program.ui.ReadLine
        var key = Program.ui.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Escape)
        {
            Program.ui.WriteLine();
            return null;
        }
        if (key.Key == ConsoleKey.Enter)
        {
            Program.ui.WriteLine();
            return string.Empty;
        }

        // Echo the first key and read the remainder of the line
        Program.ui.Write(key.KeyChar.ToString());
        var rest = Program.ui.ReadLine();
        return key.KeyChar + (rest ?? "");
    }

    private static async Task<bool> EditListInteractively(IList list, Type elemType, string display, bool isUpdate)
    {
        while (true)
        {
            var choices = new List<string>
            {
                "list    (show items)",
                "add     (insert new item)",
                "edit    (modify an item)",
                "remove  (delete an item)",
                "done    (finish editing)"
            };

            var choice = Program.ui.RenderMenu($"Choose action: {display}", choices);
            if (choice == null || choice.StartsWith("done", StringComparison.OrdinalIgnoreCase))
                return true;

            if (choice.StartsWith("list", StringComparison.OrdinalIgnoreCase))
            {
                if (list.Count == 0)
                {
                    Program.ui.WriteLine("List is empty.");
                    continue;
                }

                var itemChoices = new List<string>();
                for (int i = 0; i < list.Count; i++)
                    itemChoices.Add(FormatValue(list[i]));

                // Use RenderMenu so the items are visible while selecting; ESC (null) goes back
                var seen = Program.ui.RenderMenu($"Items in {display} (press ESC to go back)", itemChoices);
                // ignore selection results; null returns to main menu
                continue;
            }

            if (choice.StartsWith("add", StringComparison.OrdinalIgnoreCase))
            {
                if (IsSimple(elemType))
                {
                    // Prompt manually so ESC or blank entry cancels the add (but does not abort the whole edit session)
                    var hint = GetTypeInputHint(elemType);
                    Program.ui.Write($"Enter value ({hint}) or press ESC to cancel: ");
                    var input = ReadLineAllowEsc();
                    if (input == null || string.IsNullOrWhiteSpace(input))
                    {
                        Program.ui.WriteLine("Add cancelled.");
                        continue; // do not add, keep editing
                    }

                    if (TryParse(elemType, input!, out var parsed))
                    {
                        list.Add(parsed!);
                    }
                    else
                    {
                        Program.ui.WriteLine($"Could not parse '{input}' as {PrettyType(elemType)}. Add cancelled.");
                        continue;
                    }
                }
                else
                {
                    var child = Activator.CreateInstance(elemType)!;
                    if (!await FillObjectInteractively(child, elemType, isUpdate: false))
                    {
                        Program.ui.WriteLine("Add cancelled.");
                        continue; // user cancelled adding the complex item
                    }
                    list.Add(child);
                }
            }
            else if (choice.StartsWith("edit", StringComparison.OrdinalIgnoreCase))
            {
                if (list.Count == 0) { Program.ui.WriteLine("List is empty."); continue; }

                var itemChoices = new List<string>();
                for (int i = 0; i < list.Count; i++)
                    itemChoices.Add(FormatValue(list[i]));

                var selected = Program.ui.RenderMenu($"Select {display} item to edit (press ESC to cancel):", itemChoices);
                if (selected == null)
                {
                    Program.ui.WriteLine("Edit cancelled.");
                    continue;
                }

                var selectedIndex = itemChoices.IndexOf(selected);
                if (selectedIndex < 0) { Program.ui.WriteLine("Invalid selection."); continue; }

                if (IsSimple(elemType))
                {
                    object? cur = list[selectedIndex];
                    var hint = GetTypeInputHint(elemType);
                    Program.ui.Write($"Enter new value for index {selectedIndex} ({hint}) or press ESC to cancel: ");
                    var input = ReadLineAllowEsc();
                    if (input == null || string.IsNullOrWhiteSpace(input))
                    {
                        Program.ui.WriteLine("Edit cancelled.");
                        continue;
                    }
                    if (TryParse(elemType, input!, out var parsed))
                    {
                        list[selectedIndex] = parsed!;
                    }
                    else
                    {
                        Program.ui.WriteLine($"Could not parse '{input}' as {PrettyType(elemType)}. Edit cancelled.");
                        continue;
                    }
                }
                else
                {
                    var child = list[selectedIndex] ?? Activator.CreateInstance(elemType)!;
                    if (!await FillObjectInteractively(child!, elemType, isUpdate: true))
                    {
                        Program.ui.WriteLine("Edit cancelled.");
                        continue;
                    }
                    list[selectedIndex] = child!;
                }
            }
            else if (choice.StartsWith("remove", StringComparison.OrdinalIgnoreCase))
            {
                if (list.Count == 0) { Program.ui.WriteLine("List is empty."); continue; }

                var itemChoices = new List<string>();
                for (int i = 0; i < list.Count; i++)
                    itemChoices.Add(FormatValue(list[i]));

                var selected = Program.ui.RenderMenu($"Select {display} item to remove (press ESC to cancel):", itemChoices);
                if (selected == null)
                {
                    Program.ui.WriteLine("Remove cancelled.");
                    continue;
                }

                var removeIndex = itemChoices.IndexOf(selected);
                if (removeIndex < 0) { Program.ui.WriteLine("Invalid selection."); continue; }

                Program.ui.Write($"Are you sure you want to remove '{list[removeIndex]}'? (y/N): ");
                var confirm = ReadLineAllowEsc();
                if (confirm == null || !string.Equals(confirm, "y", StringComparison.OrdinalIgnoreCase))
                {
                    Program.ui.WriteLine("Remove cancelled.");
                    continue;
                }

                list.RemoveAt(removeIndex);
            }
        }
    }

    /* ---------- parsing & utilities ---------- */

    private static bool IsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum ||
            t == typeof(string) || t == typeof(decimal) ||
            t == typeof(DateTime) || t == typeof(Guid) ||
            t == typeof(double) || t == typeof(float) ||
            t == typeof(int) || t == typeof(long) ||
            t == typeof(short) || t == typeof(byte) ||
            t == typeof(bool);
    }

    private static bool IsList(Type t, out Type elemType)
    {
        if (t.IsArray)
        {
            elemType = t.GetElementType()!;
            return true;
        }
        if (t.IsGenericType && typeof(IList).IsAssignableFrom(t))
        {
            elemType = t.GetGenericArguments()[0];
            return true;
        }
        if (t.IsGenericType && t.GetInterfaces().Any(i => i == typeof(IList))) // safety
        {
            elemType = t.GetGenericArguments()[0];
            return true;
        }
        // handle List<T> properties typed as IList<T>/ICollection<T>/IEnumerable<T>
        var listIface = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
        if (listIface != null)
        {
            elemType = listIface.GetGenericArguments()[0];
            return true;
        }
        elemType = typeof(object);
        return false;
    }

    private static object? GetDefault(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    private static string PrettyType(Type t) => (Nullable.GetUnderlyingType(t) ?? t).Name;

    private static string FormatValue(object? v) =>
        v == null ? "null" :
        v is DateTime dt ? dt.ToString("u") :
        v is IEnumerable e && v is not string
            ? "[" + string.Join(", ", e.Cast<object>().Select(FormatValue)) + "]"
            : v.ToString() ?? "";

    private static bool TryParse(Type type, string input, out object? value)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (t == typeof(string)) { value = input; return true; }
        if (t == typeof(int)) { if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { value = i; return true; } }
        if (t == typeof(long)) { if (long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) { value = l; return true; } }
        if (t == typeof(short)) { if (short.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) { value = s; return true; } }
        if (t == typeof(byte)) { if (byte.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)) { value = b; return true; } }
        if (t == typeof(decimal)) { if (decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) { value = d; return true; } }
        if (t == typeof(double)) { if (double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var f)) { value = f; return true; } }
        if (t == typeof(float)) { if (float.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var fl)) { value = fl; return true; } }
        if (t == typeof(bool)) { if (bool.TryParse(input, out var bo)) { value = bo; return true; } }
        if (t == typeof(Guid)) { if (Guid.TryParse(input, out var g)) { value = g; return true; } }
        if (t == typeof(DateTime)) { if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)) { value = dt; return true; } }
        if (t.IsEnum)
        {
            // allow numeric or named values (case-insensitive)
            if (Enum.TryParse(t, input, ignoreCase: true, out var ev)) { value = ev; return true; }
        }

        value = null;
        return false;
    }

    private static string GetTypeInputHint(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return "text";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return "integer";
        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) return "number";
        if (t == typeof(bool)) return "true/false";
        if (t == typeof(Guid)) return "guid (e.g. 01234567-89ab-cdef-0123-456789abcdef)";
        if (t == typeof(DateTime)) return "datetime (ISO, e.g. 2023-05-01T12:00:00Z)";
        if (t.IsEnum) return "one of: " + string.Join("|", Enum.GetNames(t));
        return PrettyType(t);
    }

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
