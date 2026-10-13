using System.Diagnostics;

namespace Printman.Services.Cups;

public sealed record ExternalToolResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs standard command-line tools (cupsfilter, pdfinfo, qpdf). Injected so tests can substitute a fake.
/// </summary>
public interface IExternalTools
{
    /// <summary>Full path of <paramref name="name"/> on PATH or in the usual sbin folders, or null.</summary>
    string? Find(string name);

    /// <summary>
    /// Runs the tool with an argument list (never a shell string). Standard output goes to
    /// <paramref name="stdoutFile"/> when given (binary safe), otherwise it is captured.
    /// </summary>
    Task<ExternalToolResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? stdoutFile = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}

public sealed class ExternalTool : IExternalTools
{
    private static readonly string[] ExtraSearchPaths = ["/usr/sbin", "/usr/local/sbin", "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin"];

    public string? Find(string name) =>
        Find(name, Environment.GetEnvironmentVariable("PATH"), File.Exists);

    public static string? Find(string name, string? path, Func<string, bool> fileExists)
    {
        var dirs = (path ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(ExtraSearchPaths);

        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, name);
            if (fileExists(candidate)) return candidate;
        }
        return null;
    }

    public async Task<ExternalToolResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? stdoutFile = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        psi.Environment["LC_ALL"] = "C"; // stable, untranslated messages

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));

        try
        {
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            string stdout = "";
            if (stdoutFile != null)
            {
                await using var file = File.Create(stdoutFile);
                await process.StandardOutput.BaseStream.CopyToAsync(file, timeoutCts.Token);
            }
            else
            {
                stdout = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            }

            await process.WaitForExitAsync(timeoutCts.Token);
            return new ExternalToolResult(process.ExitCode, stdout, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }
}
