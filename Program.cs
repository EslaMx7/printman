using Microsoft.Extensions.DependencyInjection;
using OhMyPrinter.CLI;
using OhMyPrinter.Core.Abstractions;
using OhMyPrinter.Interactive;
using OhMyPrinter.Services;
using OhMyPrinter.Services.Renderers;

namespace OhMyPrinter;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // 1. Configure Dependency Injection Container following SOLID principles
        var services = new ServiceCollection();
        ConfigureServices(services);

        using var serviceProvider = services.BuildServiceProvider();

        // 2. Dispatch between Interactive mode and CLI mode
        try
        {
            if (args.Length == 0)
            {
                var wizard = serviceProvider.GetRequiredService<InteractiveWizard>();
                await wizard.RunAsync();
                return 0;
            }

            var parsedArgs = CommandLineParser.Parse(args);

            if (parsedArgs.Command == Core.Models.CliCommandType.Interactive)
            {
                var wizard = serviceProvider.GetRequiredService<InteractiveWizard>();
                await wizard.RunAsync();
                return 0;
            }

            var cliHandler = serviceProvider.GetRequiredService<CliHandler>();
            return await cliHandler.ExecuteAsync(parsedArgs);
        }
        catch (ArgumentException ex)
        {
            ConsoleUi.PrintError(ex.Message);
            Console.WriteLine("Run 'ohmyprinter --help' for syntax and options.");
            return 1;
        }
        catch (FormatException ex)
        {
            ConsoleUi.PrintError(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"Unexpected fatal error: {ex.Message}");
            return 1;
        }
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        // Core discovery and rendering abstractions
        services.AddSingleton<IPrinterDiscoveryService, WindowsPrinterDiscoveryService>();

        // Register document renderers (Open/Closed principle: easily extend with new renderers)
        services.AddSingleton<IDocumentRenderer, PdfDocumentRenderer>();
        services.AddSingleton<IDocumentRenderer, ImageDocumentRenderer>();
        services.AddSingleton<IDocumentRenderer, TextDocumentRenderer>();
        services.AddSingleton<IDocumentRendererResolver, DocumentRendererResolver>();

        // Core business logic & validation
        services.AddTransient<IPrintJobValidator, PrintJobValidator>();
        services.AddTransient<IPrintService, WindowsPrintService>();

        // Web Server, fast cache, and SSE services
        services.AddSingleton<IFileCacheService, FileCacheService>();
        services.AddSingleton<IPrintEventHub, PrintEventHub>();
        services.AddSingleton<Server.PrintingWebServerHost>();

        // Presentation & execution layers
        services.AddTransient<CliHandler>();
        services.AddTransient<InteractiveWizard>();
    }
}
