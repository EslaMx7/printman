# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
