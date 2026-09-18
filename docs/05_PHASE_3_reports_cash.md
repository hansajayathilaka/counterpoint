# Phase 3 — Reports and Cash Discipline

**Duration:** 3 weeks · **Tasks:** 9 core (P3-T01–P3-T09) + 7 UI redesign (P3-T10–P3-T16, added
2026-09-15 via `/plan-feature`, see that section below) + 3 UI redesign v2 (P3-T17–P3-T19, added
2026-09-18 — the owner rejected the shipped P3-T10–P3-T16 visual style itself, see below) ·
**Exit:** the owner has visibility and the day closes cleanly and immutably.

## Scope

Full shift and cash management, X and Z reports, the complete report suite from SRS §9, rollup tables, exports, the audit log viewer and exception reporting.

## The theme of this phase

**Reconciliation.** FR-9.6 and AC-12 are not guidelines: net sales, tenders and Z reports must tie exactly for the same period. Every report in this phase is built against one shared query layer so there is one definition of "net sales", not five.

**Blocker:** Q-A (500 vs 1 000 bills/day design target) decides how aggressive the rollup strategy needs to be.

---

### P3-T01 — Shift lifecycle and cash management
**Depends on:** P1-T14 · **Est:** 2d · **SRS:** FR-8.1, FR-8.2, FR-8.6, FR-8.7

**Context.** Phase 1 built shift open and recovery. This adds the cash movements that make the drawer reconcilable.

**Do this.**
1. Cash in (float top-up, owner deposit) and cash out (petty expense, supplier payment, banking), each with a reason from a configurable list and an optional printed slip (FR-8.2).
2. Expected drawer calculation, defined once and used by both the X and Z reports:
   `expected = opening_float + cash sales − cash refunds + cash in − cash out`
3. Owner authorisation for cash out above a configurable threshold.
4. Cash movement history within the shift, visible to the cashier for their own shift.
5. `no sale` drawer open — owner authorised, audited (FR-7.7).

**Deliverables.** Cash movement service and UI, expected-cash calculator, cash slips, no-sale flow.

**Risks.** Two implementations of the expected-cash formula, one in X and one in Z. It must be a single method with a single test.

**Done when.**
- [ ] Cash in and out are recorded with reason, user and timestamp
- [ ] Expected cash matches a hand-worked example including refunds, cash in and cash out
- [ ] Cash out above the threshold requires owner authorisation and is audited
- [ ] A no-sale drawer open writes an audit row
- [ ] The expected-cash formula exists in exactly one place (verified by review and by both reports using the same service)

---

### P3-T02 — X report
**Depends on:** P3-T01 · **Est:** 1d · **SRS:** FR-8.3, RPT-04

**Context.** A mid-shift snapshot that changes nothing. The important property is that it is non-clearing — taking one must be free of side effects.

**Do this.**
1. X report content: sales count and value, returns, discounts, tax breakdown, tenders by type, cash movements, expected drawer, current shift duration.
2. Rendered to the thermal printer and to screen; exportable.
3. Available to the cashier for their own shift.

**Deliverables.** X report query, layout, print.

**Risks.** Accidentally mutating shift state. Add a test that takes 10 X reports and asserts the shift row is byte-identical afterwards.

**Done when.**
- [ ] X report figures match hand-computed values on a seeded shift
- [ ] Taking an X report changes no data whatsoever (asserted)
- [ ] It prints correctly on the thermal printer
- [ ] A cashier can take one for their own shift but not for another user's

---

### P3-T03 — Z report, shift close and rollups
**Depends on:** P3-T02 · **Est:** 2.5d · **SRS:** FR-8.4, FR-8.5, FR-8.8, AC-11, NFR-P5

**Context.** The most consequential transaction in the system after a sale. It locks a period permanently and it is where the rollup tables are written.

**Do this.**
1. Close flow: display expected cash, prompt for a physical count (denomination breakdown optional), compute variance, require a note when variance exceeds a configurable threshold.
2. Close transaction, all in one:
   - write `closed_at`, `counted_cash`, `expected_cash`, `variance`, `status = CLOSED`
   - build `daily_sales_summary` and `daily_product_summary` for the business date
   - audit row
   - `print_job` for the Z report
   - trigger a backup (FR-11.1)
3. The closed-shift insert trigger (already in the schema) prevents any later posting into that shift.
4. A Z report can never be deleted or re-run (FR-8.8) — the service has no such method, and re-close is rejected.
5. Variance history retained and charted over time (FR-8.6).

**Deliverables.** Z report, close transaction, rollup builders, variance history.

**Risks.** Rollups drifting from the raw data if a correction is later posted into an earlier date. Corrections go into the *current* period per FR-8.8; add a rollup-verification command that recomputes and compares, run monthly.

**Done when.**
- [ ] AC-11: X and Z reports are produced; Z variance is computed correctly against a deliberately mis-counted drawer, and the shift locks
- [ ] Attempting to post a sale into a closed shift is rejected by the database
- [ ] Re-running or deleting a Z report is impossible through any code path
- [ ] Rollup rows match a recomputation from raw data exactly
- [ ] A backup is taken automatically on close
- [ ] Variance history displays across 30 seeded shifts

---

### P3-T04 — Report query layer
**Depends on:** P3-T03 · **Est:** 2d · **SRS:** FR-9.1–9.6, NFR-P5

**Context.** One shared layer so that "net sales" has exactly one definition. This is what makes AC-12 achievable rather than a recurring bug.

