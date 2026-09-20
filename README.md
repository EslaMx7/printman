# Oh-My-Printer 🖨️

A modern, high-performance, zero-dependency Windows CLI and interactive printing tool built with .NET and native Windows WinRT APIs.

Architected following **SOLID principles** so that the underlying printing engine can be seamlessly shared between this CLI tool and a future remote LAN web printing server (e.g., ASP.NET Core Minimal API).

---

## 🌟 Key Features

- **Zero Third-Party Dependencies:** Uses built-in .NET SDK and native Windows APIs (`Windows.Data.Pdf` for vector PDF rasterization, `System.Drawing.Printing` for printer spooling and hardware controls).
- **Multiple Document Formats:**
  - **PDF Documents (`.pdf`):** High-resolution rasterization (300 DPI by default) with aspect-ratio scaling.
  - **Images (`.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif`, `.tiff`):** Automatic scaling and orientation fitting.
  - **Text / Code (`.txt`, `.log`, `.csv`, `.json`, `.md`, etc.):** Clean font layout with automatic line wrapping and pagination.
- **Flexible Execution Modes:**
  - **CLI Command Mode:** Pass arguments to print immediately in automated scripts or pipelines.
  - **Interactive Wizard:** Run with no arguments to launch an interactive menu with guided prompts.
- **Intelligent Printer Matching:** Fuzzy name search (e.g. `-printer "HP Laser"` resolves `"HP LaserJet Professional P1102"`).
- **Full Printing Control:** Page range selection (`-pages 1:3`, `1-3`, `1,3,5`, `2-`), paper size preference (`-size A4`, `Letter`), copies (`-copies 2`), duplex (`-duplex vertical`), orientation (`-orientation landscape`), and color mode.

---

## 🚀 Quick Start & CLI Usage

### 1. Print Documents

```powershell
# Print entire document to default printer
ohmyprinter.exe "./doc.pdf"

# Print to a specific printer (fuzzy match)
ohmyprinter.exe "./doc.pdf" -printer "HP Laser"

# Print specific page range (e.g., pages 1 through 3)
ohmyprinter.exe "./doc.pdf" -pages 1:3

# Print with specific paper size
ohmyprinter.exe "./doc.pdf" -size A4

# Complete full-featured print job
ohmyprinter.exe "./invoice.pdf" -p "HP" -pages 1:2 -size A4 -copies 2 -duplex vertical -orientation portrait
```

### 2. Printer Management Commands

```powershell
# List all installed printers, status, default indicator, and capabilities
ohmyprinter.exe list

# Show detailed printer specifications (paper sizes with mm dimensions, resolutions, duplex, etc.)
ohmyprinter.exe info "HP LaserJet"

# View CLI help and all supported flags
ohmyprinter.exe help
```

### 3. Mobile LAN Web Server (`serve` / `server`)

Spin up a local web server to print from your smartphone or other devices on the same Wi-Fi:

```powershell
# Start server on default port 5000
ohmyprinter.exe serve

# Start server on custom port
ohmyprinter.exe server --port 8080
```

The console will display accessible URLs:
```
  Access from this machine or your phone on the same Wi-Fi:
    Local:    http://localhost:5000
    Network:  http://192.168.1.50:5000
```
- **Mobile-friendly UI:** Drag & drop documents, multi-file queue, printer picker, paper size filter, copies, duplex, and color mode.
- **Fast File Deduplication:** Uploads are hashed (SHA-256) into a local `cache/` directory next to the executable; duplicate files are instantly detected and reused without redundant disk writes.
- **Live Real-Time Feedback:** Server-Sent Events (SSE) stream progress and spooling status directly to your phone without WebSockets.

### 4. Interactive Wizard

Run without arguments (or with `interactive`):
```powershell
ohmyprinter.exe
```
This guides you through file path selection (supports drag-and-drop), selecting from installed printers, choosing page ranges, paper size, duplex, and displays a review summary before sending the job to the spooler.

---

## ⚙️ CLI Options Reference

| Option | Aliases | Description | Example |
| :--- | :--- | :--- | :--- |
| `-printer` | `-p`, `--printer` | Target printer name or substring | `-p "HP Laser"` |
| `-pages` | `--pages` | Page range or discrete pages | `-pages 1:3`, `-pages 2,5` |
| `-size` | `-s`, `--size` | Paper size preference | `-size A4`, `-size Letter` |
| `-copies` | `-c`, `--copies` | Number of copies | `-copies 2` |
| `-orientation` | `-o`, `--orientation` | Orientation: `portrait`, `landscape`, `auto` | `-o landscape` |
| `-duplex` | `-d`, `--duplex` | Duplex mode: `simplex`, `vertical`, `horizontal` | `-d vertical` |
| `-color` | `--color` | Color mode: `color`, `mono` | `-color mono` |
| `-dpi` | `--dpi` | Rasterization resolution for PDF/images | `-dpi 300` |
| `-fit` | `-nofit` | Fit to printable page margins (default: true) | `-fit` |
| `-output` | `-out`, `--output` | Print to file (for virtual printers like PDF/XPS) | `-output "out.xps"` |

---

## 🏛️ Architecture & SOLID Design

The codebase is partitioned into distinct layers:

```
OhMyPrinter/
├── Core/
│   ├── Abstractions/             # ISP / DIP contracts
│   │   ├── IPrinterDiscoveryService.cs   # Printer query and fuzzy matching
│   │   ├── IDocumentRenderer.cs          # Pluggable rendering strategy
│   │   ├── IDocumentRendererResolver.cs  # Renderer resolution
│   │   ├── IPrintJobValidator.cs         # Request validation
│   │   └── IPrintService.cs              # Core print orchestration
│   └── Models/                   # Plain models / DTOs
│       ├── PrintJobRequest.cs
│       ├── PrintJobResult.cs
│       ├── PrinterInfo.cs
│       └── PageRange.cs
├── Services/                     # Concrete business logic implementations
│   ├── WindowsPrinterDiscoveryService.cs
│   ├── PrintJobValidator.cs
│   ├── DocumentRendererResolver.cs
│   ├── WindowsPrintService.cs
│   └── Renderers/
│       ├── PdfDocumentRenderer.cs        # WinRT Windows.Data.Pdf
│       ├── ImageDocumentRenderer.cs      # System.Drawing.Image
│       └── TextDocumentRenderer.cs       # Pagination & layout
├── CLI/                          # Command-line interface layer
│   ├── CommandLineParser.cs
│   └── CliHandler.cs
└── Interactive/                  # Console UI & Interactive Wizard
    ├── ConsoleUi.cs
    └── InteractiveWizard.cs
```

### Future Web Server Integration (Stage 2)
Because `IPrintService` and `IPrinterDiscoveryService` have no dependency on the console or CLI, exposing remote LAN printing is as straightforward as:
1. Creating an ASP.NET Core Minimal API endpoint (`app.MapPost("/api/print", async (PrintJobRequest req, IPrintService svc) => await svc.PrintAsync(req));`).
2. Mobile and LAN devices can upload documents over Wi-Fi and execute print jobs on the server seamlessly.

---

## 🔨 Building from Source

### Prerequisites
- Windows 10 (1809+) or Windows 11
- .NET 9 or .NET 10 SDK

### Build & Run
```powershell
# Build
dotnet build

# Run CLI command
dotnet run -- list

# Publish standalone executable
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```
The published binary is available at `./publish/ohmyprinter.exe`.

---

## 🤖 Agent Instructions & Roadmap
For AI coding assistants and contributors, detailed architecture rules, SOLID boundaries, and the Stage 2 Web Server roadmap are documented in [AGENTS.md](AGENTS.md).

