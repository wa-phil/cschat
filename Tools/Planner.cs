using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public record PlanStep(bool Done, string ToolName, object ToolInput, string Reason)
{
    public static PlanStep Complete(string reason) => new PlanStep(true, string.Empty, new NoInput(), reason);
    public static PlanStep Run(string toolName, object toolInput, string reason) => new PlanStep(false, toolName, toolInput, reason);
}

public class Planner
{
    private bool done = false;
    private List<string> results = new List<string>();
    private HashSet<string> actionsTaken = new HashSet<string>();

    private async Task<PlanProgress> GetPlanProgress(Context context, string goal, string userInput) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var references = context.GetContext().Select(c => $"{c.Reference}: {c.Chunk}").ToList();
        var working = new Context($"""
You are helping evaluate whether the user's goal has been successfully achieved.

### Goal
{goal}

### User Input
{userInput}

### Steps Taken So Far
{string.Join("\n", results)}

Determine: Is the goal now complete? Respond ONLY with JSON matching the PlanProgress class.

{(references.Count > 0 ? $"\nAdditional context:\n{string.Join("\n", references)}" : "")}
""");
        // marshal Context context into working Context
        context.GetContext().ForEach(c => working.AddContext(c.Reference, c.Chunk));
        working.AddUserMessage("Have we achieved the goal?");
        var response = await TypeParser.GetAsync(working, typeof(PlanProgress));
        if (response == null || response is not PlanProgress progress)
        {
            ctx.Failed("Failed to parse plan progress from response.", Error.EmptyResponse);
            return new PlanProgress { GoalAchieved = false };
        }
        ctx.Append(Log.Data.Progress, response != null ? response.ToJson() : "<null>");
        ctx.Succeeded();
        return progress;
    });

    private async Task<object?> GetToolInput(string toolName, ITool tool, string goal) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Name, toolName);
        ctx.Append(Log.Data.TypeToParse, tool.InputType.Name);

        // If the tool doesn't need input, return a default instance
        if (tool.InputType == typeof(NoInput))
        {
            ctx.Succeeded();
            return new NoInput();
        }

        var Context = new Context($"""
You are an input generator for the tool '{toolName}'.  
Your task is to generate appropriate input for this tool based on the goal and context.

The stated goal: {goal}
Tool Description: {tool.Description}
Tool Usage: {tool.Usage}
Input Type: {tool.InputType.Name}

{(results.Count > 0 ? $"Context from previous steps:\n{string.Join("\n", results.TakeLast(3))}" : "")}

Generate the appropriate input for this tool in JSON format.
The JSON should match the structure of the {tool.InputType.Name} class.
Respond with ONLY the JSON object that matches {tool.InputType.Name}.
""");

        Context.AddUserMessage("Create the inputs JSON for this tool based on the goal and context.");

        var typedInput = await TypeParser.GetAsync(Context, tool.InputType);
        ctx.Append(Log.Data.ParsedInput, typedInput?.ToJson() ?? "<null>");        
        ctx.Succeeded();
        return typedInput;
    });

    private async Task<ToolSelection> GetToolSelection(string goal, Context history) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var context = string.Join("\n", results.TakeLast(5));
        var tools = ToolRegistry.GetToolDescriptions();

        var Context = new Context($"""
You are a tool selection agent.
Your task is to determine which tool to use based on the following:
1. The user's goal: {goal}
{(results.Count >0 ? 
    $"2. Recent steps taken: {string.Join("\n", results.TakeLast(5))}" : 
    "2. No steps taken yet."
)}
3. The available tools:
{string.Join("\n", tools)}

**Important:**
- Do not select tools that have already been used with similar inputs.
- Pick the best tool not already chosen to achieve the goal based on the context provided.
- Your output will be parsed as JSON. Do NOT include markdown, commentary, or explanations.
- Do NOT include any additional text or explanations.
""");
        // marshal Context context into working Context
        history.GetContext().ForEach(c => Context.AddContext(c.Reference, c.Chunk));
        Context.AddToolMessage($"Progress so far:\n{string.Join("\n", results)}");
        Context.AddUserMessage("What is the next tool to use?");
        var response = await TypeParser.GetAsync(Context, typeof(ToolSelection));
        if (response == null || response is not ToolSelection toolSelection)
        {
            ctx.Failed("Failed to parse tool selection from response.", Error.EmptyResponse);
            return new ToolSelection { ToolName = "", Reasoning = "No further action required." };
        }
        ctx.Succeeded();
        return (ToolSelection)response;
    });

    private async Task<PlanObjective> GetObjective(Context context) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var input = context.Messages()
            .Take(5)
            .Select(m => $"{m.Role.ToString()}: {m.Content}")
            .Aggregate((current, next) => $"{current}\n{next}");

        var working = new Context($"""
You are a goal planner.
Your task is to decide if a tool should be used to answer the user's question in pursuit of providing a more accurate or personalized response.
If the user's query depends on realtime or runtime data (see tool_names for list of available tools) assume action is required, and that you will be able to complete it.
You do not need to summarize the user's question, or comment on it, or explain your answer, just decide if a tool is needed to answer the question.
""");

        context.GetContext().ForEach(c => working.AddContext(c.Reference, c.Chunk));
        working.AddUserMessage("---ONLY RESPOND WITH THE JSON OBJECT, DO NOT RESPOND WITH ANYTHING ELSE---");
        working.AddUserMessage(input);
        var result = await TypeParser.GetAsync(working, typeof(PlanObjective));
        if (result == null || result is not PlanObjective goal)
        {
            ctx.Failed("Failed to parse goal from response.", Error.EmptyResponse);
            return new PlanObjective { TakeAction = false, Goal = "No further action required" };
        }
        ctx.Succeeded();
        return (PlanObjective)result;
    });

    public async Task<(string result, Context Context)> PostChatAsync(Context context) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        // We're just getting started, reset results and actionsTaken for each session
        results = new List<string>(); 
        actionsTaken = new HashSet<string>();

        var objective = await GetObjective(context);
        ctx.Append(Log.Data.Goal, objective != null ? objective.ToJson() : "<null>");

        // EARLY EXIT: If no action is needed, skip planning loop and just answer
        if (null == objective ||
            objective.TakeAction == false ||
            string.IsNullOrEmpty(objective.Goal.Trim() ?? string.Empty) ||
            objective.Goal.Equals("No further action required", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Construct a user-facing natural language reply using the original context
            ctx.Append(Log.Data.Message, "No planning required, generating response directly.");
            context.SetSystemMessage(Program.config.SystemPrompt);
            var finalResult = await Engine.Provider!.PostChatAsync(context, Program.config.Temperature);
            ctx.Succeeded();
            return (finalResult, context); // context is unchanged
        }

        var input = context.Messages().LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        int stepsTaken = 0, maxAllowedSteps = Program.config.MaxSteps;
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"working on: {objective.Goal}");
        Console.ResetColor();

        ctx.Append(Log.Data.Message, "Taking steps to achieve the goal.");
        // Conversation implies action on the part of the planner.
        int duplicatesAllowed = 3;
        bool planningFailed = false;
        await foreach (var step in Steps(objective.Goal, context, input, onPlanningFailure: reason =>
        {
            planningFailed = true;
            ctx.Append(Log.Data.Reason, reason);
        }))
        {
            // Prevent infinite loops by limiting the number of steps taken (user configureable).
            if (stepsTaken++ > maxAllowedSteps)
            {
                ctx.Append(Log.Data.Message, $"Exceeded maximum allowed steps ({maxAllowedSteps}). Stopping planning.");
                break;
            }

            // Follow the DRY principle to avoid repeating steps...
            var key = $"{step.ToolName}:{step.ToolInput.ToJson() ?? ""}";
            if (actionsTaken.Contains(key))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"Skipping duplicate step: {step.ToolName ?? "<unknown>"} with already attempted input.");
                Console.ResetColor();

                if (0 == --duplicatesAllowed)
                {
                    ctx.Append(Log.Data.Message, $"Exceeded maximum allowed duplicates ({duplicatesAllowed}). Stopping planning.");
                    context.SetSystemMessage(Program.config.SystemPrompt);
                    context.AddUserMessage($"Planning failed for goal: {objective.Goal}. Please summarize the results of the steps taken so far.");
                    var finalResponse = await Engine.Provider!.PostChatAsync(context, Program.config.Temperature);

                    ctx.Failed("Planning failed, summarizing results.", Error.PlanningFailed);
                    return (finalResponse, context);
                }
                continue; // skip duplicate steps
            }
            actionsTaken.Add(key);

            ctx.Append(Log.Data.Count, stepsTaken);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"Step {stepsTaken}: {step.ToolName}...");
            Console.ResetColor();

            var result = await ToolRegistry.InvokeInternalAsync(step!.ToolName!, step!.ToolInput!, context);
            var status = result.Succeeded ? "✅" : "❌";
            
            // Summarize internally for plan progress evaluation
            var summaryContext = new Context($"""
                Summarize the following tool output to help evaluate whether the user's goal was met.

                Tool: {step.ToolName}
                Goal: {objective.Goal}

                Tool Output:
                {result.Response}

                Provide a brief one-paragraph summary of what this output tells us.
            """);
            summaryContext.AddUserMessage("Summarize the tool output.");
            var summary = await Engine.Provider!.PostChatAsync(summaryContext, 0.2f);
            var stepSummaryForPlanner = $"--- step {stepsTaken}: output from {step.ToolName}({step.ToolInput.ToJson()}) ---\n{summary}\n--- end step {stepsTaken} ---";

            context = result.context;
            context.AddToolMessage(stepSummaryForPlanner);
            results.Add(stepSummaryForPlanner);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{status}\n{summary}"); // show what user would see
            Console.ResetColor();

            // check progress towards the goal
            var progress = await GetPlanProgress(context, objective.Goal, input);
            if (progress == null || progress is not PlanProgress)
            {
                throw new CsChatException($"Failed to get plan progress for goal '{objective.Goal}'.", Error.EmptyResponse);
            }

            if (progress.GoalAchieved)
            {
                // Final tool output is the actual answer, no need to run final summary
                ctx.Append(Log.Data.Message, "Returning raw tool output as final result.");
                return (result.Response, context);
            }            
        }

        // Handle the case where planning failed, and bail out early with a summary of the results taken so far.
        if (planningFailed)
        {
            context.SetSystemMessage(Program.config.SystemPrompt);
            context.AddUserMessage($"Planning failed for goal: {objective.Goal}. Please summarize the results of the steps taken so far.");
            var finalResponse = await Engine.Provider!.PostChatAsync(context, Program.config.Temperature);
            ctx.Failed("Planning failed, summarizing results.", Error.PlanningFailed);
            return (finalResponse, context);
        }

        // If we're here, planning didn't fail, but we didn't achieve the goal either; so
        // summarize the steps taken so far and return that as the final result.
        var builder = new StringBuilder();
        int lines = 0;
        Log.GetOutput().ToList().Aggregate(builder, (sb, txt) => { sb.AppendLine(txt); lines++; return sb; });
        var log = builder.ToString();
        var working = new Context($"""
Below is a conversation leading up to and including the steps taken, and their results, to achieve that goal.
Note that the goal very likely has not been achieved, and that very likely something during the planning or execution of the steps against the planned goal failed.

The implied goal before action was taken was: {objective.Goal}

The user stated: {input}.

Explain the steps that were taken, the results of those steps, and hypothesize why the goal was not achieved.
Use the following context to inform your response to the user's statement.
--- log (lines: {lines}) ---
{log}
--- end log ---
""");
        // marshal passed-in context into working context
        context.GetContext().ForEach(c => working.AddContext(c.Reference, c.Chunk));
        results.ForEach(r => working.AddToolMessage(r));
        working.AddUserMessage($"Use the results of the steps taken to achieve the goal: '{objective.Goal}' to inform your response to the original statement: '{input}'");
        var final = await Engine.Provider!.PostChatAsync(working, Program.config.Temperature);

        ctx.Succeeded();
        return (final, context);
    });

    private enum State { failed, success, noFurtherActionRequired };

    public async IAsyncEnumerable<PlanStep> Steps(string goal, Context Context, string userInput, Action<string> onPlanningFailure = null!) 
    {
        while (!done)
        {
            // Get the next step based on the goal and current context
            string reason = string.Empty;            
            State state = State.success;
            PlanStep? step = null;
            try
            {
                step = await GetNextStep(goal, Context, userInput);
                if (step == null)
                {
                    state = State.failed;
                    reason = "ERROR: Planner failed to return a valid step; check logs for further details.";
                }
                else if (step.Done)
                {
                    state = State.noFurtherActionRequired;
                    reason = step.Reason;
                }
                else if (string.IsNullOrEmpty(step.ToolName))
                {
                    state = State.failed;
                    reason = "ERROR: Planner returned a step with an empty tool name; check logs for further details.";
                }
                else if (!ToolRegistry.IsToolRegistered(step.ToolName))
                {
                    state = State.failed;
                    reason = $"ERROR: Planner returned an invalid tool name: {step.ToolName}; check logs for further details.";
                }
                else if (step.ToolInput == null)
                {
                    state = State.failed;
                    reason = "ERROR: Planner returned a step with null tool input; check logs for further details.";
                }
            } 
            catch (Exception ex)
            {
                state = State.failed;
                reason = $"ERROR: Planner failed to return a valid step due to an exception: {ex.Message}";
            }

            if (State.failed == state)
            {
                onPlanningFailure?.Invoke(reason);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(reason);
                Console.ResetColor();
                yield break;
            }

            if (State.noFurtherActionRequired == state)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Done.");
                Console.ResetColor();
                yield break;
            }

            yield return step!;
        }
    }

    private async Task<PlanStep> GetNextStep(string goal, Context Context, string userInput) => await Log.MethodAsync(
        retryCount: 2,
        shouldRetry: e => e is CsChatException cce && cce.ErrorCode == Error.EmptyResponse,
        func: async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Goal, goal);
        
        // Phase 1: Determine what tool to use
        var toolSelection = await GetToolSelection(goal, Context);
        if (done || toolSelection == null || string.IsNullOrEmpty(toolSelection.ToolName))
        {
            // No action required
            ctx.Succeeded();
            done = true;
            return PlanStep.Complete(toolSelection?.Reasoning ?? "No further action required.");
        }

        // Phase 2: Get typed input for the selected tool
        var tool = ToolRegistry.GetTool(toolSelection.ToolName);
        if (tool == null)
        {
            throw new CsChatException($"Tool '{toolSelection.ToolName}' not found.", Error.ToolNotAvailable);
        }

        var typedInput = await GetToolInput(toolSelection.ToolName, tool, goal);
        if (typedInput == null)
        {
            throw new CsChatException($"Failed to get input for tool '{toolSelection.ToolName}'.", Error.EmptyResponse);
        }

        ctx.Succeeded();
        return PlanStep.Run(toolSelection.ToolName, typedInput, toolSelection.Reasoning);
    });
}