**Do this.**
1. `Reporting/Queries/` with Dapper queries returning DTOs. Date-range filter with the presets from FR-9.1 (today, yesterday, this week, this month, last month, this year, custom).
2. Canonical definitions, written down in `docs/report-definitions.md` and implemented once:
   - **Gross sales** = sum of line totals before discount, excluding tax
   - **Net sales** = gross − discounts − returns, excluding tax
   - **COGS** = sum of `sale_line.unit_cost × qty_base` − returned equivalent
   - **Gross profit** = net sales − COGS
   - **Tender total** = sum of `payment.amount` for the period
3. Query routing: ranges wholly in closed periods read rollups; ranges touching the open shift read raw tables and union.
4. Owner-only projections exclude cost and margin fields entirely for cashier sessions (AC-17).
5. Every query parameterised, no string concatenation.

**Deliverables.** Query layer, canonical definitions document, routing logic.

**Risks.** Rollup/raw union double-counting at the boundary. The boundary is the business date of the open shift; test a range that spans it.

**Done when.**
- [ ] Every definition in `report-definitions.md` has exactly one implementation
- [ ] A range spanning closed and open periods returns the same figures as the same range computed from raw data only
- [ ] A one-year range returns in under 10 s on the seeded database (NFR-P5)
- [ ] Cashier-session DTOs contain no cost or margin field

---

### P3-T05 — Sales, returns and profit reports
**Depends on:** P3-T04 · **Est:** 2.5d · **SRS:** §9 RPT-01, RPT-02, RPT-03, FR-9.4

**Context.** The reports the owner actually opens.

**Do this.**
1. **RPT-01 Sales summary**: by day, by bill, by hour-of-day; bill count, average bill value, gross, discount, tax, net.
2. **RPT-02 Sales by item / category / brand**: quantity, net, COGS, margin, ranked.
3. **RPT-03 Profit report**: net sales, COGS, gross profit and margin percentage by period, category and item. Owner only.
4. Returns report: by reason, by item, by disposition, unlinked vs linked, with rate against sales.
5. Drill-down from summary to bill list to individual bill.

**Deliverables.** Four report screens with drill-down.

**Risks.** Margin computed from the current cost instead of the snapshot cost. Use `sale_line.unit_cost` always.

**Done when.**
- [ ] Each report's totals match a hand-computed figure on a fixed seeded dataset
- [ ] Profit uses the COGS snapshot, verified by changing a product's cost after a sale and confirming the report does not move
- [ ] Cashier sessions cannot open RPT-03 (service-level test)
- [ ] Drill-down from a summary row reaches the correct bill

---

### P3-T06 — Stock, tax and cash reports
**Depends on:** P3-T04, P2-T11 · **Est:** 2d · **SRS:** §9 remaining RPT-*, RPT-05, NFR-L1

**Context.** The rest of the §9 catalogue, including what the accountant and the tax authority need.

**Do this.**
1. Stock valuation, stock movement / item stock card, low stock and reorder, slow-moving, damage and adjustment summary.
2. Tax report: taxable and non-taxable sales, tax collected by rate, for the period — in the format the shop's accountant needs (Q-02).
3. **RPT-05 Tender / cash reconciliation**: tenders by type for the period, tied to Z reports.
4. Shift and variance history report.
5. Supplier purchase summary.

**Deliverables.** The remaining §9 reports.

**Risks.** Tax reported on a different basis (accrual point, inclusive handling) from what the accountant expects. Confirm Q-02 before building, not after.

**Done when.**
- [ ] Every report listed in SRS §9 exists and returns correct figures on seeded data
- [ ] The tax report reconciles to the sum of `sale_line.tax` for the period
- [ ] RPT-05 tender totals equal the sum of Z report tenders for the same period
- [ ] The stock card for one item reconstructs its balance history exactly from the ledger

---

### P3-T07 — Export and print for all reports
**Depends on:** P3-T05, P3-T06 · **Est:** 1.5d · **SRS:** FR-9.2, NFR-M6

**Context.** Uniform behaviour across every report, built once.

**Do this.**
1. A shared export service: CSV (CsvHelper), XLSX (ClosedXML), PDF (QuestPDF), driven by the report DTO and column metadata — not written per report.
2. Print any report to the A4 printer with a consistent header (shop name, report name, period, run time, run by).
3. Full data export (NFR-M6): products, stock, sales, returns, customers, suppliers to CSV, plus a plain unencrypted SQLite copy on demand.

**Deliverables.** Export service, report print layout, full-export command.

**Risks.** Money exported as a scaled integer or as a locale-formatted string that Excel misreads. Export as a plain decimal number with a period separator and let the sheet format it.

**Done when.**
- [ ] Every report exports to CSV, XLSX and PDF with matching totals
- [ ] Exported money values open as numbers in Excel, not text
- [ ] The full data export completes and re-imports into a fresh database
- [ ] Report print headers carry shop name, period, run time and user

---

### P3-T08 — Audit log viewer and exception reporting
**Depends on:** P3-T04 · **Est:** 1.5d · **SRS:** FR-9 (exceptions), NFR-S8, NFR-L2

**Context.** The owner's shrinkage and error control. The SRS positions cash variance patterns as the shop's main indicator of loss (FR-8.6); exceptions are the rest of that picture.

**Do this.**
1. Audit log viewer: filter by date, user, action, entity. Read-only, with no delete or edit affordance anywhere.
2. Exceptions report gathering: bill cancellations, over-limit discounts, unlinked returns, non-returnable overrides, no-sale drawer opens, negative-stock sales, adjustments above threshold, price changes, cash variances beyond threshold, open-item sales.
3. `VerifyChainCommand` exposed in the UI: verifies the `sale` and `audit_log` hash chains and reports the first break with its row id.
4. Exception counts on the owner dashboard.

**Deliverables.** Audit viewer, exceptions report, chain verification UI.

**Risks.** The exceptions report becoming noise. Make each category thresholded in settings so the owner can tune it.

