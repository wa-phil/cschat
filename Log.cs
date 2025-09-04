// ================================================
// Example usage:
//
// Log.SetOutput(data => {
//     foreach (var kv in data) Console.Write($" {kv.Key.ToString()}: {kv.Value.ToString()}");
//     Console.WriteLine();
// });
//
// Log.Method(ctx =>
// {
//     ctx.Append(Data.Path, "file.txt");
//     ctx.Succeeded();
// });
//
// int result = Log.Method(
//     retryCount: 2,
//     shouldRetry: e => e is TimeoutException,
//     func: ctx =>
//     {
//         if (new Random().NextDouble() < 0.5) throw new TimeoutException();
//         ctx.Succeeded();
//         return 42;
//     });
// ===============================================

using cschat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Diagnostics.Tracing;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

public static class Log
{
    public enum Data : UInt32
    {
        Method, Level, Timestamp, Message, Success, ErrorCode, IsRetry, Threw, Caught, Exception, PlugIn, Count, Source, ThreadId,
        Path, IsValid, IsAuthed, Assembly, Interface, Role, Token, SecureBase, DirectFile, Response, Progress,
        Provider, Model, Version, GitHash, ProviderSet, Result, FilePath, Query, Name, Scores, Registered, Reason,
        ToolName, ToolInput, ParsedInput, Enabled, Error, Reference, Goal, Step, Input, TypeToParse, PlanningFailed,
        Command, ServerName, Names, Schema, ExampleText, MenuTop, ConsoleHeight, ConsoleWidth, InputTop, Host, Output,
        Id, Kql,
    }

    public enum Level { Verbose, Information, Warning, Error }

    private static EventLogListener? eventListener = null;
    public static void SetOutput(Action<Dictionary<Data, object>> output) => _output = output;
    private static Action<Dictionary<Data, object>> _output = (_) => { };

    public sealed class Context : IDisposable
    {
        private readonly Dictionary<Data, object> _items = new();

        public Context(Level level)
        {
            _items[Data.Level] = level;
            _items[Data.Success] = false;
            _items[Data.GitHash] = BuildInfo.GitCommitHash;
            _items[Data.ThreadId] = Environment.CurrentManagedThreadId;
        }

        public Context Append(Data key, object value)
        {
            _items[key] = value;
            return this;
        }

        public void OnlyEmitOnFailure()
        {
            _items[Data.Level] = Level.Verbose;
        }

        public void Succeeded(bool success = true)
        {
            _items[Data.Success] = success;
            // format the time as local time, but down to milliseconds
            _items[Data.Timestamp] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        public void Warn(string message)
        {
            _items[Data.Level] = Level.Warning;
            _items[Data.Message] = message;
            Succeeded(false);
        }

        public void Failed(string message, Exception ex)
        {
            _items[Data.Level] = Level.Error;
            _items[Data.Message] = message;
            _items[Data.Threw] = ex.StackTrace?.ToString() ?? "<no stack trace>";
            _items[Data.Caught] = $"{ex.GetType().Name}: {ex.Message}";
            _items[Data.ErrorCode] = $"0x{ex.HResult:X8}";

            Succeeded(false);
        }

        public void Failed(string message, Error errorCode)
        {
            _items[Data.Level] = Level.Error;
            _items[Data.Message] = message;
            _items[Data.ErrorCode] = errorCode.ToString();
            Succeeded(false);
        }

        public void Dispose()
        {
            if (_items[Data.Level] is Level level && level == Level.Verbose && _items[Data.Success] is bool success && success)
            {
                // Don't output verbose logs by default
                return;
            }
            _output(_items);
        }
    }

    private static string GetCallingTypeName()
    {
        var thisTypeDefinition = typeof(Log);
        var stackTrace = new StackTrace();

        for (int i = 1; i < stackTrace.FrameCount; i++)
        {
            var method = stackTrace.GetFrame(i)?.GetMethod();
            var declaringType = method?.DeclaringType;
            if (declaringType == null) continue;

            // Walk up to the outermost declaring type
            var rootType = declaringType;
            while (null != rootType && rootType.IsNested)
                rootType = rootType.DeclaringType;

            // Skip Log itself
            if (rootType == thisTypeDefinition)
                continue;

            // Skip common framework namespaces (adjust as needed)
            var ns = declaringType.Namespace ?? "";
            if (ns.StartsWith("System.") || ns.StartsWith("Microsoft.") || ns == "System" || ns == "Microsoft")
                continue;

            return declaringType?.FullName ?? "<unknown>";
        }

        return "<unknown>";
    }

    public static void Method(
        Action<Context> impl,
        int retryCount = 0,
        Func<Exception, bool>? shouldRetry = null,
        [CallerMemberName] string callerName = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0
    ) =>
        MethodInternal(Level.Information, ctx => { impl(ctx); return true; }, retryCount, shouldRetry, callerName, callerFile, callerLine);

    public static T Method<T>(
        Func<Context, T> func,
        int retryCount = 0,
        Func<Exception, bool>? shouldRetry = null,
        [CallerMemberName] string callerName = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0
    ) =>
        MethodInternal(Level.Information, func, retryCount, shouldRetry, callerName, callerFile, callerLine);

    private static T MethodInternal<T>(
        Level level,
        Func<Context, T> func,
        int retryCount,
        Func<Exception, bool>? shouldRetry,
        string callerName,
        string callerFile,
        int callerLine)
    {
        int attempts = 0;
        using var ctx = new Context(level);
        var path = Path.GetFileName(callerFile);
        ctx.Append(Data.Source, $"{path}:{callerLine}");
        ctx.Append(Data.Method, $"{GetCallingTypeName()}.{callerName}");

        while (true)
        {
            try
            {
                return func(ctx);
            }
            catch (Exception e) when (attempts++ < retryCount && (shouldRetry?.Invoke(e) ?? false))
            {
                ctx.Append(Data.Caught, e.ToString());
                ctx.Append(Data.IsRetry, true);
            }
            catch (Exception e)
            {
                ctx.Failed("Unhandled exception", e);
                throw;
            }
        }
    }

