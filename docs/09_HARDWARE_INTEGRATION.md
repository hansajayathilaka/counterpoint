# Hardware Integration Track (HW)

**Duration:** ~1 week on site · **Tasks:** 10 · **Exit:** every peripheral and the shop
terminal itself are verified against the finished software, and the on-terminal
performance figures are recorded.

## Why this track exists

Counterpoint is built and accepted **against the Linux device fakes** first
(`FileReceiptPrinter`, `NullScale`, `FileKeyStore`, a temp-directory "USB" target).
Every automated acceptance test, every architecture test and every phase gate runs
without a single piece of shop hardware attached. This is deliberate: the software
is the hard part and the long pole, and none of it should wait on a courier
delivering a printer.

What the fakes **cannot** prove is exactly the set of week-one risks the original
plan front-loaded into Phase 0: does the actual ESC/POS printer honour the cut and
drawer-kick commands, does SQLCipher's native asset survive a self-contained publish
on the real machine, does the terminal cold-start inside NFR-P6 on low-powered
hardware, does a serial scale's protocol adapter match the unit on the counter.

This track collects all of that into one place, run **after the software is
feature-complete** (all of `P1-T16`, `P2-T12`, `P3-T09`, `P4-T08` green in CI) and
**before** `P5-T09` go-live. Each task here picks up the physical "Done when"
checkboxes that were removed from a software task in phases 0–5; the software task
kept only what a byte-stream snapshot or a fake-failure test can verify on Linux.

## What stays software-only (does **not** belong here)

- The ESC/POS renderer, the receipt intermediate representation, receipt templates,
  the `print_job` outbox and the `PrintWorker` — all snapshot-tested on Linux.
- The label layout and its byte stream.
- The backup snapshot / encrypt / restore pipeline and its `PRAGMA integrity_check`.
- The scale protocol adapters as pure parsers (feed them captured serial frames).
- The failure-injection harness in `P4-T08` — it drives the fakes.
- Every AC-01…AC-20 automated test. `HW-T10` re-runs the suite on the terminal; it
  does not replace it.

## The "software-complete" milestone

Declared when, on CI (Linux, fakes):

1. `dotnet test` green, including `ArchitectureTests` and every `AC*` class.
2. `P1-T16`, `P2-T12`, `P3-T09`, `P4-T08` all passed.
3. The app runs to the sales screen under the EF Core compiled model.
4. `docs/perf-baseline.md` still shows its budgets as **unmeasured** — that is
   correct; they are measured here.

At that point this track starts. `Q-H` (exact printer and scanner models) must be
answered and the hardware must be on site before `HW-T01`.

---

## Task template

Same shape as the phase documents:

```
### HW-T<NN> — <title>
**Depends on:** <task ids> · **Needs on site:** <hardware> · **SRS:** <ids>

**Context.**      Why this task exists.
**Do this.**      Numbered, concrete steps.
**Deliverables.** Files and artifacts that must exist when it is done.
**Done when.**    Checkable criteria. All must pass.
```

All HW tasks are `mode=human` in `.claude/state/deps.tsv`: they cannot be completed
unattended and the autopilot hands them back.

---

### HW-T01 — Windows spooler receipt printing and cash drawer
**Depends on:** P0-T05, P1-T11 · **Needs on site:** the shop's ESC/POS receipt printer, cash drawer · **SRS:** FR-7.1, FR-7.7, §10, §14.2

**Context.** Highest-uncertainty external interface. The renderer and templates are
already snapshot-verified; this proves the bytes actually drive the physical unit.

**Do this.**
1. Implement `IReceiptPrinter` → `WindowsRawPrinter` (`Devices/Printing/`): P/Invoke
   `OpenPrinter` / `StartDocPrinter` (datatype `RAW`) / `WritePrinter` /
   `EndDocPrinter`; enumerate installed printers. Guard with
   `OperatingSystem.IsWindows()` and `[SupportedOSPlatform("windows")]`.
2. Register it in the composition root in place of `FileReceiptPrinter` when running
   on Windows with a configured printer name.
3. Print the SRS §10.1 specimen receipt and a real completed sale's receipt.
4. Fire the drawer-kick (`ESC p 0 25 250`) on a cash tender.
5. Record any command the model gets wrong in `docs/adr/printer-quirks.md` and add a
   capability flag for it.

