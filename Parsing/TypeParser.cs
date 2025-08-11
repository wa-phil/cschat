using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

class TypeParser
{
    public static async Task<object> GetAsync(Context Context, Type t) => await Log.MethodAsync(async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.TypeToParse, t.Name);

        var exampleTextAttr = t.GetCustomAttribute<ExampleText>();
        if (exampleTextAttr != null)
        {
            Context.AddSystemMessage($"Example text for {t.Name}:\n{exampleTextAttr.Text}");
        }

        // Reflection to the rescue!
        var method = typeof(TypeParser).GetMethods()
            .FirstOrDefault(m => m.Name == nameof(PostChatAndParseAsync)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(Context));

        if (method == null)
        {
            throw new InvalidOperationException($"Method {nameof(PostChatAndParseAsync)} not found.");
        }

        var genericMethod = method.MakeGenericMethod(t);
        if (genericMethod == null)
        {
            throw new InvalidOperationException($"Failed to create generic method for type {t.Name}.");
        }

        var task = genericMethod.Invoke(null, new object[] { Context }) as Task;
        if (task == null)
        {
            throw new InvalidOperationException($"Failed to invoke method {nameof(PostChatAndParseAsync)} with type {t.Name}.");
        }

        await task.ConfigureAwait(false); // await Task<T> and extract the result

        // Use reflection to get Task<T>.Result
        var resultProperty = task.GetType().GetProperty("Result");
        if (resultProperty == null)
        {
            throw new InvalidOperationException($"Result property not found on task of type {task.GetType().Name}.");
        }

        var result = resultProperty.GetValue(task);
        if (result == null)
        {
            throw new InvalidOperationException($"Result is null for task of type {task.GetType().Name}.");
        }
        ctx.Append(Log.Data.Result, result.ToJson());
        ctx.Succeeded();
        return result;
    });

    public static async Task<T> PostChatAndParseAsync<T>(Context Context) where T : class
        => await Log.MethodAsync(
            shouldRetry: e => e is CsChatException cce && (cce.ErrorCode == Error.EmptyResponse || cce.ErrorCode == Error.FailedToParseResponse),
            retryCount: 2,
            func: async ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var lastMessage = Context.Messages().LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        ctx.Append(Log.Data.TypeToParse, typeof(T).Name);
        ctx.Append(Log.Data.Input, Context.Messages().ToJson());

        // Send Context to the provider's PostChatAsync method
        var response = await Engine.Provider!.PostChatAsync(Context, 0.05f);
        ctx.Append(Log.Data.Response, response);

        if (string.IsNullOrWhiteSpace(response))
        {
            throw new CsChatException($"Received empty response from provider for: {lastMessage}.", Error.EmptyResponse);
        }

        if (response.TrimStart().StartsWith("```"))
        {
            // Remove code block markers if present
            response = Regex.Replace(response, @"^```[\w-]*\s*", "", RegexOptions.Multiline).Trim();
            response = Regex.Replace(response, @"\s*```$", "", RegexOptions.Multiline).Trim();
        }

        response = FixCommonJsonErrors(response);

        if (!response.TrimStart().StartsWith("{"))
        {
            throw new CsChatException($"LLM returned invalid JSON: hallucinated preamble or natural language detected. Response: {response}", Error.FailedToParseResponse);
        }

        if (!response.TrimEnd().EndsWith("}"))
        {
            throw new CsChatException($"LLM returned invalid JSON: hallucinated postamble or missing closing brace detected. Response: {response}", Error.FailedToParseResponse);
        }

        Console.WriteLine("RESPONSE:");
        Console.WriteLine(response);

        // Parse the response into the specified type
        var parsedObject = response.FromJson<T>();
        if (null == parsedObject)
        {
            throw new CsChatException($"Failed to parse response into type {typeof(T).Name}.", Error.FailedToParseResponse);
        }

        ctx.Succeeded();
        return parsedObject;
    });
    private static string FixCommonJsonErrors(string json)
    {
        // Fix trailing commas before closing brackets/braces
        //json = Regex.Replace(json, @",(\s*[}\]])", "$1");
        
        // Fix casing for GraphDto - convert lowercase to proper casing
        json = Regex.Replace(json, @"""entities""", @"""Entities""", RegexOptions.IgnoreCase);
        json = Regex.Replace(json, @"""relationships""", @"""Relationships""", RegexOptions.IgnoreCase);
        json = Regex.Replace(json, @"""name""", @"""Name""", RegexOptions.IgnoreCase);
        json = Regex.Replace(json, @"""type""", @"""Type""", RegexOptions.IgnoreCase);
        json = Regex.Replace(json, @"""attributes""", @"""Attributes""", RegexOptions.IgnoreCase);
        json = Regex.Replace(json, @"""source""", @"""Source""", RegexOptions.IgnoreCase);
        json = Regex.Replace(json, @"""target""", @"""Target""", RegexOptions.IgnoreCase);
        json = Regex.Replace(json, @"""description""", @"""Description""", RegexOptions.IgnoreCase);
        
        // Fix duplicate consecutive fields in objects by removing duplicates
        //json = Regex.Replace(json, @"""(Target|Source|Type|Description)""\s*:\s*""[^""]*""\s*,?\s*""(Target|Source|Type|Description)""\s*:", @"""$2"":");
        
        // Remove any broken trailing content after the main JSON structure
        /*var lastBraceIndex = json.LastIndexOf('}');
        if (lastBraceIndex != -1 && lastBraceIndex < json.Length - 1)
        {
            var afterLastBrace = json.Substring(lastBraceIndex + 1).Trim();
            if (!string.IsNullOrWhiteSpace(afterLastBrace))
            {
                json = json.Substring(0, lastBraceIndex + 1);
            }
        }*/
        
        return json;
    }
}