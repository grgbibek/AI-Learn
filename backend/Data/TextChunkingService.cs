using System.Text;

namespace TaskFlow.Api.Data;

// Splits raw text into overlapping, size-bounded chunks suitable for embedding.
public class TextChunkingService
{
    public List<string> ChunkText(string text, int maxChunkSize = 500, int overlap = 50)
    {
        var paragraphs = text
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (current.Length > 0 && current.Length + paragraph.Length > maxChunkSize)
            {
                chunks.Add(current.ToString().Trim());

                // Carry the tail of the previous chunk forward so context isn't lost at the boundary.
                var previous = current.ToString();
                var overlapText = previous.Length > overlap ? previous[^overlap..] : previous;
                current.Clear();
                current.Append(overlapText).Append(' ');
            }

            current.Append(paragraph).Append("\n\n");
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        return chunks;
    }
}