**Deliverables.** `WindowsRawPrinter`, updated composition root, `printer-quirks.md`,
photos of the printed specimen and sale receipts in the commissioning record.

**Done when.**
- [ ] A receipt matching §10.1 prints on the actual printer, correctly aligned, and cuts
- [ ] A real sale's receipt prints from the `print_job` outbox via `PrintWorker`
- [ ] The cash drawer opens on a cash tender and does not on a card tender
- [ ] Physically disconnecting the printer mid-sale: the sale still completes and the
      job sits `PENDING`/`FAILED` (AC-16 on hardware)
- [ ] The `.verified.txt` snapshots from `P0-T05`
      (`SpecimenReceiptTests.Srs_10_1_TheSpecimenBillRendersToTheCommittedByteStream.verified.txt`)
      and from `P1-T11` are unchanged by this work — a quirk is fixed with a
      `PrinterCapabilities` flag, never by editing the renderer
- [ ] Printer quirks documented

---

### HW-T02 — Barcode scanner bring-up
**Depends on:** P1-T09 · **Needs on site:** the shop's barcode scanner · **SRS:** FR-2.9, FR-3.1, NFR-P1

**Context.** The scanner is a HID keyboard wedge; little code, but the scan-to-line
path and its latency need confirming on the real device and the real screen.

**Do this.**
1. Configure the scanner for the shop's barcode symbologies and a suffix key
   (Enter). Record the config in the commissioning notes.
2. Verify the sales-screen scan-box focus behaviour and the scanner-input filter
   (rapid keystrokes + suffix = one scan) against real scans.
3. Time scan-to-line on the seeded database (20 000 SKUs) informally; a formal
   figure is `HW-T07`.

**Deliverables.** Scanner configuration record, a note in the commissioning log.

**Done when.**
- [ ] Scanning a seeded product adds its line on the real sales screen
- [ ] An unknown barcode shows the "not found" prompt, not an error
- [ ] Manual keyboard entry into the scan box still works alongside the scanner
- [ ] Scan-to-line feels within NFR-P1 (formal measurement deferred to `HW-T07`)

---

### HW-T03 — Label printer driver and verification
**Depends on:** P1-T12 · **Needs on site:** the shop's label printer, label stock · **SRS:** FR-2.10, FR-2.12

**Context.** `P1-T12` built the label layout and snapshot-tested its byte stream
with no printer available. This prints real labels.

**Do this.**
1. Implement the label `IReceiptPrinter`/`ILabelPrinter` binding for the actual
   model (raw spooler or vendor language), Windows-guarded.
2. Print a shelf label and a product label on the shop's label stock; check
   barcode scannability with the `HW-T02` scanner.
3. Record label dimensions and any margin offsets in settings.

**Deliverables.** Label printer binding, a printed-and-scanned label in the record.

**Done when.**
- [ ] A label prints at the correct size with a scannable barcode
- [ ] The `P1-T12` byte-stream snapshot is unchanged
- [ ] Label printer absent or unplugged degrades with a warning, no crash

---

### HW-T04 — Serial weighing scale driver
**Depends on:** P5-T05 · **Needs on site:** the scale, its serial cable / USB-serial adapter · **SRS:** §14.2

**Context.** `P5-T05` built `IScale` and one or more protocol adapters as pure
parsers over captured frames. This binds a real serial port.

**Do this.**
1. Implement `IScale` → serial via `System.IO.Ports`, Windows-guarded, selecting the
   adapter for the shop's model. COM port and baud in settings.
2. Read live weight into the quantity field for a weighed-item sale.
3. Confirm the `NullScale` fallback and manual entry when the scale is unplugged.

**Deliverables.** Serial `IScale` implementation, port settings, a weighed-sale
record.

**Done when.**
- [ ] A live weight reads into the quantity field correctly on the actual scale
- [ ] The reading matches the scale's own display to its resolution
- [ ] Disconnecting the scale falls back to manual entry with a warning; the sale
      completes
- [ ] The `P5-T05` adapter parser tests are unchanged

---

### HW-T05 — Windows key store and data-directory ACLs on the terminal
**Depends on:** P0-T03, P0-T07 · **Needs on site:** the shop terminal · **SRS:** NFR-S3, NFR-S6, NFR-M2

