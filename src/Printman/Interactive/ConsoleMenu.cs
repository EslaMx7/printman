namespace Printman.Interactive;

/// <summary>
/// Terminal single-select menu. Uses an arrow-key list on an interactive console
/// and falls back to a typed number when input or output is redirected.
/// </summary>
public static class ConsoleMenu
{
    /// <summary>
    /// Lets the user pick one option. Returns its index, or -1 when the user cancels with Esc.
    /// </summary>
    public static int PickOne(string title, IReadOnlyList<string> options, int defaultIndex = 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n{title}");
        Console.ResetColor();

        if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
        {
            try
            {
                return RunList(options, defaultIndex);
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                // Terminal resized mid-draw or does not support cursor positioning
                Console.WriteLine();
            }
        }

        return RunNumberPrompt(options, defaultIndex);
    }

    private static int RunList(IReadOnlyList<string> options, int defaultIndex)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Up/Down move   Enter select   Esc exit");
        Console.ResetColor();

        // Reserve the block (scrolling the window if needed), then draw over it in place
        for (int i = 0; i < options.Count; i++) Console.WriteLine();
        int top = Console.CursorTop - options.Count;
        if (top < 0)
        {
            throw new InvalidOperationException("Console is too small for the menu.");
        }

        int cursor = defaultIndex;
        bool cursorVisible = TrySetCursorVisible(false);

        try
        {
            while (true)
            {
                Draw(options, cursor, top);

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow or ConsoleKey.K:
                        cursor = (cursor - 1 + options.Count) % options.Count;
                        break;
                    case ConsoleKey.DownArrow or ConsoleKey.J or ConsoleKey.Tab:
                        cursor = (cursor + 1) % options.Count;
                        break;
                    case ConsoleKey.Home:
                        cursor = 0;
                        break;
                    case ConsoleKey.End:
                        cursor = options.Count - 1;
                        break;
                    case ConsoleKey.Enter:
                        EndBlock(top, options.Count);
                        return cursor;
                    case ConsoleKey.Escape:
                        EndBlock(top, options.Count);
                        return -1;
                    default:
                        // Number keys pick that option right away
                        if (key.KeyChar is >= '1' and <= '9' && key.KeyChar - '1' < options.Count)
                        {
                            cursor = key.KeyChar - '1';
                            Draw(options, cursor, top);
                            EndBlock(top, options.Count);
                            return cursor;
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

    private static void Draw(IReadOnlyList<string> options, int cursor, int top)
    {
        int width = Math.Max(20, SafeWindowWidth() - 1);
        for (int i = 0; i < options.Count; i++)
        {
            var text = $" {(i == cursor ? ">" : " ")} [{i + 1}] {options[i]}";
            if (text.Length > width) text = text[..width];

            Console.SetCursorPosition(0, top + i);
            Console.ForegroundColor = i == cursor ? ConsoleColor.Yellow : ConsoleColor.Gray;
            Console.Write(text.PadRight(width));
            Console.ResetColor();
        }
    }

    private static void EndBlock(int top, int blockHeight)
    {
        // Park on the block's last row and break the line, so later output starts below the menu
        Console.SetCursorPosition(0, top + blockHeight - 1);
        Console.WriteLine();
    }

    private static int RunNumberPrompt(IReadOnlyList<string> options, int defaultIndex)
    {
        for (int i = 0; i < options.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {options[i]}");
        }

        while (true)
        {
            Console.Write($"\nSelect an option [1-{options.Count}] (default {defaultIndex + 1}, 'q' to exit): ");
            var input = Console.ReadLine()?.Trim();
            if (input == null || input.Equals("q", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }
            if (input.Length == 0)
            {
                return defaultIndex;
            }
            if (int.TryParse(input, out int n) && n >= 1 && n <= options.Count)
            {
                return n - 1;
            }
            ConsoleUi.PrintWarning($"Invalid option. Please enter a number from 1 to {options.Count}.");
        }
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
