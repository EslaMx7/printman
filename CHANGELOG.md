# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