**Context.** Development uses `FileKeyStore` (never shipped). The real key path is
DPAPI + Windows Credential Manager, and it can only be verified on Windows.

**Do this.**
1. Confirm `IDatabaseKeyStore` → `WindowsDatabaseKeyStore` is selected on the
   terminal: 256-bit key generated on first run, DPAPI-protected (`CurrentUser`),
   stored in Credential Manager.
2. Reboot the terminal; confirm the app reopens the database with the stored key
   and no prompt.
3. Copy the database file to a second Windows machine; confirm it cannot be opened
   (key is machine/user bound).
4. Confirm `%ProgramData%\Counterpoint\{db,backups,logs}` ACLs from the installer:
   the cashier account can read/write its own data and cannot read the other
   accounts' profiles.

**Deliverables.** A commissioning note recording the key-store path, the reboot
test and the cross-machine test.

**Done when.**
- [ ] Database opens with the stored key after a reboot, no passphrase prompt
- [ ] The same file will not open on a different machine or user
- [ ] No secret appears in any config file or log (grep the logs)
- [ ] Data directory ACLs are as specified

---

### HW-T06 — Installer and clean-machine commissioning
**Depends on:** P0-T07 · **Needs on site:** a clean Windows 10 machine with no .NET, the shop terminal · **SRS:** NFR-M2

**Context.** `P0-T07` produces the self-contained publish and the Inno Setup
script. This proves the installer on a machine that has never had the toolchain.

**Do this.**
1. Build the installer from the `win-x64` self-contained ReadyToRun publish.
2. Install on a clean Windows 10 machine with no .NET runtime present.
3. Confirm it creates `%ProgramFiles%\Counterpoint`, the `%ProgramData%` tree with
   ACLs, a desktop shortcut and a working uninstaller.
4. Launch; confirm the app reaches the sales screen.
5. Record the installer size and the publish size.

**Deliverables.** `installer/Counterpoint.iss` verified, sizes recorded in
`docs/perf-baseline.md` under Packaging.

**Done when.**
- [ ] The installer runs on a clean Windows 10 machine with no .NET and the app starts
- [ ] `%ProgramData%\Counterpoint` is created with the specified ACLs
- [ ] The uninstaller removes the program and leaves the data directory intact
- [ ] Installer size and publish size recorded

---

### HW-T07 — On-terminal performance gate
**Depends on:** P1-T16, P3-T09, HW-T06 · **Needs on site:** the shop terminal, the seeded database · **SRS:** NFR-P1…P7, AC-18

**Context.** The one place NFR-P1…P7 become real figures. `P1-T16` built the seed
generator (20 000 SKUs, 100 000 lines) and a software perf harness that runs in CI
as a **relative regression guard only**. Absolute pass/fail against the budgets is
measured here, on the terminal the shop will actually use.

**Do this.**
1. Copy the seeded database (`artifacts/data/pos-perf.db`, from `bash scripts/seed.sh`)
   to the terminal.
2. Run the `/perf-gate` measurements on the terminal:

   | Requirement | Budget | Operation |
   |---|---|---|
   | NFR-P1 | 300 ms | Barcode scan to line on the bill |
   | NFR-P2 | 500 ms | Search results begin appearing |
   | NFR-P3 | 2 s | Bill save, stock update, print dispatch |
   | NFR-P4 | 1 s | Bill lookup by number |
   | NFR-P5 | 10 s | Any one-year report |
   | NFR-P6 | 10 s | Cold start to the sales screen |
   | NFR-P7 | — | No degradation at 500 000+ lines |

3. If a budget fails, diagnose before optimising — the order in `/perf-gate` §4
   (missing index, EF tracking on a read path, report reading raw tables, compiled
   model not wired, whole-collection UI rebuilds). Do not relax a budget.
4. Record every figure **and the terminal's hardware spec** in
   `docs/perf-baseline.md`. Flag any figure within 20% of its budget.

**Deliverables.** `docs/perf-baseline.md` fully populated with figures and hardware.

