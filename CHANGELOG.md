# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Native Windows Print Spooler management (`winspool.drv`) via `IPrintQueueService` and `WindowsPrintQueueService` with zero third-party dependencies.
- Real-time printer hardware state interrogation: Paper Jam, Out of Paper, Offline, Door Open, Paused, and Busy diagnostics.
- Unified queue management combining in-flight rendering/pipeline jobs and native Windows Spooler jobs.
- Web SPA live spooler queue tab with live SSE updates, dynamic active count badge, and individual job cancel buttons.
- Emergency queue purge functionality (`POST /api/queue/purge` and `printman purge [printer]`).
- Duplicate print prevention safeguard in web interface alerting users when submitting documents already queued or printing on that printer.
- Headless CLI commands: `printman queue [printer] [--watch]`, `printman cancel <jobId>`, and `printman purge [printer]`.
- Interactive Wizard spooler queue management menu with live-refreshing ANSI watcher dashboard and hotkey controls (`[C]`, `[A]`, `[Q]`).
- Light and dark theme support with automatic OS preference detection, manual toggle control, and persistent preference.

### Changed
- Automatically clear uploaded files from web UI selection immediately upon print submission to eliminate double-clicks.
- Modernized web SPA design tokens with clean card, input, elevation, and typography styling.
- Extracted mobile web SPA to dedicated `Server/Web/index.html` file embedded directly into assembly binary via MSBuild `EmbeddedResource`.
- Implemented in-memory cached loader in `Server/WebAssets.cs` with development live-reload support in `#if DEBUG`.

## [1.0.0] - 2026-10-01

### Added
- Initial release of Printman
- CLI mode with full printing options (page range, paper size, copies, duplex, orientation, color, DPI)
- Interactive wizard mode for guided printing
- Mobile LAN web server (`serve` command) with PIN authentication
- PDF, image, and text document rendering
- Fuzzy printer name matching
- Server-Sent Events (SSE) for real-time print progress
- SHA-256 file deduplication and LRU cache eviction
- CSRF protection and file type whitelist for web uploads
- Zero third-party NuGet dependencies
