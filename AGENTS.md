# AGENTS.md — Agent Guidelines & Architecture Manual

This document provides context, architectural constraints, and operational instructions for AI agents working in this repository.

---

## 1. Project Overview

**Printman** is a zero-dependency, high-performance Windows CLI and mobile LAN printing platform built on modern .NET (`net10.0-windows` with `TargetPlatformVersion 10.0.19041.0`) with native Windows WinRT integration.

- **Primary Binary:** `printman.exe` (built from `src/Printman/`)
- **Solution:** `Printman.slnx` — `src/Printman` (shipping app) + `tests/Printman.Tests` (unit tests, MSTest 4 + Microsoft.Testing.Platform)
- **Current State:** CLI commands, interactive launcher menu, PDF/Image/Text rendering, printer discovery and fuzzy matching, plus embedded mobile LAN web server (`serve`) and network printer sharing (`share`).

---

## 2. Fundamental Architectural Rules & Constraints

### ⚠️ Critical Constraints
1. **Zero Third-Party NuGet Dependencies (shipped code):**
   - Under no circumstances should third-party NuGet packages (e.g., iTextSharp, PdfSharp, CommandLineParser, Spectre.Console) be added to the shipping project (`src/Printman`).
   - All functionality must rely strictly on standard .NET BCL, native Windows SDK / WinRT (`Windows.Data.Pdf`), and Microsoft framework references (`Microsoft.AspNetCore.App`, `Microsoft.WindowsDesktop.App`).
   - **Test-only exemption:** `tests/Printman.Tests` may reference **Microsoft-owned** test packages (MSTest 4 via `MSTest.Sdk`, Microsoft.Testing.Platform). Test dependencies must never flow into `printman.exe` or the published output.
2. **Strict Adherence to SOLID Principles:**
   - **Single Responsibility (SRP):** Keep discovery (`IPrinterDiscoveryService`), document rendering (`IDocumentRenderer`), validation (`IPrintJobValidator`), and spooling (`IPrintService`) strictly isolated. Presentation code (CLI / Interactive) must never talk directly to Windows spoolers or GDI+ graphics.
   - **Open/Closed (OCP):** New document formats must be added by implementing `IDocumentRenderer` and registering in `Program.ConfigureServices` without modifying `WindowsPrintService`.
   - **Interface Segregation (ISP):** Keep interfaces fine-grained in `Core/Abstractions/`.
   - **Dependency Inversion (DIP):** Presentation and web servers must depend exclusively on abstractions injected via `IServiceProvider`.

---

## 3. Directory Layout & Module Roles

