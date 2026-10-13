using Printman.Core.Models;

namespace Printman.Services.Cups;

/// <summary>
/// Startup check for Linux / macOS: every command except help and version needs a running cupsd.
/// Explains how to install or start it (per package manager) and offers a retry on a terminal.
/// </summary>
public static class CupsReadiness
{
    /// <summary>Commands that work without CUPS.</summary>
    public static bool Needs(CliCommandType command) => command is not (CliCommandType.Help or CliCommandType.Version);

    /// <summary>Package managers and the command that installs CUPS with them (first match on PATH wins).</summary>
    private static readonly (string Tool, string Install)[] PackageManagers =
    [
        ("apt-get", "sudo apt install cups cups-filters"),
        ("dnf", "sudo dnf install cups cups-filters"),
        ("yum", "sudo yum install cups cups-filters"),
        ("pacman", "sudo pacman -S cups cups-filters"),
        ("zypper", "sudo zypper install cups cups-filters"),
        ("apk", "sudo apk add cups cups-filters")
    ];

    /// <summary>
    /// Returns null when cupsd answers. Otherwise prints what is wrong and how to fix it; on an interactive
    /// terminal the user can retry after fixing it. Returns the exit code to end the process with when it gives up.
    /// </summary>
    public static async Task<int?> EnsureAsync(
        CupsClient cups,
        IExternalTools tools,
        Func<string?> readLine,
        TextWriter output,
        bool interactive,
        CancellationToken ct = default)
    {
        while (true)
        {
            var problem = await cups.ProbeAsync(ct);
            if (problem == null)
            {
                return null;
            }

            foreach (var line in Explain(problem, cups.Endpoint, OperatingSystem.IsMacOS(), tools.Find("cupsd") != null, DetectInstallCommand(tools)))
            {
                output.WriteLine(line);
            }

            if (!interactive)
            {
                return 1;
            }

            output.Write("Press Enter to retry once CUPS is running, or type q to quit: ");
            var answer = readLine()?.Trim();
            if (answer == null || answer.Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }
            output.WriteLine();
        }
    }

    /// <summary>The install command for the first package manager found on PATH, or null (e.g. macOS).</summary>
    public static string? DetectInstallCommand(IExternalTools tools)
    {
        foreach (var (tool, install) in PackageManagers)
        {
            if (tools.Find(tool) != null) return install;
        }
        return null;
    }

    /// <summary>Human readable diagnosis: what failed, then the steps for this OS.</summary>
    public static IReadOnlyList<string> Explain(string problem, CupsEndpoint endpoint, bool macOS, bool cupsdInstalled, string? installCommand)
    {
        var lines = new List<string>
        {
            $"[ERROR] {problem}",
            "",
            "Printman prints through CUPS on this system, and CUPS is not answering."
        };

        if (macOS)
        {
            // CUPS ships with macOS; it only needs to be running
            lines.Add("CUPS is part of macOS. Start it with:");
            lines.Add("    sudo launchctl load -w /System/Library/LaunchDaemons/org.cups.cupsd.plist");
            lines.Add("then add your printer in System Settings > Printers & Scanners.");
        }
        else if (cupsdInstalled)
        {
            lines.Add("CUPS is installed but the service is not running. Start it with:");
            lines.Add("    sudo systemctl enable --now cups");
        }
        else
        {
            lines.Add("Install CUPS and start its service:");
            lines.Add($"    {installCommand ?? "<install the 'cups' and 'cups-filters' packages with your package manager>"}");
            lines.Add("    sudo systemctl enable --now cups");
            lines.Add("then add your printer (desktop printer settings, or http://localhost:631).");
        }

        if (endpoint.ViaSocket == false && (endpoint.Host != "localhost" || endpoint.Port != 631))
        {
            lines.Add($"CUPS_SERVER points at {endpoint}; unset it to use the local CUPS service.");
        }

        lines.Add("");
        return lines;
    }
}
