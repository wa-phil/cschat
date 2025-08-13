using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

public static class ADOExtensions
{
    public static List<WorkItemSummary> ToSummaries(this IEnumerable<WorkItem> items)
        => items.Select(WorkItemSummary.FromWorkItem).ToList();

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
}
