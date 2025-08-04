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