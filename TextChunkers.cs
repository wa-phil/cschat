using System;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

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
            chunks.Add(($"{path}:{i}", content));
        }
        return chunks;
    }
}