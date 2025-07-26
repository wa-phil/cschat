using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

class TypeParser
{
    public static async Task<object> GetAsync(Context Context, Type t) => await Log.MethodAsync(async ctx=>{
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
        var lastMessage = Context.Messages.LastOrDefault(m => m.Role == Roles.User)?.Content ?? "";
        ctx.Append(Log.Data.TypeToParse, typeof(T).Name);

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

        if (!response.TrimStart().StartsWith("{"))
        {
            throw new CsChatException($"LLM returned invalid JSON: hallucinated preamble or natural language detected. Response: {response}", Error.FailedToParseResponse);
        }

        if (!response.TrimEnd().EndsWith("}"))
        {
            throw new CsChatException($"LLM returned invalid JSON: hallucinated postamble or missing closing brace detected. Response: {response}", Error.FailedToParseResponse);
        }

        // Parse the response into the specified type
        var parsedObject = response.FromJson<T>();
        if (null == parsedObject)
        {
            ctx.Append(Log.Data.Response, response);
            throw new CsChatException($"Failed to parse response into type {typeof(T).Name}.", Error.FailedToParseResponse);
        }
        ctx.Append(Log.Data.Result, $"Successfully parsed {typeof(T).Name}");
        ctx.Succeeded();
        return parsedObject;
    });    
}