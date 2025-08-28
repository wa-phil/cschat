using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

public enum KustoAuthMode
{
    devicecode,
    prompt,
    azcli,
    managedIdentity,
}

[UserManaged("Kusto Config", "Connection details and saved queries for a Kusto database")]
public sealed class KustoConfig
{
    [UserKey]
    public string Name { get; set; } = "";

    [UserField(required: true, display: "Cluster URI")]
    public string ClusterUri { get; set; } = "";

    [UserField(required: true, display: "Database Name")]
    public string Database { get; set; } = "";

    [UserField(required: true, display: "Authentication method", hint: "devicecode|prompt|azcli|managedIdentity")]
    public KustoAuthMode AuthMode { get; set; } = KustoAuthMode.devicecode;

    public int DefaultTimeoutSeconds { get; set; } = 60;

    public List<KustoQuery> Queries { get; set; } = new();        // child items
}

public sealed class KustoQuery
{
    [UserKey] public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kql { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}
