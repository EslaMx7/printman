using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Printman.Services.Renderers;

/// <summary>
/// Dependency-free PDF page counter: finds the page tree root (a /Type /Pages dictionary without /Parent)
/// in the file body and in Flate-compressed object streams, and reads its /Count.
/// Used where no PDF engine is available (Linux / macOS, where CUPS does the rendering).
/// </summary>
public static class PdfPageCounter
{
    private const long MaxBytes = 512L * 1024 * 1024;
    private const int MaxInflatedBytes = 64 * 1024 * 1024;

    private static readonly Regex PagesTypeRegex = new(@"/Type\s*/Pages(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly Regex ObjStmTypeRegex = new(@"/Type\s*/ObjStm(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly Regex CountRegex = new(@"/Count\s+(\d+)", RegexOptions.Compiled);
    private static readonly Regex ParentRegex = new(@"/Parent\s+\d+\s+\d+\s+R", RegexOptions.Compiled);
    private static readonly Regex DirectLengthRegex = new(@"/Length\s+(\d+)(?!\s+\d+\s+R)", RegexOptions.Compiled);
    private static readonly Regex LinearizedRegex = new(@"/Linearized\s+[\d.]+[^>]*?/N\s+(\d+)", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Page count, or null when it cannot be determined (e.g. encrypted object streams).</summary>
    public static int? TryCount(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length > MaxBytes) return null;

        // Latin-1 maps every byte to one char, so string offsets equal byte offsets
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(filePath));
        if (!text.StartsWith("%PDF", StringComparison.Ordinal) && text.IndexOf("%PDF", 0, Math.Min(text.Length, 1024), StringComparison.Ordinal) < 0)
        {
            return null;
        }

        // Later objects win: incremental updates append the current page tree at the end
        int? count = null;
        int countPosition = -1;

        void Consider(string source, int position)
        {
            var root = FindRootCount(source);
            if (root is int c && position >= countPosition)
            {
                count = c;
                countPosition = position;
            }
        }

        Consider(text, 0);

        foreach (Match m in ObjStmTypeRegex.Matches(text))
        {
            var inflated = InflateStreamOf(text, m.Index);
            if (inflated != null)
            {
                Consider(inflated, m.Index);
            }
        }

        if (count is > 0) return count;

        // Linearized files state the page count in their first object
        var lin = LinearizedRegex.Match(text, 0, Math.Min(text.Length, 4096));
        if (lin.Success && int.TryParse(lin.Groups[1].Value, out int n) && n > 0) return n;

        return null;
    }

    /// <summary>/Count of the last page tree root (Pages dictionary without /Parent) in <paramref name="source"/>.</summary>
    private static int? FindRootCount(string source)
    {
        int? result = null;
        foreach (Match m in PagesTypeRegex.Matches(source))
        {
            var dict = EnclosingDictionary(source, m.Index)?.Text;
            if (dict == null || ParentRegex.IsMatch(dict)) continue;

            var c = CountRegex.Match(dict);
            if (c.Success && int.TryParse(c.Groups[1].Value, out int value))
            {
                result = value;
            }
        }
        return result;
    }

    /// <summary>Start offset and text of the innermost &lt;&lt; ... &gt;&gt; dictionary containing <paramref name="index"/>.</summary>
    private static (int Start, string Text)? EnclosingDictionary(string s, int index)
    {
        int depth = 0;
        int start = -1;
        for (int i = index - 1; i > 0; i--)
        {
            if (s[i] == '>' && s[i - 1] == '>') { depth++; i--; }
            else if (s[i] == '<' && s[i - 1] == '<')
            {
                if (depth == 0) { start = i - 1; break; }
                depth--;
                i--;
            }
        }
        if (start < 0) return null;

        depth = 0;
        for (int i = start; i < s.Length - 1; i++)
        {
            if (s[i] == '<' && s[i + 1] == '<') { depth++; i++; }
            else if (s[i] == '>' && s[i + 1] == '>')
            {
                depth--;
                i++;
                if (depth == 0) return (start, s.Substring(start, i + 1 - start));
            }
        }
        return null;
    }

    /// <summary>Inflates the FlateDecode stream that follows the dictionary containing <paramref name="index"/>.</summary>
    private static string? InflateStreamOf(string s, int index)
    {
        if (EnclosingDictionary(s, index) is not (int dictStart, string dict) ||
            !dict.Contains("/FlateDecode", StringComparison.Ordinal))
        {
            return null;
        }

        int keyword = s.IndexOf("stream", dictStart + dict.Length, StringComparison.Ordinal);
        if (keyword < 0 || keyword - (dictStart + dict.Length) > 16) return null;

        int dataStart = keyword + "stream".Length;
        if (dataStart < s.Length && s[dataStart] == '\r') dataStart++;
        if (dataStart < s.Length && s[dataStart] == '\n') dataStart++;

        int dataEnd;
        var len = DirectLengthRegex.Match(dict);
        if (len.Success && int.TryParse(len.Groups[1].Value, out int length) && dataStart + length <= s.Length)
        {
            dataEnd = dataStart + length;
        }
        else
        {
            dataEnd = s.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (dataEnd < 0) return null;
        }

        try
        {
            var bytes = Encoding.Latin1.GetBytes(s.Substring(dataStart, dataEnd - dataStart));
            using var zlib = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxInflatedBytes) return null;
            }
            return Encoding.Latin1.GetString(output.GetBuffer(), 0, (int)output.Length);
        }
        catch (InvalidDataException)
        {
            return null; // encrypted or damaged stream
        }
    }
}
