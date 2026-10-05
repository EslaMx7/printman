using Printman.Core.Models;

namespace Printman.Interactive;

/// <summary>
/// Terminal multi-select list of printers. Uses an arrow-key checklist on an interactive console
/// and falls back to typed numbers (e.g. "1,3") when input or output is redirected.
/// </summary>
public static class PrinterPicker
{
    /// <summary>
    /// Lets the user tick printers. Returns the selected printers (possibly empty),
    /// or null when the user cancels with Esc.
    /// </summary>
    public static IReadOnlyList<PrinterInfo>? PickMany(
        IReadOnlyList<PrinterInfo> printers,
        IEnumerable<string> preselectedNames,
        string title)
    {
        var selected = new bool[printers.Count];
        var preselected = new HashSet<string>(preselectedNames, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < printers.Count; i++)
        {
            selected[i] = preselected.Contains(printers[i].Name);
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n{title}");
        Console.ResetColor();

        bool interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        bool confirmed;
        if (interactive)
        {
            try
            {
                confirmed = RunChecklist(printers, selected);
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                // Terminal resized mid-draw or does not support cursor positioning
                Console.WriteLine();
                confirmed = RunNumberPrompt(printers, selected);
            }
        }
        else
        {
            confirmed = RunNumberPrompt(printers, selected);
        }
        if (!confirmed)
        {
            return null;
        }

        return printers.Where((_, i) => selected[i]).ToList();
    }

    private static bool RunChecklist(IReadOnlyList<PrinterInfo> printers, bool[] selected)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Up/Down move   Space select   A all/none   Enter confirm   Esc cancel");
        Console.ResetColor();

        // The list scrolls inside the visible window: absolute cursor positions are only valid
        // within the console buffer, which can be as small as the window (Windows Terminal, VS Code)
        int visibleRows = Math.Min(printers.Count, Math.Max(3, SafeWindowHeight() - 4));
        int blockHeight = visibleRows + 1; // + status line

        // Reserve the block (scrolling the window if needed), then draw over it in place
        for (int i = 0; i < blockHeight; i++) Console.WriteLine();
        int top = Console.CursorTop - blockHeight;
        if (top < 0)
        {
            throw new InvalidOperationException("Console is too small for the checklist.");
        }

        int cursor = Math.Max(0, Array.IndexOf(selected, true));
        int offset = 0;
        bool cursorVisible = TrySetCursorVisible(false);

        try
        {
            while (true)
            {
                if (cursor < offset) offset = cursor;
                if (cursor >= offset + visibleRows) offset = cursor - visibleRows + 1;
                Draw(printers, selected, cursor, top, offset, visibleRows);

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow or ConsoleKey.K:
                        cursor = (cursor - 1 + printers.Count) % printers.Count;
                        break;
                    case ConsoleKey.DownArrow or ConsoleKey.J or ConsoleKey.Tab:
                        cursor = (cursor + 1) % printers.Count;
                        break;
                    case ConsoleKey.Home:
                        cursor = 0;
                        break;
                    case ConsoleKey.End:
                        cursor = printers.Count - 1;
                        break;
                    case ConsoleKey.Spacebar:
                        selected[cursor] = !selected[cursor];
                        break;
                    case ConsoleKey.A:
                        bool all = selected.All(s => s);
                        Array.Fill(selected, !all);
                        break;
                    case ConsoleKey.Enter:
                        EndBlock(top, blockHeight);
                        return true;
                    case ConsoleKey.Escape:
                        EndBlock(top, blockHeight);
                        return false;
                    default:
                        // Number keys jump to and toggle that printer
                        if (key.KeyChar is >= '1' and <= '9' && key.KeyChar - '1' < printers.Count)
                        {
                            cursor = key.KeyChar - '1';
                            selected[cursor] = !selected[cursor];
                        }
                        break;
                }
            }
        }
        finally
        {
            if (cursorVisible) TrySetCursorVisible(true);
        }
    }

    private static void Draw(IReadOnlyList<PrinterInfo> printers, bool[] selected, int cursor, int top, int offset, int visibleRows)
    {
        int width = Math.Max(20, SafeWindowWidth() - 1);
        for (int row = 0; row < visibleRows; row++)
        {
            int i = offset + row;
            var p = printers[i];
            var marker = i == cursor ? ">" : " ";
            var box = selected[i] ? "[x]" : "[ ]";
            var tags = p.IsDefault ? "  (default)" : "";
            WriteRow(top + row, $" {marker} {box} {p.Name}{tags}", width,
                i == cursor ? ConsoleColor.Yellow : selected[i] ? ConsoleColor.Green : ConsoleColor.Gray);
        }

        int count = selected.Count(s => s);
        var status = $"   {count} selected";
        if (visibleRows < printers.Count)
        {
            status += $"   (showing {offset + 1}-{offset + visibleRows} of {printers.Count}, scroll for more)";
        }
        WriteRow(top + visibleRows, status, width, ConsoleColor.DarkGray);
    }

    private static void WriteRow(int y, string text, int width, ConsoleColor color)
    {
        Console.SetCursorPosition(0, y);
        if (text.Length > width) text = text[..width];
        Console.ForegroundColor = color;
        Console.Write(text.PadRight(width));
        Console.ResetColor();
    }

    private static void EndBlock(int top, int blockHeight)
    {
        // Park on the block's last row and break the line, so later output starts below the list
        Console.SetCursorPosition(0, top + blockHeight - 1);
        Console.WriteLine();
    }

    private static int SafeWindowHeight()
    {
        try { return Console.WindowHeight; } catch { return 25; }
    }

    private static bool RunNumberPrompt(IReadOnlyList<PrinterInfo> printers, bool[] selected)
    {
        for (int i = 0; i < printers.Count; i++)
        {
            var tags = printers[i].IsDefault ? " (default)" : "";
            Console.WriteLine($"  [{i + 1}] {(selected[i] ? "[x]" : "[ ]")} {printers[i].Name}{tags}");
        }

        Console.Write("Printer numbers to share, e.g. 1,3 ('all', Enter to keep [x] marks, 'q' to cancel): ");
        var input = Console.ReadLine()?.Trim();
        if (input == null || input.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (input.Length == 0)
        {
            return true;
        }
        if (input.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            Array.Fill(selected, true);
            return true;
        }

        Array.Fill(selected, false);
        foreach (var part in input.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int n) && n >= 1 && n <= printers.Count)
            {
                selected[n - 1] = true;
            }
            else
            {
                ConsoleUi.PrintWarning($"Ignoring '{part}': not a printer number.");
            }
        }
        return true;
    }

    private static int SafeWindowWidth()
    {
        try { return Console.WindowWidth; } catch { return 80; }
    }

    private static bool TrySetCursorVisible(bool visible)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            bool previous = Console.CursorVisible;
            Console.CursorVisible = visible;
            return previous;
        }
        catch
        {
            return false;
        }
    }
}
