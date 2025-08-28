using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

public static class JSONParser
{
    [ThreadStatic] static Stack<List<string>> splitArrayPool = null!;
    [ThreadStatic] static StringBuilder stringBuilder = null!;
    [ThreadStatic] static Dictionary<Type, Dictionary<string, FieldInfo>> fieldInfoCache = null!;
    [ThreadStatic] static Dictionary<Type, Dictionary<string, PropertyInfo>> propertyInfoCache = null!;

    public static T? FromJson<T>(this string json)
    {
        splitArrayPool ??= new Stack<List<string>>();
        stringBuilder ??= new StringBuilder(2048);
        fieldInfoCache ??= new Dictionary<Type, Dictionary<string, FieldInfo>>();
        propertyInfoCache ??= new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        return (T?)ParseValue(typeof(T), json);
    }
    
    public static object? FromJson(this string json, Type type)
    {
        splitArrayPool ??= new Stack<List<string>>();
        stringBuilder ??= new StringBuilder(2048);
        fieldInfoCache ??= new Dictionary<Type, Dictionary<string, FieldInfo>>();
        propertyInfoCache ??= new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        return ParseValue(type, json);
    }

    public static object? ParseValue(Type type, string json)
    {
        json = json.Trim();
        if (json == "null") return null;

        if (type == typeof(string))
        {
            // Check if the string is properly quoted and has enough length
            if (json.Length < 2 || json[0] != '"' || json[json.Length - 1] != '"')
                return string.Empty; // Handle malformed strings gracefully
            return Unescape(json.Substring(1, json.Length - 2));
        }
        if (type == typeof(int)) return int.TryParse(json, out var i) ? i : 0;
        if (type == typeof(float)) return float.TryParse(json, out var f) ? f : 0f;
        if (type == typeof(double)) return double.TryParse(json, out var d) ? d : 0.0;
        if (type == typeof(bool)) return json == "true";
        if (type == typeof(object)) return ParseDynamic(json);
        if (type == typeof(Guid))
        {
            // GUIDs in JSON are always quoted strings. Parse only string forms and
            // return Guid.Empty for any other format.
            json = json.Trim();
            if (json.Length >= 2 && json[0] == '"' && json[json.Length - 1] == '"')
            {
                string inner = Unescape(json.Substring(1, json.Length - 2));
                return Guid.TryParse(inner, out var g) ? g : Guid.Empty;
            }
            // If it's an empty unquoted value or malformed, return Guid.Empty
            return Guid.Empty;
        }

        if (type.IsArray)
        {
            Type? elementType = type.GetElementType();
            if (elementType == null) return null;
            if (json.Length < 2 || json[0] != '[' || json[json.Length - 1] != ']')
                return Array.CreateInstance(elementType, 0); // Return empty array for malformed input
            string content = json.Substring(1, json.Length - 2);
            List<string> elems = Split(content);
            Array array = Array.CreateInstance(elementType, elems.Count);
            for (int i = 0; i < elems.Count; i++)
            {
                object? val = ParseValue(elementType, elems[i]);
                if (val != null) array.SetValue(val, i);
            }
            return array;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            Type elementType = type.GetGenericArguments()[0];
            if (json.Length < 2 || json[0] != '[' || json[json.Length - 1] != ']')
                return Activator.CreateInstance(type)!; // Return empty list for malformed input
            string content = json.Substring(1, json.Length - 2);
            List<string> elems = Split(content);
            IList list = (IList)Activator.CreateInstance(type)!;
            foreach (var elem in elems)
            {
                object? val = ParseValue(elementType, elem);
                if (val != null) list.Add(val);
            }
            return list;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(string))
        {
            Type valueType = type.GetGenericArguments()[1];
            if (json.Length < 2 || json[0] != '{' || json[json.Length - 1] != '}')
                return Activator.CreateInstance(type)!; // Return empty dictionary for malformed input
            string content = json.Substring(1, json.Length - 2);
            List<string> elems = Split(content);
            IDictionary dict = (IDictionary)Activator.CreateInstance(type)!;
            foreach (string pair in elems)
            {
                int colonIndex = pair.IndexOf(':');
                if (colonIndex == -1) continue;
                string key = pair.Substring(0, colonIndex).Trim();
                string valStr = pair.Substring(colonIndex + 1).Trim();
                if (key.Length < 2 || key[0] != '"' || key[key.Length - 1] != '"')
                    continue; // Skip malformed keys
                key = Unescape(key.Substring(1, key.Length - 2));
                object? val = ParseValue(valueType, valStr);
                dict[key] = val;
            }
            return dict;
        }

