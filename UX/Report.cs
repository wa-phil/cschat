// Report.cs
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

public sealed class Report
{
    public sealed class Node
    {
        public string Kind { get; }
        public string? Text { get; }
        public Table? Table { get; }
        public List<Node> Children { get; } = new();
        internal Node(string kind, string? text = null, Table? table = null)
        { Kind = kind; Text = text; Table = table; }
    }

    public string Title { get; }
    private readonly List<Node> _nodes = new();

    private Report(string title) { Title = title ?? ""; }

    public static Report Create(string title) => new(title);

    // Fluent API
    public Report Paragraph(string text)
    { _nodes.Add(new Node("p", text)); return this; }

    public Report Bulleted(params string[] items)
    {
        var n = new Node("ul");
        foreach (var it in items ?? Array.Empty<string>()) n.Children.Add(new Node("li", it));
        _nodes.Add(n);
        return this;
    }

    public Report Numbered(params string[] items)
    {
        var n = new Node("ol");
        foreach (var it in items ?? Array.Empty<string>()) n.Children.Add(new Node("li", it));
        _nodes.Add(n);
        return this;
    }

    public Report TableBlock(Table table)
    { _nodes.Add(new Node("table", table: table)); return this; }

    public Report Section(string heading, Action<Report>? build = null)
    {
        var child = new Report(heading);
        build?.Invoke(child);
        var n = new Node("section", text: heading);
        n.Children.AddRange(child._nodes);
        _nodes.Add(n);
        return this;
    }

    // ---- Rendering helpers (UI chooses which) ----
    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Title)) sb.AppendLine($"# {Escape(Title)}").AppendLine();
        foreach (var n in _nodes) RenderMd(n, sb, 2);
        return sb.ToString();

        static void RenderMd(Node n, StringBuilder sb, int h)
        {
            switch (n.Kind)
            {
                case "p": sb.AppendLine(Escape(n.Text)).AppendLine(); break;
                case "ul":
                    foreach (var li in n.Children) sb.AppendLine($"- {Escape(li.Text)}");
                    sb.AppendLine();
                    break;
                case "ol":
                    for (int i = 0; i < n.Children.Count; i++)
                        sb.AppendLine($"{i+1}. {Escape(n.Children[i].Text)}");
                    sb.AppendLine();
                    break;
                case "table":
                    if (n.Table is { } t) sb.AppendLine(ToMdTable(t)).AppendLine();
                    break;
                case "section":
                    sb.AppendLine($"{new string('#', Math.Clamp(h, 2, 6))} {Escape(n.Text)}").AppendLine();
                    foreach (var c in n.Children) RenderMd(c, sb, Math.Min(6, h+1));
                    break;
            }
        }

        static string ToMdTable(Table t)
        {
            var sb = new StringBuilder();
            if (t.Headers.Count > 0)
            {
                sb.Append("| ").Append(string.Join(" | ", t.Headers.Select(Escape))).AppendLine(" |");
                sb.Append("| ").Append(string.Join(" | ", t.Headers.Select(_ => "---"))).AppendLine(" |");
            }
            foreach (var r in t.Rows)
                sb.Append("| ").Append(string.Join(" | ", r.Select(c => Escape(c ?? "")))).AppendLine(" |");
            return sb.ToString();
        }

        static string Escape(string? s)
            => (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");
    }

    public string ToPlainText(int width = 100)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Title))
        {
            sb.AppendLine(Title);
            sb.AppendLine(new string('=', Math.Min(Title.Length, width)));
            sb.AppendLine();
        }
        foreach (var n in _nodes) RenderTxt(n, sb, width, 0);
        return sb.ToString().TrimEnd();

        static void RenderTxt(Node n, StringBuilder sb, int width, int level)
        {
            switch (n.Kind)
            {
                case "p": sb.AppendLine(n.Text ?? ""); sb.AppendLine(); break;
                case "ul":
                    foreach (var li in n.Children) sb.AppendLine($"- {li.Text}");
                    sb.AppendLine();
                    break;
                case "ol":
                    for (int i = 0; i < n.Children.Count; i++)
                        sb.AppendLine($"{i+1}. {n.Children[i].Text}");
                    sb.AppendLine();
                    break;
                case "table":
                    if (n.Table is { } t) sb.AppendLine(ToAsciiTable(t, width)).AppendLine();
                    break;
                case "section":
                    var head = (level==0) ? n.Text ?? "" : new string('#', Math.Min(5, level+1)) + " " + (n.Text ?? "");
                    sb.AppendLine(head);
                    sb.AppendLine(new string('-', Math.Min(head.Length, width)));
                    sb.AppendLine();
                    foreach (var c in n.Children) RenderTxt(c, sb, width, level + 1);
                    break;
            }
        }

        static string ToAsciiTable(Table t, int width)
        {
            if (t.Headers.Count == 0) return "";
            var cols = t.Headers.Count;
            var rows = new List<string[]> { t.Headers.ToArray() };
            rows.AddRange(t.Rows.Select(r => Enumerable.Range(0, cols).Select(i => i<r.Length? r[i] ?? "" : "").ToArray()));

            // naive width allocation
            var maxLens = Enumerable.Range(0, cols).Select(i => rows.Max(r => (r[i] ?? "").Length)).ToArray();
            var sep = "+" + string.Join("+", maxLens.Select(w => new string('-', Math.Min(w, Math.Max(3, w)) + 2))) + "+";
            string Line(string[] r) => "| " + string.Join(" | ", Enumerable.Range(0, cols).Select(i => Pad(r[i] ?? "", maxLens[i]))) + " |";
            static string Pad(string s, int w) => (s.Length<=w) ? s.PadRight(w) : s.Substring(0, Math.Max(0, w-1)) + "…";

            var sb = new StringBuilder();
            sb.AppendLine(sep);
            sb.AppendLine(Line(rows[0]));
            sb.AppendLine(sep);
            foreach (var r in rows.Skip(1)) sb.AppendLine(Line(r));
            sb.AppendLine(sep);
            return sb.ToString();
        }
    }
}
