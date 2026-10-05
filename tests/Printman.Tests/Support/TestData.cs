using Printman.Core.Models;

namespace Printman.Tests.Support;

/// <summary>Factory helpers so tests stay readable and intention-revealing.</summary>
public static class TestData
{
    public static PaperSizeOption A4 { get; } = new("A4", 9, 827, 1169);
    public static PaperSizeOption Letter { get; } = new("Letter", 1, 850, 1100);

    public static PrinterInfo Printer(
        string name = "Fake Printer",
        bool isDefault = false,
        bool supportsColor = true,
        bool canDuplex = true,
        IReadOnlyList<PaperSizeOption>? paperSizes = null) => new()
    {
        Name = name,
        IsDefault = isDefault,
        DriverName = "Fake Driver",
        PortName = "FAKE001",
        SupportsColor = supportsColor,
        CanDuplex = canDuplex,
        SupportedPaperSizes = paperSizes ?? [A4, Letter]
    };

    public static PrinterStatusInfo Status(string printerName = "Fake Printer") => new()
    {
        PrinterName = printerName,
        StatusText = "Ready",
        IsOnline = true
    };

    public static SharedPrinter Shared(
        string windowsName = "Fake Printer",
        string? slug = null,
        string displayName = "Printman - Fake Printer") => new()
    {
        WindowsName = windowsName,
        Slug = slug ?? Slugify(windowsName),
        DisplayName = displayName,
        Uuid = Guid.Parse("11111111-2222-3333-4444-555555555555")
    };

    public static PipelineItem Item(string fileId = "abc123", PrintJobRequest? request = null) => new()
    {
        FileId = fileId,
        DisplayName = "doc.pdf",
        JobTitle = "doc"
    };

    public static PipelineBatch Batch(string? printer = "Fake Printer", params PipelineItem[] items) => new()
    {
        Printer = printer,
        Items = items.Length == 0 ? [Item()] : items,
        Source = "web"
    };

    public static PrintJobRequest Request(string filePath = "C:\\temp\\doc.pdf") => new()
    {
        FilePath = filePath,
        TargetPrinterName = "Fake Printer",
        JobTitle = "doc"
    };

    public static FileCacheResult Cached(string fileId = "abc123", string path = "C:\\temp\\abc123.pdf", long size = 10, string extension = ".pdf") =>
        new(fileId, $"doc{extension}", path, size, false, extension);

    public static string Slugify(string name) =>
        string.Join('-', name.ToLowerInvariant().Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries));
}
