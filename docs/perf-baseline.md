# Performance baseline

Measured figures against the seeded database (20 000 SKUs, 100 000 bill lines).

**These rows stay blank until `HW-T07`.** The absolute NFR-P1…P7 budgets are
measured once, on the shop's terminal, as part of the hardware-integration track
(`docs/09_HARDWARE_INTEGRATION.md`). A figure without the shop's hardware recorded
is not a figure — NFR-P6 in particular is about the actual low-powered terminal,
not a developer machine or a CI runner. The `P1-T16` software perf harness runs in
CI as a *relative regression guard* against whatever figures land here; it does not
populate them.

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
