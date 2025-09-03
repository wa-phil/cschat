using System;
using System.Collections.Generic;
using System.Linq;

public sealed class Table
{
    public IReadOnlyList<string> Headers { get; }
    public List<string[]> Rows { get; }

    public Table(IReadOnlyList<string> headers, List<string[]> rows)
    {
        Headers = headers ?? Array.Empty<string>();
        Rows = rows ?? new List<string[]>();
    }

    public int Col(string name)
        => Headers.Select((c, i) => new { c, i }).FirstOrDefault(x => string.Equals(x.c, name, StringComparison.OrdinalIgnoreCase))?.i ?? -1;

    // Render to console-friendly table string. maxWidth is optional.
    public string ToText(int maxWidth = 140)
    {
        var hs = Headers.ToList();
        var rowList = Rows.ToList();

        int origCount = hs.Count;
        var indices = Enumerable.Range(0, origCount).ToList();

        if (rowList.Count > 0)
        {
            indices = indices.Where(i => rowList.Any(r => i < r.Length && !string.IsNullOrEmpty(r[i]))).ToList();
            if (indices.Count == 0) indices = Enumerable.Range(0, origCount).ToList();
        }

        var maxLens = new List<int>();
        foreach (var i in indices)
        {
            int w = hs[i]?.Length ?? 0;
            foreach (var r in rowList)
            {
                if (i < r.Length && r[i] != null)
                    w = Math.Max(w, r[i].Length);
            }
            maxLens.Add(w);
        }

        int colCount = indices.Count;
        if (colCount == 0) return string.Empty;

        int sepWidth = 3 * Math.Max(0, colCount - 1);
        int contentMax = Math.Max(1, maxWidth - sepWidth);

        int minCol = 6;
        if (contentMax < colCount * minCol)
        {
            minCol = Math.Max(1, contentMax / colCount);
        }

        var widths = new int[colCount];
        int allocated = 0;
        for (int idx = 0; idx < colCount; idx++)
        {
            int remaining = colCount - idx - 1;
            int minForRemaining = remaining * minCol;
            int availableForThis = contentMax - allocated - minForRemaining;
            int desired = Math.Min(maxLens[idx], contentMax);
            int w = Math.Clamp(desired, minCol, Math.Max(minCol, availableForThis));
            widths[idx] = w;
            allocated += w;
        }

        int leftover = contentMax - allocated;
        for (int i = 0; leftover > 0 && i < colCount; i++)
        {
            int add = Math.Min(leftover, Math.Max(0, maxLens[i] - widths[i]));
            widths[i] += add;
            leftover -= add;
        }
        for (int i = 0; leftover > 0 && i < colCount; i++)
        {
            widths[i] += 1;
            leftover -= 1;
        }

        string Fit(string s, int w) => (s.Length <= w) ? s.PadRight(w) : s.Substring(0, Math.Max(0, w - 1)) + "…";

        var lines = new List<string>();
        lines.Add(string.Join(" │ ", indices.Select((origIdx, j) => Fit(hs[origIdx] ?? "", widths[j]))));
        lines.Add(string.Join("─┼─", widths.Select(c => new string('─', Math.Max(1, c)))));

        foreach (var row in rowList)
        {
            var parts = new List<string>();
            for (int j = 0; j < colCount; j++)
            {
                var origIdx = indices[j];
                var s = (origIdx < row.Length) ? (row[origIdx] ?? "") : "";
                parts.Add(Fit(s, widths[j]));
            }
            lines.Add(string.Join(" │ ", parts));
        }

        return string.Join("\n", lines);
    }

    // Return a new table excluding the specified column names (if present)
    public Table Slice(IEnumerable<string> excludeColumnNames)
    {
        var exclude = new HashSet<string>(excludeColumnNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var keepIndices = Headers.Select((h, i) => new { h, i }).Where(x => !exclude.Contains(x.h)).Select(x => x.i).ToList();
        var newHeaders = keepIndices.Select(i => Headers[i]).ToList();
        var newRows = Rows.Select(r => keepIndices.Select(i => i < r.Length ? r[i] ?? string.Empty : string.Empty).ToArray()).ToList();
        return new Table(newHeaders, newRows);
    }

    // Return a new table where rows are filtered by the predicate which examines each (columnName, cellValue).
    // The predicate should return true if the row should be excluded for a given (columnName, cellValue).
    // A row is excluded if the predicate returns true for any column in that row.
    public Table Slice(Func<string, string, bool> excludeRowPredicate)
    {
        if (excludeRowPredicate == null) return new Table(Headers, Rows.Select(r => r.ToArray()).ToList());
        var newRows = new List<string[]>();
        foreach (var r in Rows)
        {
            bool exclude = false;
            for (int i = 0; i < Headers.Count; i++)
            {
                var colName = Headers[i];
                var val = i < r.Length ? r[i] ?? string.Empty : string.Empty;
                if (excludeRowPredicate(colName, val)) { exclude = true; break; }
            }
            if (!exclude) newRows.Add(r.ToArray());
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
}
