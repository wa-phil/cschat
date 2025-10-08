using System;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

[IsConfigurable("BlockChunk")]
public class BlockChunk : ITextChunker
{
    int chunkSize = 500;
    int overlap = 50;

    public BlockChunk(Config config)
    {
        chunkSize = config.RagSettings.ChunkSize;
        overlap = config.RagSettings.Overlap;
    }

    public List<(Reference Reference, string Content)> ChunkText(string path, string text)
    {
        var chunks = new List<(Reference, string)>();
        for (int i = 0; i < text.Length; i += chunkSize - overlap)
        {
            chunks.Add((Reference.Partial(path, i, i + chunkSize), text.Substring(i, Math.Min(chunkSize, text.Length - i))));
        }
        return chunks;
    }
}

[IsConfigurable("LineChunk")]
public class LineChunk : ITextChunker
{
    int chunkSize = 500;
    int overlap = 50;

    public LineChunk(Config config)
    {
        chunkSize = config.RagSettings.ChunkSize;
        overlap = config.RagSettings.Overlap;
    }

    public List<(Reference Reference, string Content)> ChunkText(string path, string text)
    {
        var chunks = new List<(Reference, string)>();
        var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i += chunkSize - overlap)
        {
            var content = string.Join("\n", lines.Skip(i).Take(chunkSize));
            if (string.IsNullOrWhiteSpace(content)) continue; // Skip empty chunks
            chunks.Add((Reference.Partial(path, i + 1, i + chunkSize), content)); // line numbers are 1-based for user-friendliness
        }
        return chunks;
    }
}

[IsConfigurable("SmartChunk")]
public class SmartChunk : ITextChunker
{
    private readonly int maxTokens;
    private readonly int overlap;
    private readonly int maxLineLength;
    // Line-level filtering rules now derived from UserManagedData RagFileType entries (Include/Exclude patterns).
    // Legacy RagSettings.FileFilters (obsolete) only used as a fallback if no user-managed data available.
    private readonly Dictionary<string, FileFilterRules> lineFilters;

    public SmartChunk(Config config)
    {
        maxTokens = config.RagSettings.MaxTokensPerChunk;
        overlap = config.RagSettings.Overlap;
        maxLineLength = config.RagSettings.MaxLineLength;

        lineFilters = new Dictionary<string, FileFilterRules>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Build from RagFileType entries (user-managed)
            var items = Program.userManagedData.GetItems<RagFileType>();
            foreach (var it in items)
            {
                if (it == null || string.IsNullOrWhiteSpace(it.Extension)) continue;
                // Only add if there are actual line-level patterns defined to reduce overhead
                var hasInclude = it.Include?.Any(p => !string.IsNullOrWhiteSpace(p)) == true;
                var hasExclude = it.Exclude?.Any(p => !string.IsNullOrWhiteSpace(p)) == true;
                if (hasInclude || hasExclude)
                {
                    lineFilters[it.Extension.ToLowerInvariant()] = new FileFilterRules
                    {
                        Include = it.Include?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>(),
                        Exclude = it.Exclude?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>()
                    };
                }
            }
        }
        catch { /* Swallow – fall back to legacy if needed */ }

#pragma warning disable CS0618 // Using obsolete members deliberately as fallback
        if (lineFilters.Count == 0 && config.RagSettings.FileFilters != null && config.RagSettings.FileFilters.Count > 0)
        {
            foreach (var kvp in config.RagSettings.FileFilters)
            {
                lineFilters[kvp.Key.ToLowerInvariant()] = kvp.Value;
            }
        }
#pragma warning restore CS0618

        if (overlap >= config.RagSettings.ChunkSize)
        {
            throw new ArgumentException("Overlap must be less than chunk size");
        }
    }

    public List<(Reference Reference, string Content)> ChunkText(string path, string text)
    {
        var lines = PreprocessLines(text, TryExtractRealFileExtension(path));
        var chunks = new List<(Reference, string)>();
        int i = 0;

        while (i < lines.Count)
        {
            int tokenCount = 0, start = i;
            var buffer = new List<string>();

            while (i < lines.Count)
            {
                var line = lines[i];
                var lineTokens = ApproximateTokenCount(line);
                if (tokenCount + lineTokens > maxTokens) { break; }

                buffer.Add(line);
                tokenCount += lineTokens;
                i++;
            }

            if (buffer.Count > 0)
            {
                var content = string.Join("\n", buffer);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    // Create a reference that includes the path and line range, if the content
                    // is the whole file only describe just the path, else describe the range
                    var reference = (start == 0 && i >= lines.Count)
                        ? Reference.Full(path)
                        : Reference.Partial(path, start + 1, i);
                    chunks.Add((reference, content));
                }

                // Only back up if we didn't reach the end of input
                i = (i < lines.Count) ? Math.Max(start + 1, i - overlap) : i;
            }
            else
            {
                i++; // ensure forward progress
            }
        }

        return chunks;
    }

    private static readonly Dictionary<string, Regex> RegexCache = new();

    private Regex GetOrAddRegex(string pattern)
    {
        if (!RegexCache.TryGetValue(pattern, out var regex))
        {
            regex = new Regex(pattern, RegexOptions.Compiled);
            RegexCache[pattern] = regex;
        }
        return regex;
    }

    private List<string> PreprocessLines(string text, string? extension)
    {
        var rawLines = text.Split('\n').Where(l => l.Length <= maxLineLength);
        if (string.IsNullOrWhiteSpace(extension) || !lineFilters.TryGetValue(extension, out var rules))
        {
            // There are no filters for this extension, return all lines
            return rawLines.ToList();
        }

        // Filter lines based on include and exclude rules based on the following order:
        //  * If include rules are present, only include lines that match at least one of the include rules.
        //  * If exclude rules are present, exclude lines that match any of the exclude rules.
        //  * If neither, include all lines.
        var includeRules = rules.Include.Where(p=>!string.IsNullOrEmpty(p)).Select(p=>GetOrAddRegex(p)).ToList();
        var excludeRules = rules.Exclude.Where(p=>!string.IsNullOrEmpty(p)).Select(p=>GetOrAddRegex(p)).ToList();
        return rawLines.Where(line =>
            (includeRules.Count > 0, excludeRules.Count > 0) switch
            {
                (true, _)      => includeRules.Any(r => r.IsMatch(line)),
                (false, true)  => !(excludeRules.Any(r => r.IsMatch(line))),
                (false, false) => true
            }
        ).ToList();
    }

    private string? TryExtractRealFileExtension(string path)
    {
        try
        {
            var actual = path.Contains("::") ? path.Split("::").Last() : path;
            var ext = Path.GetExtension(actual);
            return string.IsNullOrWhiteSpace(ext) ? null : ext.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private int ApproximateTokenCount(string line) => Math.Max(1, line.Length / 3); // crude approximation
}