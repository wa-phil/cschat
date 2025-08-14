using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

public record AdoQueryRow(Guid? Id, string Name, string Path, bool IsFolder, int Depth);

public record WorkItemSummary(
    int Id,
    string Title,
    string State,
    string? Priority,
    string? AssignedTo,
    string? AreaPath,
    string? IterationPath,
    DateTime ChangedDate,
    string? Description,
    IEnumerable<string> Tags,
    IEnumerable<string> Discussion
)
{
    public static WorkItemSummary FromWorkItem(WorkItem item)
    {
        string Get(string key) =>
            item.Fields.TryGetValue(key, out var val) ? val?.ToString() ?? "" : "";

        string ParseAssignedTo() =>
            item.Fields.TryGetValue("System.AssignedTo", out var assignedTo)
                ? assignedTo switch
                {
                    IdentityRef idRef => $"{idRef.DisplayName} ({idRef.UniqueName})",
                    _ => assignedTo.ToString() ?? string.Empty
                }
                : "Unassigned";

        DateTime ParseDate(string key) =>
            DateTime.TryParse(Get(key), out var dt) ? dt : DateTime.MinValue;

        IEnumerable<string> ParseTags() =>
            Get("System.Tags")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        IEnumerable<string> ParseDiscussion()
        {
            if (item.Fields.TryGetValue("System.History", out var history) && history is string s)
                return new[] { Utilities.StripHtml(s) };
            if (item.Fields.TryGetValue("Microsoft.VSTS.Common.Discussion", out var discussion) && discussion is string d)
                return d.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(s => Utilities.StripHtml(s));
            return Enumerable.Empty<string>();
        }

        return new WorkItemSummary(
            Id: item.Id ?? 0,
            Title: Get("System.Title"),
            State: Get("System.State"),
            Priority: Get("Microsoft.VSTS.Common.Priority"),
            AssignedTo: ParseAssignedTo(),
            AreaPath: Get("System.AreaPath"),
            IterationPath: Get("System.IterationPath"),
            ChangedDate: ParseDate("System.ChangedDate"),
            Description: Utilities.StripHtml(Get("System.Description")),
            Tags: ParseTags(),
            Discussion: ParseDiscussion()
        );
    }
}
