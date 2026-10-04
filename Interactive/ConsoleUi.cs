using Printman.Core.Models;

namespace Printman.Interactive;

public static class ConsoleUi
{
    public static void ShowBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   ____       _       __                             
  / __ \_____(_)___  / /_____ ___  ____ _____        
 / /_/ / ___/ / __ \/ __/ __ `__ \/ __ `/ __ \       
/ ____/ /  / / / / / /_/ / / / / / /_/ / / / /       
/_/   /_/  /_/_/ /_/\__/_/ /_/ /_/\__,_/_/ /_/       ");
        Console.ResetColor();
        Console.WriteLine(" Print from your phone to any Windows printer over Wi-Fi");
        Console.WriteLine(" -------------------------------------------------------");
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

    public static void PrintPrinterStatus(PrinterStatusInfo status)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"[PRINTER STATUS] {status.PrinterName}: ");
        if (status.HasError || !status.IsOnline || status.IsPaperJam || status.IsOutOfPaper)
        {
            Console.ForegroundColor = ConsoleColor.Red;
        }
        else if (status.IsPaused)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
        }
        else if (status.IsBusy)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
        }
        Console.WriteLine(status.StatusText);
        Console.ResetColor();
        Console.WriteLine($"Jobs in spooler: {status.QueuedJobCount}");
    }

    public static void PrintQueueTable(IReadOnlyList<PrintJobInfo> jobs, PrinterStatusInfo? status = null)
    {
        if (status != null)
        {
            PrintPrinterStatus(status);
        }

        Console.WriteLine();
        if (jobs.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Print queue is empty. No jobs are currently pending or printing.");
            Console.ResetColor();
            Console.WriteLine();
            return;
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("{0,-9} {1,-28} {2,-18} {3,-10} {4,-15} {5,-10}", "Job ID", "Document", "Status", "Pages", "Printer", "Submitted");
        Console.WriteLine(new string('-', 96));
        Console.ResetColor();

        foreach (var job in jobs)
        {
            string idStr = job.IsPrintmanPipelineJob ? "Spooling" : $"#{job.JobId}";
            string pagesStr = job.TotalPages > 0 ? $"{job.PagesPrinted}/{job.TotalPages}" : $"{job.PagesPrinted}";

            switch (job.StatusCode)
            {
                case PrintJobStatusCode.Printing:
                    Console.ForegroundColor = ConsoleColor.Green;
                    break;
                case PrintJobStatusCode.Spooling:
                case PrintJobStatusCode.Queued:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    break;
                case PrintJobStatusCode.Paused:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case PrintJobStatusCode.Error:
                case PrintJobStatusCode.PaperJam:
                case PrintJobStatusCode.PaperOut:
                case PrintJobStatusCode.Offline:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
            }

            Console.WriteLine(
                "{0,-9} {1,-28} {2,-18} {3,-10} {4,-15} {5,-10}",
                idStr,
                Truncate(job.DocumentName, 27),
                Truncate(job.StatusDescription, 17),
                pagesStr,
                Truncate(job.PrinterName, 14),
                job.SubmittedAt.ToString("HH:mm:ss"));

            Console.ResetColor();
        }
        Console.WriteLine();
    }

    public static void PrintHelp()
    {
        ShowBanner();
        Console.WriteLine(@"
USAGE:
  printman <file-path> [options]
  printman <command> [arguments]

COMMANDS:
  serve, server                 Start mobile-friendly local LAN web printing server
                                Options: --port <n> (default: 5000), --ip <addr>,
                                         --pin <pin> (custom PIN; auto-generated by default),
                                         --no-auth (disable PIN requirement),
                                         --max-upload-mb <n> (default: 50),
                                         --cache-limit-mb <n> (default: 500)
                                Network printer sharing (opt-in, no PIN for native printing):
                                         --share [printer] (repeatable; default printer if no name)
                                                Shows up as ""Printman - <printer>"" in iPhone/iPad
                                                (AirPrint), Android, Windows, macOS & Linux dialogs
                                         --ipp-port <n> (default: 631), --no-mdns (no discovery)
  queue, q [printer] [--watch]  Inspect real-time Windows Spooler & pipeline queue
  cancel <job-id> [-p <name>]   Cancel a specific print job by ID
  purge [printer]               Purge / cancel all jobs on a printer queue
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
  Driverless raster:            .pwg (PWG Raster), .urf (Apple Raster / AirPrint)

EXAMPLES:
  printman.exe ""./doc.pdf""
      Prints entire PDF to the default printer.

  printman.exe ""./doc.pdf"" -printer ""HP Laser""
      Finds printer matching 'HP Laser' and prints whole document.

  printman.exe ""./doc.pdf"" -pages 1:3
      Prints pages 1 through 3 to the default printer.

  printman.exe ""./doc.pdf"" -size A4
      Prints whole document using A4 paper size.

  printman.exe ""./invoice.pdf"" -p ""HP"" -pages 1:2 -size A4 -copies 2 -duplex vertical
      Full featured print job: prints 2 copies of pages 1-2 on A4 double-sided.

  printman.exe list
      Lists all detected printers on this machine.

  printman.exe serve --share ""HP Laser"" --share ""Canon""
      Web server plus two network printers that phones and PCs find in their print dialogs.

  printman.exe info ""HP LaserJet""
      Shows paper trays, duplex support, and capabilities of the printer.
");
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