        if (json.Length > 0 && json[0] == '{')
        {
            object obj = Activator.CreateInstance(type)!;
            if (json.Length < 2)
                return obj; // Return empty object for malformed input
            string content = json.Substring(1, json.Length - 2);
            List<string> elems = Split(content);

            if (!fieldInfoCache.TryGetValue(type, out var fields))
            {
                fields = new Dictionary<string, FieldInfo>();
                foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (f.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                        continue;
                    var name = f.GetCustomAttribute<DataMemberAttribute>()?.Name ?? f.Name;
                    fields[name] = f;
                }
                fieldInfoCache[type] = fields;
            }

            if (!propertyInfoCache.TryGetValue(type, out var props))
            {
                props = new Dictionary<string, PropertyInfo>();
                foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!p.CanWrite || p.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                        continue;
                    var name = p.GetCustomAttribute<DataMemberAttribute>()?.Name ?? p.Name;
                    props[name] = p;
                }
                propertyInfoCache[type] = props;
            }

            foreach (string pair in elems)
            {
                int colonIndex = pair.IndexOf(':');
                if (colonIndex == -1) continue;
                string key = pair.Substring(0, colonIndex).Trim();
                string valStr = pair.Substring(colonIndex + 1).Trim();
                if (key.Length < 2 || key[0] != '"' || key[key.Length - 1] != '"')
                    continue; // Skip malformed keys
                key = Unescape(key.Substring(1, key.Length - 2));

                if (fields.TryGetValue(key, out var field))
                {
                    object? val = ParseValue(field.FieldType, valStr);
                    field.SetValue(obj, val);
                }
                else if (props.TryGetValue(key, out var prop))
                {
                    object? val = ParseValue(prop.PropertyType, valStr);
                    prop.SetValue(obj, val);
                }
            }
            return obj;
        }

        return null;
    }

    static object? ParseDynamic(string json)
    {
        json = json.Trim();
        if (json == "null") return null;
        if (json.StartsWith("\"")) 
        {
            if (json.Length < 2) return string.Empty;
            return Unescape(json.Substring(1, json.Length - 2));
        }
        if (json == "true") return true;
        if (json == "false") return false;
        if (json.IndexOf('.') >= 0 && double.TryParse(json, out var dbl)) return dbl;
        if (int.TryParse(json, out var i)) return i;

        if (json.StartsWith("["))
        {
            var list = new List<object?>();
            if (json.Length >= 2)
            {
                var elements = Split(json.Substring(1, json.Length - 2));
                foreach (var elem in elements)
                    list.Add(ParseDynamic(elem));
            }
            return list;
        }

        if (json.StartsWith("{"))
        {
            var dict = new Dictionary<string, object?>();
            if (json.Length >= 2)
            {
                var pairs = Split(json.Substring(1, json.Length - 2));
                foreach (var pair in pairs)
                {
                    int colonIndex = pair.IndexOf(':');
                    if (colonIndex == -1) continue;
                    string key = pair.Substring(0, colonIndex).Trim();
                    string valStr = pair.Substring(colonIndex + 1).Trim();
                    if (key.Length < 2 || key[0] != '"' || key[key.Length - 1] != '"')
                        continue; // Skip malformed keys
                    key = Unescape(key.Substring(1, key.Length - 2));
                    dict[key] = ParseDynamic(valStr);
                }
            }
            return dict;
        }

        return json;
    }

    static string Unescape(string str)
    {
        StringBuilder sb = new();
        for (int i = 0; i < str.Length; i++)
        {
            if (str[i] == '\\' && i + 1 < str.Length)
            {
                i++;
                switch (str[i])
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < str.Length)
                        {
                            string hex = str.Substring(i + 1, 4);
                            if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ushort code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default: sb.Append(str[i]); break;
                }
            }
            else
            {
                sb.Append(str[i]);
            }
        }
        return sb.ToString();
    }

    static List<string> Split(string json)
    {
    // If there's no content, return an empty list (important for parsing '[]' -> zero elements)
    if (string.IsNullOrEmpty(json)) return new List<string>();

    var result = (splitArrayPool != null && splitArrayPool.Count > 0) ? splitArrayPool.Pop() : new List<string>();
    result.Clear();
        int depth = 0;
        int start = 0;
        bool inQuotes = false;
        bool escape = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (escape)
            {
                escape = false;
                continue;
            }
            if (c == '\\')
            {
                escape = true;
                continue;
            }
            switch (c)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case '{': case '[':
                    if (!inQuotes) depth++;
                    break;
                case '}': case ']':
                    if (!inQuotes) depth--;
                    break;
                case ',':
                    if (depth == 0 && !inQuotes)
                    {
                        result.Add(json.Substring(start, i - start));
                        start = i + 1;
                    }
                    break;
            }
        }
        result.Add(json.Substring(start));
        return result;
    }
}