using System.Text.Json.Serialization;

namespace Printman.Core.Models;

public record FileCacheResult(
    string FileId,
    string OriginalFileName,
    string CachedFilePath,
    long FileSizeBytes,
    bool IsDuplicate,
    string Extension);

public class UploadResponse
{
    [JsonPropertyName("fileId")]
    public required string FileId { get; init; }

    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("fileSize")]
    public long FileSize { get; init; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; init; }

    [JsonPropertyName("isDuplicate")]
    public bool IsDuplicate { get; init; }

    [JsonPropertyName("extension")]
    public required string Extension { get; init; }
}

public class WebPrintItem
{
    [JsonPropertyName("fileId")]
    public required string FileId { get; init; }

    [JsonPropertyName("pages")]
    public string? Pages { get; init; }

    [JsonPropertyName("copies")]
    public int Copies { get; init; } = 1;

    [JsonPropertyName("paperSize")]
    public string? PaperSize { get; init; }

    [JsonPropertyName("orientation")]
    public string? Orientation { get; init; } // "auto", "portrait", "landscape"

    [JsonPropertyName("duplex")]
    public string? Duplex { get; init; } // "default", "simplex", "vertical", "horizontal"

    [JsonPropertyName("color")]
    public string? Color { get; init; } // "default", "color", "mono"
}

public class WebBatchPrintRequest
{
    [JsonPropertyName("printer")]
    public string? Printer { get; init; }

    [JsonPropertyName("items")]
    public List<WebPrintItem> Items { get; init; } = [];
}

public class PrintEvent
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("type")]
    public required string Type { get; init; } // "queued", "progress", "completed", "error"

    [JsonPropertyName("jobId")]
    public string? JobId { get; init; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    [JsonPropertyName("printer")]
    public string? Printer { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("pagesPrinted")]
    public int? PagesPrinted { get; init; }

    [JsonPropertyName("totalPages")]
    public int? TotalPages { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public class PinVerifyRequest
{
    [JsonPropertyName("pin")]
    public string? Pin { get; init; }
}
