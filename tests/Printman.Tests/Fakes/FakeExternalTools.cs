using Printman.Services.Cups;

namespace Printman.Tests.Fakes;

/// <summary>Scripted command-line tools: which ones "exist" and what running them produces.</summary>
public sealed class FakeExternalTools : IExternalTools
{
    public Dictionary<string, string> Installed { get; } = new(StringComparer.Ordinal);
    public List<(string Executable, List<string> Arguments, string? StdoutFile)> Runs { get; } = [];

    /// <summary>Scripted outcome: exit code, stderr, and bytes written to the stdout file (when one is given).</summary>
    public int ExitCode { get; set; }
    public string StandardError { get; set; } = "";
    public string StandardOutput { get; set; } = "";
    public byte[]? StdoutFileContent { get; set; } = "%PDF-1.4 fake"u8.ToArray();

    public string? Find(string name) => Installed.TryGetValue(name, out var path) ? path : null;

    public Task<ExternalToolResult> RunAsync(string executable, IEnumerable<string> arguments, string? stdoutFile = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Runs.Add((executable, arguments.ToList(), stdoutFile));
        if (stdoutFile != null && StdoutFileContent != null)
        {
            File.WriteAllBytes(stdoutFile, StdoutFileContent);
        }
        return Task.FromResult(new ExternalToolResult(ExitCode, StandardOutput, StandardError));
    }
}
