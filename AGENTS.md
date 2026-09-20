# AGENTS.md — Agent Guidelines & Architecture Manual

This document provides context, architectural constraints, and operational instructions for AI agents working in this repository.

---

## 1. Project Overview

**Oh-My-Printer** is a zero-dependency, high-performance Windows CLI and interactive printing tool built on modern .NET (`net10.0-windows10.0.19041.0`) with native Windows WinRT integration.

- **Primary Binary:** `ohmyprinter.exe`
- **Current State:** Stage 1 complete (CLI commands, interactive wizard, PDF/Image/Text rendering, printer discovery and fuzzy matching).
- **Upcoming Milestone:** Stage 2 (Remote LAN Web Server for mobile and network printing).

---

## 2. Fundamental Architectural Rules & Constraints

### ⚠️ Critical Constraints
1. **Zero Third-Party NuGet Dependencies:**
   - Under no circumstances should third-party NuGet packages (e.g., iTextSharp, PdfSharp, CommandLineParser, Spectre.Console) be added to this project.
   - All functionality must rely strictly on standard .NET BCL, native Windows SDK / WinRT (`Windows.Data.Pdf`), and Microsoft framework references (`Microsoft.AspNetCore.App`, `Microsoft.WindowsDesktop.App`).
2. **Strict Adherence to SOLID Principles:**
   - **Single Responsibility (SRP):** Keep discovery (`IPrinterDiscoveryService`), document rendering (`IDocumentRenderer`), validation (`IPrintJobValidator`), and spooling (`IPrintService`) strictly isolated. Presentation code (CLI / Interactive) must never talk directly to Windows spoolers or GDI+ graphics.
   - **Open/Closed (OCP):** New document formats must be added by implementing `IDocumentRenderer` and registering in `Program.ConfigureServices` without modifying `WindowsPrintService`.
   - **Liskov Substitution (LSP):** All renderers must support synchronous drawing onto the supplied `Graphics` surface while respecting the target `printableArea`, DPI, and aspect ratio.
   - **Interface Segregation (ISP):** Keep interfaces fine-grained in `Core/Abstractions/`.
   - **Dependency Inversion (DIP):** Presentation and future web servers must depend exclusively on abstractions injected via `IServiceProvider`.

---

## 3. Directory Layout & Module Roles

```
OhMyPrinter/
├── Core/
│   ├── Abstractions/            # Fine-grained interfaces
│   │   ├── IPrinterDiscoveryService.cs  # Enumerate & fuzzy-match printers
│   │   ├── IDocumentRenderer.cs         # Strategy for rendering document pages
│   │   ├── IDocumentRendererResolver.cs # Resolves renderer by file extension
│   │   ├── IPrintJobValidator.cs        # Pre-execution request validation
│   │   └── IPrintService.cs             # Print orchestration and spooling
│   └── Models/                  # Pure data structures / DTOs
│       ├── PrintJobRequest.cs           # Agnostic print job payload
│       ├── PrintJobResult.cs            # Outcome status, counts, error messages
│       ├── PrinterInfo.cs               # Printer metadata, paper sizes, duplex
│       ├── PaperSizeOption.cs           # Name, width/height mm
│       ├── PageRange.cs                 # Expression parser (1:3, 1-3, 1,3,5, all)
│       └── PrintEnums.cs                # Orientation, Duplex, ColorMode
├── Services/                    # Concrete implementations
│   ├── WindowsPrinterDiscoveryService.cs # System.Drawing.Printing discovery
│   ├── PrintJobValidator.cs              # Validates paths, pages, and capabilities
│   ├── DocumentRendererResolver.cs       # Extension-based resolver
│   ├── WindowsPrintService.cs            # PrintDocument spooling & page loop
│   └── Renderers/
│       ├── PdfDocumentRenderer.cs        # WinRT Windows.Data.Pdf (300 DPI)
│       ├── ImageDocumentRenderer.cs      # GDI+ image rasterization
│       └── TextDocumentRenderer.cs       # Monospaced line-wrapped text
├── CLI/                         # Command-Line Parser & Subcommand Dispatcher
│   ├── ParsedArguments.cs
│   ├── CommandLineParser.cs              # Positional + flag parser
│   └── CliHandler.cs
├── Interactive/                 # Terminal UI & Interactive Wizard
│   ├── ConsoleUi.cs                      # ANSI colors, tables, banner
│   └── InteractiveWizard.cs              # Step-by-step guided printing prompt
├── OhMyPrinter.csproj           # Project configuration
├── Program.cs                   # Composition Root & DI configuration
├── sample.txt                   # Sample test text file
└── test_sample.pdf              # Sample 3-page test PDF
```

---

## 4. Web Server Architecture (`server` / `serve`)

The embedded LAN Web Server is implemented via ASP.NET Core Minimal APIs / Kestrel (enabled via `<FrameworkReference Include="Microsoft.AspNetCore.App" />`):
1. **Command:** `ohmyprinter server --port 5000` (or `serve`, alias `--ip 0.0.0.0`).
2. **Features:**
   - Detects local LAN IPv4 network interfaces and displays mobile-accessible URLs (e.g. `http://192.168.1.X:5000`).
   - Mobile-first responsive web SPA in `Server/WebAssets.cs` (drag-and-drop, multi-file queue, printer picker, paper size filter, copies, duplex, and color options).
   - **Fast Hash File Cache (`IFileCacheService` / `FileCacheService`):**
     - Uploads are SHA-256 hashed and cached in `cache/{hash}{ext}` next to the executable.
     - Automatically deduplicates existing files to avoid redundant writes.
   - **Real-Time Streaming via SSE (`IPrintEventHub` / `PrintEventHub`):**
     - Real-time updates delivered to web clients via Server-Sent Events (`GET /api/events`) over HTTP without WebSockets.
   - **REST API Endpoints:**
     - `GET /` -> Mobile SPA web page.
     - `GET /api/printers` -> JSON list of installed printers.
     - `GET /api/printers/{name}` -> JSON details of a specific printer.
     - `POST /api/upload` -> Multipart file upload with fast hash deduplication & page count discovery.
     - `POST /api/print` -> Submits batch print request; processes files sequentially while streaming live progress.
     - `GET /api/events` -> SSE event stream (`text/event-stream`).

---

## 5. Testing & Verification Protocols

When verifying changes:
1. **Never send test jobs to physical printers:**
   Always use virtual printers (`-printer "XPS"` or `Print to PDF`) along with `-output "output.xps"` to ensure tests run headlessly and silently without paper or toner consumption.
2. **Compilation check:**
   `dotnet build` must always produce **0 warnings and 0 errors**.
3. **Core commands smoke test:**
   ```powershell
   dotnet run -- list
   dotnet run -- info "HP Laser"
   dotnet run -- "test_sample.pdf" -printer "XPS" -pages 1:2 -output "test.xps"
   ```
4. **Publishing standalone binary:**
   ```powershell
   dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
   ```
