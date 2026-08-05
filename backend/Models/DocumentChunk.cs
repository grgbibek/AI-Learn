namespace TaskFlow.Api.Models;

// A single chunk of an ingested document, along with its embedding vector for similarity search.
public class DocumentChunk
{
    public int Id { get; set; }
    public required string SourceTitle { get; set; }
    public required string Content { get; set; }
    public int ChunkIndex { get; set; }
    public float[] Embedding { get; set; } = [];
}
