---
name: escpos-receipts
description: Use when working on receipt printing, the ESC/POS renderer, receipt templates, the cash drawer, label printing or the print outbox. Covers the rendering pipeline, printer quirks and the never-block-the-sale rule.
---

# ESC/POS receipts and the print pipeline

## The rule above all others

**A printer failure must never block, delay or roll back a sale** (C-05, FR-7.8, AC-16).

```
sale transaction commits -> print_job row (PENDING)
                              |
                       PrintWorker (background)
                              |
     Scriban template -> receipt IR -> EscPosRenderer -> byte[]
                              |
        Win32 OpenPrinter / StartDocPrinter("RAW") / WritePrinter
                              |
        PRINTED   |   FAILED after 3 attempts -> status bar warning, F10 reprint
```

**No printer call is ever made inside a database transaction.** The test for this is simple and mandatory: the sale must complete with the printer physically unplugged.

## The intermediate representation

Templates render to an IR, and the IR renders to bytes. This keeps the layout owner-editable (FR-7.3, FR-10.8) and lets a `RasterText` node be added later without reworking callers.

```
TextLine(text, align, bold, doubleHeight)
Columns(left, right)      // right column is the money column, fixed width
Divider
Barcode(data, symbology)
QrCode(data)
Feed(n) | Cut | Kick
```

80 mm paper is 48 characters at font A. Wrap long descriptions; right-align amounts in a fixed column so the receipt stays readable.

## Common ESC/POS commands

| Purpose | Bytes |
|---|---|
| Initialise | `ESC @` |
| Align left / centre / right | `ESC a 0/1/2` |
| Bold on / off | `ESC E 1` / `ESC E 0` |
| Double height/width | `GS ! n` |
| Feed n lines | `ESC d n` |
| Partial cut | `GS V 1` |
| Drawer kick | `ESC p 0 25 250` |
| Native barcode | `GS k` |
| Raster image | `GS v 0` |

## Printer quirks are the norm

Printers claim ESC/POS and then differ on cut, codepage and native barcode support. Capability-flag anything the model gets wrong and record it in `docs/adr/printer-quirks.md`. Prefer the native `GS k` barcode; fall back to a ZXing-generated raster via `GS v 0` where the model handles it badly. Decide this once at setup and store the choice in settings — do not probe at print time.

## Non-Latin scripts

Thermal printers have no font for Sinhala, Tamil or most non-Latin scripts. If a second language is ever enabled (Q-C), those lines must be **rendered to a bitmap and printed as raster** — 3–5× slower per line, plus a font shaping step. The IR is designed to accept a `RasterText` node for this. Do not build it until the client asks.

## Do not share a layout engine with A4

Thermal receipts and A4 invoices are separate renderers over the same data model. QuestPDF does A4 and PDF export; the IR does thermal. Attempting to unify them produces something that is bad at both.

## Testing on Linux

Snapshot tests (Verify) compare the rendered byte stream against a committed `.verified.txt`. They test the renderer, not the hardware, and **must pass on Linux** so CI and Claude Code web are useful. The Linux `IReceiptPrinter` writes to `artifacts/receipts/*.bin`.

Hardware verification — does it physically print, cut, and open the drawer — is a manual step recorded in the task's "Done when" list. Do not mark a printing task done on snapshot tests alone.
