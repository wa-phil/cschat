using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

public sealed class Table
{
    public IReadOnlyList<string> Headers { get; }
    public List<string[]> Rows { get; }

    public Table(IReadOnlyList<string> headers, List<string[]> rows)
    {
        Headers = headers ?? Array.Empty<string>();
        Rows = rows ?? new List<string[]>();
    }

    // Factory: construct a Table from an enumerable of T using reflection to derive columns.
    // For simple/primitive T (string, numeric, DateTime, enum) the table will have a single "Value" column.
    // For complex types, public instance readable properties (in declaration order via metadata token fallback) are used as columns.
    public static Table FromEnumerable<T>(IEnumerable<T>? items)
    {
        var list = (items == null) ? new List<T>() : items.ToList();
        if (!list.Any()) return new Table(Array.Empty<string>(), new List<string[]>());

        var t = typeof(T);
        // Treat primitives, enums, string, decimal, DateTime as scalar values
        bool isScalar = t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime);
        if (isScalar)
        {
            var rows = list.Select(x => new string[] { x?.ToString() ?? string.Empty }).ToList();
            return new Table(new List<string> { "Value" }, rows);
        }

        // Prefer public instance properties that are readable
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead)
                     .OrderBy(p => p.MetadataToken)
                     .ToList();

        // If no properties, fall back to public fields
        if (props.Count == 0)
        {
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                          .OrderBy(f => f.MetadataToken)
                          .ToList();
            if (fields.Count > 0)
            {
                var headers = fields.Select(f => f.Name).ToList();
                var rows = list.Select(x => fields.Select(f => {
                    var v = f.GetValue(x);
                    return v?.ToString() ?? string.Empty;
                }).ToArray()).ToList();
                return new Table(headers, rows);
            }
            // Nothing to reflect; treat as single value
            var scalarRows = list.Select(x => new string[] { x?.ToString() ?? string.Empty }).ToList();
            return new Table(new List<string> { "Value" }, scalarRows);
        }

        var headers2 = props.Select(p => p.Name).ToList();
        var rows2 = list.Select(x => props.Select(p => {
            try { var v = p.GetValue(x); return v?.ToString() ?? string.Empty; }
            catch { return string.Empty; }
        }).ToArray()).ToList();
        return new Table(headers2, rows2);
    }

    public int Col(string name)
        => Headers.Select((c, i) => new { c, i }).FirstOrDefault(x => string.Equals(x.c, name, StringComparison.OrdinalIgnoreCase))?.i ?? -1;

    // Return a new table excluding the specified column names (if present)
    public Table Slice(IEnumerable<string> excludeColumnNames)
    {
        var exclude = new HashSet<string>(excludeColumnNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var keepIndices = Headers.Select((h, i) => new { h, i }).Where(x => !exclude.Contains(x.h)).Select(x => x.i).ToList();
        var newHeaders = keepIndices.Select(i => Headers[i]).ToList();
        var newRows = Rows.Select(r => keepIndices.Select(i => i < r.Length ? r[i] ?? string.Empty : string.Empty).ToArray()).ToList();
        return new Table(newHeaders, newRows);
    }

    // Exclude rows where the predicate returns true for the specified column's value.
    public Table Slice(string columnName, Func<string, bool> valuePredicate)
    {
        if (string.IsNullOrWhiteSpace(columnName) || valuePredicate == null)
            return new Table(Headers, Rows.Select(r => r.ToArray()).ToList());
        int idx = Col(columnName);
        if (idx < 0) return new Table(Headers, Rows.Select(r => r.ToArray()).ToList());
        var newRows = new List<string[]>();
        foreach (var r in Rows)
        {
            var val = (idx < r.Length) ? (r[idx] ?? string.Empty) : string.Empty;
            if (!valuePredicate(val)) newRows.Add(r.ToArray());
        }
        return new Table(Headers.ToList(), newRows);
    }

    public string ToCsv()
    {
        static string E(string s) => s.Contains('"') || s.Contains(',') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var lines = new List<string> { string.Join(",", Headers) };
        lines.AddRange(Rows.Select(r => string.Join(",", r.Select(E))));
        return string.Join("\n", lines);
    }

    public string ToJson()
    {
        var list = Rows.Select(r =>
        {
            var o = new Dictionary<string, object?>();
            for (int i = 0; i < Headers.Count; i++)
                o[Headers[i]] = i < r.Length ? r[i] : null;
            return o;
        }).ToList();
        return list.ToJson();
    }

    // Return indices for the requested column names (in same order)
    public int[] Indices(params string[] names)
        => names.Select(n => Col(n)).ToArray();

    // Return a function that given a column name returns the cell value for the provided row (empty string if missing)
    public Func<string, string> Accessor(string[] row)
    {
        return name => {
            var idx = Col(name);
            return (idx >= 0 && idx < row.Length) ? (row[idx] ?? string.Empty) : string.Empty;
        };
    }

    // Project rows using a projector that receives a per-row accessor (name -> value).
    public IEnumerable<T> SelectRows<T>(Func<Func<string, string>, T> projector)
        => Rows.Select(r => projector(Accessor(r))).ToList();

    // Order the table by a specified column using a key selector that converts the cell string to a comparable key.
    public Table OrderBy<TKey>(string columnName, Func<string, TKey> keySelector, bool descending = false) where TKey : IComparable
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        int idx = Col(columnName);
        IEnumerable<string[]> ordered;
        if (descending)
        {
            ordered = Rows.OrderByDescending(r => {
                try {
                    return (idx >= 0 && idx < r.Length) ? keySelector(r[idx] ?? string.Empty) : default(TKey)!;
                } catch { return default(TKey)!; }
            });
        }
        else
        {
            ordered = Rows.OrderBy(r => {
                try {
                    return (idx >= 0 && idx < r.Length) ? keySelector(r[idx] ?? string.Empty) : default(TKey)!;
                } catch { return default(TKey)!; }
            });
        }
        var rowsOut = ordered.Select(r => r.ToArray()).ToList();
        return new Table(Headers.ToList(), rowsOut);
    }

    // Convenience: order lexicographically by the column (string comparison).
    public Table OrderBy(string columnName, bool descending = false)
        => OrderBy<string>(columnName, s => s ?? string.Empty, descending);

    // Return a new table with only the first `count` rows (or all rows if count >= row count)
    public Table Take(int count)
    {
        if (count <= 0) return new Table(Headers.ToList(), new List<string[]>());
        var rowsOut = Rows.Take(count).Select(r => r.ToArray()).ToList();
        return new Table(Headers.ToList(), rowsOut);
    }

    // Generic grouping: project each row (via an accessor) into T and group by the group column value.
    public Dictionary<string, List<T>> GroupRowsBy<T>(string groupColumn, Func<Func<string, string>, T> projector)
    {
        if (projector == null) throw new ArgumentNullException(nameof(projector));
        int gidx = Col(groupColumn);
        var result = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Rows)
        {
            var g = (gidx >= 0 && gidx < r.Length) ? (r[gidx] ?? string.Empty) : string.Empty;
            if (!result.TryGetValue(g, out var list)) { list = new List<T>(); result[g] = list; }
            var acc = Accessor(r);
            var obj = projector(acc);
            list.Add(obj);
        }
        return result;
    }

    // Return a new table containing the newest N rows per group (grouped by groupColumn).
    // selectColumns defines which columns to include in the output (in addition to the group column).
    // dateColumn is used to order within each group (newest first). If a date cannot be parsed it is treated as MinValue.
    public Table LatestPerGroup(string groupColumn, string categoryColumn, string categoryValue, string dateColumn, int perGroup, IEnumerable<string> selectColumns)
    {
    // Filter by category using the column-based Slice overload
    var filtered = this.Slice(categoryColumn, val => !string.Equals(val, categoryValue, StringComparison.OrdinalIgnoreCase));

        int igroup = filtered.Col(groupColumn);
        int idate = filtered.Col(dateColumn);
        var selectList = (selectColumns ?? Enumerable.Empty<string>()).ToList();
        var selIndices = selectList.Select(c => filtered.Col(c)).ToList();

        var rowsOut = new List<string[]>();
        foreach (var g in filtered.Rows.GroupBy(r => (igroup >= 0 && igroup < r.Length) ? r[igroup] : string.Empty))
        {
            var ordered = g.OrderByDescending(r => {
                if (idate >= 0 && idate < r.Length && DateTime.TryParse(r[idate], out var d)) return d;
                return DateTime.MinValue;
            }).Take(Math.Max(1, perGroup));

            foreach (var r in ordered)
            {
                var row = new List<string> { g.Key };
                for (int i = 0; i < selIndices.Count; i++)
                {
                    var idx = selIndices[i];
                    row.Add(idx >= 0 && idx < r.Length ? r[idx] : string.Empty);
                }
                rowsOut.Add(row.ToArray());
            }
        }

        var headers = new List<string> { groupColumn };
        headers.AddRange(selectList);
        return new Table(headers, rowsOut);
    }
}