```
printman/
├── src/
│   └── Printman/                # Shipping application (printman.exe)
│       ├── Core/
│       │   ├── Abstractions/            # Fine-grained interfaces
│       │   │   ├── IPrinterDiscoveryService.cs  # Enumerate & fuzzy-match printers
│       │   │   ├── IDocumentRenderer.cs         # Strategy for rendering document pages
│       │   │   ├── IDocumentRendererResolver.cs # Resolves renderer by file extension
│       │   │   ├── IPrintJobValidator.cs        # Pre-execution request validation
│       │   │   ├── IFileCacheService.cs         # Content-addressed hashing & LRU cache
│       │   │   ├── IPrintEventHub.cs            # SSE streaming abstraction
│       │   │   ├── IPrintQueueService.cs        # Spooler & pipeline queue management
│       │   │   ├── IPrintJobPipeline.cs         # Serialized print queue shared by web UI and IPP
│       │   │   ├── ISharedPrinterRegistry.cs    # Printers shared on the network (+ cached caps/status)
│       │   │   ├── IIppRequestHandler.cs        # IPP operation processing (transport independent)
│       │   │   ├── IIppJobStore.cs              # IPP job ids and state tracking
│       │   │   ├── IDnsSdServiceFactory.cs      # Shared printers -> DNS-SD service descriptions
│       │   │   ├── IServiceAdvertiser.cs        # mDNS / DNS-SD advertising
│       │   │   ├── IFirewallInspector.cs        # Read-only inbound firewall check
│       │   │   └── IPrintService.cs             # Print orchestration and spooling
│       │   └── Models/                  # Pure data structures / DTOs
│       │       ├── PrintJobRequest.cs           # Agnostic print job payload
│       │       ├── PrintJobResult.cs            # Outcome status, counts, error messages
│       │       ├── PrintJobInfo.cs              # Spooler & pipeline job metadata
│       │       ├── PrinterStatusInfo.cs         # Real-time hardware status flags
│       │       ├── PrinterInfo.cs               # Printer metadata, paper sizes, duplex
│       │       ├── PaperSizeOption.cs           # Name, width/height mm
│       │       ├── ServerModels.cs              # Web upload, batch print, and SSE event payloads
│       │       ├── PageRange.cs                 # Expression parser (1:3, 1-3, 1,3,5, all)
│       │       ├── PrintEnums.cs                # Orientation, Duplex, ColorMode
│       │       ├── ServerOptions.cs             # `serve` options incl. ShareOptions (--share, --ipp-port)
│       │       ├── PipelineModels.cs            # PipelineBatch / PipelineItem / PipelineTicket
│       │       ├── IppModels.cs                 # IPP message/attribute model, IppJob, SharedPrinter, settings
│       │       └── DnsSdService.cs              # DNS-SD service instance description
│       ├── Services/                    # Concrete implementations
│       │   ├── WindowsPrinterDiscoveryService.cs # System.Drawing.Printing discovery
│       │   ├── WindowsPrintQueueService.cs       # winspool.drv native spooler & pipeline manager
│       │   ├── PrintJobValidator.cs              # Validates paths, pages, and capabilities
│       │   ├── DocumentRendererResolver.cs       # Extension-based resolver
│       │   ├── FileCacheService.cs               # SHA-256 disk cache & LRU quota manager
│       │   ├── PrintEventHub.cs                  # SSE subscription & channel broadcast
│       │   ├── WindowsPrintService.cs            # PrintDocument spooling & page loop
│       │   ├── PrintJobPipeline.cs               # Serialized queue worker (web + IPP jobs)
│       │   ├── SharedPrinterRegistry.cs          # Resolves --share names, slugs, stable UUIDs
│       │   ├── Ipp/
│       │   │   ├── IppMessageReader.cs / IppMessageWriter.cs  # application/ipp binary codec
│       │   │   ├── IppRequestHandler.cs          # IPP Everywhere operations -> pipeline
│       │   │   ├── IppPrinterAttributeBuilder.cs # Printer description attributes
│       │   │   ├── IppJobStore.cs                # Job ids / states
│       │   │   ├── IppDocumentFormats.cs         # MIME <-> extension, magic-byte sniffing
│       │   │   ├── PwgMediaMapper.cs             # Windows paper sizes <-> PWG media names
│       │   │   └── IppDnsSdServiceFactory.cs     # _ipp._tcp TXT records (AirPrint / Mopria keys)
│       │   ├── Discovery/
│       │   │   ├── DnsMessage.cs                 # DNS wire format (names, compression, records)
│       │   │   ├── MdnsResponder.cs              # Per-interface mDNS responder on UDP 5353
│       │   │   └── WindowsFirewallInspector.cs   # HNetCfg.FwPolicy2 read-only rule check
│       │   └── Renderers/
│       │       ├── PdfDocumentRenderer.cs        # WinRT Windows.Data.Pdf (300 DPI)
│       │       ├── ImageDocumentRenderer.cs      # GDI+ image rasterization
│       │       ├── TextDocumentRenderer.cs       # Monospaced line-wrapped text
│       │       ├── RasterDocumentRenderer.cs     # Shared CUPS-style raster decoder base
│       │       ├── PwgRasterDocumentRenderer.cs  # PWG Raster (.pwg)
│       │       └── UrfDocumentRenderer.cs        # Apple Raster / AirPrint (.urf)
│       ├── Server/                      # Embedded Kestrel LAN Web Server
│       │   ├── PrintingWebServerHost.cs          # Minimal API routes, listeners, banner
│       │   ├── IppEndpoints.cs                   # /ipp/print routes (LAN filter, body limits)
│       │   ├── WebAssets.cs                      # In-assembly embedded resource loader & live-reload
│       │   └── Web/
│       │       └── index.html                    # Mobile-responsive web SPA & CSS/JS
│       ├── CLI/                         # Command-Line Parser & Subcommand Dispatcher
│       │   ├── ParsedArguments.cs
│       │   ├── CommandLineParser.cs              # Positional + flag parser
│       │   └── CliHandler.cs
│       ├── Interactive/                 # Terminal UI & Interactive Wizard
│       │   ├── ConsoleUi.cs                      # ANSI colors, tables, banner
│       │   ├── PrinterPicker.cs                  # Multi-select printer checklist (share --select, launcher)
│       │   ├── ConsoleMenu.cs                    # Single-select arrow-key menu (launcher)
│       │   └── InteractiveWizard.cs              # No-arg launcher menu + "More tools" (print wizard, queue)
│       ├── Printman.csproj              # Project configuration
│       ├── app.ico                      # Application icon
│       └── Program.cs                   # Composition Root & DI configuration
├── tests/
│   ├── Printman.Tests/                  # MSTest 4 unit tests (net10.0-windows, MTP)
│   │   ├── Printman.Tests.csproj        # MSTest.Sdk project
│   │   ├── MSTestSettings.cs            # Assembly-level parallelization
│   │   ├── Cli/                         # Command-line parsing (share/serve flags)
│   │   ├── Ipp/                         # IPP codec round-trips, malformed input, PWG media
│   │   └── Models/                      # PageRange parser and other DTO behaviour
│   └── fixtures/
│       ├── sample.txt                   # Sample test text file
│       └── test_sample.pdf              # Sample 3-page test PDF
├── Printman.slnx                # Solution (src + tests)
├── global.json                  # .NET SDK pin + Microsoft.Testing.Platform runner
└── Directory.Build.props        # Shared build settings
```

