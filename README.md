# Printman 🚀🖨️

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)]()

A modern, high-performance, zero-dependency Windows CLI and mobile LAN printing platform built with .NET and native Windows WinRT APIs.

> *"The platform for building, sending, and managing print requests."*

Architected following **SOLID principles** so that the underlying printing engine is seamlessly shared between the fast command-line tool, an interactive wizard, and an embedded mobile-first LAN web printing server.

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
  - **Mobile LAN Web Server (`serve`):** Instant browser-based printing for smartphones and other devices on the same Wi-Fi.
- **Intelligent Printer Matching:** Fuzzy name search (e.g. `-printer "HP Laser"` resolves `"HP LaserJet Professional P1102"`).
- **Full Printing Control:** Page range selection (`-pages 1:3`, `1-3`, `1,3,5`, `2-`), paper size preference (`-size A4`, `Letter`), copies (`-copies 2`), duplex (`-duplex vertical`), orientation (`-orientation landscape`), and color mode.

---

## 🚀 Quick Start & CLI Usage

### 1. Print Documents

```powershell
# Print entire document to default printer
printman.exe "./doc.pdf"

# Print to a specific printer (fuzzy match)
printman.exe "./doc.pdf" -printer "HP Laser"

# Print specific page range (e.g., pages 1 through 3)
printman.exe "./doc.pdf" -pages 1:3

# Print with specific paper size
printman.exe "./doc.pdf" -size A4

# Complete full-featured print job
printman.exe "./invoice.pdf" -p "HP" -pages 1:2 -size A4 -copies 2 -duplex vertical -orientation portrait
```

### 2. Printer Management Commands

```powershell
# List all installed printers, status, default indicator, and capabilities
printman.exe list

# Show detailed printer specifications (paper sizes with mm dimensions, resolutions, duplex, etc.)
printman.exe info "HP LaserJet"

# View CLI help and all supported flags
printman.exe help
```

### 3. Mobile LAN Web Server (`serve` / `server`)

Spin up a local web server to print from your smartphone or other devices on the same Wi-Fi:

```powershell
# Start server with auto-generated pairing PIN on default port 5000
printman.exe serve

# Start server with a custom PIN
printman.exe server --port 8080 --pin 123456

# Start server with open unauthenticated access (no PIN required)
printman.exe serve --no-auth

# Start server with custom upload and cache limits
printman.exe serve --max-upload-mb 100 --cache-limit-mb 1000
```

The console will display accessible URLs with the quick-auth PIN embedded:
```
  [WEB SERVER RUNNING]  Port: 5000
  [SECURITY] PIN Protected:  849201

  Access from this machine or your phone on the same Wi-Fi:
    Local:    http://localhost:5000/?pin=849201
    Network:  http://192.168.1.50:5000/?pin=849201
```
- **Mobile-friendly UI:** Responsive SPA with light/dark theme (auto OS detection + manual toggle), drag & drop, multi-file queue, printer picker, paper size, copies, duplex, and color mode.
- **PIN Pairing & Authentication:** Protects physical printers from unauthorized network access. Mobile clients connect with one tap via the banner URL or enter the 6-digit PIN into the web interface.
- **Intranet CSRF Defense:** Enforces origin checks and custom headers (`X-Requested-With: Printman`) to prevent malicious websites from issuing drive-by print requests.
- **Serialized Spooler Queue:** In-memory queue worker serializes print jobs one by one to prevent Windows Print Spooler race conditions and collisions.
- **Storage Limits & LRU Cache Eviction:** Enforces 50 MB single-file upload limits and an aggregate cache quota (default 500 MB) with automatic LRU eviction.
- **Fast File Deduplication:** Uploads are hashed (SHA-256) into `cache/`; duplicate files are instantly recognized without redundant disk writes.
- **Live Real-Time Feedback:** Server-Sent Events (SSE) stream progress and queue position directly to connected devices without WebSockets.

### 4. Interactive Wizard

Run without arguments (or with `interactive`):
```powershell
printman.exe
```
This guides you through file path selection (supports drag-and-drop), selecting from installed printers, choosing page ranges, paper size, duplex, and displays a review summary before sending the job to the spooler.

---

## ⚙️ CLI Options Reference

### Printing Options
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

### Web Server Options (`serve` / `server`)
| Option | Description | Default |
| :--- | :--- | :--- |
| `--port`, `-p` | Port number for Kestrel HTTP listener | `5000` |
| `--ip`, `--bind` | Bind network IP address | `0.0.0.0` |
| `--pin` | Custom pairing PIN for mobile access | Auto-generated 6-digit PIN |
| `--no-auth` | Disable PIN authentication (open LAN access) | Disabled |
| `--max-upload-mb`| Maximum single file upload size (MB) | `50` |
| `--cache-limit-mb`| Maximum total file cache storage (MB) with LRU eviction | `500` |

---

## 🏛️ Architecture & SOLID Design

The codebase is partitioned into distinct layers:

```
Printman/
├── Core/
│   ├── Abstractions/             # ISP / DIP contracts
│   │   ├── IPrinterDiscoveryService.cs   # Printer query and fuzzy matching
│   │   ├── IDocumentRenderer.cs          # Pluggable rendering strategy
│   │   ├── IDocumentRendererResolver.cs  # Renderer resolution
│   │   ├── IPrintJobValidator.cs         # Request validation
│   │   ├── IFileCacheService.cs          # Content-addressed hashing & LRU cache
│   │   ├── IPrintEventHub.cs             # SSE streaming abstraction
│   │   └── IPrintService.cs              # Core print orchestration
│   └── Models/                   # Plain models / DTOs
│       ├── PrintJobRequest.cs
│       ├── PrintJobResult.cs
│       ├── PrinterInfo.cs
│       ├── PaperSizeOption.cs
│       ├── ServerModels.cs
│       └── PageRange.cs
├── Services/                     # Concrete business logic implementations
│   ├── WindowsPrinterDiscoveryService.cs
│   ├── PrintJobValidator.cs
│   ├── DocumentRendererResolver.cs
│   ├── FileCacheService.cs
│   ├── PrintEventHub.cs
│   ├── WindowsPrintService.cs
│   └── Renderers/
│       ├── PdfDocumentRenderer.cs        # WinRT Windows.Data.Pdf
│       ├── ImageDocumentRenderer.cs      # System.Drawing.Image
│       └── TextDocumentRenderer.cs       # Pagination & layout
├── Server/                       # Embedded Kestrel LAN Web Server
│   ├── PrintingWebServerHost.cs          # Minimal API routes & queue worker
│   ├── WebAssets.cs                      # In-assembly embedded resource loader & live-reload
│   └── Web/
│       └── index.html                    # Mobile-responsive web SPA & CSS/JS
├── CLI/                          # Command-line interface layer
│   ├── CommandLineParser.cs
│   ├── ParsedArguments.cs
│   └── CliHandler.cs
└── Interactive/                  # Console UI & Interactive Wizard
    ├── ConsoleUi.cs
    └── InteractiveWizard.cs
```

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
The published binary is available at `./publish/printman.exe`.

---

## 📄 License
This project is licensed under the MIT License — see [LICENSE](LICENSE) for details.

## 🤝 Contributing
Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to get started.

## 🔒 Security
For security concerns, please see [SECURITY.md](SECURITY.md) for our responsible disclosure policy.

---

## 🤖 Agent Instructions & Roadmap
For AI coding assistants and contributors, detailed architecture rules, SOLID boundaries, and security protocols are documented in [AGENTS.md](AGENTS.md).
