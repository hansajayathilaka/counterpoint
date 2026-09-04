---
name: avalonia-pos-screens
description: Use when building or reviewing Avalonia UI for this POS — sales screen, dialogs, viewmodels, keyboard handling or scanner input. Covers keyboard-first design, the MVVM boundary and the constraints of a low-powered shop terminal.
---

# Avalonia screens for the POS

## Keyboard-first is a requirement, not a preference

The mouse must be optional throughout (UI-01). A cashier with a customer waiting does not reach for a mouse.

Fixed function-key map (UI-02), always visible on screen:

```
F1 Help   F2 New sale   F3 Search   F4 Return   F5 Hold   F6 Recall
F7 Discount   F8 Customer   F9 Pay   F10 Reprint   F11 Stock   F12 Day close
Esc Cancel
```

Benchmark from the SRS (NFR-U2): a 5-item bill completes with **no mouse** and at most 4 keystrokes beyond the scans.

## The MVVM boundary

The viewmodel holds a `Bill` aggregate from `Domain` and asks it to recalculate. **It never computes a total itself.** Duplicating pricing logic in a viewmodel is how the screen and the receipt end up disagreeing.

`Counterpoint.Ui` may reference `Application` and `Domain` only — never `Infrastructure`, `Devices`, `Reporting` or `Backup`. An architecture test enforces this. If a viewmodel needs data, it goes through an Application service, which is also what makes authorisation real rather than cosmetic (AC-17).

## Scanner input

USB HID scanners are keyboard wedges — input arrives as ordinary keystrokes. Distinguish a scan from typing by inter-keystroke timing (scanner bursts are under 30 ms per character) plus a configurable prefix/suffix.

**Route scans to the scan field regardless of focus.** A mis-focused dialog silently eating a scan mid-bill is a real and infuriating failure mode.

## Performance on a low-powered terminal

The target machine is a dual-core with 4 GB. Budgets: scan to line 300 ms, search results 500 ms, cold start 10 s.

- Update the line collection **incrementally**. Never rebuild the whole `ObservableCollection` on each scan.
- Debounce search at ~120 ms and cancel the in-flight query on the next keystroke.
- Virtualise long lists (product search, report grids, audit log).
- Defer loading the reports and settings modules until navigated to — cold start is a hard requirement.
- Do not cache the catalogue in memory at startup. SQLite is already the cache, and eager loading is a direct hit to NFR-P6.

## Layout and legibility

- Fully usable at **1366×768** (UI-04). Test at that size, not on a developer's large monitor.
- The running total is the most prominent element on the sales screen (UI-03).
- Large, high-contrast text. This is a shop counter, not a design portfolio.
- Status bar (UI-09): user, shift, last backup, cloud status, printer status.

## Error messages

Plain language with a next step (UI-06). Never a stack trace, never "An error occurred."

```
Good: "The printer did not respond. The bill is saved - press F10 to reprint when it's back."
Bad:  "SocketException: connection refused"
```

The log gets the stack trace and a correlation id. The cashier gets the sentence.

## Numeric input

Accept both the keypad and the number row. Reject invalid characters silently (UI-10) — no error popup for a stray keystroke while a customer is waiting.
