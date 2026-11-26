namespace Printman.Core.Models;

/// <summary>
/// Live runtime snapshot of a started Printman web and/or network sharing server.
/// </summary>
public sealed record ServerRuntimeInfo
{
    public bool WebUiEnabled { get; init; }
    public int WebPort { get; init; }
    public string BindAddress { get; init; } = "0.0.0.0";
    public bool RequireAuth { get; init; }
    public string? Pin { get; init; }
    public string? LocalUrl { get; init; }
    public IReadOnlyList<string> LanUrls { get; init; } = [];
    public IReadOnlyList<string> LanIps { get; init; } = [];
    public bool SharingEnabled { get; init; }
    public int IppPort { get; init; }
    public bool MdnsActive { get; init; }
    public string? MdnsHostName { get; init; }
    public IReadOnlyList<SharedPrinterEndpoint> SharedPrinters { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record SharedPrinterEndpoint
{
    public required string SystemName { get; init; }
    public required string DisplayName { get; init; }
    public required string ResourcePath { get; init; }
    public IReadOnlyList<string> IppUrls { get; init; } = [];
}
