using System;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;

public static class JSONWriter
{
    public static string ToJson(this object item)
    {
        StringBuilder stringBuilder = new StringBuilder();
        AppendValue(stringBuilder, item);
        return stringBuilder.ToString();
    }

    static void AppendValue(StringBuilder stringBuilder, object? item)
    {
        if (stringBuilder == null)
            throw new ArgumentNullException(nameof(stringBuilder));

        if (item == null)
        {
            stringBuilder.Append("null");
            return;
        }

        Type type = item.GetType();
        if (type == typeof(string) || type == typeof(char))
        {
            stringBuilder.Append('"');
            string str = item.ToString() ?? string.Empty;
            foreach (char c in str)
            {
                switch (c)
                {
                    case '"': stringBuilder.Append("\\\""); break;
                    case '\\': stringBuilder.Append("\\\\"); break;
                    case '\b': stringBuilder.Append("\\b"); break;
                    case '\f': stringBuilder.Append("\\f"); break;
                    case '\n': stringBuilder.Append("\\n"); break;
                    case '\r': stringBuilder.Append("\\r"); break;
                    case '\t': stringBuilder.Append("\\t"); break;
                    default:
                        if (char.IsControl(c) || c > 127)
                            stringBuilder.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            stringBuilder.Append(c);
                        break;
                }
            }
            stringBuilder.Append('"');
        }
        else if (item is byte or sbyte or short or ushort or int or uint or long or ulong)
        {
            stringBuilder.Append(Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (item is float or double or decimal)
        {
            stringBuilder.Append(Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (item is bool b)
        {
            stringBuilder.Append(b ? "true" : "false");
        }
        else if (item is DateTime dt)
        {
            stringBuilder.Append('"').Append(dt.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
        }
        else if (type.IsEnum)
        {
            stringBuilder.Append('"').Append(item.ToString()).Append('"');
        }
        else if (item is Guid guid)
        {
            // Serialize GUIDs as quoted strings so they parse back correctly
            stringBuilder.Append('"').Append(guid.ToString()).Append('"');
        }
        else if (item is IList list)
        {
            stringBuilder.Append('[');
            bool isFirst = true;
            foreach (var val in list)
            {
                if (!isFirst) stringBuilder.Append(',');
                isFirst = false;
                AppendValue(stringBuilder, val);
            }
            stringBuilder.Append(']');
        }
        else if (item is IDictionary dict && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(string))
        {
            stringBuilder.Append('{');
            bool isFirst = true;
            foreach (DictionaryEntry kv in dict)
            {
                if (!isFirst) stringBuilder.Append(',');
                isFirst = false;
                stringBuilder.Append('"').Append(kv.Key).Append("\":");
                AppendValue(stringBuilder, kv.Value);
            }
            stringBuilder.Append('}');
        }
        else
        {
            stringBuilder.Append('{');
            bool isFirst = true;

            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy))
            {
                if (f.IsDefined(typeof(IgnoreDataMemberAttribute), true)) continue;
                var value = f.GetValue(item);
                if (value != null)
                {
                    if (!isFirst) stringBuilder.Append(',');
                    isFirst = false;
                    stringBuilder.Append('"').Append(GetMemberName(f)).Append("\":");
                    AppendValue(stringBuilder, value);
                }
            }

            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy))
            {
                if (!p.CanRead || p.IsDefined(typeof(IgnoreDataMemberAttribute), true)) continue;
                var value = p.GetValue(item, null);
                if (value != null)
                {
                    if (!isFirst) stringBuilder.Append(',');
                    isFirst = false;
                    stringBuilder.Append('"').Append(GetMemberName(p)).Append("\":");
                    AppendValue(stringBuilder, value);
                }
            }

            stringBuilder.Append('}');
        }
    }

    static string GetMemberName(MemberInfo member)
    {
        var attr = member.GetCustomAttribute<DataMemberAttribute>(true);
        return attr != null && !string.IsNullOrEmpty(attr.Name) ? attr.Name : member.Name;
    }
}