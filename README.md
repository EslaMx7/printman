# Printman 🖨️

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)]()
[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-FFDD00?style=flat&logo=buy-me-a-coffee&logoColor=black)](https://buymeacoffee.com/eslamx7)

A small, zero-dependency Windows tool to print documents from your phone over local Wi-Fi to old USB Windows printers.

---

## ⚡ TL;DR

Turn any old USB Windows printer into a wireless phone printer in 10 seconds:
1. **Start the server on your Windows PC:**
   ```powershell
   printman serve
   ```
2. **Open the link on your phone:** Connect via local Wi-Fi (e.g. `http://192.168.1.50:5000`).
3. **Print:** Send PDFs, images, or documents directly from your mobile browser to your Windows printer.

---

## 💡 Why Printman?

> *"Do not replace reliable hardware. Modernize the access layer."*

* **The Story:**  
  In 2014, I bought an **HP LaserJet Professional P1102** printer. For more than 12 years, this printer moved with my family to every new home. The hardware has never broken down or failed. One toner cartridge provides three years of continuous use. It is durable, economical, and dependable.

* **The Problem:**  
  The printer has one major limitation: it requires a direct USB connection to a Windows computer. Drivers for macOS and Linux are no longer supported.  
  During the school season, our children need printed homework and study sheets from school. The daily workflow was slow:
  1. Download the document on a phone or iPad.
  2. Transfer the file to the desktop computer.
  3. Log in to the desktop and start the print job manually.  

* **The Solution:**  
  Modern Wi-Fi printers are often fragile, expensive, and dependent on cloud accounts. Instead of replacing functional hardware, I built **Printman**.  
  Printman converts the host Windows machine into a lightweight, zero-dependency local print server (`printman serve`). It gives our 12-year-old USB printer instant wireless printing capabilities.  
  Now, my wife prints assignments from her phone and my kids can print their homework from their iPad directly.

---

## 🌟 What It Does

- **Zero Extra Installs:** Built entirely on standard Windows APIs and the .NET runtime. No third-party packages or bloated drivers (assuming the Printer driver is installed).
- **Prints Common Formats:** Handles PDF documents, images (`.png`, `.jpg`, `.bmp`), and plain text or code files (`.txt`, `.csv`, `.md`, `.json`).
- **Phone-Ready Web UI:** Run `printman serve` to launch a mobile web page. Anyone on your home Wi-Fi can open it and print from their phone.
- **Live Spooler & Hardware Diagnostics:** Interrogates the native Windows Spooler and hardware status flags in real time (Paper Jam, Out of Paper, Offline, Door Open, Busy, Paused).
- **Duplicate Prevention & Queue Control:** Proactively warns before submitting duplicate print jobs when jobs are pending/stuck; allows canceling individual jobs or purging all jobs in one click.
- **Two CLI Modes:** Pass command-line flags to print immediately, or run `printman` with no arguments to use a guided interactive terminal wizard.
- **Smart Printer Search:** Type partial printer names. For example, `HP Laser` automatically finds `HP LaserJet Professional P1102`.
- **Full Print Controls:** Set page ranges (e.g. `1:3`, `2,5`), paper sizes (`A4`, `Letter`), copies, orientation, duplex, and color mode.

---

## 🚀 How to Use It

### 1. Quick Terminal Printing

```powershell
# Print to your default printer
printman.exe "./doc.pdf"

# Print to a specific printer (partial name search)
printman.exe "./doc.pdf" -printer "HP Laser"

# Print specific pages (pages 1 to 3)
printman.exe "./doc.pdf" -pages 1:3

# Print with custom paper size and two copies
printman.exe "./doc.pdf" -size A4 -copies 2

# Full print job with duplex and orientation
printman.exe "./notes.pdf" -p "HP" -pages 1:2 -size A4 -copies 2 -duplex vertical -orientation portrait
```

### 2. Printer & Spooler Queue Tools

```powershell
# List all connected printers and their status
printman.exe list

# Show supported paper sizes and hardware details for a printer
printman.exe info "HP LaserJet"

# View the real-time Windows Print Spooler queue
printman.exe queue

# Continuous live watcher dashboard for a specific printer
printman.exe queue "HP Laser" --watch

# Cancel a stuck or unwanted print job by its Job ID
printman.exe cancel 12

# Purge and clear all pending/stuck jobs on a printer queue
printman.exe purge "HP Laser"

# View all available CLI flags and commands
printman.exe help
```

### 3. Print from Your Phone (`serve`)

Start the local web server on your Windows PC:

```powershell
# Start with an auto-generated 6-digit PIN
printman.exe serve

# Start with a specific port and custom PIN
printman.exe serve --port 8080 --pin 123456

# Start without PIN protection (open home access)
printman.exe serve --no-auth
```

Your console displays a local link with your PIN:
```text
  [WEB SERVER RUNNING]  Port: 5000
  [SECURITY] PIN Protected:  849201

  Open this link on your phone (same Wi-Fi):
    http://192.168.1.50:5000/?pin=849201
```

**Mobile Web Features:**
- **Clean Mobile UI:** Works directly in Safari, Chrome, or any mobile browser. Includes light and dark themes.
- **Simple PIN Lock:** Protects your printer from unintended network access.
- **Live Spooler Queue & Progress:** Dual-tab bottom panel (`Live Spooler Queue` and `Activity Log`) with live job progress, status badges, and single-click job cancellation.
- **Duplicate Prevention Safeguard:** Alerts and asks for confirmation before sending a file that is already pending or printing on that printer.
- **Automatic Upload Clearing:** Clears sent files from the selection immediately upon submission so users never accidentally tap "Print" twice.
- **Emergency Queue Purge:** Prominent `Purge All Jobs` button to flush a jammed spooler queue instantly.
- **Hardware Diagnostics:** Displays real-time printer status badges (Online, Paper Jam, Out of Paper, Offline, Paused).
- **Serialized Print Pipeline:** Sends jobs one-by-one so Windows GDI+/spooler race conditions never occur.
- **Automatic Storage Cleanup:** Fast SHA-256 caching with automatic LRU cleanup for old uploads.

### 4. Interactive Guided Wizard

If you do not want to remember CLI commands, run `printman` without arguments:

```powershell
printman.exe
```

The wizard prompts you step-by-step:
1. Print a document (drag & drop, printer picker, page selection, duplex/color options).
2. List installed printers.
3. Inspect detailed printer capabilities and paper sizes.
4. View & manage the Print Spooler Queue (live terminal watcher with `[C]` cancel and `[A]` purge shortcuts).
5. Start the mobile LAN web server.

---

## ⚙️ CLI Options Reference

### Commands
| Command | Aliases | Description | Example |
| :--- | :--- | :--- | :--- |
| `serve` | `server`, `--serve` | Start local LAN mobile web server | `printman serve --port 5000` |
| `queue` | `q`, `jobs` | Inspect spooler & pipeline queue (`--watch` for live dashboard) | `printman queue "HP" --watch` |
| `cancel`| `abort` | Cancel a print job by its integer Job ID | `printman cancel 14` |
| `purge` | `clear-queue` | Purge / clear all jobs on a printer queue | `printman purge "HP Laser"` |
| `list`  | `-list`, `--list` | List all installed printers | `printman list` |
| `info`  | `-info`, `--info` | Inspect printer capabilities & paper trays | `printman info "HP Laser"` |
| `interactive` | `-i` | Launch terminal guided wizard | `printman -i` |
| `help`  | `-h`, `--help` | Show command reference | `printman help` |

### Print Flags
| Option | Aliases | Description | Example |
| :--- | :--- | :--- | :--- |
| `-printer` | `-p`, `--printer` | Target printer name or partial match | `-p "HP Laser"` |
| `-pages` | `--pages` | Page range or individual pages | `-pages 1:3`, `-pages 2,5` |
| `-size` | `-s`, `--size` | Paper size name | `-size A4`, `-size Letter` |
| `-copies` | `-c`, `--copies` | Number of copies | `-copies 2` |
| `-orientation` | `-o`, `--orientation` | Orientation: `portrait`, `landscape`, `auto` | `-o landscape` |
| `-duplex` | `-d`, `--duplex` | Duplex mode: `simplex`, `vertical`, `horizontal` | `-d vertical` |
| `-color` | `--color` | Color mode: `color`, `mono` | `-color mono` |
| `-dpi` | `--dpi` | Resolution for PDF rendering (default: 300) | `-dpi 300` |
| `-fit` | `-nofit` | Fit content to printable area (default: true) | `-fit` |
| `-output` | `-out`, `--output` | Print to file (for virtual printers like XPS/PDF) | `-output "out.xps"` |

### Web Server Flags (`serve`)
| Option | Description | Default |
| :--- | :--- | :--- |
| `--port`, `-p` | Local port number | `5000` |
| `--ip`, `--bind` | Network binding address | `0.0.0.0` |
| `--pin` | Custom access PIN for mobile devices | Auto-generated 6-digit PIN |
| `--no-auth` | Disable PIN protection | Disabled |
| `--max-upload-mb`| Maximum file upload size in MB | `50` |
| `--cache-limit-mb`| Maximum disk cache size in MB before cleanup | `500` |

---

## 🔨 How to Build

### Requirements
- Windows 10 or Windows 11
- .NET 10 SDK

### Development Build
```powershell
# Build the project
dotnet build

# Run the CLI directly
dotnet run -- list
```

### Publishing Releases

#### Option A: Standalone Single Executable (Recommended)
Bundles the .NET runtime into a single executable. Runs on any Windows 10/11 computer without installing .NET:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/standalone
```

#### Option B: Framework-Dependent (Lightweight)
Creates a smaller binary package. Requires the target computer to have the .NET 10 Desktop Runtime installed:
```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish/portable
```

---

## 📄 License
This project is licensed under the MIT License — see [LICENSE](LICENSE) for details.

## 🤝 Contributing
Feedback and small fixes are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## ☕ Support
If Printman saved you time or rescued an old printer, consider [buying me a coffee](https://buymeacoffee.com/eslamx7) to support maintenance and future development.

---

## 🤖 Agent Instructions
For AI coding assistants and contributors, architectural constraints and operational rules are documented in [AGENTS.md](AGENTS.md).