**Done when.**
- [ ] Every exception category listed above appears with correct counts on seeded data
- [ ] The audit viewer has no path to modify or delete a row
- [ ] Chain verification passes on clean data and correctly identifies a deliberately tampered row
- [ ] A cashier cannot open the audit viewer

---

### P3-T09 — Phase 3 acceptance gate
**Depends on:** all P3 trading/reporting tasks (P3-T01–P3-T08) · **Est:** 1.5d · **SRS:** AC-11, AC-12, AC-18, NFR-P5

> This gate is scoped to reconciliation and reporting correctness. It does not depend on, and is
> not blocked by, the UI redesign tasks P3-T10–P3-T16 below — those are a presentation-layer
> workstream that can run before, after or interleaved with P3-T02–P3-T08 at the team's discretion
> (see the sequencing note at the head of that section).

**Context.** AC-12 is the credibility test for the whole system with the owner's accountant. It gets a generative test, not a single example.

**Do this.**
1. Acceptance tests `AC11`, `AC12`, `AC18`.
2. **Reconciliation generative test**: simulate 500 random trading days of sales, returns, exchanges, cancellations, cash movements and shift closes. For every day and every month, assert:
   - RPT-01 net sales == sum of tenders in RPT-05 == sales minus returns in RPT-02
   - rollups == recomputation from raw tables
   - `sum(payment.amount)` == `sum(sale.total) − sum(sale_return.total_refund)`
3. Extend the `P1-T16` performance harness with the report operations (NFR-P5) over a one-year range at the Q-A target volume; keep it a CI regression guard, not an absolute-budget gate.
4. Re-run all Phase 1 and 2 acceptance tests and the regression guard.

**Deliverables.** Reconciliation test suite, the report operations added to the performance harness.

**Risks.** The generative test failing on one day in 500 and being dismissed as a rounding artefact. It is not. Every failure here is a real defect in discount allocation, tax rounding or the rollup boundary.

**Done when.**
- [ ] AC-12 holds to the cent across 500 simulated days
- [ ] The performance harness covers every report over a one-year range and shows no >20% regression
- [ ] Rollups match raw recomputation for every simulated month
- [ ] Phase 1 and 2 gates still green

> AC-18 as an absolute pass/fail — every NFR-P1…P7 budget met on the shop terminal — is proven in **`HW-T07`**, not here.

---

## UI redesign (added 2026-09-15)

