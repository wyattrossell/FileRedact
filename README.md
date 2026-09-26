# FileRedact

FileRedact is a Windows desktop application that removes personally identifiable information (PII)
from documents before they are shared with AI services such as ChatGPT or Claude, uploaded, or printed.
It loads PDFs, Word documents, text files and images, finds the PII categories defined by the FBI CJIS
Security Policy, highlights them for review, and writes a new PDF in which the confirmed items are
permanently blacked out.

It also installs a **"FileRedact" printer**. Printing to it from any program (Word, Outlook, a browser,
a records system) opens the printed document in FileRedact ready for review, the same way "Microsoft
Print to PDF" works.

## Features

- **Input formats**: PDF, DOCX/DOC/RTF/ODT (via Microsoft Word when installed, with a built-in fallback
  for DOCX and TXT), TXT, PNG/JPEG/TIFF/BMP images. Scanned pages and images are OCR'd with the OCR engine
  built into Windows 10/11, entirely offline.
- **Detection** (CJIS Security Policy §4.3 PII categories):
  names, dates of birth, Social Security numbers, driver's license / state ID numbers, passport numbers,
  street and PO Box addresses, phone numbers, email addresses, financial account and payment card numbers,
  vehicle identification numbers and plates, criminal-justice identifiers (FBI/UCN, SID, booking, inmate
  and offender numbers), place of birth, mother's maiden name, IP addresses, plus user-defined terms.
- **Review workflow**: findings are highlighted on the rendered pages and listed by category, with
  identical values merged into one row ("Rossell ×50 · 20 pages") whose single checkbox selects or clears
  every occurrence; expand the row to handle occurrences individually. Click highlights to toggle them,
  drag across several highlights to redact or un-redact them together, drag over plain text to redact an
  area, right-click to redact every occurrence of a value, and preview the result as black boxes before
  committing.
- **True redaction**: the output PDF is rebuilt from scratch. Every page is rasterised with the black boxes
  burned into the pixels; only the words that were *not* redacted are written back as an invisible text
  layer so the PDF stays searchable and AI tools can read it without OCR. Nothing from the original
  file (content streams, fonts, metadata, attachments) is copied, so redacted content cannot be recovered.
- **Output**: save as PDF, or print the redacted rendering after a print preview that shows every page
  exactly as it will come out of the printer.
- **Virtual printer**: "Install FileRedact printer" adds a printer that routes jobs into FileRedact.
  A small background watcher (notification-area icon) opens each job automatically.

## Building

Requirements: Windows 10 1809 or later, [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build FileRedact.sln
dotnet test FileRedact.sln
dotnet run --project src/FileRedact.App
```

To produce a distributable folder:

```powershell
dotnet publish src/FileRedact.App -c Release -r win-x64 --self-contained false -o publish
```

## Installing

Download `FileRedact-Setup-<version>.exe` from the [releases page](https://github.com/wyattrossell/FileRedact/releases)
and run it. The installer is self-contained (no separate .NET download), installs for the current user
without administrator rights, and offers to:

- create Start menu and desktop shortcuts,
- install the **FileRedact** virtual printer (this one step asks for administrator approval),
- start the print watcher when you sign in,
- add a **Redact with FileRedact** entry to the right-click menu of PDF, Word, text and image files.

Uninstalling from *Settings → Apps* removes the printer, the shortcuts and the context-menu entries, and
asks whether to delete your settings and received print jobs. A portable `FileRedact-win-x64.zip` is
also attached to each release for machines where an installer is not wanted.

Version 1.0.0 installed per-machine (Program Files). Newer installers detect that copy, remove it (one
administrator prompt) and install per-user, so that no later update ever needs administrator approval.

To build the installer yourself, install [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`) and run:

```powershell
.\installer\build-installer.ps1    # publishes win-x64 and writes publish\FileRedact-Setup-<version>.exe
```

## Using the virtual printer

1. Start FileRedact and click **Install FileRedact printer** (Windows asks for administrator approval).
2. In any program choose **File → Print**, select the **FileRedact** printer and print.
3. FileRedact opens the printed pages, scans them and shows the findings for review.

How it works: instead of shipping a custom (signed) printer driver, the printer reuses the
"Microsoft Print To PDF" driver that ships with Windows, attached to a local port whose name is a file
path (`C:\ProgramData\FileRedact\Inbox\FileRedact-print.pdf`). The spooler writes each job to that file
silently; FileRedact watches the folder, moves the finished file to
`%LocalAppData%\FileRedact\Received` and opens it. Installing the printer also registers
`FileRedact.exe --watch` to start with Windows so jobs are picked up even when the app is not open.
The printer can be removed from the same button, or with `tools/Install-FileRedactPrinter.ps1 -Uninstall`.

