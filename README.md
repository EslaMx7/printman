# Printman 🖨️

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Zero Dependencies](https://img.shields.io/badge/Dependencies-None%20(Single%20Binary)-2ea44f)]()
[![Privacy](https://img.shields.io/badge/Network-100%25%20Local%20%E2%80%A2%20No%20Cloud-blue)]()
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS%20%7C%20Raspberry%20Pi-0078D6)]()
[![Download](https://img.shields.io/badge/Download-Latest%20Release-0078D6)](https://github.com/EslaMx7/printman/releases/latest)
[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-FFDD00?style=flat&logo=buy-me-a-coffee&logoColor=black)](https://buymeacoffee.com/eslamx7)

Turn any USB printer into a **native Wi-Fi printer** for iPhone, iPad, Android, Windows, macOS, and Linux. Run one single executable on the computer your printer is plugged into—your printer immediately appears in every device's built-in print dialog with **no phone apps, no extra drivers, and no cloud accounts**.

🌐 **Website:** [printman.eslamx.com](https://printman.eslamx.com) · 📖 **Guides:** [AirPrint & Mopria Setup](https://printman.eslamx.com/guides/share-usb-printer-airprint-mopria-windows) · 📝 **Changelog:** [printman.eslamx.com/changelog](https://printman.eslamx.com/changelog)

---

## ⚡ Quick Start (60 Seconds)

1. **[Download Printman](#📥-download)** for your OS (Windows, macOS, Linux, or Raspberry Pi). Make sure your USB printer already works on that computer.
2. **Start sharing** from your terminal:
   ```bash
   # Windows
   printman.exe share

   # macOS, Linux & Raspberry Pi
   ./printman share
   ```
3. **Print from any device** on the same Wi-Fi—open **Print** and pick **`Printman - <your printer>`**.

<p align="center">
  <a href="docs/images/printman_desktop_cli_share_dark.png">
    <img src="docs/images/printman_desktop_cli_share_dark.png" alt="printman share announcing shared network printers and their ipp:// URLs in terminal" width="100%" />
  </a>
</p>

| Your Device | How to Print |
| :--- | :--- |
| **iPhone / iPad** | AirPrint — Share → Print → pick `Printman - <printer>` |
| **Android** | Mopria / Default Print Service — Print → select the printer |
| **Windows 10 / 11** | Settings → Bluetooth & devices → Printers & scanners → Add device |
| **macOS** | System Settings → Printers & Scanners → Add Printer (Bonjour) |
| **Linux / ChromeOS** | Appears automatically as a driverless (IPP Everywhere) printer |

> Need step-by-step help? Check the [AirPrint / Mopria guide](https://printman.eslamx.com/guides/share-usb-printer-airprint-mopria-windows), [Android guide](https://printman.eslamx.com/guides/mopria-print-service-usb-printer-android), [Chromebook guide](https://printman.eslamx.com/guides/print-from-chromebook-to-usb-printer), [macOS guide](https://printman.eslamx.com/guides/print-from-mac-to-usb-printer-wifi), or [discovery troubleshooting](https://printman.eslamx.com/guides/usb-printer-not-showing-up-wifi-troubleshooting).

---

## 🔒 Privacy, Security & What to Expect

Printing sensitive documents (bills, IDs, school records) requires trust. Here is exactly how Printman behaves:

- **100% Local, Zero Telemetry:** Printman never phones home, collects analytics, or uploads documents to any external server. Everything stays on your local Wi-Fi.
- **LAN-Only Access:** Connections from outside your private network (non-LAN IP addresses) are rejected automatically.
- **PIN Protection vs. Native Printing:**
  - `printman serve` (Web UI) is **PIN-protected** by default with an auto-generated 6-digit PIN.
  - `printman share` (AirPrint / Mopria) **cannot require a PIN** because phone and OS print dialogs do not support typing one—anyone on your Wi-Fi can print to shared printers, just like a physical Wi-Fi printer.
- **First-Run OS Warnings (Unsigned Binaries):** Printman is free open-source software without expensive corporate code-signing certificates, so your OS may warn you on first launch:
  - **Windows SmartScreen:** Click **More info** → **Run anyway**.
  - **macOS Gatekeeper:** Run `xattr -d com.apple.quarantine ./printman` once in Terminal.
  - You can verify every download against the SHA-256 `checksums.txt` published with every release.

---

## 📥 Download

Grab the latest standalone binary from [**GitHub Releases**](https://github.com/EslaMx7/printman/releases/latest) (no .NET installation required, except the optional *Portable* zip):

| Platform | Direct Binary / Installer | Archive | Notes |
| :--- | :--- | :--- | :--- |
| **Windows (x64)** ⭐ | `printman-v*-win-x64.exe` | `.zip` | Standalone single-file executable for 64-bit Windows |
| **Windows (ARM64)** | `printman-v*-win-arm64.exe` | `.zip` | Snapdragon X / Copilot+ ARM64 Windows PCs |
| **Windows (Portable)** | — | `.zip` | ~6 MB lightweight build (requires .NET 10 Desktop Runtime) |
| **macOS (Apple Silicon)** | `printman-v*-osx-arm64.dmg` / binary | `.tar.gz` | M1 / M2 / M3 / M4 Macs |
| **macOS (Intel)** | `printman-v*-osx-x64.dmg` / binary | `.tar.gz` | Intel-based Macs |
| **Linux (x86_64)** | `printman-v*-linux-x64` | `.tar.gz` | Standalone Linux x86_64 binary |
| **Raspberry Pi (ARM64)** | `printman-v*-linux-arm64` | `.tar.gz` | Raspberry Pi 3 / 4 / 5 (64-bit OS) |
| **Raspberry Pi (ARMv7)** | `printman-v*-linux-arm` | `.tar.gz` | Raspberry Pi 2 / 3 / 4 (32-bit OS) |
| **Verification** | `checksums.txt` | — | SHA-256 hashes for every file in the release |

<details>
<summary><strong>OS Setup & Firewall Notes (Windows, Linux/Pi, macOS)</strong></summary>

- **Windows:** Run `printman.exe share`. On first run, approve the Windows Firewall prompt for **Private** networks. If devices still cannot discover the printer, Printman prints the exact `netsh` commands needed at startup.
- **Linux / Raspberry Pi:** Printman talks to your local **CUPS** print system. Install and start CUPS if it is not already installed:
  - **Debian / Ubuntu / Raspberry Pi OS:** `sudo apt install cups cups-filters && sudo systemctl enable --now cups`
  - **Fedora / RHEL:** `sudo dnf install cups cups-filters && sudo systemctl enable --now cups`
  - **Arch Linux:** `sudo pacman -S cups cups-filters && sudo systemctl enable --now cups`
  - **Alpine Linux:** `sudo apk add cups cups-filters && sudo rc-service cupsd start`
  - Add your printer in desktop settings or `http://localhost:631`, mark the binary executable (`chmod +x ./printman`), and run `./printman share`. If a firewall is active, allow IPP and mDNS: `sudo ufw allow 8631/tcp && sudo ufw allow 5353/udp` (plus `5000/tcp` if using the Web UI).
- **macOS:** Ensure your printer works in System Settings, then run `./printman share` in Terminal (if blocked by Gatekeeper, run `xattr -d com.apple.quarantine ./printman` once).

</details>

---

## 🧭 Two Ways to Use Printman

| | `printman share` (Recommended) | `printman serve` (Web UI) |
| :--- | :--- | :--- |
| **How you print** | Any app's **native Print dialog** | Upload files in a **mobile/desktop browser** |
| **PIN Protection** | No (works like a standard Wi-Fi printer) | **Yes** (auto-generated 6-digit PIN) |
| **Best for** | Seamless everyday printing from phones & laptops | Printing files directly, checking queue & hardware alerts |

**Want both at once?** Run `printman share --web` (or `printman serve --share`).

### Mobile Web UI (`printman serve`)

Prefer uploading PDFs, images, or text files from a browser—or monitoring your printer queue from your phone? Start the local web server:

```bash
printman serve                        # Auto-generated 6-digit PIN on port 5000
printman serve --port 8080 --pin 123456
printman serve --no-auth              # Open home Wi-Fi access (no PIN)
```

<p align="center">
  <a href="docs/images/printman_mobile_web_dark.png">
    <img src="docs/images/printman_mobile_web_dark.png" alt="Printman Mobile Web Interface" width="300" />
  </a>
</p>

- **Supported Formats:** PDF, images (`.png`, `.jpg`, `.bmp`, `.gif`, `.tiff`), and plain text/code (`.txt`, `.csv`, `.md`, `.json`).
- **Live Spooler Queue & Diagnostics:** Real-time hardware status (Ready, Paper Jam, Out of Paper, Offline, Paused) and one-click job cancel or queue purge.
- **Duplicate Safeguard:** Warns before re-sending a file that is already pending on the printer.
- **Light & Dark Themes:** Automatically matches your device theme with a manual toggle.

---

## 🛠️ Everyday Commands & Always-On Setup

### Interactive Menu (No Memorizing Commands)
Double-click `printman.exe` or run `printman` with no arguments to open a simple guided menu:
1. Start the Web UI
2. Share your default printer on the network
3. Pick specific printers from a checklist
4. Start Web UI + network sharing together
5. More tools (print a file, list printers, inspect capabilities, live queue watcher)

### Share Specific Printers
```bash
printman share                       # Share default printer
printman share "HP Laser" "Canon"    # Share by partial name
printman share --select              # Interactive checklist
printman share --all                 # Share all installed printers
```

### Print or Manage Queue from Terminal
```bash
printman "./doc.pdf"                                 # Print to default printer
printman "./doc.pdf" -p "HP Laser" -pages 1:3 -copies 2
printman list                                        # List installed printers
printman info "HP Laser"                             # Show supported paper sizes & capabilities
printman queue "HP Laser" --watch                    # Live spooler queue monitor
printman cancel 12                                   # Cancel job ID 12
printman purge "HP Laser"                            # Clear all jobs on a printer
```

### Run 24/7 on Linux or Raspberry Pi (`systemd`)

<details>
<summary><strong>Click to view systemd service setup</strong></summary>

1. Copy the binary to `/usr/local/bin`:
   ```bash
   sudo cp printman /usr/local/bin/
   sudo chmod +x /usr/local/bin/printman
   ```
2. Create `/etc/systemd/system/printman.service`:
   ```ini
   [Unit]
   Description=Printman Universal AirPrint & Mopria Print Server
   After=network.target cups.service
   Wants=cups.service

   [Service]
   Type=simple
   ExecStart=/usr/local/bin/printman share --all
   Restart=always
   RestartSec=5
   Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

   [Install]
   WantedBy=multi-user.target
   ```
3. Enable and start:
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable --now printman
   ```

</details>

---

## ⚙️ CLI Reference

### Commands
| Command | Aliases | Description |
| :--- | :--- | :--- |
| `share` | `share-select` (`share --select`) | Share printers as native network printers (AirPrint / Mopria / IPP Everywhere) |
| `serve` | `server`, `--serve` | Start the local PIN-protected mobile web UI |
| `queue` | `q`, `jobs` | Inspect spooler & pipeline queue (`--watch` for live updates) |
| `cancel` | `abort` | Cancel a print job by integer Job ID |
| `purge` | `clear-queue` | Clear all pending jobs on a printer queue |
| `list` | `-list`, `--list` | List all installed printers and status |
| `info` | `-info`, `--info` | Show supported paper sizes and capabilities for a printer |
| `interactive` | `-i` | Launch the interactive terminal menu |
| `version` | `-v`, `--version` | Show the Printman version |
| `help` | `-h`, `--help` | Show full command help |

### Key Flags

<details>
<summary><strong>Print Flags, Server Flags (<code>serve</code>), and Sharing Flags (<code>share</code>)</strong></summary>

#### Print Flags
| Option | Aliases | Description | Example |
| :--- | :--- | :--- | :--- |
| `-printer` | `-p`, `--printer` | Target printer name or partial match | `-p "HP Laser"` |
| `-pages` | `--pages` | Page range or individual pages | `-pages 1:3`, `-pages 2,5` |
| `-size` | `-s`, `--size` | Paper size name | `-size A4`, `-size Letter` |
| `-copies` | `-c`, `--copies` | Number of copies | `-copies 2` |
| `-orientation` | `-o`, `--orientation` | `portrait`, `landscape`, `auto` | `-o landscape` |
| `-duplex` | `-d`, `--duplex` | `simplex`, `vertical`, `horizontal` | `-d vertical` |
| `-color` | `--color` | `color`, `mono` | `-color mono` |
| `-dpi` | `--dpi` | PDF rendering DPI (Windows, default `300`) | `-dpi 300` |
| `-fit` | `-nofit` | Fit content to printable area (default `true`) | `-fit` |
| `-output` | `-out`, `--output` | Print to file (`cupsfilter` PDF on Unix, virtual printer on Windows) | `-output "out.pdf"` |

#### Web Server Flags (`serve`)
| Option | Description | Default |
| :--- | :--- | :--- |
| `--port`, `-p` | Local web server port | `5000` |
| `--ip`, `--bind` | Network binding address | `0.0.0.0` |
| `--pin` | Custom access PIN | Auto-generated 6-digit PIN |
| `--no-auth` | Disable PIN protection | Disabled |
| `--max-upload-mb` | Maximum file upload size in MB | `50` |
| `--cache-limit-mb` | Maximum disk cache size in MB | `500` |
| `--share [printer]` | Also share a printer on the network (repeatable) | Off |
| `--share-select` | Also share printers from an interactive checklist | Off |

#### Network Sharing Flags (`share`)
| Option | Description | Default |
| :--- | :--- | :--- |
| `[printer ...]` | Printers to share (partial names work) | System default printer |
| `--select` | Pick printers from an interactive checklist | Off |
| `--all` | Share every installed printer | Off |
| `--web`, `--ui` | Also start the Web UI (accepts `serve` flags) | Off |
| `--ipp-port` | IPP network printing port | `631` (Windows), `8631` (Linux / macOS) |
| `--no-mdns` | Disable mDNS announcement (connect by IPP URL only) | Announce on |
| `--ip`, `--bind` | Network binding address | `0.0.0.0` |

</details>

---

## 💡 Why Printman?

> *"Do not replace reliable hardware. Modernize the access layer."*

In 2014, I bought an **HP LaserJet Professional P1102**. Over 12 years later, it still prints reliably on a single toner cartridge that lasts for years—but it only has a USB port, and modern macOS/Linux drivers are gone. Meanwhile, newer Wi-Fi printers are often expensive, fragile, or locked behind cloud accounts.

Instead of throwing away solid hardware, I built **Printman** so my wife and kids can print school homework directly from their phones and iPads over Wi-Fi without installing anything.

---

## 🔨 Building from Source

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/) (and CUPS at runtime on Linux/macOS/Raspberry Pi).

```bash
# Build & run unit tests
dotnet build
dotnet test
dotnet run --project src/Printman -- list

# Publish standalone single executable (replace <rid> with win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64, etc.)
dotnet publish src/Printman/Printman.csproj -c Release -r <rid> --self-contained true -p:PublishSingleFile=true -o ./publish/<rid>
```
> **macOS note:** Publish macOS binaries on a Mac so the SDK signs them (Apple Silicon refuses to run unsigned binaries).

---

## 📄 License, Contributing & Support

- **License:** MIT — see [LICENSE](LICENSE).
- **Contributing:** Issues and pull requests are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) (and [AGENTS.md](AGENTS.md) for AI coding assistant rules).
- **Support:** If Printman saved you time or rescued an old printer, consider [buying me a coffee](https://buymeacoffee.com/eslamx7) ☕.

> *Built with AI assistance using Antigravity powered by Gemini.*