Added by `/plan-feature` in response to an owner feature request ("the current UI is not user
friendly at all — revamp the whole UI"). Reported defects, confirmed by inspection of the current
codebase before writing these tasks:

1. **No light mode.** `App.axaml` sets `RequestedThemeVariant="Default"` with a bare `FluentTheme`
   and no theme-switching UI — there is no way for the owner to choose a variant even though
   Avalonia supports one.
2. **The right-hand side panel is unreadable in one theme.** `SalesWindow.axaml`'s side panel
   (`Border Background="#F3F4F6"` with `#D1D5DB`/`#6B7280`/unstyled child text) and several other
   elements hardcode raw hex colours instead of theme-aware brushes, so whichever `ThemeVariant`
   is active, some hardcoded region clashes with it.
3. **Labels disappear once a field is filled.** Every catalogue and settings text field
   (`CustomerTabView.axaml`, `CategoryTabView.axaml` and others) uses `Watermark="..."` as its
   only label — confirmed by direct inspection. A watermark is placeholder text, not a label; SRS
   UI-06 ("plain language") and UI-10 rely on the user knowing what a field is *while it holds a
   value*, which the current pattern does not support.
4. **Create and edit forms are indistinguishable.** The same inline form, bound to whichever row
   is selected, is shared by a `_New` button and a `_Save` button with no heading, mode indicator
   or confirmation naming the record — confirmed in `CustomerTabView.axaml`, `CategoryTabView.axaml`
   and the same pattern elsewhere. There is nothing on screen that says whether the next Save
   creates a new customer or overwrites the selected one.

**Scope decision on the owner's proposed "2 separate UIs."** Read literally, a second UI could
mean a second terminal or a second installed process — that would be OS-01 (`docs/Counterpoint_
Requirements.md` §15: "Multiple cashiers working simultaneously, or a second till... Core
constraint of this project") and would break constraints C-01/C-02 and ADR-001's whole basis
(`docs/POS_Architecture_Design.md` §2, §16 R4: "a second terminal or LAN sync is ever requested...
that breaks C-01 and needs a real server tier"). **That reading is out of scope and is not what is
built below.** What is built instead is two navigation shells inside the same single process,
single `IHost`, single SQLite database, gated by the role of the one active session — which is
exactly what SRS **UI-11** already calls for ("the back office must be visually distinct from the
sales screen so the cashier cannot confuse the two") and which the two roles in §3.3 (Cashier,
Owner/Admin) already support. See P3-T13's own risk note: if implementation ever drifts towards a
second executable, a second database file or a second running instance, stop — that is a
different, out-of-scope system.

**New requirement ids** (additions to the SRS in `docs/Counterpoint_Requirements.md`, not yet
folded into that signed document — flagged to the owner in the plan-feature summary):

| ID | Requirement |
|---|---|
| UI-13 | The application must support both a light and a dark visual theme, user-selectable, with every screen legible and adequately contrasted in both. |
| UI-14 | Every data-entry field must display a persistent, always-visible label identifying what it captures, independent of whether the field currently holds a value. Placeholder/watermark text is not a substitute for a label. |
| UI-15 | Every create, edit and delete action must go through one reusable dialog pattern whose heading is unambiguous about which action and which record it is acting on. |
| NFR-U4 | No screen may rely on a hardcoded colour value that bypasses the active theme; every screen must remain legible when the theme is switched between Light and Dark. |
| AC-21 | Every screen renders with adequate contrast in both the Light and Dark theme (verified by an automated contrast-ratio test, not by opinion). |
| AC-22 | Every labelled input field's label remains visible before, during and after the field contains a value, across every screen. |
| AC-23 | Every add/edit/delete action uses the shared dialog component, and each dialog's heading states unambiguously whether it is creating a new record or editing an identified existing one. |
| AC-24 | A cashier-role session and an owner-role session, on the same install and the same database, present visually and navigationally distinct shells (UI-11), with no path from the cashier shell to an owner-only screen that bypasses the existing Application-layer role check. |

**Sequencing note.** P3-T10–P3-T13 are foundational (tokens, dialog shell, field control, shell
split) and are worth pulling forward before P3-T05–P3-T08 build their new report screens, so those
screens are built once, on the new components, rather than built plain and retrofitted immediately
after. P3-T14–P3-T16 retrofit already-shipped Phase 0/1 screens and can run at any point relative
to the rest of Phase 3 without blocking or being blocked by it — see the note on P3-T09 above. This
is a sequencing recommendation for whoever schedules `/next-task`, not a dependency encoded in the
tasks themselves.

---

### P3-T10 — Theme tokens: light and dark mode
**Depends on:** P0-T01, P1-T03 · **Est:** 2d · **SRS:** UI-03, UI-04, NFR-M1, UI-13, NFR-U4

**Context.** `App.axaml` today has `RequestedThemeVariant="Default"` and a bare `FluentTheme` with
no way to choose a variant, and colours are hardcoded per-view rather than drawn from a shared
palette (see the defects list above). This task builds the token system everything else in this
section is retrofitted onto; it does not yet touch the rest of the app.

**Do this.**
1. `Styles/Tokens.Light.axaml` and `Styles/Tokens.Dark.axaml`: semantic `DynamicResource` brushes
   (e.g. `PanelBackgroundBrush`, `PanelBorderBrush`, `PrimaryTextBrush`, `SecondaryTextBrush`,
   `AccentBrush`, `WarningBrush`, `TotalHighlightBrush`) — no screen ever references a raw hex
   value again; it references a semantic key.
2. Wire `RequestedThemeVariant` to a new `ui.theme_variant` setting (`Light`/`Dark`/`System`,
   default `System`) read through the existing settings framework (`SettingKeys`/
   `SettingDefaults`/`SettingsSerializer`, P1-T03's pattern) — do not invent a second settings
   mechanism.
3. Add a `Display` tab to Settings (new `DisplaySettingsView`/`DisplaySettingsViewModel`, same
   pattern as the existing nine `Settings/*View.axaml`) with a theme picker that switches
   `Application.Current!.RequestedThemeVariant` live and persists the choice. Build this one
   screen from the tokens from the start.
4. A contrast-ratio test over the documented semantic brush pairs (arithmetic on the resolved
   colours, not a screenshot) asserting a documented minimum ratio in both variants.

**Deliverables.** `Styles/Tokens.Light.axaml`, `Styles/Tokens.Dark.axaml`, `ui.theme_variant`
setting wired through the existing settings framework, `DisplaySettingsView`/ViewModel, contrast
test.

**Risks.** This task proves the system on one screen; it does not fix the rest of the app. Every
other view keeps its hardcoded hex until P3-T14–P3-T16 retrofit it — that is a known, explicit gap
to this task, not a silent one.

**Done when.**
- [ ] Switching the theme in the Display settings tab changes every semantic brush's resolved
      colour immediately, with no restart
- [ ] The Settings window (all ten tabs) is legible in both Light and Dark, confirmed by the
      contrast test
- [ ] `ui.theme_variant` persists across a restart
- [ ] `App.axaml` and every `Settings/*View.axaml` file contain zero raw hex colour literals
      (verified by a grep-based test)

---

### P3-T11 — Reusable add/edit/delete dialog framework
**Depends on:** P3-T10 · **Est:** 2.5d · **SRS:** UI-05, UI-06, UI-15, AC-23

**Context.** Every catalogue/settings screen shares one inline form bound to whichever row is
selected, with a `_New` button and a `_Save` button and nothing on screen naming the action or the
record — the confirmed "misleading edit and create forms" defect. This task builds the one shell
every such screen is retrofitted onto in P3-T15; it converts exactly one screen itself as proof.

**Do this.**
1. `IDialogService` in `Counterpoint.Ui` (interface only — the composition wiring for whatever
   implements it stays in `Counterpoint.App`, per the project-boundary rule in `CLAUDE.md`), with
   `ShowEditDialogAsync<TViewModel>(DialogMode mode, string subjectDescription, ...)` returning a
   typed result.
2. One `EditDialogWindow.axaml` shell: a header that renders "New {Entity}" for
   `DialogMode.Create` or "Edit {Entity} — {SubjectDescription}" for `DialogMode.Edit` — driven
   only by the explicit mode value, never inferred from whether a field happens to be blank — a
   consistent content grid, and a Save/Cancel footer. A delete confirmation reuses the same shell
   with a Confirm/Cancel footer that names the specific record (UI-05).
3. The dialog is modal only to its owner window (a back-office dialog never blocks `SalesWindow`).
4. Built from the P3-T10 tokens — no new hardcoded colour anywhere in this task.
5. Convert the Category screen (`CategoryTabView`/`CategoryTabViewModel`) to use
   `EditDialogWindow` as the one worked proof-of-concept; remove its old always-visible inline
   form.

**Deliverables.** `IDialogService`, `EditDialogWindow`, `DialogMode` enum, the converted Category
screen.

**Risks.** Scope creep into converting every screen now — that is P3-T15's job. This task delivers
the reusable piece plus exactly one proof.

**Done when.**
- [ ] `EditDialogWindow` renders "New X" for Create and "Edit X — <identifying text>" for Edit,
      driven by an explicit mode value
- [ ] A delete action through the same shell names the specific record before it happens (UI-05)
- [ ] The Category screen is fully converted; its old inline always-visible form is gone
- [ ] The dialog is fully keyboard-operable: correct Tab order, Enter saves, Escape cancels (UI-01)

---

### P3-T12 — Reusable labelled form-field control
**Depends on:** P3-T10 · **Est:** 1.5d · **SRS:** UI-06, UI-14, AC-22

**Context.** Fields across the app use `Watermark` as their only label, so the label disappears the
moment a value is entered — the confirmed "cannot see the label once filled" defect.

**Do this.**
1. A `LabeledField` control (or control family) composing a persistent label `TextBlock` with an
   input and an optional inline validation line, built from the P3-T10 tokens.
2. Variants for text, numeric (reusing the existing `NumericInputViewModel` pattern), combo/picker
   and checkbox inputs, so every field type already in use has a drop-in replacement.
3. Watermark is retained only as supplementary example text inside an already-labelled field,
   never as the sole label.
4. A view-inspection test helper: walk a view's visual tree and assert every text/numeric/combo
   input has an associated, currently-visible label element. P3-T15/P3-T16 reuse this helper.

**Deliverables.** `LabeledField` control family, the view-inspection test helper.

**Risks.** A control nobody is scheduled to adopt is not a fix — P3-T15/P3-T16 are not optional
follow-on, they are how this defect actually gets closed everywhere else.

**Done when.**
- [ ] The control shows its label when the field is empty, focused, filled and disabled
- [ ] The view-inspection test fails against the current (unconverted) `CustomerTabView` and
      passes once a view is converted
- [ ] The Category screen (P3-T11's proof-of-concept) uses `LabeledField` throughout, with no
      remaining watermark-as-label field

---

### P3-T13 — Role-based navigation shell (cashier vs back office)
**Depends on:** P1-T02, P3-T10 · **Est:** 2d · **SRS:** UI-11, NFR-S2, AC-17, AC-24

**Context.** UI-11 already requires the back office to be visually distinct from the sales screen.
Today it is not: Catalogue, Settings, Users and Purchasing are separate `Window`s reachable from a
flat button row on `SalesWindow`, hidden or shown by the same `CanManageCatalogue`/
`CanChangeSettings`/`CanManageUsers`/`CanManagePurchasing` flags already computed on
`SalesViewModel` — the surrounding chrome, colours and layout are shared, not distinct. **This task
is one process, one `IHost`, one composition root, one database — it switches which shell is shown
for the one active session's role, the same way `LoginWindow` already hands off to `SalesWindow`.
It is explicitly not a second terminal, a second installed program or a second running instance.**

**Do this.**
1. A `BackOfficeShellWindow` with its own status bar, a distinct colour accent drawn from the
   P3-T10 tokens (not a distinct theme system — the same Light/Dark choice applies inside it), and
   menu/tile navigation to Catalogue, Settings, Users, Purchasing and Labels.
2. `SalesWindow` keeps only what a cashier needs, plus one "Back office" entry point visible only
   when at least one of the existing `CanManage*`/`CanChangeSettings` flags is true, opening the
   shell.
3. Both shells read the same single current-session/user context already established by P1-T02;
   no second login, no second connection pool, no second process. Add an architecture-style test
   asserting the composition root still creates exactly one `IHost`.
4. Opening a privileged screen from the back-office shell re-checks the session's role at the
   Application layer (defence in depth alongside the existing `[RequiresRole]` checks) rather than
   relying on the shell's navigation alone to keep the cashier out.

**Deliverables.** `BackOfficeShellWindow`, updated `SalesWindow`/`SalesViewModel` navigation, the
single-`IHost` architecture test.

**Risks.** The task most likely to be misread as "build a second application." It is not — if
implementation starts to need a second executable, a second database file, or a second running
instance, stop and say so; that is out of scope (OS-01) and not what is asked for here.

**Done when.**
- [ ] A cashier-only login never sees the back-office shell or its entry point
- [ ] An owner login sees the entry point, and every screen inside the shell is visually distinct
      from the sales screen per UI-11
- [ ] The architecture test proves exactly one `IHost`/process/database connection set regardless
      of which shell is open
- [ ] Opening a privileged screen re-checks the session's role at the Application layer, not only
      by hiding the button

---

### P3-T14 — Retrofit: sales screen and side panel
**Depends on:** P3-T10, P3-T12, P3-T13 · **Est:** 2d · **SRS:** UI-01, UI-03, UI-09, NFR-U4

**Context.** `SalesWindow.axaml`'s side panel is where the reported "right side panel is not
compatible with dark mode" defect concretely lives: a hardcoded light background
(`#F3F4F6`/`#D1D5DB`) with unstyled child text that only reads correctly against one theme
variant.

**Do this.**
1. Replace every hardcoded hex colour in `SalesWindow.axaml` (status bar, total banner, side
   panel, discount/warning colours) with the P3-T10 semantic tokens.
2. Convert every side-panel field (customer search, discount amount, open-item entry, opening
   float, and the rest) to `LabeledField` (P3-T12).
3. Move the "Back office" navigation onto the P3-T13 entry point; the flat button row for
   Catalogue/Settings/Users/Purchasing/Labels moves into the shell.
4. This is a visual/structural retrofit only — the F1–F12 keyboard bindings, scan-box focus
   behaviour and sale-building logic from P1-T09/P1-T10 do not change.
5. A contrast test of the side panel and total banner in both Light and Dark.

**Deliverables.** Retrofitted `SalesWindow.axaml`, the contrast test.

**Risks.** Regressing keyboard-only operation while re-templating. Re-run every existing P1-T09/
P1-T10 test unmodified — none of them should need to change if this stayed visual-only.

**Done when.**
- [ ] Zero raw hex colour literals remain in `SalesWindow.axaml`
- [ ] The side panel passes the contrast test in both Light and Dark
- [ ] Every existing P1-T09 and P1-T10 test still passes, unmodified
- [ ] The function-key row and scan-box focus behaviour are unchanged, confirmed by their existing
      tests

---

### P3-T15 — Retrofit: catalogue and settings screens
**Depends on:** P3-T11, P3-T12, P3-T13 · **Est:** 3d · **SRS:** UI-05, UI-06, UI-14, UI-15, AC-22, AC-23

**Context.** The remaining six catalogue tabs (Brand, UOM, Tax class, Supplier, Customer, Product;
Category and Import are handled by P3-T11's proof-of-concept and separately below) and the nine
settings views share the exact inline-form-plus-New/Save pattern behind both reported defects.
This is the bulk of the retrofit surface.

**Do this.**
1. Convert each remaining catalogue tab's inline form into `EditDialogWindow` (New opens Create
   mode with blank defaults; selecting a row and choosing Edit opens Edit mode pre-filled, titled
   with the record's identifying text) and the Import tab's mapping step onto `LabeledField` where
   it uses free text today.
2. Convert every field on every one of these screens to `LabeledField`.
3. Move these screens into the `BackOfficeShellWindow` (P3-T13) navigation.
4. Run the P3-T12 view-inspection test and a "every dialog states its mode" test across all nine
   screens (Category + Import + the six others).

**Deliverables.** Nine converted screens, test coverage extending the P3-T11/P3-T12 helpers over
all of them.

**Risks.** The largest single task in this set. If it threatens to exceed 3 days, split the PR
(catalogue tabs, then settings tabs) rather than cutting a corner on any one screen.

**Done when.**
- [ ] Every one of the nine screens opens a Create dialog and an Edit dialog with an unambiguous,
      correctly worded header
- [ ] Every field on every one of the nine screens passes the P3-T12 view-inspection test
- [ ] No screen still shows an inline always-visible New/Save form for its master data
- [ ] Every existing P1-T03/P1-T04/P1-T05/P1-T06 test behind these screens still passes unmodified
      (presentation-only change; the underlying services are untouched)

---

### P3-T16 — Retrofit: remaining windows and UI redesign acceptance gate
**Depends on:** P3-T14, P3-T15 · **Est:** 2.5d · **SRS:** AC-21, AC-22, AC-23, AC-24, NFR-U4

**Context.** Login, the first-run wizard, user admin, purchasing, label printing, the print queue
and the restore wizard are the remaining hand-built windows. This task closes the retrofit and
proves the redesign as a set of acceptance criteria, the way every other phase closes with a gate.

**Do this.**
1. Retrofit `LoginWindow`, `FirstRunWizardWindow`, `UserAdminWindow`, `PurchaseOrderWindow`,
   `LabelPrintWindow`, `PrintQueueWindow` and `RestoreWizardWindow` onto the tokens/dialog/field
   framework, and into the `BackOfficeShellWindow` navigation where a session already exists
   (`LoginWindow` and the first-run wizard stay outside any shell — no session exists yet at
   either).
2. Add the four new acceptance test classes: `AC21_EveryScreenIsLegibleInBothThemes`,
   `AC22_EveryFieldLabelIsAlwaysVisible`, `AC23_EveryEditActionUsesTheSharedDialogWithAnUnambiguousHeader`,
   `AC24_CashierAndOwnerShellsAreVisuallyAndNavigationallyDistinct`.
3. A repository-wide grep-based architecture test: zero raw hex colour literals anywhere under
   `src/Counterpoint.Ui/Views/**/*.axaml` outside `Styles/Tokens.*.axaml`.
4. Update the printable one-page cheat sheet (UI-12) if any navigation path changed.

**Deliverables.** Seven converted windows, four new acceptance test classes, the hex-literal
architecture test, updated cheat sheet.

**Risks.** None of this should touch `Application`/`Domain`/`Infrastructure`. If a retrofit seems
to need a service change, that is a pre-existing view-doing-business-logic bug worth flagging, not
something to fix inside this task.

**Done when.**
- [ ] AC-21 through AC-24 pass as automated tests
- [ ] The repository-wide grep test finds zero raw hex colour literals outside the token
      dictionaries
- [ ] Every window under `src/Counterpoint.Ui/Views/` is listed in the PR description as converted
      or explicitly out of scope with a reason
- [ ] `dotnet test` is green, architecture tests are green, and the app still starts to the sales
      screen (`CLAUDE.md` definition of done)

---

## UI redesign v2 — Kiosk Tile direction (added 2026-09-18)

P3-T10–P3-T16 shipped and closed the four confirmed defects (no light mode, an unreadable panel,
vanishing labels, ambiguous create/edit forms), but the owner reviewed the result and rejected the
visual style itself: "still... the same style... I do not think the current style is good...
Redesign everything... do not use old POS style." Three concrete mockups (not a text description)
were built and shown for comparison — **Kiosk Tile** (bold, high-contrast, big tap-target tiles, a
workshop-branded palette instead of a generic dashboard blue), **Minimal Dashboard**, and **Command
Rail** — and the owner chose **Kiosk Tile**. The owner also asked, separately and explicitly, that
the cashier screen show no backup/cloud/printer status at all, and called the back-office screen
(a bare `UniformGrid` of default-styled `Button`s under a 6px accent stripe) unacceptable.

**This is a restyle and a layout rebuild, not a reopening of P3-T10–P3-T16's architecture.** The
token/theme-variant infrastructure (P3-T10), the reusable dialog shell (P3-T11), the `LabeledField`
family (P3-T12) and the single-process role-gated shell split (P3-T13) are sound and stay exactly
as built — P3-T17 restyles the *values* inside the existing token dictionaries and adds the kiosk
typography/component styles on top of that same infrastructure; P3-T18/P3-T19 rebuild the two
shells' *layouts* on top of it. Nothing here touches `Application`/`Domain`/`Infrastructure`,
schema, or the stock ledger.

**Requirement change, owner-approved, not yet written into the signed document.** SRS **UI-09**
currently reads "The status bar must permanently show: logged-in user, shift status, last backup
time, cloud backup status, printer status" — written when the status bar was assumed to be one
thing on one screen. The owner has now explicitly asked that the cashier never see backup/cloud/
printer status. UI-09 should be amended to: the cashier's status bar shows only logged-in user and
shift status; last-backup time, cloud backup status and printer status move to the back-office
status/dashboard area exclusively. **`docs/Counterpoint_Requirements.md` is permission-protected
from automated edits** (it is the signed client document), so this amendment is recorded here and
in `docs/08_TASK_INDEX.md` as owner-approved and pending — the owner (or someone with write access
to that file) still needs to make the edit by hand; P3-T18 implements the behaviour but does not
touch that file. This is **not** a reversal of UI-13 (light+dark, user-selectable) — both Kiosk
Tile token variants stay fully implemented; the owner picked the direction's palette and layout,
not a return to dark-only.

**Existing FR-9.7 Dashboard panel is out of scope here.** `SalesViewModel` already has an
on-demand Dashboard panel (opened by a button, not a reserved UI-02 key) built on
`IDashboardQueries`/`DashboardSummary` — today's sales, bill count, average bill, cash in drawer,
low stock, last backup. That panel is not the always-visible status bar the owner objected to, and
nothing here asks for it to move or change. P3-T19 reuses `IDashboardQueries` for the back-office
stat-card row instead of inventing a new query; it does not touch the Dashboard panel itself.

---

### P3-T17 — Kiosk visual language: palette, type and base component styles
**Depends on:** P3-T10 · **Est:** 2d · **SRS:** UI-13, NFR-U4

**Context.** This task restyles the existing `Tokens.Light.axaml`/`Tokens.Dark.axaml` semantic
brushes to the chosen Kiosk Tile palette and adds the typography and base component styles that
direction needs. It does not touch any screen's layout — P3-T18/P3-T19 do that on top of what this
task delivers.

**Do this.**
1. Redefine `WindowBackgroundBrush`, `PanelBackgroundBrush`, `PanelBorderBrush`, `PrimaryTextBrush`,
   `SecondaryTextBrush`, `AccentBrush`, `WarningBrush`, `TotalHighlightBrush` in both token
   dictionaries to the kiosk palette: **Dark** — charcoal surfaces (`#14171B`/`#1E2228`/`#262B33`),
   warm off-white ink (`#F4F1EC`), a Milwaukee-red accent (`#E11D2E`), mint good/amber warn.
   **Light** — an equivalent light-workshop palette at the same contrast ratios (light warm-grey
   surfaces, dark charcoal ink, the same red accent) so **UI-13 is preserved, not reverted**: both
   variants stay fully implemented and user-selectable, only their *values* change. Add whatever
   new semantic tokens the kiosk layout needs (e.g. a raised-surface brush for tiles/cards, a
   `GoodBrush` alongside the existing `WarningBrush`).
2. Bundle three open-source (SIL OFL — licence-clear to redistribute) font families as embedded
   Avalonia font assets under `Assets/Fonts/`, registered via `avares://` `FontFamily` resources —
   **never a network font load**; this is an offline-first app and the fonts must ship inside the
   package: **Barlow Condensed** (display/headers, 700/900), **Work Sans** (body, 400/500/600),
   **IBM Plex Mono** (every money/quantity column — Avalonia has no `font-variant-numeric`, so use
   the monospace face directly wherever figures must align). Expose them as role-based theme
   resources (`DisplayFontFamily`, `BodyFontFamily`, `NumeralFontFamily`) so screens bind to a role,
   never a literal font name.
3. Add `Styles/Kiosk.axaml`: reusable `ControlTheme`/`Style` resources for a big-tile button, a
   stat/total card, and an F-key chip, merged into `App.axaml` after the token dictionaries, so
   P3-T18/P3-T19 apply them by style key instead of repeating markup per screen.
4. Extend `ThemeTokenContrastTests` to the new palette values (WCAG AA 4.5:1 minimum stays the
   bar) in both variants.

**Deliverables.** Restyled `Tokens.Light.axaml`/`Tokens.Dark.axaml`, three embedded font assets and
their font-role resources, `Styles/Kiosk.axaml`, extended contrast test coverage.

**Risks.** Bundling fonts grows the installed app's size — acceptable for a Windows desktop
install; the risk is bundling them in a way that silently reaches the network at runtime instead —
don't. Do not touch any screen's layout in this task.

**Done when.**
- [ ] Both Light and Dark variants render the kiosk palette and pass the WCAG AA contrast test
- [ ] Barlow Condensed, Work Sans and IBM Plex Mono are bundled as app assets; nothing in the
      codebase loads a font over the network
- [ ] `Styles/Kiosk.axaml` exists and is merged into `App.axaml`
- [ ] No existing test regresses; zero raw hex colour literals introduced

---

### P3-T18 — Cashier screen: kiosk redesign, cashier-only status bar
**Depends on:** P3-T17, P3-T12, P3-T13 · **Est:** 2.5d · **SRS:** UI-01, UI-02, UI-03, UI-09
(amended), NFR-U4

**Context.** Two things land in this task: the UI-09 status-bar split the owner asked for, and the
Kiosk Tile layout rebuild of `SalesWindow` itself (a re-colour of the existing layout is what the
owner already rejected once, in P3-T14 — this is a genuine layout change, not a second coat of
paint on the same structure).

**Do this.**
1. Remove `StatusBackupText`/`StatusCloudText`/`StatusPrinterText`/`StatusPrintQueueText` from
   `SalesWindow`'s status bar bindings entirely. Keep the underlying `SalesViewModel` properties
   and their refresh logic untouched — P3-T19 binds them on the back-office side — only their
   presence on this screen is removed. Keep `StatusUserText`/`StatusShiftText` on the cashier bar.
2. Rebuild the sale screen on the P3-T17 kiosk styles: a prominent total card
   (`TotalHighlightBrush`, `DisplayFontFamily` for the label, `NumeralFontFamily` for the amount)
   replacing the current plain total line; Hold/Discount/Pay as big tile-style buttons (the P3-T17
   tile style, not a plain `Button` row); the ticket list restyled onto the kiosk surface and type
   roles.
3. Rebuild the F1–F12 row as a horizontal strip of compact chip tiles (key + label, per the chosen
   mockup) using the P3-T17 F-key chip style, replacing today's plain buttons. The `Window`-level
   `KeyBinding`s themselves, and every P1-T09/P1-T10 scanner/keyboard behaviour, do not change —
   this is presentation only, exactly like P3-T14's own constraint.
4. `docs/Counterpoint_Requirements.md` is permission-protected from automated edits — do not
   attempt to write to it. The UI-09 amendment is recorded above and in `docs/08_TASK_INDEX.md` as
   owner-approved and pending a manual edit by someone with access to that file.

**Deliverables.** Redesigned `SalesWindow.axaml`/`SalesSidePanelView.axaml` on the P3-T17 kiosk
styles, extended contrast/label-inspection coverage for the new markup.

**Risks.** Same regression risk P3-T14 already named: re-run every P1-T09/P1-T10 test unmodified —
none of them should need to change if this stayed visual/structural-only. Removing the status
bindings from the *view* must not remove the ViewModel properties or the refresh logic feeding
them; P3-T19 still needs both.

**Done when.**
- [ ] `SalesWindow`'s status bar shows only user and shift status; backup/cloud/printer/print-queue
      status appears nowhere on the cashier screen
- [ ] The sale screen matches the chosen Kiosk Tile direction (total card, tile actions, F-key chip
      row) built from P3-T17's tokens/styles; zero raw hex colour literals
- [ ] Every existing P1-T09/P1-T10 test still passes, unmodified
- [ ] The UI-09 amendment is recorded in the task docs as owner-approved and pending (not written
      into `docs/Counterpoint_Requirements.md`, which this task does not touch)

---

### P3-T19 — Back office: kiosk redesign, dashboard stat row and tile navigation
**Depends on:** P3-T17, P3-T13, P3-T18 · **Est:** 2d · **SRS:** UI-09 (amended), UI-11, NFR-U4

**Context.** The owner called the shipped back office (a bare `UniformGrid` of default-styled
`Button`s under a 6px accent stripe) unacceptable. This task rebuilds `BackOfficeShellWindow` on
the chosen Kiosk Tile direction and is where backup/cloud/printer status now lives exclusively,
per P3-T18's UI-09 amendment.

**Do this.**
1. Add a stat-card row (the P3-T17 stat/total card style) showing today's sales, low-stock count
   and open-shift duration read through the existing `IDashboardQueries`/`DashboardSummary`
   (FR-9.7) — do not invent a new query for figures that abstraction already provides — plus,
   moved here from the cashier screen, last-backup time, cloud backup status and printer status as
   a status-dot-plus-text pair using the P3-T17 `GoodBrush`/`WarningBrush`.
2. Replace the `UniformGrid` of plain `Button`s with a big-tile navigation grid (the P3-T17 tile
   style) for Catalogue/Settings/Users/Purchasing/Labels. Each tile keeps exactly the role gating
   it has today (`CanManageCatalogue` etc.) — presentation only, no change to the Application-layer
   authorisation checks behind any of them.
3. Restyle the existing accent-stripe/status-bar concept from P3-T13 (the UI-11 visual-distinctness
   mechanism) onto the kiosk tokens and typography; keep the mechanism, change only its look.
4. Extend `AC24_CashierAndOwnerShellsAreVisuallyAndNavigationallyDistinct` and
   `AC21_EveryScreenIsLegibleInBothThemes` coverage to the new markup — re-run, don't rewrite, their
   underlying assertions.

**Deliverables.** Redesigned `BackOfficeShellWindow.axaml` (and `BackOfficeShellViewModel.cs` only
if new stat-row bindings are needed) on the P3-T17 kiosk styles.

**Risks.** None of this should touch `Application`/`Domain`/`Infrastructure`. If a stat the mockup
wants isn't already exposed by `IDashboardQueries`, use what is available now and flag the gap in
the report rather than adding a new query inside a UI-only task.

**Done when.**
- [ ] `BackOfficeShellWindow` shows the stat-card row (backup/cloud/printer status now exclusively
      here) and a big-tile navigation grid, matching the chosen Kiosk Tile direction
- [ ] Role-gating on every tile is unchanged and still enforced at the Application layer (existing
      AC-24 test re-run, not rewritten, and green)
- [ ] AC-21 contrast coverage extends to the new layout and stays green in both themes
- [ ] `dotnet test` is green, architecture tests are green, and the app still starts to the sales
      screen