---

## 4. Web Server Architecture (`server` / `serve`)

The embedded LAN Web Server is implemented via ASP.NET Core Minimal APIs / Kestrel (enabled via `<FrameworkReference Include="Microsoft.AspNetCore.App" />`):
1. **Command:** `printman server [options]` (aliases: `serve`)
   - `--port <port>`: Port to bind (default: `5000`).
   - `--ip <address>`: IP binding address (default: `0.0.0.0`).
   - `--pin <pin>`: Explicit 4-8 character PIN (default: auto-generates secure 6-digit random PIN).
   - `--no-auth` / `--allow-anonymous`: Disable PIN authentication (open access mode).
   - `--max-upload-mb <n>`: Maximum file upload size limit in MB (default: `50`).
   - `--cache-limit-mb <n>`: Total disk cache limit in MB before LRU eviction (default: `500`).
   - `--share [printer]`: Also share printers next to the web UI; repeatable; no name = Windows default printer. Advertised as `Printman - <printer>`.
   - `--share-select`: Same, picking printers from a checklist (`Interactive/PrinterPicker.cs`, run by `CliHandler` before the server starts; `--share` names are preselected; falls back to typed numbers when stdin/stdout are redirected).
   - `--ipp-port <n>`: IPP port (default: `631`; falls back to the web port if busy).
   - `--no-mdns`: Serve IPP without mDNS / DNS-SD announcements.
   - Hidden/dev: `--output-dir <dir>` (print every server job to a file), `--ipp-allow-any-source`.
   **Command:** `printman share [printer ...] [options]` (alias: `share-select` = `share --select`)
   - Network printers only: same host with `ServerOptions.EnableWebUi = false`. Kestrel listens only on the IPP port, everything outside `/ipp/*` is a 404, no PIN is generated and `printer-more-info` / `adminurl` never point at a web port.
   - Positional printer names (fuzzy); no name = Windows default printer. `--select` (checklist), `--all` (every printer), `--web` / `--ui` (also start the web UI; equivalent to `serve --share`).
   - Accepts `--ipp-port`, `--no-mdns`, `--ip`, `--cache-limit-mb`, the hidden flags, and the web flags (used with `--web`; `--port` is also the IPP fallback port). Exits with code 1 when nothing can be shared.
   **No arguments:** `InteractiveWizard` shows a launcher menu (`Interactive/ConsoleMenu.cs`): web UI / share default printer / choose printers to share / both / More tools / Exit.
