using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

// Input classes for tools
[ExampleText("""
{ "Path": "<path_to_file_or_directory>" }

Where <path_to_file_or_directory> is either a full, or a relative path to a file on the local filesystem.
If the path is empty, the current working directory will be used.
ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.
""")]
public class PathInput
{
    public string Path { get; set; } = string.Empty;
}

[ExampleText("""
{ "Pattern": "<regex_pattern>" }

Where <regex_pattern> is a valid .NET regular expression pattern.
ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.
""")]
public class RegexInput  
{
    public string Pattern { get; set; } = string.Empty;
}

[ExampleText("""
{ "Path": "<path_to_file_or_directory>", "Pattern": "<regex_pattern>" }

Where 
<path_to_file_or_directory> is either a full, or a relative path to a file on the local filesystem.  If the path is empty, the current working directory will be used.
<regex_pattern> is a valid .NET regular expression pattern.
ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.
""")]
class PathAndRegexInput
{
    public string Path { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
}

[ExampleText("{ null }")]
public class NoInput
{
    // Empty class for tools that don't require input
}

[ExampleText("""
Respond with **only** one of the following JSON options:

If no further action is needed:

{ \"ToolName\": \"\", \"Reasoning\": \"No further action required.\" }

If a tool should be used:

{ \"ToolName\": \"<tool_name>\", \"Reasoning\": \"<reasoning>\" }

Where:
- `<tool_name>` is the exact name of the tool to use
- `<reasoning>` is a brief explanation of why this tool was selected

ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.
""")]
public class ToolSelection
{
    public string ToolName  { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}

[ExampleText("""
If a meaningful and useful response can be generated from context obtained from previous steps, respond with:

{ \"GoalAchieved\": true }

If action is required to achieve the goal, respond with:

{ \"GoalAchieved\": false }

ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.
""")]
public class PlanProgress
{
    public bool GoalAchieved { get; set; } = false; // Indicates if the goal was achieved
}

[ExampleText("""
If a meaningful and useful static response can be generated from existing knowledge, respond with:

{  \"TakeAction\": false, \"Goal\": \"<reason>\" }

If action is required, respond with:

{  \"TakeAction\": true,  \"Goal\": \"<goal>\"   }

Where:
  * <goal> is a statement of what the user is trying to achieve, e.g., "Summarize the repo contents" or "Help me plan a trip to Paris".
  * <reason> is a statement of why no action is required, e.g., "The user is asking for a summary of the repo contents, which can be generated from existing knowledge."

At this point, you don't need to know how to achieve the goal, just clearly state the goal as you understand it, so that you can plan the steps to achieve it later.
ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.
""")]
public class PlanObjective
{
    public bool TakeAction { get; set; } = false;
    public string Goal { get; set; } = string.Empty; // The goal statement if action is required
}

[ExampleText("""
{ "Prompt": "<prompt>", "Text": "<text>" }

Where:
  * <prompt> is optional and is the prompt to use for summarization, e.g., "Summarize the following text"
  * <text> is required and is the text to summarize
""")]
public class SummarizeText
{
    public string Prompt { get; set; } = "Summarize the following text";
    public string Text { get; set; } = string.Empty;
}

[ExampleText("""
{ "Name": "<entity_name>", "Type": "<entity_type>", "Attributes": "<entity_attributes>" }

Where:
  * <entity_name> is the name/identifier of the entity, e.g., "John Smith" or "Microsoft Corporation"
  * <entity_type> is the category/type of the entity, e.g., "Person", "Organization", "Location", "Concept"
  * <entity_attributes> is a brief description of key attributes or properties, e.g., "CEO of company" or "Technology company founded in 1975"

ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.
""")]
public class EntityDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Attributes { get; set; } = string.Empty;
}

[ExampleText("""
{ "Source": "<source_entity>", "Target": "<target_entity>", "Type": "<relationship_type>", "Description": "<relationship_description>" }

Where:
  * <source_entity> is the name of the entity that initiates the relationship, e.g., "John Smith"
  * <target_entity> is the name of the entity that receives the relationship, e.g., "Microsoft Corporation"
  * <relationship_type> is the category of relationship, e.g., "WORKS_FOR", "LOCATED_IN", "PART_OF", "CREATED_BY"
  * <relationship_description> is a brief description of the relationship, e.g., "works as CEO" or "headquartered in"

ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.
""")]
public class RelationshipDto
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

[ExampleText("""
{ 
  "Entities": [
    { "Name": "John Smith", "Type": "Person", "Attributes": "Senior Developer at tech company" },
    { "Name": "Microsoft", "Type": "Organization", "Attributes": "Technology company" }
  ],
  "Relationships": [
    { "Source": "John Smith", "Target": "Microsoft", "Type": "WORKS_FOR", "Description": "employed as Senior Developer" }
  ]
}

IMPORTANT RULES:
- Entities is an array of EntityDto objects with Name, Type, and Attributes
- Relationships is an array of RelationshipDto objects connecting the entities
- Extract all meaningful entities (people, places, organizations, concepts) and their relationships from the given text
- Each Entity MUST have only one each of the following: Name, Type, Attributes
- Each Relationship MUST have only one each of the following: Source, Target, Type, Description
- Source and Target must reference entity names from the Entities array
- Use consistent capitalized relationship types like: WORKS_FOR, LOCATED_IN, PART_OF, CREATED_BY, MANAGES, etc.
- Return ONLY valid JSON with no explanations or markdown
- Source and Target must exactly match Entity Names
- NO missing fields allowed

CRITICAL: Return ONLY valid JSON. No explanations, no markdown, no duplicated fields. Each relationship object must have exactly one Source, Target, Type, and Description field.

ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE.
""")]
public class GraphDto
{
    public List<EntityDto> Entities { get; set; } = new List<EntityDto>();
    public List<RelationshipDto> Relationships { get; set; } = new List<RelationshipDto>();
}