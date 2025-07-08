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
// ================================================

using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public static class Log
{
    public enum Data : UInt32
    {
        Method, Level, Timestamp, Message, Success, ErrorCode, IsRetry, Threw, Caught, PlugIn, Count, Source,
        Path, IsValid, IsAuthed, Assembly, Interface, Role, Token, SecureBase, DirectFile, 
        Provider, Model, Version, GitHash, ProviderSet, Result, FilePath, Query, Name, Scores
    }

    public enum Level { Information, Error }

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
        }

        public Context Append(Data key, object value)
        {
            _items[key] = value;
            return this;
        }

        public void Succeeded(bool success = true)
        {
            _items[Data.Success] = success;
            _items[Data.Timestamp] = DateTime.UtcNow;
        }

        public void Failed(string message, Exception? ex)
        {
            _items[Data.Level] = Level.Error;
            _items[Data.Message] = message;
            if (ex != null)
            {
                _items[Data.Threw] = ex.Message;
                _items[Data.ErrorCode] = $"0x{ex.HResult:X8}";
            }
            Succeeded(false);
        }

        public void Failed(string message, Error errorCode)
        {
            _items[Data.Level] = Level.Error;
            _items[Data.Message] = message;
            _items[Data.ErrorCode] = $"0x{errorCode:X8}";
            Succeeded(false);
        }

        public void Dispose()
        {
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
        ctx.Append(Data.Source, $"{Path.GetFileName(callerFile)}:{callerLine}");
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

    public static void Initialize()
    {
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
                    Data.Level,
                    Data.GitHash,
                    Data.Source,
                    Data.Method,
                    Data.Success,
                    Data.ErrorCode,
                    Data.IsRetry,
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
