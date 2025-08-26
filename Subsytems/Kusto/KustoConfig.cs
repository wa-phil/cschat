using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

[UserManaged("Kusto Config", "Connection details and saved queries for a Kusto database")]
public sealed class KustoConfig
{
    [UserKey] public string Name { get; set; } = "";             // logical key
    [UserField(required: true, display: "Cluster URI")] public string ClusterUri { get; set; } = "";
    [UserField(required: true)] public string Database { get; set; } = "";

    public int DefaultTimeoutSeconds { get; set; } = 60;
    public List<KustoQuery> Queries { get; set; } = new();        // child items

    // Optional, populated by the schema tool:
    public string? SchemaJson { get; set; }                       // tables/columns/types
}

public sealed class KustoQuery
{
    [UserKey] public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kql { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}
