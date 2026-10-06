# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

A rendered version of this file is available at [printman.eslamx.com/changelog](https://printman.eslamx.com/changelog).

## [Unreleased]

## [1.1.0] - 2026-10-06

### Added
- Network printer sharing (`printman share [printer ...]`, or `printman serve --share [printer]` alongside the web UI): shared printers appear as `Printman - <printer>` in the native print dialogs of iOS/iPadOS (AirPrint), Android (Mopria / Default Print Service), Windows, macOS and Linux.
- IPP Everywhere / AirPrint print server (port 631, `--ipp-port`) supporting Print-Job, Validate-Job, Create-Job, Send-Document, Close-Job, Get-Jobs, Get-Job-Attributes, Cancel-Job, Cancel-My-Jobs, Identify-Printer and Get-Printer-Attributes.
- `printman share --select` (alias `share-select`; `serve --share-select` with the web UI): choose the printers to share from an interactive checklist; named printers start ticked.
- `printman share --all` shares every installed printer; `printman share --web` also starts the web UI.
- `printman share` runs network printing on its own: no web UI, API, PIN or web port.
- Built-in mDNS / DNS-SD responder (`_ipp._tcp` with `_universal` and `_print` subtypes); disable with `--no-mdns`.
- PWG Raster (`.pwg`) and Apple Raster (`.urf`) renderers.
- Read-only Windows Firewall check that prints the exact `netsh` commands when inbound printing traffic looks blocked.
- Hidden `--output-dir <dir>` server option that prints every job to a file (headless testing with virtual printers).
- Solution restructure: the app now lives in `src/Printman/` with `Printman.slnx`, `global.json` (SDK pin + Microsoft.Testing.Platform runner) and `Directory.Build.props`; existing file history is preserved.
- Unit test project `tests/Printman.Tests` (MSTest 4 + Microsoft.Testing.Platform) with 799 tests covering the IPP wire codec and request handler, DNS wire codec, print pipeline, file cache, shared printer registry, CLI parsing, models and DTOs.
- Code coverage ratchet: `coverage.runsettings` scopes measurement to unit-testable code and `eng/check-coverage.ps1` enforces 99% line / 92% branch coverage in CI, including pull requests; the build posts a sticky coverage report comment on PRs.

### Changed
- `printman version` / `-v` / `--version` reports the running version, and the startup banner shows it.
- Running `printman` without arguments now opens a small launcher menu (web UI, share default printer, choose printers to share, both); the print wizard, printer list and queue tools moved under "More tools". The wizard no longer prompts for port and PIN; use `serve` flags for custom values.
- The serialized print queue is now a shared `IPrintJobPipeline` service used by both the web UI and network printing.
- Startup banner lists addresses of adapters with a default gateway (Wi-Fi / Ethernet) before virtual and VPN adapters.

## [1.0.0] - 2026-10-03

### Added
- Initial public release of Printman: zero-dependency Windows CLI and mobile LAN printing utility.
- Mobile LAN Web Server (`serve` / `server`) with pairing PIN authentication and automatic network IP discovery.
- Mobile-first responsive web SPA with light and dark themes (automatic OS preference detection + manual toggle).
- Native vector PDF rendering at 300 DPI via Windows WinRT (`Windows.Data.Pdf`).
- Image rendering with automatic aspect-ratio scaling and page fitting (`.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif`, `.tiff`).
- Monospaced line-wrapped text rendering with automatic pagination (`.txt`, `.log`, `.csv`, `.json`, `.md`).
- CLI mode with complete print options: page ranges (`1:3`, `1,3,5`, `2-`), paper sizes (`A4`, `Letter`), copies, duplex, orientation, and color modes.
- Interactive terminal wizard (`printman`) with guided prompts and file drag-and-drop.
- Native Windows Print Spooler management (`winspool.drv`) with live hardware status flags (Paper Jam, Out of Paper, Offline, Door Open, Paused, Busy).
- Unified queue control with live monitoring (`printman queue`), individual job cancellation (`printman cancel <id>`), and full queue purging (`printman purge`).
- Real-time streaming updates to web clients using Server-Sent Events (SSE).
- Duplicate print prevention safeguard warning before submitting jobs already in the queue.
- Fuzzy printer name matching (e.g. `-printer "HP Laser"` finds `"HP LaserJet Professional P1102"`).
- SHA-256 file caching with automatic LRU storage quota eviction.
- Dual-package release pipeline: standalone single-file executable (zero setup required) and lightweight framework-dependent package.
- Zero third-party NuGet dependencies.
