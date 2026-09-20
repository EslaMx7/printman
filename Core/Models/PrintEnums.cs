namespace OhMyPrinter.Core.Models;

public enum PrintOrientation
{
    Auto,
    Portrait,
    Landscape
}

public enum PrintDuplex
{
    Default,
    Simplex,
    Horizontal, // Two-sided short edge
    Vertical    // Two-sided long edge
}

public enum PrintColorMode
{
    Default,
    Color,
    Monochrome
}

public enum CliCommandType
{
    Print,
    ListPrinters,
    PrinterInfo,
    Help,
    Interactive,
    Server
}
