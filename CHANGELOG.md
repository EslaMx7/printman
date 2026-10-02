# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Light and dark theme support with automatic OS preference detection, manual toggle control, and persistent preference.

### Changed
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
