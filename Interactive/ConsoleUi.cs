using OhMyPrinter.Core.Models;

namespace OhMyPrinter.Interactive;

public static class ConsoleUi
{
    public static void ShowBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   ____  __       __  ___           ____       _       __           
  / __ \/ /_     /  |/  /_  __     / __ \_____(_)___  / /____  _____
 / / / / __ \   / /|_/ / / / /    / /_/ / ___/ / __ \/ __/ _ \/ ___/
/ /_/ / / / /  / /  / / /_/ /    / ____/ /  / / / / / /_/  __/ /    
\____/_/ /_/  /_/  /_/\__, /    /_/   /_/  /_/_/ /_/\__/\___/_/     
                     /____/                                         ");
        Console.ResetColor();
        Console.WriteLine(" Universal CLI & Interactive Windows Printing Engine");
        Console.WriteLine(" ----------------------------------------------------");
    }

    public static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[SUCCESS] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    public static void PrintInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("[INFO] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    public static void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("[WARN] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    public static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[ERROR] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    public static void PrintPrintersTable(IReadOnlyList<PrinterInfo> printers)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("{0,-4} {1,-38} {2,-9} {3,-8} {4,-7} {5,-8}", "#", "Printer Name", "Default", "Duplex", "Color", "Papers");
        Console.WriteLine(new string('-', 80));
        Console.ResetColor();

        for (int i = 0; i < printers.Count; i++)
        {
            var p = printers[i];
            if (p.IsDefault)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }

            Console.WriteLine(
                "{0,-4} {1,-38} {2,-9} {3,-8} {4,-7} {5,-8}",
                i + 1,
                Truncate(p.Name, 37),
                p.IsDefault ? "YES" : "-",
                p.CanDuplex ? "Yes" : "No",
                p.SupportsColor ? "Yes" : "Mono",
                $"{p.SupportedPaperSizes.Count} sizes");

            Console.ResetColor();
        }
        Console.WriteLine();
    }

    public static void PrintPrinterDetails(PrinterInfo printer)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Printer Details: {printer.Name} ===");
        Console.ResetColor();

        Console.WriteLine($"Default Printer:    {(printer.IsDefault ? "Yes" : "No")}");
        Console.WriteLine($"Duplex Printing:    {(printer.CanDuplex ? "Supported" : "Not supported")}");
        Console.WriteLine($"Color Printing:     {(printer.SupportsColor ? "Supported" : "Monochrome only")}");

        if (printer.SupportedResolutions.Count > 0)
        {
            Console.WriteLine($"Resolutions:        {string.Join(", ", printer.SupportedResolutions.Take(5))}");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"Supported Paper Sizes ({printer.SupportedPaperSizes.Count}):");
        Console.ResetColor();

        foreach (var size in printer.SupportedPaperSizes)
        {
            Console.WriteLine($"  - {size.Name,-25} ({size.WidthMm:F0} x {size.HeightMm:F0} mm)");
        }
        Console.WriteLine();
    }

    public static void PrintHelp()
    {
        ShowBanner();
        Console.WriteLine(@"
USAGE:
  ohmyprinter <file-path> [options]
  ohmyprinter <command> [arguments]

COMMANDS:
  list, -list, --list           List all installed printers and their status
  info <printer-name>           Show details & supported paper sizes for a printer
  interactive, -i               Launch the interactive printing wizard
  help, -h, --help              Display this help reference

PRINT OPTIONS:
  -printer, -p <name>           Target printer name or substring (e.g. -printer ""HP Laser"")
  -pages <range>                Pages to print (e.g. 1:3, 1-3, 1,3,5, 2-, -4, all)
  -size, -s <paper>             Paper size preference (e.g. A4, Letter, Legal, A3)
  -copies, -c <n>               Number of copies to print (default: 1)
  -orientation, -o <p|l>        Orientation: portrait (p), landscape (l), auto
  -duplex, -d <mode>            Duplex mode: simplex, vertical (long-edge), horizontal (short-edge)
  -color <color|mono>           Color mode preference (default: printer default)
  -dpi <number>                 Rasterization resolution for PDF/images (default: 300)
  -fit / -nofit                 Scale to fit page margins (default: enabled)

SUPPORTED FILE TYPES:
  PDF documents:                .pdf (Native high-resolution WinRT engine)
  Images:                       .png, .jpg, .jpeg, .bmp, .gif, .tiff
  Text / Code files:            .txt, .log, .csv, .json, .md, .xml, .yaml

EXAMPLES:
  ohmyprinter.exe ""./doc.pdf""
      Prints entire PDF to the default printer.

  ohmyprinter.exe ""./doc.pdf"" -printer ""HP Laser""
      Finds printer matching 'HP Laser' and prints whole document.

  ohmyprinter.exe ""./doc.pdf"" -pages 1:3
      Prints pages 1 through 3 to the default printer.

  ohmyprinter.exe ""./doc.pdf"" -size A4
      Prints whole document using A4 paper size.

  ohmyprinter.exe ""./invoice.pdf"" -p ""HP"" -pages 1:2 -size A4 -copies 2 -duplex vertical
      Full featured print job: prints 2 copies of pages 1-2 on A4 double-sided.

  ohmyprinter.exe list
      Lists all detected printers on this machine.

  ohmyprinter.exe info ""HP LaserJet""
      Shows paper trays, duplex support, and capabilities of the printer.
");
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
