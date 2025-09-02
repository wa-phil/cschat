using System;
using System.Collections.Generic;

[UserManaged("S360 Profile", "S360 settings for a manager/service tree (IDs only; pick Kusto at runtime)")]
public sealed class S360Profile
{
    [UserKey]
    public string Name { get; set; } = "";

    [UserField(required: true, display: "Service Tree IDs (GUIDs)")]
    public List<Guid> ServiceIds { get; set; } = new();

    // Triage tuning
    public int FreshDays { get; set; } = 7;
    public int SoonDays { get; set; } = 7;

    public float W_RecentChange { get; set; } = 1.0f;
    public float W_Unassigned { get; set; } = 1.0f;
    public float W_DueSoon { get; set; } = 2.0f;
    public float W_SlaAtRisk { get; set; } = 2.2f;
    public float W_MissingEta { get; set; } = 1.6f;
    public float W_ManyEtaChgs { get; set; } = 1.2f;
    public float W_Delegated { get; set; } = 0.6f;

    // Wave/off-track & burndown heuristics
    public float W_OffTrackWave { get; set; } = 2.5f; // EndDate passed and remaining > 0
    public float W_BurnDownNeg { get; set; } = 1.8f; // Recent slope non-negative (no progress)
    public int BurnDownMinPoints { get; set; } = 3; // Require this many points to trust slope
    public int OffTrackGraceDays { get; set; } = 2; // Days after EndDate before off-track

    public override string ToString() => $"{Name} (services:{ServiceIds.Count})";
}
