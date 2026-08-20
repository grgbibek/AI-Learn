namespace TaskFlow.Api.Data;

// Folder ingestion reads arbitrary files off local disk, so it is opt-in per environment:
// only paths under one of these roots may be indexed (prevents path-traversal into unrelated folders).
public sealed class RagFolderIngestionOptions
{
    public List<string> AllowedRoots { get; set; } = [];
    public int MaxFilesPerRequest { get; set; } = 200;
    public int MaxFileSizeBytes { get; set; } = 300_000;
}
