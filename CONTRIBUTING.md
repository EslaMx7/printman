# Contributing to Printman

Thank you for your interest in contributing to Printman! This document provides guidelines and instructions for setting up your development environment, building the project, and submitting contributions.

## Development Environment Setup

### Prerequisites

- **Operating System:** Windows 10 (version 19041.0 or later) or Windows 11
- **.NET SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- **IDE (optional):** Visual Studio 2022 17.10+, Visual Studio Code with C# Dev Kit, or JetBrains Rider

### Clone the Repository

```powershell
git clone https://github.com/eslamx7/printman.git
cd printman
```

### Verify the SDK

```powershell
dotnet --version
```

Ensure the output shows a .NET 10 SDK version (e.g., `10.0.100` or later).

## Build Instructions

### Debug Build

```powershell
dotnet build
```

The build must produce **0 warnings and 0 errors**. Treat all warnings as errors before submitting a pull request.

### Release Build

```powershell
dotnet build -c Release
```

### Publish Standalone Binary

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

This produces `printman.exe` in the `./publish` directory, ready for deployment.

## Code Style Guidelines

### SOLID Principles

Printman adheres strictly to SOLID principles. When contributing code, ensure your changes respect:

- **Single Responsibility (SRP):** Each class should have one reason to change. Keep discovery, rendering, validation, and spooling logic in separate classes.
- **Open/Closed (OCP):** New document formats should be added by implementing `IDocumentRenderer` and registering in `Program.ConfigureServices` — do not modify `WindowsPrintService`.
- **Liskov Substitution (LSP):** All renderers must support synchronous drawing onto the supplied `Graphics` surface while respecting the target `printableArea`, DPI, and aspect ratio.
- **Interface Segregation (ISP):** Keep interfaces fine-grained in `Core/Abstractions/`.
- **Dependency Inversion (DIP):** Presentation and web servers must depend exclusively on abstractions injected via `IServiceProvider`.

### General Style

- **Indentation:** 4 spaces (no tabs)
- **Nullable reference types:** Enabled (`<Nullable>enable</Nullable>`). Annotate all reference types appropriately.
- **File-scoped namespaces:** Use file-scoped namespace declarations (e.g., `namespace Printman.Core;`).
- **XML documentation:** All public types and members must have XML documentation comments.
- **Async methods:** Use `async`/`await` with `CancellationToken` where applicable. Follow the `Async` suffix convention for async method names.
- **Zero third-party dependencies:** Do not add NuGet packages. Use only the .NET BCL, Windows SDK / WinRT, and framework references (`Microsoft.AspNetCore.App`, `Microsoft.WindowsDesktop.App`).

## Pull Request Process

1. **Fork the repository** and create a feature branch from `main`:
   ```powershell
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes** following the code style guidelines above.

3. **Build and verify:**
   ```powershell
   dotnet build
   ```
   Ensure 0 warnings and 0 errors.

4. **Run smoke tests** (see below).

5. **Commit your changes** with a clear, descriptive commit message:
   ```
   feat: add support for TIFF rendering
   fix: resolve race condition in print queue worker
   docs: update API endpoint documentation
   ```

6. **Push to your fork** and open a pull request against the `main` branch.

7. **Fill out the PR template** with a description of changes, motivation, and any breaking changes.

8. **Address review feedback** and ensure CI passes before merge.

## Smoke Tests

Before submitting a PR, run the following smoke tests to verify core functionality:

### List Printers

```powershell
dotnet run -- list
```

### Get Printer Info

```powershell
dotnet run -- info "Microsoft Print to PDF"
```

### Print a PDF (Pages 1-2)

```powershell
dotnet run -- "test_sample.pdf" -printer "Microsoft Print to PDF" -pages 1:2 -output "test.xps"
```

### Print an Image

```powershell
dotnet run -- "sample.png" -printer "Microsoft Print to PDF" -output "output.xps"
```

### Start the Web Server

```powershell
dotnet run -- server --port 5000
```

Then navigate to `http://localhost:5000` in your browser.

## Project Structure Overview

```
Printman/
├── Core/
│   ├── Abstractions/            # Fine-grained interfaces (IPrinterDiscoveryService, IDocumentRenderer, etc.)
│   └── Models/                  # Pure data structures / DTOs (PrintJobRequest, PrinterInfo, etc.)
├── Services/                    # Concrete implementations
│   ├── WindowsPrinterDiscoveryService.cs
│   ├── PrintJobValidator.cs
│   ├── DocumentRendererResolver.cs
│   ├── FileCacheService.cs
│   ├── PrintEventHub.cs
│   ├── WindowsPrintService.cs
│   └── Renderers/
│       ├── PdfDocumentRenderer.cs
│       ├── ImageDocumentRenderer.cs
│       └── TextDocumentRenderer.cs
├── Server/                      # Embedded Kestrel LAN Web Server
│   ├── PrintingWebServerHost.cs
│   ├── WebAssets.cs
│   └── Web/
│       └── index.html
├── CLI/                         # Command-Line Parser & Subcommand Dispatcher
│   ├── ParsedArguments.cs
│   ├── CommandLineParser.cs
│   └── CliHandler.cs
├── Interactive/                 # Terminal UI & Interactive Wizard
│   ├── ConsoleUi.cs
│   └── InteractiveWizard.cs
├── Printman.csproj              # Project configuration
├── Program.cs                   # Composition Root & DI configuration
└── tests/
    └── fixtures/
        ├── sample.txt           # Sample test text file
        └── test_sample.pdf      # Sample 3-page test PDF
```

## Getting Help

If you have questions or need clarification, feel free to:

- Open a [GitHub Discussion](https://github.com/eslamx7/printman/discussions)
- Reach out in the project's issue tracker

Thank you for contributing to Printman!