Requirements: the "Microsoft Print to PDF" Windows feature must be enabled (it is by default).

## Updates and releases

Updates are applied by the application itself; users never need to visit GitHub. Once a day (and on
demand via **Check for updates** in the toolbar or the notification-area menu) FileRedact asks the
GitHub Releases API for the latest release. When a newer one exists it downloads the installer in the
background, verifies its size and SHA-256 digest against the release metadata, and shows a single
question: **Install and restart** or **Not now**. Installing runs the setup silently (no wizard, no
administrator prompt), keeps the options chosen at installation, and starts FileRedact again when it is
finished. Only the public `api.github.com/repos/wyattrossell/FileRedact/releases/latest` endpoint and
the release asset URL are contacted; no document content or personal data is sent. The check can be
disabled by setting `"CheckForUpdates": false` in `%LocalAppData%\FileRedact\settings.json`.

The complete flow is exercised by `tools\Test-UpdateFlow.ps1`, which installs an old build, serves a fake
release feed from localhost (`FILEREDACT_UPDATE_URL` overrides the feed URL), triggers the in-app update
and checks that the new version restarts. `tools\Smoke-Test-UI.ps1` drives the main window through open,
review, save, print and update-check using UI Automation.

To publish a release, push a version tag; the GitHub Actions workflow builds, tests, publishes a
self-contained win-x64 build, compiles the installer and attaches both to the release:

```powershell
git tag v1.1.0
git push origin v1.1.0
```

The application compares its own assembly version (set from the tag by the workflow, or from
`Directory.Build.props` for local builds) with the release tag. Keep `<Version>` in
`Directory.Build.props` equal to the next release number so development builds do not offer an older
release as an "update".

## Project layout

```
src/FileRedact.Core     Detection engine, PDF text extraction, OCR, redaction writer, printer setup
src/FileRedact.App      WPF user interface, tray icon, single-instance handling, printing
tests/FileRedact.Tests  Detector unit tests and an end-to-end redaction test
installer/              Inno Setup script and build script for FileRedact-Setup-<version>.exe
tools/                  Stand-alone PowerShell script to install/remove the printer
samples/                A fictitious incident report for trying the application
```

Key types:

| Type | Purpose |
| --- | --- |
| `Detection/DetectionEngine` | Runs all detectors per page, resolves overlaps, maps text spans to word boxes |
| `Detection/Detectors/*` | One class per PII category; regex plus context labels, confidence scored |
| `Pdf/PdfTextExtractor` | PdfPig-based word extraction in reading order with rotation handling |
| `Pdf/RedactionWriter` | Rasterises pages (PDFium), paints boxes, assembles the new PDF (PDFsharp) |
| `Documents/DocumentConverter` | Normalises Word/text/image input to PDF (Word COM automation or fallback) |
| `Ocr/WindowsOcr` | Windows.Media.Ocr for pages without a text layer |
| `Printer/PrinterInstaller`, `Printer/InboxWatcher` | Virtual printer setup and job pickup |
| `Update/UpdateChecker` | Queries GitHub Releases, compares versions, downloads the release asset |

## Detection notes and limitations

- Structured identifiers (SSN, phone, email, card numbers, VIN, dates) are matched by format and
  validated where possible (SSN area/group rules, Luhn check for cards). Identifiers whose format varies
  by state (driver's licenses, FBI/SID numbers) are recognised from a nearby label such as "DL#" or
  "SID:".
- Names are found from role labels ("Victim:", "Defendant -"), honorifics and ranks ("Mr.", "Det."),
  the "LAST, FIRST M" layout used on law-enforcement forms, and a gazetteer of common given names
  followed by capitalised words. Once a name is found, its surname is also flagged wherever it appears
  in the document. The word lists live in `src/FileRedact.Core/Detection/Data` and can be extended.
- Dates without a date-of-birth label and IP addresses are shown but not pre-selected, because in most
  reports they are not PII.
- Automatic detection is an aid, not a guarantee. Reviewers should read the highlighted document before
  sharing it, and add anything missed with a drag selection or the "always redact" list.
- Text inside images on an otherwise text-based page is not OCR'd (OCR runs only on pages without a
  usable text layer). Print such documents to the FileRedact printer or save them as images first.

## Settings and data

- Settings (custom terms, output resolution): `%LocalAppData%\FileRedact\settings.json`
- Working copies of converted documents: `%LocalAppData%\FileRedact\Work` (safe to delete)
- Printed jobs: `%LocalAppData%\FileRedact\Received`
- Log: `%LocalAppData%\FileRedact\fileredact.log`

No document content ever leaves the machine.