2. **Features & Security Architecture:**
   - Detects local LAN IPv4 network interfaces and displays mobile-accessible URLs with quick-auth token links (e.g. `http://192.168.1.X:5000/?pin=123456`).
   - Mobile-first responsive web SPA in `Server/Web/index.html` (embedded into assembly via MSBuild `<EmbeddedResource>` and loaded via in-memory cached loader `Server/WebAssets.cs` with development live-reload support; auto/manual light & dark theme, PIN lock screen, drag-and-drop, multi-file queue, printer picker, paper size filter, copies, duplex, and color options).
   - **PIN Authentication & Session Security (`sec-01`):**
     - Constant-time PIN verification preventing timing attacks.
     - Ephemeral session tokens issued via HTTP-only / SameSite cookies (`printman_auth`) or `X-Printer-Pin` / `X-Session-Token` headers.
     - All API routes and SSE streams require active authentication when PIN protection is enabled.
   - **Intranet CSRF Mitigation (`sec-04`):**
     - Custom anti-CSRF header enforcement (`X-Requested-With: Printman`).
     - Strict `Origin` and `Referer` validation against local server binding hosts for state-changing requests.
   - **Strict File Type Whitelist & DoS Protection (`sec-02`):**
     - Extension whitelist (`.pdf`, `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff`, `.tif`, `.txt`, `.log`, `.csv`, `.json`, `.md`).
     - Kestrel request body limit and streaming byte counter enforcement (`MaxFileSizeBytes`).
   - **LRU Cache Quota Eviction (`sec-05`):**
     - `IFileCacheService` tracks disk usage against `MaxCacheSizeBytes` (default 500 MB) and evicts oldest unreferenced files on disk.
   - **Serialized Print Spooling Queue (`sec-06`):**
     - `IPrintJobPipeline` (`Services/PrintJobPipeline.cs`) serializes print jobs from both the web UI and IPP clients to prevent Windows GDI+/spooler race conditions and thread pool starvation. Enqueue returns a `PipelineTicket` (state, cancel, completion).
   - **Network Printer Sharing (`share` / `serve --share`, IPP Everywhere / AirPrint / Mopria):**
     - Second Kestrel listener on the IPP port (the only listener for `share` without `--web`); a port-separation middleware serves only `/ipp/*` there and never on the web port.
     - `POST /ipp/print/{slug}` (`/ipp/print` = first shared printer). Unauthenticated by design (native dialogs cannot send a PIN); restricted to private source IPs and `Content-Type: application/ipp`.
     - Documents are sniffed by magic bytes (PDF, JPEG, PNG, PWG `RaS2`, URF `UNIRAST`) and cached via `IFileCacheService.StoreFileAsync(..., maxFileSizeBytes, ...)`.
     - IPP jobs render with `PrintJobRequest.FullPage = true` (whole sheet, not the 1-inch default margins).
     - `MdnsResponder` binds UDP 5353 per IPv4 interface (shared with Windows/Bonjour), probes, announces `_ipp._tcp` + `_universal` / `_print` subtypes, answers queries and sends goodbyes on shutdown. `_universal` and `URF=` are only advertised when a `.urf` renderer is registered.
   - **Generic Sanitized Error Responses (`sec-03`):**
     - Internal stack traces and file paths stripped from API client responses; detailed traces logged to console only.
   - **Fast Hash File Cache (`IFileCacheService` / `FileCacheService`):**
     - Uploads are SHA-256 hashed and cached in `cache/{hash}{ext}` next to the executable.
     - Automatically deduplicates existing files to avoid redundant writes.
   - **Real-Time Streaming via SSE (`IPrintEventHub` / `PrintEventHub`):**
     - Real-time updates delivered to web clients via Server-Sent Events (`GET /api/events`) over HTTP without WebSockets.
   - **REST API Endpoints:**
     - `GET /` -> Mobile SPA web page.
     - `GET /api/auth/status` -> Check if authentication is enabled and session is authenticated.
     - `POST /api/auth/verify` -> Verify PIN and obtain session cookie/token.
     - `GET /api/printers` -> JSON list of installed printers.
     - `GET /api/printers/{name}` -> JSON details of a specific printer.
      - `GET /api/printers/{name}/status` -> Real-time hardware status flags (Paper Jam, Out of Paper, Offline, Busy, Paused).
      - `GET /api/queue` -> Real-time unified spooler & pipeline queue snapshot (?printer=<name>).
      - `POST /api/queue/cancel` -> Cancels a specific job by integer ID or pipeline ID.
      - `POST /api/queue/purge` -> Bulk purge/cancels all jobs on a printer queue.
     - `POST /api/upload` -> Multipart file upload with fast hash deduplication & page count discovery.
     - `POST /api/print` -> Submits batch print request; enqueues into serialized print worker with live SSE progress.
     - `GET /api/events` -> SSE event stream (`text/event-stream`).

