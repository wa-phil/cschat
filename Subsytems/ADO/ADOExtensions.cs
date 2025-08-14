using System.Text;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

public static class ADOExtensions
{
    public static List<WorkItemSummary> ToSummaries(this IEnumerable<WorkItem> items)
        => items.Select(WorkItemSummary.FromWorkItem).ToList();

    public static List<string> ToMenuRows(this IEnumerable<WorkItemSummary> summaries, int? titleWidthOverride = null)
    {
        if (!summaries.Any()) return new List<string> { "(no work items found)" };

        // Fixed widths for the first three columns; Title expands to fill the rest.
        const int idW = 9;          // right-aligned
        const int changedW = 10;    // "yyyy-MM-dd"
        const int assignedW = 25;   // left-aligned
        // spaces between columns = 3

        int totalFixed = idW + 1 + changedW + 1 + assignedW + 1; // + single spaces
        int consoleW = SafeConsoleWidth();
        int titleW = titleWidthOverride ?? Math.Max(24, consoleW - totalFixed - 2); // small margin

        return summaries.Select(s =>
            $"{s.Id,idW} {s.ChangedDate:yyyy-MM-dd} {Truncate(DisplayNameOnly(s.AssignedTo), assignedW),-assignedW} {Truncate(s.Title, titleW)}"
        ).ToList();

        static string DisplayNameOnly(string? assigned)
        {
            if (string.IsNullOrWhiteSpace(assigned)) return "Unassigned";
            var idx = assigned.IndexOf(" (", StringComparison.Ordinal);
            return idx > 0 ? assigned[..idx] : assigned;
        }

        static string Truncate(string? input, int max)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            return input.Length <= max ? input : input[..Math.Max(0, max - 3)] + "...";
        }

        static int SafeConsoleWidth()
        {
            if (Console.IsOutputRedirected) return 120;
            try { return Math.Max(60, Console.WindowWidth); }
            catch { return 120; }
        }
    }

    public static string ToTable(this IEnumerable<WorkItemSummary> summaries)
    {
        if (!summaries.Any())
            return "(no work items found)";

        var header = $"{"ID",6} {"State",-12} {"Priority",8} {"Assigned",-20} {"Title"}";
        var rows = summaries.Select(s =>
            $"{s.Id,6} {s.State,-12} {s.Priority,8} {Truncate(s.AssignedTo, 20),-20} {Truncate(s.Title, 80)}");

        return header + "\n" + string.Join("\n", rows);
    }

    private static string Truncate(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        return input.Length <= maxLength ? input : input.Substring(0, maxLength - 3) + "...";
    }

    public static string ToDetailText(this WorkItemSummary s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ID: {s.Id}");
        sb.AppendLine($"Title: {s.Title}");
        sb.AppendLine($"State: {s.State}");
        if (!string.IsNullOrWhiteSpace(s.Priority)) sb.AppendLine($"Priority: {s.Priority}");
        if (!string.IsNullOrWhiteSpace(s.AssignedTo)) sb.AppendLine($"Assigned To: {s.AssignedTo}");
        if (!string.IsNullOrWhiteSpace(s.AreaPath)) sb.AppendLine($"Area: {s.AreaPath}");
        if (!string.IsNullOrWhiteSpace(s.IterationPath)) sb.AppendLine($"Iteration: {s.IterationPath}");
        sb.AppendLine($"Changed: {s.ChangedDate:u}");
        if (!string.IsNullOrWhiteSpace(s.Description))
        {
            sb.AppendLine();
            sb.AppendLine("Description:");
            sb.AppendLine(s.Description);
        }
        if (s.Tags != null && s.Tags.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Tags: " + string.Join(", ", s.Tags));
        }
        if (s.Discussion != null && s.Discussion.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Discussion:");
            foreach (var line in s.Discussion) sb.AppendLine("- " + line);
        }
        return sb.ToString();
    }
    
    public static string ToTreeRow(this AdoQueryRow row, int assignedNameWidth = 0)
    {
        // Simple “tree” indent; avoids fancy ├/└ complexity while remaining readable
        var indent = new string(' ', Math.Max(0, row.Depth * 2));
        var icon = row.IsFolder ? "📁" : "📃";
        return $"{indent}{icon} {row.Name}";
    }    
}
