# Performance baseline

Measured figures against the seeded database (20 000 SKUs, 100 000 bill lines).

**These rows stay blank until `HW-T07`.** The absolute NFR-P1…P7 budgets are
measured once, on the shop's terminal, as part of the hardware-integration track
(`docs/09_HARDWARE_INTEGRATION.md`). A figure without the shop's hardware recorded
is not a figure — NFR-P6 in particular is about the actual low-powered terminal,
not a developer machine or a CI runner.

The `P1-T16` software perf harness
(`tests/Counterpoint.Integration.Tests/Performance/PerformanceRegressionGuardTests.cs`) runs in
CI on every build as a *relative regression guard* for NFR-P1, P2, P3, P4 and P6, against the
same 20,000-SKU/100,000-historical-bill-line database as this table (built by
`PerformanceDatasetSeeder`, the engine behind `tools/SeedGenerator` and `scripts/seed.sh`). It
compares against **`docs/perf-regression-baseline.json`**, a separate, deliberately
machine-relative record of what the harness last measured — never against the blank budget rows
above, and it never writes to either file. A >20% drift from that JSON file fails the build; the
JSON file is updated by hand, deliberately, after a reviewed performance change, the same way this
table is updated by hand after `HW-T07`. NFR-P5 (a one-year report) and NFR-P7 (500,000+ lines)
are not part of the software guard: no report engine exists yet (Phase 3), and 500k lines is
absolute-budget territory for `HW-T07` alone.

| Requirement | Budget | Measured | Hardware | Date | Note |
|---|---|---|---|---|---|
| NFR-P1 scan to line | 300 ms | — | | | |
| NFR-P2 search results | 500 ms | — | | | |
| NFR-P3 bill save | 2 s | — | | | |
| NFR-P4 bill lookup | 1 s | — | | | |
| NFR-P5 one-year report | 10 s | — | | | |
| NFR-P6 cold start | 10 s | — | | | |
| NFR-P7 at 500k lines | no degradation | — | | | |

## Packaging

| Metric | Value | Date |
|---|---|---|
| Self-contained publish size | — | |
| Installer size | — | |
| Clean-machine install verified | — | |
