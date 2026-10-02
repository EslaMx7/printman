# Printman 🖨️

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)]()

A small, zero-dependency Windows tool to print documents from your terminal, an interactive menu, or directly from your phone over local Wi-Fi.

> *"A simple printing bridge for your Windows desk PC."*

---

## 💡 Why Printman?

> *"Do not replace reliable hardware. Modernize the access layer."*

* **The Story:**  
  In 2014, I bought an **HP LaserJet Professional P1102** printer using revenue from my first freelance projects. For more than 12 years, this printer moved with my family to every new home. The hardware has never broken down or failed. One toner cartridge provides three years of continuous use. It is durable, economical, and dependable.

* **The Problem:**  
  The printer has one major limitation: it requires a direct USB connection to a Windows computer. Drivers for macOS and Linux are difficult to find and configure.  
  During the school season, our children need printed homework and study sheets from school portals. The daily workflow was slow:
  1. Download the document on a phone or laptop.
  2. Transfer the file to the desktop computer.
  3. Log in to the desktop and start the print job manually.  
  
  This manual process made me the bottleneck for every document in the house. My wife needed a direct, self-service way to print school materials from her smartphone.

* **The Solution:**  
  Modern Wi-Fi printers are often fragile, expensive, and dependent on cloud accounts. Instead of replacing functional hardware, I built **Printman**.  
  Printman converts the host Windows machine into a lightweight, zero-dependency local print server (`printman serve`). It gives our 12-year-old USB printer instant wireless printing capabilities.  
  Now, my wife prints assignments directly from her phone browser in seconds. We removed household friction, prevented electronic waste, and kept a proven machine in service.

---

## 🌟 What It Does

- **Zero Extra Installs:** Built entirely on standard Windows APIs and the .NET runtime. No third-party packages or bloated drivers.
- **Prints Common Formats:** Handles PDF documents (crisp 300 DPI vector rendering), images (`.png`, `.jpg`, `.bmp`), and plain text or code files (`.txt`, `.csv`, `.md`, `.json`).
- **Phone-Ready Web UI:** Run `printman serve` to launch a mobile web page. Anyone on your home Wi-Fi can open it and print from their phone.
- **Two CLI Modes:** Pass command-line flags to print immediately, or run `printman` with no arguments to use a guided terminal wizard.
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

### 2. Printer Tools

```powershell
# List all connected printers and their status
printman.exe list

# Show supported paper sizes and hardware details for a printer
printman.exe info "HP LaserJet"

# View all available CLI flags
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
- **Print Queue:** Sends jobs one by one so the Windows spooler never locks up.
- **Automatic Storage Cleanup:** Keeps uploaded files in a local cache and clears old files automatically.
- **Live Progress:** Shows print progress on your phone in real time.

### 4. Interactive Guided Wizard

If you do not want to remember CLI commands, run `printman` without arguments:

```powershell
printman.exe
```

The wizard prompts you step-by-step:
1. Select or drag-and-drop your file.
2. Choose from a list of installed printers.
3. Select page ranges, paper size, and copies.
4. Review your settings and send the job.

---

## ⚙️ CLI Options Reference

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
- Windows 10 (1809+) or Windows 11
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

---

## 🤖 Agent Instructions
For AI coding assistants and contributors, architectural constraints and operational rules are documented in [AGENTS.md](AGENTS.md).
