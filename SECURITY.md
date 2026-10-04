# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 1.x     | ✅ Yes    |
| < 1.0   | ❌ No     |

We recommend always using the latest release to ensure you have the most up-to-date security patches.

## Reporting a Vulnerability

We take the security of Printman seriously. If you discover a security vulnerability, please report it responsibly.

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, please report vulnerabilities via [GitHub Private Security Advisory](https://github.com/eslamx7/printman/security/advisories/new):

1. Navigate to the **Security** tab of the repository.
2. Click **"Report a vulnerability"**.
3. Provide a detailed description of the vulnerability, including:
   - Steps to reproduce
   - Potential impact
   - Affected versions
   - Any suggested mitigations or fixes

We will acknowledge your report within 72 hours and work with you to understand and address the issue before any public disclosure.

## Existing Security Measures

Printman implements several security measures to protect users, especially when the embedded LAN web server is exposed on a network:

### PIN Authentication & Session Security

- Constant-time PIN verification to prevent timing attacks.
- Ephemeral session tokens issued via HTTP-only / SameSite cookies (`printman_auth`) or `X-Printer-Pin` / `X-Session-Token` headers.
- All API routes and SSE streams require active authentication when PIN protection is enabled.

### Intranet CSRF Mitigation

- Custom anti-CSRF header enforcement (`X-Requested-With: Printman`).
- Strict `Origin` and `Referer` validation against local server binding hosts for state-changing requests.

### Strict File Type Whitelist & DoS Protection

- Extension whitelist: `.pdf`, `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff`, `.tif`, `.txt`, `.log`, `.csv`, `.json`, `.md`, `.pwg`, `.urf`.
- Kestrel request body limit and streaming byte counter enforcement (`MaxFileSizeBytes`).

### LRU Cache Quota Eviction

- `IFileCacheService` tracks disk usage against `MaxCacheSizeBytes` (default 500 MB).
- Oldest unreferenced files are evicted automatically when the cache exceeds the configured limit.

### Serialized Print Spooling Queue

- Background queue worker (`IPrintJobPipeline`) serializes concurrent print jobs from the web UI and network (IPP) clients.
- Prevents Windows GDI+/spooler race conditions and thread pool starvation.

### Generic Sanitized Error Responses

- Internal stack traces and file paths are stripped from API client responses.
- Detailed traces are logged to the console only.

### Network Printer Sharing (`--share`)

- Opt-in only. Without `--share`, no IPP endpoint is opened and nothing is announced on the network.
- The IPP endpoint (`/ipp/print/*`, port 631 by default) is **unauthenticated by design**: native print dialogs (AirPrint, Mopria, Windows) cannot send a PIN. Anyone on the local network can submit print jobs to shared printers.
- It serves only `/ipp/*` on its own port; the PIN-protected web UI and `/api/*` are unreachable through it.
- Requests from non-private source addresses (anything outside loopback, RFC 1918, link-local, CGNAT and IPv6 unique-local ranges) are rejected.
- Requests must use `Content-Type: application/ipp`, so browsers cannot submit cross-site form posts.
- Hardening limits:
  - attribute section capped at 64 KB, with every length bounds-checked
  - per-job document size limit
  - at most 50 pending jobs per printer
  - raster page dimensions validated before decoding
- Documents are identified by content (magic bytes) and only formats with a registered renderer are accepted.
- mDNS announcements are withdrawn (goodbye packets) when the server stops.

## ⚠️ Warning: `--no-auth` Flag

The `--no-auth` (or `--allow-anonymous`) flag disables PIN authentication, leaving the web server open to anyone on the network. **This is insecure for production use.**

Only use `--no-auth` in trusted, isolated development environments. Never expose an unauthenticated Printman server on a public or untrusted network.

## Security Best Practices for Operators

- Always run the server with PIN authentication enabled in production environments.
- Bind the server to a specific network interface (`--ip`) rather than `0.0.0.0` when possible.
- Keep the server behind a firewall and restrict access to trusted LAN segments.
- Regularly update to the latest version to receive security patches.
- Monitor server logs for unusual activity.