**Done when.**
- [ ] All seven budgets measured on the shop terminal against the seeded database
- [ ] NFR-P1…P6 within budget; NFR-P7 shows no degradation at 500 000 lines
- [ ] `docs/perf-baseline.md` records each figure and the exact hardware
- [ ] Any figure within 20% of budget is flagged as an early warning

---

### HW-T08 — Failure injection on real peripherals
**Depends on:** P4-T08, HW-T01, HW-T02, HW-T04 · **Needs on site:** the terminal and every peripheral · **SRS:** NFR-R1…R5, AC-16

**Context.** `P4-T08` built the software failure-injection harness against the
fakes. This repeats the scenarios by physically pulling cables.

**Do this.**
1. For each: printer unplugged mid-job, printer out of paper, scanner unplugged,
   drawer disconnected, scale unplugged, USB backup stick removed, network cable
   pulled, disk near-full — perform the action during a live sale and confirm the
   sale completes and the failure surfaces as a warning with a next step.
2. Reconnect each and confirm recovery (queued print jobs flush, backup resumes).

**Deliverables.** A commissioning checklist with each scenario signed off.

**Done when.**
- [ ] Every listed physical failure leaves the sale completed and the DB intact
- [ ] Each failure shows a plain-language warning with a next step (UI-06)
- [ ] Reconnecting each device recovers without a restart

---

### HW-T09 — Offline trading-day dress rehearsal
**Depends on:** P1-T16, P4-T08, HW-T07, HW-T08 · **Needs on site:** the terminal, all peripherals, a person to run the till · **SRS:** AC-13, AC-15

**Context.** A full simulated trading day on the shop hardware with the network
cable **physically unplugged**, plus the power-cut integrity test on the real
machine.

**Do this.**
1. Unplug the network cable. Run a full day's worth of sales, returns, a shift
   open and a shift close, backups, reports — every routine operation — with zero
   network.
2. During the day, cut power to the terminal mid-transaction at least 20 times
   (target 100 across the commissioning period); after each, restart and run
   `PRAGMA integrity_check` and `VerifyChainCommand`.
3. Record any functional loss or degradation.

**Deliverables.** A signed trading-day report; an integrity-check log with one line
per power-cut.

**Done when.**
- [ ] A full trading day completes offline with zero functional loss (AC-13)
- [ ] Every mid-transaction power cut leaves the database intact and the hash
      chains verifying (AC-15 on hardware)
- [ ] No operation degraded beyond its documented warning behaviour

---

### HW-T10 — Full AC-01…AC-20 run on the shop terminal
**Depends on:** P1-T16, P2-T12, P3-T09, P4-T08, P5-T08, HW-T01…HW-T09 · **Needs on site:** the terminal, all peripherals · **SRS:** §16, AC-01…AC-20

**Context.** The on-hardware acceptance record that `P5-T09` go-live requires. The
automated suite already proves each criterion on CI; this is the same suite plus
the manual criteria, executed on the machine the shop keeps.

**Do this.**
1. Run the automated `AC*` suite on the terminal against a copy of the seeded
   database. Record each result.
2. Execute the manual criteria (AC-13, AC-14, AC-16 hardware paths, AC-18) using
   the results from `HW-T07`, `HW-T08`, `HW-T09` and the `P4-T06` restore drill.
3. Produce the acceptance record: criterion, method, result, date, operator.

**Deliverables.** A completed AC-01…AC-20 acceptance record, filed with the
handover package (`P5-T07`, `P5-T09`).

**Done when.**
- [ ] All 20 acceptance criteria pass on the shop terminal, each recorded
- [ ] The record is in the handover package
- [ ] `P5-T09` can proceed

---

## Commissioning exit review

Before `P5-T09` go-live, confirm and write down:

| # | Question | Source |
|---|---|---|
| 1 | Printer, drawer, scanner, label printer, scale all verified? | HW-T01…T04 |
| 2 | Key store survives reboot; DB not portable to another machine? | HW-T05 |
| 3 | Clean-machine install verified; sizes recorded? | HW-T06 |
| 4 | All seven performance budgets met on the terminal; figures in `docs/perf-baseline.md`? | HW-T07 |
| 5 | Every physical failure leaves the sale intact? | HW-T08 |
| 6 | A full offline trading day with zero functional loss? | HW-T09 |
| 7 | AC-01…AC-20 acceptance record complete? | HW-T10 |
