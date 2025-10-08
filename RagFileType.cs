using System;
using System.Collections.Generic;

// Represents a supported file type for RAG ingestion and its filtering rules.
// This replaces (over time) RagSettings.SupportedFileTypes and RagSettings.FileFilters.
// Migration logic in Program.InitProgramAsync will populate initial entries from legacy config.
[UserManagedAttribute("RAG File Type", "Supported file type for RAG ingestion and filtering rules.")]
public class RagFileType
{
    [UserKey]
    [UserField(required: true, display: "Extension", hint: "File extension including leading dot (e.g. .cs, .md)", FieldKind = UiFieldKind.String)]
    public string Extension { get; set; } = string.Empty;

    [UserField(display: "Enabled", hint: "If unchecked, this file type will be ignored during ingestion.", FieldKind = UiFieldKind.Bool)]
    public bool Enabled { get; set; } = true;

    [UserField(display: "Include Patterns", hint: "Regex patterns; a file must match at least one include (if any present).", FieldKind = UiFieldKind.String)]
    public List<string> Include { get; set; } = new();

    [UserField(display: "Exclude Patterns", hint: "Regex patterns; if any match the file will be skipped.", FieldKind = UiFieldKind.String)]
    public List<string> Exclude { get; set; } = new();

    [UserField(display: "Description", hint: "Optional description for this file type.", FieldKind = UiFieldKind.Text)]
    public string? Description { get; set; }

    public override string ToString() => $"{Extension} {(Enabled?"✅":"❌")}{(Include.Count>0?" Include=["+string.Join(", ", Include)+"]":"")}{(Exclude.Count>0?" Exclude=["+string.Join(", ", Exclude)+"]":"")}";
}
