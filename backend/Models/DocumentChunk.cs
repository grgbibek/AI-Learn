using Microsoft.Data.SqlTypes;

namespace TaskFlow.Api.Models;

// A single chunk of an ingested document, along with its embedding vector for similarity search.
// Embedding uses SQL Server 2025's native `vector` column type (EF Core 10+) so similarity search
// runs inside the database via VECTOR_DISTANCE() instead of loading every row into app memory.
public class DocumentChunk
{
    public int Id { get; set; }
    public required string SourceTitle { get; set; }
    public required string Content { get; set; }
    public int ChunkIndex { get; set; }
    public SqlVector<float> Embedding { get; set; }
}
