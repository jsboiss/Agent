namespace Agent.Memory;

public sealed record MemoryExtractionResult(
    IReadOnlyList<ExtractedMemory> Memories,
    string? Error = null,
    string Provider = "",
    string Model = "",
    int RawResponseLength = 0,
    string RawResponsePreview = "",
    string ParseStatus = "");
