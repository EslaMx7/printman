using Microsoft.Extensions.DependencyInjection;
using Printman.CLI;
using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Interactive;
using Printman.Services;
#if !WINDOWS
using Printman.Services.Cups;
#endif
using Printman.Services.Discovery;
using Printman.Services.Ipp;
using Printman.Services.Renderers;

namespace Printman;

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
            var parsedArgs = args.Length == 0 ? null : CommandLineParser.Parse(args);

#if !WINDOWS
            // Everything but help / version talks to cupsd; explain and stop early when it is not there
            if (parsedArgs == null || CupsReadiness.Needs(parsedArgs.Command))
            {
                var exitCode = await CupsReadiness.EnsureAsync(
                    serviceProvider.GetRequiredService<CupsClient>(),
                    serviceProvider.GetRequiredService<IExternalTools>(),
                    Console.ReadLine,
                    Console.Out,
                    interactive: !Console.IsInputRedirected && !Console.IsOutputRedirected);
                if (exitCode is int code)
                {
                    return code;
                }
            }
#endif

            if (parsedArgs == null)
            {
                var wizard = serviceProvider.GetRequiredService<InteractiveWizard>();
                await wizard.RunAsync();
                return 0;
            }

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
            Console.WriteLine("Run 'printman --help' for syntax and options.");
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
        // OS print system: Windows spooler + GDI+, or CUPS on Linux / macOS (chosen at build time, see Printman.csproj)
#if WINDOWS
        services.AddSingleton<IPrinterDiscoveryService, WindowsPrinterDiscoveryService>();
        services.AddTransient<IPrintService, WindowsPrintService>();
        services.AddSingleton<IPrintQueueService, WindowsPrintQueueService>();
        services.AddSingleton<IFirewallInspector, WindowsFirewallInspector>();
#else
        services.AddSingleton<CupsClient>();
        services.AddSingleton<IExternalTools, ExternalTool>();
        services.AddSingleton<IPrinterDiscoveryService, CupsPrinterDiscoveryService>();
        services.AddTransient<IPrintService, CupsPrintService>();
        services.AddSingleton<IPrintQueueService, CupsPrintQueueService>();
        services.AddSingleton<IFirewallInspector, NoFirewallInspector>();
#endif

        // Register document renderers (Open/Closed principle: easily extend with new renderers)
        services.AddSingleton<IDocumentRenderer, PdfDocumentRenderer>();
        services.AddSingleton<IDocumentRenderer, ImageDocumentRenderer>();
        services.AddSingleton<IDocumentRenderer, TextDocumentRenderer>();
        services.AddSingleton<IDocumentRenderer, PwgRasterDocumentRenderer>();
        services.AddSingleton<IDocumentRenderer, UrfDocumentRenderer>();
        services.AddSingleton<IDocumentRendererResolver, DocumentRendererResolver>();

        // Core business logic & validation
        services.AddTransient<IPrintJobValidator, PrintJobValidator>();

        // Web Server, fast cache, and SSE services
        services.AddSingleton<IFileCacheService, FileCacheService>();
        services.AddSingleton<IPrintEventHub, PrintEventHub>();
        services.AddSingleton<IPrintJobPipeline, PrintJobPipeline>();

        // Network printer sharing: IPP Everywhere / AirPrint endpoint and mDNS / DNS-SD discovery
        services.AddSingleton<IppServerSettings>();
        services.AddSingleton<ISharedPrinterRegistry, SharedPrinterRegistry>();
        services.AddSingleton<IIppJobStore, IppJobStore>();
        services.AddSingleton<IppDocumentFormats>();
        services.AddSingleton<IppPrinterAttributeBuilder>();
        services.AddSingleton<IIppRequestHandler, IppRequestHandler>();
        services.AddSingleton<IDnsSdServiceFactory, IppDnsSdServiceFactory>();
        services.AddSingleton<IServiceAdvertiser, MdnsResponder>();
        services.AddSingleton<Server.PrintingWebServerHost>();

        // Presentation & execution layers
        services.AddTransient<CliHandler>();
        services.AddTransient<InteractiveWizard>();
    }
}
