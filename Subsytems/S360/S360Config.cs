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
    [UserField(required: false, display: "Fresh Days (recommended 7)")]
    public int FreshDays { get; set; } = 7;

    [UserField(required: false, display: "Due Soon Days (recommended 7)")]
    public int SoonDays { get; set; } = 7;

    [UserField(required: false, display: "Weight: Recent Change (1.0 recommended)")]
    public float W_RecentChange { get; set; } = 1.0f;

    [UserField(required: false, display: "Weight: Unassigned (1.0 recommended)")]
    public float W_Unassigned { get; set; } = 1.0f;

    [UserField(required: false, display: "Weight: Due Soon (2.0 recommended)")]
    public float W_DueSoon { get; set; } = 2.0f;

    [UserField(required: false, display: "Weight: SLA At Risk (2.2 recommended)")]
    public float W_SlaAtRisk { get; set; } = 2.2f;

    [UserField(required: false, display: "Weight: Missing ETA (1.6 recommended)")]
    public float W_MissingEta { get; set; } = 1.6f;

    [UserField(required: false, display: "Weight: Many ETA Changes (1.2 recommended)")]
    public float W_ManyEtaChgs { get; set; } = 1.2f;

    [UserField(required: false, display: "Weight: Delegated (0.6 recommended)")]
    public float W_Delegated { get; set; } = 0.6f;

    // Wave/off-track & burndown heuristics
    [UserField(required: false, display: "Weight: Off-Track Wave; EndDate passed and remaining > 0, (2.5 recommended)")]
    public float W_OffTrackWave { get; set; } = 2.5f; // EndDate passed and remaining > 0

    [UserField(required: false, display: "Weight: Burndown Negative; recent trend non-negative/no-progress (1.8 recommended)")]
    public float W_BurnDownNeg { get; set; } = 1.8f; // Recent slope non-negative (no progress)

    [UserField(required: false, display: "Weight: Burndown Minimum Points, require this many points to trust slope (3 recommended)")]
    public int BurnDownMinPoints { get; set; } = 3; // Require this many points to trust slope

    [UserField(required: false, display: "Off-Track Grace Days (2 recommended)")]
    public int OffTrackGraceDays { get; set; } = 2; // Days after EndDate before off-track

    public override string ToString() => $"{Name} (services:{ServiceIds.Count})";
}
