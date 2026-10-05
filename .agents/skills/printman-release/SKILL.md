---
name: printman-release
description: >-
  Runbook and procedures to prepare, verify, and execute automated dual-package
  releases for Printman using Semantic Versioning and GitHub Actions.
---

# Printman Automated Release Runbook

This skill defines the end-to-end procedure for releasing a new version of Printman. 
The release pipeline is fully automated via the GitHub Actions workflow in [`.github/workflows/release.yml`](../../.github/workflows/release.yml).

Every push or merge to the `release` branch automatically calculates the next Semantic Version, extracts the curated changelog, builds both standalone and framework-dependent packages, and publishes a GitHub Release.

---

## 1. Pre-Release Verification

Before triggering a release, ensure the code builds cleanly and passes all smoke tests:

### Step 1.1: Verify Zero Warnings & Errors
Run a compilation check to ensure strict .NET and WinRT compatibility:
```powershell
dotnet build Printman.slnx -t:Compile
```
Expected result: `0 Warning(s), 0 Error(s)`.

### Step 1.2: Unit Tests & Coverage Ratchet
```powershell
dotnet test Printman.slnx --coverage --coverage-output-format cobertura --coverage-settings coverage.runsettings
./eng/check-coverage.ps1
```
Expected result: all tests pass (`0 failed`) and the coverage gate reports `COVERAGE GATE PASSED`. The floors in `eng/check-coverage.ps1` may only be raised, never lowered.

### Step 1.3: Headless Virtual Smoke Test
Never send automated tests to a physical printer. Always verify using a virtual printer (`Print to PDF` or `XPS`) and output to a temporary file:
```powershell
dotnet run --project src/Printman -- "tests/fixtures/test_sample.pdf" -printer "XPS" -pages 1:2 -output "test_release.xps"
Remove-Item "test_release.xps" -ErrorAction SilentlyContinue
```

---

## 2. Update the Changelog

Printman adheres to [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) in [`CHANGELOG.md`](../../CHANGELOG.md).

1. Open [`CHANGELOG.md`](../../CHANGELOG.md).
2. Move the completed items from `## [Unreleased]` into a new version block:
   ```markdown
   ## [1.1.0] - 2026-10-02

   ### Added
   - ...

   ### Changed
   - ...
   ```
3. Ensure a fresh, empty `## [Unreleased]` section remains at the top.
4. Commit the changelog update:
   ```powershell
   git add CHANGELOG.md
   git commit -m "docs(changelog): update changelog for upcoming release"
   ```

> **Note:** If changes remain under `## [Unreleased]` when the workflow triggers, the release pipeline automatically falls back to extracting `[Unreleased]` so release descriptions are never empty.

---

## 3. Semantic Versioning Rules

The release workflow determines the version bump automatically by inspecting commits since the last Git tag (`vX.Y.Z`):

| Commit Pattern | Bump Type | Example |
| :--- | :--- | :--- |
| `BREAKING CHANGE:` or `type!:` | **Major** | `v1.0.0` ➔ `v2.0.0` |
| `feat:` or `feat(...):` | **Minor** | `v1.0.0` ➔ `v1.1.0` |
| `fix:`, `docs:`, `chore:`, merge | **Patch** | `v1.0.0` ➔ `v1.0.1` |

*If no previous Git tags exist, the workflow initializes at `v1.0.0`.*

---

## 4. Triggering the Release

To trigger the automated release, merge or push to the `release` branch:

```powershell
# 1. Switch to or create the release branch from main
git checkout release 2>$null || git checkout -b release

# 2. Merge latest main branch
git merge main --ff-only

# 3. Push to remote release branch
git push origin release
```

---

## 5. What the Release Pipeline Executes

Once pushed to `release`, GitHub Actions runs [`.github/workflows/release.yml`](../../.github/workflows/release.yml) on `windows-latest`:

1. **Calculates Semantic Version & Git Tag:** Determines `$version` (e.g. `1.1.0`) and `$tag` (e.g. `v1.1.0`).
2. **Extracts Curated Changelog:** Reads the corresponding section from `CHANGELOG.md`.
3. **Builds Standalone Single-File Release:**
   ```powershell
   dotnet publish src/Printman/Printman.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$version -o ./dist/standalone
   ```
   Compresses to `printman-v$version-win-x64.zip` (includes standalone `printman.exe`, `README.md`, `LICENSE`).
4. **Builds Framework-Dependent Release:**
   ```powershell
   dotnet publish src/Printman/Printman.csproj -c Release -r win-x64 --self-contained false -p:Version=$version -o ./dist/portable
   ```
   Compresses to `printman-v$version-win-x64-framework-dependent.zip`.
5. **Creates GitHub Release:**
   Uses `gh release create` to publish the release with both assets attached, the extracted changelog, download recommendations, and git commit history.

---

## 6. Verification and Post-Release Sync

### Check Release Status
Monitor the workflow run or view the created release:
```powershell
gh run watch
gh release view
```

### Sync Main Branch
If any commits were created directly on `release`, merge them back to `main`:
```powershell
git checkout main
git merge release
git push origin main
```
