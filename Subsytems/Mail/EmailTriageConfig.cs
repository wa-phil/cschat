using System;
using System.Collections.Generic;
using System.Linq;

[UserManaged("EmailTriage", "Rules for auto-triaging email topics to folders and spam hints.")]
public class EmailTriageConfig
{
    [UserKey] public string Name { get; set; } = "Default";

    // Organization domain used for “external sender” spam hint (e.g., microsoft.com)
    [UserField(display:"Organization domain (for external detection)")] public string OrgDomain { get; set; } = "microsoft.com";

    // Preferred names used to detect mis-addressed spam (“Pam”, “Patric”, etc. instead of Phil/Phillip)
    [UserField(display:"Acceptable given names for me")] public List<string> MyNames { get; set; } = new() { "Phil", "Phillip", "Philip" };

    // Topic routing
    public List<EmailTopicRule> Topics { get; set; } = new();
}

public class EmailTopicRule
{
    [UserField(required:true, display:"Topic Name")] public string Topic { get; set; } = "";
    [UserField(required:true, display:"Move to Folder (name or id)")] public string DestinationFolder { get; set; } = "";
    [UserField(display:"Keywords (any match)")] public List<string> Keywords { get; set; } = new();
    [UserField(display:"From domains (optional)")] public List<string> FromDomains { get; set; } = new();
}
