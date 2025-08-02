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

    public List<(string Reference, string Content)> ChunkText(string path, string text)
    {
        var chunks = new List<(string, string)>();
        for (int i = 0; i < text.Length; i += chunkSize - overlap)
        {
            chunks.Add(($"{path}:@{i}", text.Substring(i, Math.Min(chunkSize, text.Length - i))));
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

    public List<(string Reference, string Content)> ChunkText(string path, string text)
    {
        var chunks = new List<(string, string)>();
        var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i += chunkSize - overlap)
        {
            var content = string.Join("\n", lines.Skip(i).Take(chunkSize));
            if (string.IsNullOrWhiteSpace(content)) continue; // Skip empty chunks
            chunks.Add(($"{path}, line {i + 1} to {i + chunkSize}", content)); // line numbers are 1-based for user-friendliness
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
    private readonly Dictionary<string, FileFilterRules> filters;

    public SmartChunk(Config config)
    {
        maxTokens = config.RagSettings.MaxTokensPerChunk;
        overlap = config.RagSettings.Overlap;
        maxLineLength = config.RagSettings.MaxLineLength;
        filters = config.RagSettings.FileFilters;

        if (overlap >= config.RagSettings.ChunkSize)
        {
            throw new ArgumentException("Overlap must be less than chunk size");
        }
    }

    public List<(string Reference, string Content)> ChunkText(string path, string text)
    {
        var lines = PreprocessLines(text, TryExtractRealFileExtension(path));
        var chunks = new List<(string, string)>();
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
                        ? path
                        : $"{path}, Lines {start + 1} to {i}";
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

    private List<string> PreprocessLines(string text, string? extension)
    {
        var rawLines = text.Split('\n').Where(l => l.Length <= maxLineLength);
        if (string.IsNullOrWhiteSpace(extension) || !filters.TryGetValue(extension, out var rules))
        {
            // There are no filters for this extension, return all lines
            return rawLines.ToList();
        }

        // Filter lines based on include and exclude rules based on the following order:
        //  * If include rules are present, only include lines that match at least one of the include rules.
        //  * If exclude rules are present, exclude lines that match any of the exclude rules.
        //  * If neither, include all lines.
        var includeRules = rules.IncludeRegexPatterns.Select(p => new Regex(p)).ToList();
        var excludeRules = rules.ExcludeRegexPatterns.Select(p => new Regex(p)).ToList();
        return rawLines.Where(line =>
            (includeRules.Count > 0, excludeRules.Count > 0) switch
            {
                (true, _)       => includeRules.Any(r => r.IsMatch(line)),
                (false, true)   => !(excludeRules.Any(r => r.IsMatch(line))),
                (false, false)  => true
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

    private int ApproximateTokenCount(string line) => Math.Max(1, line.Length / 4); // crude approximation
}