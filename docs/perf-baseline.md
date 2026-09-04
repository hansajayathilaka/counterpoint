# Performance baseline

Measured figures against the seeded database (20 000 SKUs, 100 000 bill lines).
Update after every `/perf-gate` run. A figure without hardware recorded is not a figure —
NFR-P6 in particular is about the shop's actual low-powered terminal, not a developer machine.

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