---

## 5. Testing & Verification Protocols

When verifying changes:
1. **Never send test jobs to physical printers:**
   Always use virtual printers (`-printer "XPS"` or `Print to PDF`) along with `-output "output.xps"` to ensure tests run headlessly and silently without paper or toner consumption.
2. **Unit tests:**
   ```powershell
   dotnet test Printman.slnx            # MSTest 4 + Microsoft.Testing.Platform (runner pinned in global.json)
   ```
   Must report **0 failures**. Tests live in `tests/Printman.Tests` and cover pure logic (IPP/DNS codecs, CLI parsing, models, raster indexing). Keep spooler and printer-discovery access behind interfaces so it stays out of unit tests.
3. **Compilation check:**
   `dotnet build` (auto-discovers `Printman.slnx`) must always produce **0 warnings and 0 errors**.
4. **Core commands smoke test:**
   ```powershell
   dotnet run --project src/Printman -- list
   dotnet run --project src/Printman -- info "HP Laser"
   dotnet run --project src/Printman -- "tests/fixtures/test_sample.pdf" -printer "XPS" -pages 1:2 -output "test.xps"
   ```
5. **Network printer sharing smoke test (headless):**
   ```powershell
   dotnet run --project src/Printman -- share "Microsoft Print to PDF" --output-dir out                    # network printers only
   dotnet run --project src/Printman -- serve --no-auth --share "Microsoft Print to PDF" --output-dir out  # web UI + network printers
   dns-sd -B _ipp._tcp,_universal          # Bonjour tool, if installed: lists "Printman - Microsoft Print to PDF"
   ```
   Send IPP requests (Get-Printer-Attributes / Print-Job with `application/ipp` bodies) to `http://localhost:631/ipp/print/microsoft-print-to-pdf`; jobs land in `out/` as PDFs.
6. **Publishing standalone binary:**
   ```powershell
   dotnet publish src/Printman/Printman.csproj -c Release -r win-x64 --self-contained false -o ./publish
   ```
