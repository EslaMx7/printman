using Printman.Core.Models;

namespace Printman.CLI;

public static class CommandLineParser
{
    public static ParsedArguments Parse(string[] args)
    {
        var result = new ParsedArguments();

        if (args.Length == 0)
        {
            result.Command = CliCommandType.Interactive;
            return result;
        }

        // Check first argument for commands
        var firstArg = args[0].Trim();
        if (firstArg.Equals("help", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("/?", StringComparison.OrdinalIgnoreCase))
        {
            result.Command = CliCommandType.Help;
            return result;
        }

        if (firstArg.Equals("list", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("-list", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("--list", StringComparison.OrdinalIgnoreCase))
        {
            result.Command = CliCommandType.ListPrinters;
            return result;
        }

        if (firstArg.Equals("info", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("-info", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("--info", StringComparison.OrdinalIgnoreCase))
        {
            result.Command = CliCommandType.PrinterInfo;
            result.QueryTarget = args.Length > 1 ? args[1] : null;
            return result;
        }

        if (firstArg.Equals("interactive", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("-i", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("--interactive", StringComparison.OrdinalIgnoreCase))
        {
            result.Command = CliCommandType.Interactive;
            return result;
        }

        if (firstArg.Equals("server", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("serve", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("--server", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("--serve", StringComparison.OrdinalIgnoreCase))
        {
            result.Command = CliCommandType.Server;
            for (int j = 1; j < args.Length; j++)
            {
                var serverArg = args[j].Trim();
                string key = serverArg;
                string? value = null;
                int eq = serverArg.IndexOf('=');
                if (eq > 0)
                {
                    key = serverArg[..eq];
                    value = serverArg[(eq + 1)..].Trim('"', '\'');
                }

                switch (key.ToLowerInvariant())
                {
                    case "--port" or "-port" or "-p":
                        var portVal = value ?? (j + 1 < args.Length ? args[++j].Trim('"', '\'') : null);
                        if (int.TryParse(portVal, out int p) && p > 0 && p <= 65535)
                        {
                            result.ServerPort = p;
                        }
                        else
                        {
                            throw new FormatException($"Invalid port number: '{portVal}'. Must be an integer between 1 and 65535.");
                        }
                        break;

                    case "--ip" or "-ip" or "--bind" or "-bind":
                        var ipVal = value ?? (j + 1 < args.Length ? args[++j].Trim('"', '\'') : null);
                        if (!string.IsNullOrWhiteSpace(ipVal))
                        {
                            result.BindAddress = ipVal;
                        }
                        break;

                    case "--pin" or "-pin":
                        var pinVal = value ?? (j + 1 < args.Length ? args[++j].Trim('"', '\'') : null);
                        if (!string.IsNullOrWhiteSpace(pinVal))
                        {
                            result.ServerPin = pinVal;
                            result.RequireAuth = true;
                        }
                        break;

                    case "--no-auth" or "-no-auth" or "--allow-anonymous" or "-allow-anonymous":
                        result.RequireAuth = false;
                        break;

                    case "--max-upload-mb" or "-max-upload-mb":
                        var maxMbVal = value ?? (j + 1 < args.Length ? args[++j].Trim('"', '\'') : null);
                        if (int.TryParse(maxMbVal, out int maxMb) && maxMb > 0)
                        {
                            result.MaxUploadMb = maxMb;
                        }
                        else
                        {
                            throw new FormatException($"Invalid max upload MB: '{maxMbVal}'. Must be an integer > 0.");
                        }
                        break;

                    case "--cache-limit-mb" or "-cache-limit-mb":
                        var cacheMbVal = value ?? (j + 1 < args.Length ? args[++j].Trim('"', '\'') : null);
                        if (int.TryParse(cacheMbVal, out int cacheMb) && cacheMb > 0)
                        {
                            result.CacheLimitMb = cacheMb;
                        }
                        else
                        {
                            throw new FormatException($"Invalid cache limit MB: '{cacheMbVal}'. Must be an integer > 0.");
                        }
                        break;

                    case "--share" or "-share":
                        // Optional value: "--share" alone shares the default printer; repeat to share several
                        result.SharePrinters = true;
                        var shareVal = value;
                        if (shareVal == null && j + 1 < args.Length && !args[j + 1].StartsWith('-'))
                        {
                            shareVal = args[++j].Trim('"', '\'');
                        }
                        if (!string.IsNullOrWhiteSpace(shareVal) &&
                            !result.SharedPrinterNames.Contains(shareVal, StringComparer.OrdinalIgnoreCase))
                        {
                            result.SharedPrinterNames.Add(shareVal);
                        }
                        break;

                    case "--ipp-port" or "-ipp-port":
                        var ippPortVal = value ?? (j + 1 < args.Length ? args[++j].Trim('"', '\'') : null);
                        if (int.TryParse(ippPortVal, out int ippPort) && ippPort > 0 && ippPort <= 65535)
                        {
                            result.IppPort = ippPort;
                        }
                        else
                        {
                            throw new FormatException($"Invalid IPP port number: '{ippPortVal}'. Must be an integer between 1 and 65535.");
                        }
                        break;

                    case "--no-mdns" or "-no-mdns":
                        result.EnableMdns = false;
                        break;

                    case "--ipp-allow-any-source" or "-ipp-allow-any-source":
                        result.IppAllowAnySource = true;
                        break;

                    case "--output-dir" or "-output-dir":
                        var outDirVal = value ?? (j + 1 < args.Length ? args[++j].Trim('"', '\'') : null);
                        if (!string.IsNullOrWhiteSpace(outDirVal))
                        {
                            result.ServerOutputDirectory = outDirVal;
                        }
                        break;
                }
            }
            return result;
        }

        if (firstArg.Equals("queue", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("q", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("jobs", StringComparison.OrdinalIgnoreCase))
        {
            result.Command = CliCommandType.Queue;
            for (int j = 1; j < args.Length; j++)
            {
                var qArg = args[j].Trim();
                if (qArg.Equals("--watch", StringComparison.OrdinalIgnoreCase) ||
                    qArg.Equals("-w", StringComparison.OrdinalIgnoreCase))
                {
                    result.WatchQueue = true;
                }
                else if (qArg.StartsWith("-printer", StringComparison.OrdinalIgnoreCase) ||
                         qArg.StartsWith("--printer", StringComparison.OrdinalIgnoreCase) ||
                         qArg.StartsWith("-p", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = qArg.IndexOf('=');
                    if (eq > 0)
                    {
                        result.TargetPrinterName = qArg[(eq + 1)..].Trim('"', '\'');
                    }
                    else if (j + 1 < args.Length)
                    {
                        result.TargetPrinterName = args[++j].Trim('"', '\'');
                    }
                }
                else if (!qArg.StartsWith('-') && string.IsNullOrWhiteSpace(result.TargetPrinterName))
                {
                    result.TargetPrinterName = qArg.Trim('"', '\'');
                }
            }
            return result;
        }

        if (firstArg.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("abort", StringComparison.OrdinalIgnoreCase))
        {
            result.Command = CliCommandType.CancelJob;
            for (int j = 1; j < args.Length; j++)
            {
                var cArg = args[j].Trim();
                if (cArg.StartsWith("-printer", StringComparison.OrdinalIgnoreCase) ||
                    cArg.StartsWith("--printer", StringComparison.OrdinalIgnoreCase) ||
                    cArg.StartsWith("-p", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = cArg.IndexOf('=');
                    if (eq > 0)
                    {
                        result.TargetPrinterName = cArg[(eq + 1)..].Trim('"', '\'');
                    }
                    else if (j + 1 < args.Length)
                    {
                        result.TargetPrinterName = args[++j].Trim('"', '\'');
                    }
                }
                else if (!cArg.StartsWith('-') && string.IsNullOrWhiteSpace(result.JobId))
                {
                    result.JobId = cArg.Trim('"', '\'');
                }
            }
            return result;
        }

        if (firstArg.Equals("purge", StringComparison.OrdinalIgnoreCase) ||
            firstArg.Equals("clear-queue", StringComparison.OrdinalIgnoreCase))
        {
            result.Command = CliCommandType.PurgeQueue;
            for (int j = 1; j < args.Length; j++)
            {
                var pArg = args[j].Trim();
                if (pArg.StartsWith("-printer", StringComparison.OrdinalIgnoreCase) ||
                    pArg.StartsWith("--printer", StringComparison.OrdinalIgnoreCase) ||
                    pArg.StartsWith("-p", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = pArg.IndexOf('=');
                    if (eq > 0)
                    {
                        result.TargetPrinterName = pArg[(eq + 1)..].Trim('"', '\'');
                    }
                    else if (j + 1 < args.Length)
                    {
                        result.TargetPrinterName = args[++j].Trim('"', '\'');
                    }
                }
                else if (!pArg.StartsWith('-') && string.IsNullOrWhiteSpace(result.TargetPrinterName))
                {
                    result.TargetPrinterName = pArg.Trim('"', '\'');
                }
            }
            return result;
        }

        // Otherwise, assume it's a Print command with flags and/or a file path
        result.Command = CliCommandType.Print;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();

            // Handle Key=Value style arguments (e.g. -size=A4 or --printer="HP Laser")
            string key = arg;
            string? value = null;
            int equalIndex = arg.IndexOf('=');
            if (equalIndex > 0)
            {
                key = arg[..equalIndex];
                value = arg[(equalIndex + 1)..].Trim('"', '\'');
            }

            switch (key.ToLowerInvariant())
            {
                case "-printer" or "--printer" or "-p":
                    result.TargetPrinterName = value ?? GetNextValue(args, ref i, "printer name");
                    break;

                case "-pages" or "--pages":
                    var pagesExp = value ?? GetNextValue(args, ref i, "page range (e.g. 1:3)");
                    result.PageRange = PageRange.Parse(pagesExp);
                    break;

                case "-size" or "--size" or "-s":
                    result.PaperSizeName = value ?? GetNextValue(args, ref i, "paper size (e.g. A4, Letter)");
                    break;

                case "-copies" or "--copies" or "-c":
                    var copiesStr = value ?? GetNextValue(args, ref i, "copies count");
                    if (int.TryParse(copiesStr, out int c) && c > 0)
                    {
                        result.Copies = c;
                    }
                    else
                    {
                        throw new FormatException($"Invalid number of copies: '{copiesStr}'. Must be an integer >= 1.");
                    }
                    break;

                case "-orientation" or "--orientation" or "-o":
                    var orientStr = (value ?? GetNextValue(args, ref i, "orientation")).ToLowerInvariant();
                    result.Orientation = orientStr switch
                    {
                        "portrait" or "p" => PrintOrientation.Portrait,
                        "landscape" or "l" => PrintOrientation.Landscape,
                        _ => PrintOrientation.Auto
                    };
                    break;

                case "-duplex" or "--duplex" or "-d":
                    var duplexStr = (value ?? GetNextValue(args, ref i, "duplex mode")).ToLowerInvariant();
                    result.Duplex = duplexStr switch
                    {
                        "simplex" or "single" or "1" => PrintDuplex.Simplex,
                        "vertical" or "long" or "2" => PrintDuplex.Vertical,
                        "horizontal" or "short" => PrintDuplex.Horizontal,
                        _ => PrintDuplex.Default
                    };
                    break;

                case "-color" or "--color":
                    var colorStr = (value ?? GetNextValue(args, ref i, "color mode")).ToLowerInvariant();
                    result.ColorMode = colorStr switch
                    {
                        "color" => PrintColorMode.Color,
                        "mono" or "monochrome" or "bw" or "grayscale" => PrintColorMode.Monochrome,
                        _ => PrintColorMode.Default
                    };
                    break;

                case "-dpi" or "--dpi":
                    var dpiStr = value ?? GetNextValue(args, ref i, "DPI value");
                    if (int.TryParse(dpiStr, out int dpi) && dpi > 0)
                    {
                        result.Dpi = dpi;
                    }
                    break;

                case "-output" or "--output" or "-out":
                    result.OutputFilePath = value ?? GetNextValue(args, ref i, "output file path");
                    break;

                case "-fit" or "--fit":
                    result.FitToPage = true;
                    break;

                case "-nofit" or "--nofit":
                    result.FitToPage = false;
                    break;

                default:
                    if (!arg.StartsWith('-') && !arg.StartsWith('/'))
                    {
                        // Positional file path
                        result.FilePath = arg.Trim('"', '\'');
                    }
                    else
                    {
                        throw new ArgumentException($"Unrecognized command-line argument: '{arg}'. Run 'printman --help' for usage.");
                    }
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(result.FilePath))
        {
            throw new ArgumentException("No document file path was provided. Specify a file to print or run without arguments for interactive mode.");
        }

        return result;
    }

    private static string GetNextValue(string[] args, ref int index, string expectedDescription)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            throw new ArgumentException($"Missing required value for {expectedDescription} after '{args[index]}'.");
        }
        index++;
        return args[index].Trim('"', '\'');
    }
}
