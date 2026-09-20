using OhMyPrinter.Core.Models;

namespace OhMyPrinter.CLI;

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
                        throw new ArgumentException($"Unrecognized command-line argument: '{arg}'. Run 'ohmyprinter --help' for usage.");
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
