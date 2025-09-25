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
                var form = DataFormBuilder.BuildForm(instance, type, $"Add {metadata.Name}");
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
                    var form = DataFormBuilder.BuildForm(editable, type, $"Edit {metadata.Name}");

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

public static class DataFormBuilder
{
    public static IEnumerable<PropertyInfo> GetEditableProperties(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
         .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

    public static bool IsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum ||
            t == typeof(string) || t == typeof(decimal) ||
            t == typeof(DateTime) || t == typeof(Guid) ||
            t == typeof(double) || t == typeof(float) ||
            t == typeof(int) || t == typeof(long) ||
            t == typeof(short) || t == typeof(byte) ||
            t == typeof(bool) || t == typeof(TimeSpan);
    }

    public static bool IsList(Type t, out Type elemType)
    {
        if (t.IsArray) { elemType = t.GetElementType()!; return true; }
        var listIface = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
        if (listIface != null) { elemType = listIface.GetGenericArguments()[0]; return true; }
        elemType = typeof(object); return false;
    }

    public static UiForm BuildForm(object model, Type type, string title)
    {
        // Create a typed clone so form.Model is the right type (not Dictionary<,>)
        var createMi = typeof(UiForm).GetMethod("Create")!.MakeGenericMethod(type);
        var form = (UiForm)createMi.Invoke(null, new object[] { title, model })!;

        foreach (var p in GetEditableProperties(type))
        {
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            var key = p.Name;
            var uf = p.GetCustomAttribute<UserFieldAttribute>();
            var label = uf?.Display ?? p.Name;
            var required = uf?.Required ?? false;
            var hint = uf?.Hint;

            // Lists of simple types → Array repeater
            if (IsList(t, out var elemType) && IsSimple(elemType))
            {
                var getter = BuildListGetter<object>(p, elemType);
                var setter = BuildListSetter<object>(p, elemType);
                var field = form.AddList<object, object>(label,
                    _ => (IList<object>)getter(_),
                    (_, v) => setter(_, v), key);
                if (hint != null) field.WithHelp(hint);
                if (!required) field.MakeOptional();
                continue;
            }

            // Scalars/Enums
            if (t == typeof(string))
                form.AddText<object>(label, m => (string)(p.GetValue(m) ?? ""), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t == typeof(int))
                form.AddInt<object>(label, m => (int)(p.GetValue(m) ?? default(int)), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t == typeof(long))
                form.AddLong<object>(label, m => (long)(p.GetValue(m) ?? default(long)), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t == typeof(decimal))
                form.AddDecimal<object>(label, m => (decimal)(p.GetValue(m) ?? default(decimal)), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t == typeof(double))
                form.AddDouble<object>(label, m => Convert.ToDouble(p.GetValue(m) ?? 0d), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t == typeof(float))
                form.AddFloat<object>(label, m => Convert.ToSingle(p.GetValue(m) ?? 0f), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t == typeof(bool))
                form.AddBool<object>(label, m => (bool)(p.GetValue(m) ?? false), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t == typeof(Guid))
                form.AddGuid<object>(label, m => (Guid)(p.GetValue(m) ?? Guid.Empty), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t == typeof(DateTime))
                form.AddDate<object>(label, m => (DateTime)(p.GetValue(m) ?? default(DateTime)), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t == typeof(TimeSpan))
                form.AddTime<object>(label, m => (TimeSpan)(p.GetValue(m) ?? default(TimeSpan)), (m, v) => p.SetValue(m, v), key)
                    .WithHelp(hint).MakeOptionalIf(!required);
            else if (t.IsEnum)
            {
                var method = typeof(UiForm).GetMethod(nameof(UiForm.AddEnum))!.MakeGenericMethod(typeof(object), t);
                var getter = BuildGetter<object>(p);
                var setter = BuildSetter<object>(p);
                var field = (IUiField)method.Invoke(form, new object[] { label, getter, setter, key })!;
                if (hint != null) field.WithHelp(hint);
                if (!required) field.MakeOptional();
            }
            // TODO: handle complex objects/lists-of-complex
        }
        return form;
    }

    private static Func<TModel, object> BuildGetter<TModel>(PropertyInfo p) => _ => p.GetValue(_)!;
    private static Action<TModel, object> BuildSetter<TModel>(PropertyInfo p) => (_, v) => p.SetValue(_, v);

    private static Func<TModel, IList<object>> BuildListGetter<TModel>(PropertyInfo p, Type elemType)
        => _ =>
        {
            var raw = p.GetValue(_) as IEnumerable ?? Array.CreateInstance(elemType, 0);
            var list = new List<object>();
            foreach (var it in raw) list.Add(it!);
            return list;
        };

    private static Action<TModel, IList<object>> BuildListSetter<TModel>(PropertyInfo p, Type elemType)
        => (_, v) =>
        {
            // Try to preserve existing list instance if possible
            var cur = p.GetValue(_);
            if (cur is IList dest && cur.GetType().IsGenericType)
            {
                dest.Clear();
                foreach (var it in v) dest.Add(ConvertItem(it, elemType));
                return;
            }
            // Otherwise build a new List<elemType>
            var typed = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elemType))!;
            foreach (var it in v) typed.Add(ConvertItem(it, elemType));
            p.SetValue(_, typed);
        };

    private static object ConvertItem(object value, Type elemType)
    {
        if (value is null) return elemType.IsValueType ? Activator.CreateInstance(elemType)! : null!;
        var t = Nullable.GetUnderlyingType(elemType) ?? elemType;
        if (t == typeof(string)) return Convert.ToString(value) ?? "";
        if (t == typeof(int)) return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (t == typeof(long)) return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        if (t == typeof(decimal)) return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        if (t == typeof(double)) return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (t == typeof(float)) return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        if (t == typeof(bool)) return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        if (t == typeof(Guid)) return (value is Guid g) ? g : Guid.Parse(Convert.ToString(value)!);
        if (t == typeof(DateTime)) return (value is DateTime dt) ? dt : DateTime.Parse(Convert.ToString(value)!, CultureInfo.InvariantCulture);
        if (t == typeof(TimeSpan)) return (value is TimeSpan ts) ? ts : TimeSpan.Parse(Convert.ToString(value)!, CultureInfo.InvariantCulture);
        return value;
    }

    private static IUiField MakeOptionalIf(this IUiField field, bool cond)
        => cond ? field.MakeOptional() : field;
}