    public static async Task<T> MethodAsync<T>(
        Func<Context, Task<T>> func,
        int retryCount = 0,
        Func<Exception, bool>? shouldRetry = null,
        [CallerMemberName] string callerName = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        int attempts = 0;
        using var ctx = new Context(Level.Information);
        ctx.Append(Data.Source, $"{Path.GetFileName(callerFile)}:{callerLine}");
        ctx.Append(Data.Method, $"{GetCallingTypeName()}.{callerName}");

        while (true)
        {
            try
            {
                var result = await func(ctx);
                return result;
            }
            catch (Exception e) when (attempts++ < retryCount && (shouldRetry?.Invoke(e) ?? false))
            {
                ctx.Append(Data.Caught, e.ToString());
                ctx.Append(Data.IsRetry, true);
            }
            catch (Exception e)
            {
                ctx.Failed("Unhandled exception", e);
                throw;
            }
        }
    }

    public static async Task MethodAsync(
        Func<Context, Task> func,
        int retryCount = 0,
        Func<Exception, bool>? shouldRetry = null,
        [CallerMemberName] string callerName = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        await MethodAsync<bool>(async ctx =>
        {
            await func(ctx);
            return true;
        }, retryCount, shouldRetry, callerName, callerFile, callerLine);
    }

    private static List<object> _buffer = new List<object>();
    public static IEnumerable<string> GetOutput() => _buffer.Select(item => item.ToJson());
    public static void ClearOutput() => _buffer.Clear();

    public static void PrintColorizedOutput()
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"Log Entries [{_buffer.Count}]:");
        Console.ResetColor();

        _buffer.OfType<Dictionary<string, object>>()
               .Select(dict => dict.Where(kv => Enum.TryParse<Data>(kv.Key, out _))
               .ToDictionary(kv => Enum.Parse<Data>(kv.Key), kv => kv.Value))
               .ToList()
               .ForEach(ColorizedConsoleLogger.WriteColorizedLog);
    }

    public static void Initialize()
    {
        if (eventListener == null)
        {
            eventListener = new EventLogListener();
        }

        var buffer = _buffer; // Use a local reference to avoid recursion
        SetOutput((obj) =>
        {
            if (obj is Dictionary<Data, object> data)
            {
                var result = new Dictionary<string, object>();
                var seen = new HashSet<Data>();

                // Explicit field priority
                Data[] priority = new[]
                {
                    Data.Timestamp,
                    Data.Success,
                    Data.Level,
                    Data.GitHash,
                    Data.Source,
                    Data.Method,
                    Data.ErrorCode,
                    Data.ThreadId,
                    Data.IsRetry,
                    Data.Name,
                    Data.TypeToParse,
                    Data.Input,
                    Data.Response,
                    Data.Result,
                    Data.Message
                };

                // Emit priority fields in order
                foreach (var key in priority)
                {
                    if (data.TryGetValue(key, out var value))
                    {
                        result[key.ToString()] = value;
                        seen.Add(key);
                    }
                }

                // Emit remaining fields in declared enum order
                foreach (Data key in Enum.GetValues(typeof(Data)))
                {
                    if (!seen.Contains(key) && data.TryGetValue(key, out var value))
                    {
                        result[key.ToString()] = value;
                        seen.Add(key);
                    }
                }
                buffer.Add(result); // Use the local reference
            }
        });
    }
}


// Custom EventSourceListener that bridges Azure SDK events to our Log class
public class EventLogListener : EventListener
{
    protected override void OnEventSourceCreated(EventSource eventSource) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        if (eventSource == null || string.IsNullOrEmpty(eventSource.Name))
        {
            ctx.Failed("EventSource is null or has no name.", Error.Unknown);
            return;
        }
        ctx.Append(Log.Data.Name, eventSource.Name);
        Program.config.EventSources.TryGetValue(eventSource.Name, out var enabled);
        if (Program.config.VerboseEventLoggingEnabled && enabled == true)
        {
            EnableEvents(eventSource, EventLevel.Verbose);
        }
        else if (!Program.config.EventSources.ContainsKey(eventSource.Name))
        {
            Program.config.EventSources[eventSource.Name] = false;
        }
        ctx.Append(Log.Data.Enabled, enabled);
        ctx.Succeeded();
    });

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        Program.config.EventSources.TryGetValue(eventData.EventSource.Name, out var enabled);
        if (enabled == true)
        {
            var level = eventData.Level == EventLevel.Error ? Log.Level.Error : Log.Level.Information;

            using var ctx = new Log.Context(level);
            ctx.OnlyEmitOnFailure();
            ctx.Append(Log.Data.Level, level);
            ctx.Append(Log.Data.Source, $"Azure.{eventData.EventSource.Name}");
            ctx.Append(Log.Data.Message, $"[{eventData.EventName}] {eventData.Message}");
            if (eventData.Payload != null && eventData.Payload.Count > 0)
            {
                var payload = string.Join(", ", eventData.Payload);
                ctx.Append(Log.Data.Message, $"[{eventData.EventName}] {eventData.Message} - Payload: {payload}");
            }
            ctx.Succeeded(eventData.Level == EventLevel.Informational || eventData.Level == EventLevel.Verbose);
        }
    }
}