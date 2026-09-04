# Software Requirements Specification (SRS)
## Point of Sale & Inventory System — Single-Cashier Hardware Shop

| Field | Value |
|---|---|
| Document title | SRS — Counterpoint (Single Terminal) |
| Version | 1.0 (Draft for review) |
| Date | 3 September 2026 |
| Status | For client sign-off |
| Prepared for | Owner / Proprietor, Hardware Shop |
| System type | Offline-first desktop application, single POS terminal, cloud used **only** for backup |

### Revision history

| Ver | Date | Author | Change |
|---|---|---|---|
| 0.1 | — | — | Initial outline |
| 1.0 | 2026-09-03 | — | Full draft after market scan of existing hardware-store POS products |

---

## Table of contents

1. Introduction
2. Review of existing systems (market scan)
3. Business context, stakeholders and scope
4. System overview and architecture
5. Assumptions, dependencies and constraints
6. Functional requirements (FR)
7. Business rules (BR)
8. Key workflows
9. Reports specification
10. Bill / receipt printing specification
11. Data model
12. User interface requirements
13. Non-functional requirements (NFR)
14. Hardware and software requirements
15. Out of scope
16. Acceptance criteria
17. Phased delivery plan
18. Open questions for the client
19. Glossary

---

# 1. Introduction

## 1.1 Purpose

This document specifies the complete requirements for a Point of Sale (POS) and inventory management system for a single-branch, single-cashier hardware shop. It is written to be used as:

- the basis for client sign-off,
- the development contract / build specification,
- the source of test cases and acceptance criteria.

Every requirement is uniquely numbered so that it can be traced to design, code and test.

## 1.2 Product scope

The system will handle the shop's day-to-day counter operations end to end:

- **Product management** — creating and maintaining the item catalogue, including hardware-specific complexity (variants, multiple units of measure, loose/cut-to-length goods).
- **Inventory management** — stock receipt from suppliers, adjustments, stock takes, reorder alerts, valuation.
- **Selling items** — fast barcode/code-driven checkout, discounts, multiple payment methods, held bills.
- **Returning items** — refunds, exchanges and credit notes, with correct stock and cash effects.
- **Reports** — sales, stock, profit, returns, tax and day-end reconciliation.
- **Bill printing** — thermal receipts and optional A4/A5 invoices.
- **Backup** — automatic local backup plus encrypted off-site backup to a web/cloud store.

## 1.3 Definitions of intent

The words **Must**, **Should** and **Could** are used in the MoSCoW sense:

| Priority | Meaning |
|---|---|
| **M** (Must) | Mandatory. The system is not acceptable without it. |
| **S** (Should) | Important, high value, but the shop can trade without it on day one. |
| **C** (Could) | Desirable. Include if budget and time allow. |
| **W** (Won't, this release) | Explicitly excluded — see Section 15. |

## 1.4 Intended readers

Shop owner, cashier, the development team, the tester, and any future maintainer of the system.

---

# 2. Review of existing systems (market scan)

Before writing these requirements, mainstream and specialist POS products serving hardware and building-supply retail were reviewed, together with general guidance on offline-first POS architecture, unit-of-measure handling, returns processing and end-of-day reconciliation.

## 2.1 Systems reviewed

| System | Positioning | Notable strengths relevant to us | Why it does not fit as-is |
|---|---|---|---|
| **KORONA POS** | Cloud POS with a dedicated hardware-store edition | Parent products with variants for size, colour and material; purchase-order management; strong barcode handling | Cloud-dependent, subscription per terminal, more features than a single-till shop needs |
| **Lightspeed Retail** | Cloud retail POS, hardware & paint vertical | Real-time stock levels, product variations (paint colours, tool sizes), reorder insight | Subscription cost, internet dependency, multi-store bias |
| **Epicor Eagle / ECI Spruce / RockSolid MAX** | Enterprise building-supply and hardware ERP | Alternate units of measure, job/contractor accounts, special orders, bulk goods | Heavyweight, expensive, proprietary licensing, long implementation |
| **Celerant / NCR Counterpoint (POS Highway)** | Mid-market retail platform | Deep inventory, purchasing, hardware peripheral compatibility | Complexity and cost far beyond a one-counter shop |
| **Square for Retail** | SMB cloud POS | Clean returns/exchange workflow, item-level restock choice, unlinked-refund controls | Card-processor lock-in, weak on hardware UOM complexity |
| **Odoo POS / UniCenta / Loyverse** (open source & freemium) | Low-cost general retail POS | Free/cheap, extensible, offline-capable variants exist | Generic retail model; loose goods, cut-to-length and pack-to-piece conversions need customisation |

## 2.2 What the market scan tells us

Findings that directly shaped the requirements below:

1. **Hardware retail is SKU-heavy and variant-heavy.** Industry commentary notes that a single fastener type can explode into dozens of SKUs once length, thread pitch and finish are counted — one 1/4" hex bolt across 10 lengths, 2 thread types and 3 finishes is 60 distinct SKUs. Generic retail POS designs fail here.
   → Drives **FR-2 (variants, attributes, fast search)** and **FR-2.6 (matrix/bulk SKU creation)**.

2. **Alternate units of measure are the defining hardware feature.** Specialist systems track rope sold by the foot or by the spool, PVC pipe by the metre or as a 6 m length, wire by metre/coil/drum, with conversion factors held against the item master. Fixed pack factors are constants (1 box = 100 pcs); variable-measure goods capture the actual quantity per transaction.
   → Drives **FR-2.4 (UOM & conversions)**, **FR-3.6 (decimal quantities)**, **FR-4.9 (bulk breaking)**.

3. **Offline-first is a real architectural choice, not a fallback.** Guidance on POS resilience is consistent: the local database should be the single source of truth, the UI should never wait on the network, and the cloud should receive data asynchronously afterwards. Cloud-only designs create a single point of failure where an ISP outage stops trading.
   → Drives **Section 4 architecture** and **FR-11 (backup)**. Since the client wants web *only* for backup, we go further than most products: the network is never on the critical path for any sale.

4. **Returns need structure, not a negative sale.** Good systems look up the original bill by receipt number or customer, let the operator choose per item whether the goods go back to sellable stock or to a damaged bin, calculate exchange differences automatically, and distinguish refund / exchange / store credit for reporting. They also treat no-receipt ("unlinked") refunds as an elevated-risk action requiring authorisation, because receipt fraud and switch fraud are common return-fraud patterns.
   → Drives the whole of **FR-5**.

5. **Day-end reconciliation is a standard, well-defined artefact.** The X report (mid-day snapshot, non-clearing) and Z report (end-of-day, locks the period, compares counted cash against expected cash and records the variance) are near-universal and are what auditors expect.
   → Drives **FR-8 (cash management)** and **RPT-04/RPT-05**.

6. **Receipt printing has a de facto standard.** 80 mm thermal printers with an auto-cutter, ESC/POS command support and an RJ11/RJ12 cash-drawer kick port are commodity hardware and cheap. Building to ESC/POS avoids peripheral lock-in.
   → Drives **Section 10** and **NFR-HW**.

## 2.3 Design position taken

The system will be a **locally installed, offline-first desktop application** that deliberately implements the *narrow* feature set of a small hardware shop with the *depth* the hardware vertical needs — specifically UOM conversion, variants and disciplined returns — while dropping the multi-store, e-commerce, loyalty and franchising machinery that makes the commercial products expensive and slow.

---

# 3. Business context, stakeholders and scope

## 3.1 Business context

A single-location retail hardware shop selling tools, fasteners, plumbing, electrical, paint, adhesives, garden and general building items. One counter, one cashier at a time. Customers are walk-in retail buyers and a smaller number of repeat trade/contractor customers. Stock is bought from multiple suppliers, sometimes on credit, and arrives in packs, boxes, coils and drums that are broken down for retail sale.

## 3.2 Stakeholders

| Stakeholder | Interest in the system |
|---|---|
| **Owner / Manager** | Profitability, stock control, shrinkage prevention, reports, pricing, purchasing, backups |
| **Cashier** | Fast, simple, forgiving checkout; correct bills; easy returns |
| **Customer** | Accurate, legible bill; fast service; smooth returns |
| **Supplier** | Accurate goods-received records and payables |
| **Tax authority / auditor** | Complete, sequential, tamper-evident sales records |

## 3.3 User roles

The system supports exactly **two roles on one machine**. This is a deliberate consequence of the single-cashier constraint.

| ID | Role | Description | Access |
|---|---|---|---|
| ROLE-1 | **Cashier** | The person at the counter | Sell, return (within policy), hold/recall bills, reprint bills, view own shift totals, check stock levels, open/close shift |
| ROLE-2 | **Owner / Admin** | Proprietor or manager | Everything the cashier can do, **plus** product management, cost prices, purchasing/GRN, stock adjustments and stock takes, price changes, discount overrides above limit, unlinked refunds, voids and bill cancellation, all reports, settings, backup/restore, user management |

**Constraint:** only one user session is active at a time. There is no concurrent multi-user operation, no terminal-to-terminal synchronisation and no server component on the LAN.

## 3.4 In scope (summary)

Product management · inventory management · sales · returns · reports · bill printing · purchasing & goods receipt · customer & supplier records · cash management & day close · configuration · audit log · backup and restore.

## 3.5 Out of scope

See Section 15 for the explicit exclusion list.

---

# 4. System overview and architecture

## 4.1 Architectural principle

> **The local database is the single source of truth. The network is never required to complete a sale.**

The internet connection is used for exactly one purpose: shipping encrypted backup copies off-site. If the internet is down for a month, the shop trades normally and the only consequence is a backup-overdue warning.

## 4.2 Deployment topology

```
┌──────────────────────────────────────────────────────────┐
│  SHOP COUNTER — single Windows PC / laptop               │
│                                                          │
│  ┌────────────────────────────────────────────┐          │
│  │  POS Application (desktop, single instance)│          │
│  │   • Sales screen      • Back office        │          │
│  │   • Returns           • Reports            │          │
│  └───────────────┬────────────────────────────┘          │
│                  │                                       │
│        ┌─────────▼──────────┐                            │
│        │  Local database    │  ← SINGLE SOURCE OF TRUTH  │
│        │  (embedded file DB)│                            │
│        └─────────┬──────────┘                            │
│                  │                                       │
│     ┌────────────▼─────────────┐                         │
│     │ Local backup folder      │  (daily .bak files)     │
│     │ + attached USB drive     │                         │
│     └────────────┬─────────────┘                         │
│                  │                                       │
│  Peripherals: barcode scanner (USB HID) ·                │
│  80 mm thermal printer (ESC/POS) · cash drawer (RJ11) ·  │
│  optional label printer · optional weighing scale        │
└──────────────────┬───────────────────────────────────────┘
                   │  (asynchronous, non-blocking,
                   │   encrypted, retry-on-failure)
                   ▼
        ┌──────────────────────────────┐
        │  Cloud backup store          │
        │  (object storage / Drive)    │
        │  + small web page to browse  │
        │    and download backup files │
        └──────────────────────────────┘
```

## 4.3 Component responsibilities

| Component | Responsibility |
|---|---|
| POS application | All business logic and UI. Runs fully offline. |
| Local database | All transactional and master data. Embedded, file-based, zero admin (e.g. SQLite or equivalent). |
| Backup service | Background job: dump → compress → encrypt → write locally → upload when a connection exists. |
| Cloud backup store | Passive storage of encrypted backup files with retention. Holds **no** live business logic. |
| Backup web page | Read-only listing of available backup files with download and integrity status. Login-protected, owner only. |

## 4.4 What the cloud explicitly does NOT do

- It does not process, validate or authorise any sale, return or price.
- It does not hold live stock levels.
- It is not queried during trading.
- Losing it entirely costs the shop nothing except off-site copies.

---

# 5. Assumptions, dependencies and constraints

## 5.1 Assumptions

| ID | Assumption |
|---|---|
| A-01 | One physical counter, one POS terminal, one cashier serving at any moment. |
| A-02 | The owner may use the same machine for back-office work outside busy hours. |
| A-03 | A reliable barcode scanner and 80 mm thermal printer will be provided. |
| A-04 | Internet is intermittent and may be unavailable for extended periods. |
| A-05 | Card payments, if accepted, are processed on a **separate standalone card terminal**; the POS records the tender type and amount only. |
| A-06 | Catalogue size: up to ~20,000 SKUs; up to ~500 bills per day. Design headroom: 50,000 SKUs. |
| A-07 | Currency, tax rates and tax labels are configurable; they are not hard-coded. |
| A-08 | Existing product/stock data may be supplied as a spreadsheet for initial import. |
| A-09 | The shop's return policy will be supplied by the owner and configured in the system, not hard-coded. |

## 5.2 Constraints

| ID | Constraint |
|---|---|
| C-01 | Single-cashier / single-terminal only. No LAN server, no second till, no multi-branch. |
| C-02 | The web/cloud tier is for backup only. No cloud dependency during trading. |
| C-03 | Must run on modest hardware (see Section 14). |
| C-04 | Must be operable primarily by keyboard and barcode scanner, by a non-technical user. |
| C-05 | Sales must never be blocked by a printer, scanner, network or backup failure. |

---

# 6. Functional requirements

## FR-1 — Authentication, users and access control

| ID | Requirement | Pri |
|---|---|---|
| FR-1.1 | The system must require login (username + PIN/password) before any operation. | M |
| FR-1.2 | The system must support the two roles defined in §3.3, with the stated permission split. | M |
| FR-1.3 | Passwords/PINs must be stored salted and hashed, never in plain text. | M |
| FR-1.4 | The owner must be able to create, disable and reset the cashier account. The system must always retain at least one enabled owner account. | M |
| FR-1.5 | The system must auto-lock the screen after a configurable idle period (default 10 minutes) and require re-entry of the PIN. In-progress bills must be preserved through the lock. | M |
| FR-1.6 | Every privileged action (price override, discount above limit, void, unlinked refund, stock adjustment, cost-price view, settings change, restore) must be recorded against the user who performed it. | M |
| FR-1.7 | A cashier attempting a privileged action must be able to call the owner for an on-screen supervisor override without logging the cashier out or losing the current bill. | M |
| FR-1.8 | The system must maintain an append-only audit log: timestamp, user, action, entity, before/after values. The log must not be editable or deletable from the UI. | M |
| FR-1.9 | The audit log must be viewable and filterable by the owner (date, user, action type). | S |

## FR-2 — Product management

### Item master

| ID | Requirement | Pri |
|---|---|---|
| FR-2.1 | The owner must be able to create, edit, deactivate and reactivate products. Products that have transaction history must never be hard-deleted, only deactivated. | M |
| FR-2.2 | Each product must hold at minimum: internal item code (unique), name/description, category, brand, unit of measure, selling price, cost price, tax class, reorder level, active flag. | M |
| FR-2.3 | Each product must support optional fields: supplier(s), rack/bin location, HSN/tax code, secondary description (local language), image, warranty period, notes, minimum sellable quantity, maximum discount %. | S |
| FR-2.4 | **Units of measure.** Each product must have a *base unit* in which stock is held (e.g. piece, metre, kg, litre). The system must support additional *alternate units* with a conversion factor to the base unit (e.g. 1 box = 100 pcs; 1 coil = 90 m; 1 carton = 12 bottles). | M |
| FR-2.5 | The system must allow a distinct selling price per unit of measure (e.g. cable at 120/metre or 9,500/full 90 m coil), and must decrement the same base-unit stock pool regardless of which unit is sold. | M |
| FR-2.6 | **Variants.** The system must support a parent product with child variants across attributes such as size, length, thread, material, finish and colour, and must provide a matrix/bulk generator so that a fastener family can be created in one operation rather than one SKU at a time. Each variant is a stock-carrying SKU in its own right. | M |
| FR-2.7 | The system must support product types: *standard* (sold in whole units), *decimal-quantity* (sold by length/weight/volume, accepts fractional quantities), *service/charge* (no stock, e.g. cutting charge, key cutting, delivery), and *non-inventory* (sold but not stock-tracked). | M |
| FR-2.8 | The system must support "open item" / miscellaneous sale lines with a manually typed description and price, for one-off items not in the catalogue. Use of open items must be reportable. | S |

### Barcodes and identification

| ID | Requirement | Pri |
|---|---|---|
| FR-2.9 | Each product must support multiple barcodes (manufacturer EAN/UPC plus shop-generated codes), all resolving to the same SKU. | M |
| FR-2.10 | The system must generate internal barcodes for loose or unbarcoded items and print shelf/product labels showing name, code, barcode, unit and price. | M |
| FR-2.11 | Product search at the counter must work by barcode scan, item code, name fragment, brand, category and rack location, and must return results as the user types. | M |
| FR-2.12 | The system must support barcode label printing for a selected list of products or for a whole goods-receipt batch. | S |

### Pricing

| ID | Requirement | Pri |
|---|---|---|
| FR-2.13 | The system must hold a retail selling price per product/variant/unit. | M |
| FR-2.14 | The system must support a second **trade/wholesale price tier** applied automatically to customers flagged as trade. | S |
| FR-2.15 | The system must support quantity-break pricing (e.g. 1–9 pcs at one price, 10+ at another). | S |
| FR-2.16 | The system must support a time-bound promotional price with start and end dates, reverting automatically. | C |
| FR-2.17 | The system must retain price-change history (old price, new price, date, user). | S |
| FR-2.18 | The system must warn the owner when a selling price is set at or below cost price, and require confirmation. | M |
| FR-2.19 | The system must support bulk price update by category, brand or supplier, by percentage or fixed amount, with a preview before applying. | S |

### Categories and catalogue tools

| ID | Requirement | Pri |
|---|---|---|
| FR-2.20 | The system must support a two-level category structure (category → sub-category), e.g. Plumbing → PVC Fittings. | M |
| FR-2.21 | The system must support brands and suppliers as maintained lists linked to products. | M |
| FR-2.22 | The system must import products from CSV/Excel with a column-mapping step, a validation report and a dry-run preview before committing. | M |
| FR-2.23 | The system must export the full catalogue with current stock and prices to CSV/Excel. | M |
| FR-2.24 | The system must detect and warn about probable duplicate products on creation (same barcode, or very similar name + brand + size). | S |

## FR-3 — Sales (selling items)

### Building the bill

| ID | Requirement | Pri |
|---|---|---|
| FR-3.1 | The cashier must be able to start a new sale in one keystroke, with the cursor placed in the scan/search field by default. | M |
| FR-3.2 | Scanning a barcode must add the item to the bill instantly; scanning the same code again must increment its quantity rather than create a second line (configurable). | M |
| FR-3.3 | The cashier must be able to add items by typing a code or partial name and choosing from a live result list. | M |
| FR-3.4 | Each bill line must show: item name, unit, unit price, quantity, line discount, line total. | M |
| FR-3.5 | The cashier must be able to edit quantity, remove a line, and clear the whole bill (with confirmation). | M |
| FR-3.6 | The system must accept decimal quantities to a configurable number of decimal places (default 3) for decimal-quantity products, e.g. 2.75 m of cable, 0.450 kg of nails. | M |
| FR-3.7 | The cashier must be able to switch the selling unit on a line (piece ↔ box ↔ coil), with price and stock impact recalculated automatically via the conversion factor. | M |
| FR-3.8 | The system must optionally accept a weight from a connected scale for weighed items; if no scale is connected, weight must be enterable manually. | C |
| FR-3.9 | The cashier must be able to add a free-text note to a line (e.g. "cut to 2.4 m") that prints on the bill. | S |
| FR-3.10 | The system must display the running subtotal, tax, discount and grand total at all times in large, legible type. | M |
| FR-3.11 | The system must show live stock-on-hand for the scanned item on screen. | M |

### Stock behaviour and controls during sale

| ID | Requirement | Pri |
|---|---|---|
| FR-3.12 | Selling an item must decrement stock in base units at the moment the bill is completed, not while the bill is being built. | M |
| FR-3.13 | If quantity requested exceeds stock on hand, the system must warn. Configurable policy: *block*, or *warn and allow* (allowing negative stock). Default: warn and allow, since counter reality often runs ahead of the records. | M |
| FR-3.14 | Every negative-stock occurrence must be logged and listed in an exceptions report for the owner. | M |
| FR-3.15 | Inactive/discontinued products must not be sellable except by owner override. | S |

### Discounts and price overrides

| ID | Requirement | Pri |
|---|---|---|
| FR-3.16 | The cashier must be able to apply a line discount as a percentage or fixed amount. | M |
| FR-3.17 | The cashier must be able to apply a whole-bill discount as a percentage or fixed amount, distributed proportionally across lines for reporting and tax. | M |
| FR-3.18 | Discounts must be capped at a configurable cashier limit (per line and per bill). Exceeding the cap must require owner override. | M |
| FR-3.19 | Manual price override on a line must be an owner-only action, must be logged with a reason, and must be reportable. | M |
| FR-3.20 | The system must support rounding the bill total to the nearest configurable denomination, recording the rounding amount separately. | S |

### Customer on the bill

| ID | Requirement | Pri |
|---|---|---|
| FR-3.21 | A sale must be completable with no customer attached (anonymous walk-in) in the default flow. | M |
| FR-3.22 | The cashier must be able to attach a customer by name or phone search, or create a customer inline in under 10 seconds. | S |
| FR-3.23 | Attaching a trade customer must automatically apply the trade price tier and recalculate the bill. | S |

### Payment and completion

| ID | Requirement | Pri |
|---|---|---|
| FR-3.24 | The system must support tender types: **Cash**, **Card**, **Mobile/bank transfer**, **Credit (on account)**, **Store credit / credit note redemption**. | M |
| FR-3.25 | The system must support split tender across two or more types on a single bill (e.g. part cash, part card). | M |
| FR-3.26 | For cash, the system must calculate and display change due prominently, and must offer quick-tender buttons for common note denominations. | M |
| FR-3.27 | Credit (on account) sales must be permitted only for customers flagged as credit customers, and must be blocked or require owner override when the customer's outstanding balance would exceed their credit limit. | S |
| FR-3.28 | On completion the system must: allocate the next sequential bill number, persist the sale atomically, decrement stock, open the cash drawer (for cash tender), and print the bill. | M |
| FR-3.29 | Bill numbers must be strictly sequential with no gaps, per configurable prefix and financial year. Cancelled bills must retain their number and be marked cancelled, never reused. | M |
| FR-3.30 | Any failure during completion must roll back entirely — a bill must never be half-saved with stock already moved. | M |
| FR-3.31 | A completed bill must be immutable. Corrections happen by cancellation (same day, owner only) or by a return. | M |

### Held bills, voids and lookups

| ID | Requirement | Pri |
|---|---|---|
| FR-3.32 | The cashier must be able to **hold** an in-progress bill (customer went to fetch another item) and recall it later. Multiple held bills must be supported, each labelled. | M |
| FR-3.33 | Held bills must survive an application restart or power cut. | M |
| FR-3.34 | The cashier must be able to void an *uncompleted* bill freely. Voiding/cancelling a *completed* bill must be owner-only, same business day only, requires a reason, reverses stock, and prints a cancellation slip. | M |
| FR-3.35 | The cashier must be able to look up any past bill by bill number, date range, amount, customer or item, and view it. | M |
| FR-3.36 | The cashier must be able to reprint any past bill; every reprint must be marked "DUPLICATE" on the printout and logged. | M |

## FR-4 — Inventory management

### Stock model

| ID | Requirement | Pri |
|---|---|---|
| FR-4.1 | The system must maintain stock on hand per SKU in base units, always derived from an immutable ledger of stock movements — never by editing a stock number directly. | M |
| FR-4.2 | Every stock movement must record: SKU, quantity (signed), base-unit value, movement type, reference document, timestamp, user, and resulting balance. | M |
| FR-4.3 | Movement types must include: Opening, Goods Receipt, Sale, Sales Return, Purchase Return, Adjustment (+/−), Damage/Write-off, Stock-take Correction, Bulk Break, Internal/Shop Use. | M |
| FR-4.4 | The system must maintain moving-average cost per SKU, recalculated on each goods receipt, and use it for margin and valuation reporting. | M |

### Purchasing and goods receipt

| ID | Requirement | Pri |
|---|---|---|
| FR-4.5 | The owner must be able to raise a purchase order to a supplier: supplier, expected date, lines with SKU, quantity, unit and expected cost. PO must be printable/exportable. | S |
| FR-4.6 | The system must generate a suggested purchase order from items at or below reorder level, grouped by preferred supplier, with suggested quantities. | S |
| FR-4.7 | The owner must be able to record a **Goods Receipt Note (GRN)** against a supplier, with or without a preceding PO, capturing supplier invoice number, date, per-line quantity, unit, cost, discount and tax. | M |
| FR-4.8 | Posting a GRN must increase stock, update moving-average cost, and optionally prompt to update selling prices where cost has risen — showing the resulting margin before the owner confirms. | M |
| FR-4.9 | **Bulk breaking / repackaging.** The owner must be able to convert stock from one form to another where units differ (e.g. 1 coil of 90 m → 90 m of loose cable; 1 box of 100 → 100 loose pcs), with the system posting the paired decrement/increment and carrying cost across correctly. | M |
| FR-4.10 | The system must support partial receipt against a PO and keep the balance open. | C |
| FR-4.11 | The owner must be able to record a **purchase return** (goods sent back to supplier), decrementing stock and recording a debit against the supplier. | S |

### Adjustments, damage and stock take

| ID | Requirement | Pri |
|---|---|---|
| FR-4.12 | The owner must be able to post a manual stock adjustment (increase or decrease) with a mandatory reason from a configurable list (damaged, expired, lost/shrinkage, found, shop use, correction) plus free-text notes. | M |
| FR-4.13 | Damaged/written-off stock must be tracked as a distinct movement type and valued in reports, so shrinkage is visible. | M |
| FR-4.14 | The system must support a **stock take**: generate a count sheet (by category, brand, supplier, rack or whole shop), record counted quantities (typed or scanned), display variance against system quantity and value, and post corrections in one confirmed batch. | M |
| FR-4.15 | Stock takes must be saveable in progress and resumable, so a large count can span several days. | S |
| FR-4.16 | Every stock take must be retained as a historical record with its variance report. | M |

### Alerts and visibility

| ID | Requirement | Pri |
|---|---|---|
| FR-4.17 | The system must show a dashboard alert listing items at or below reorder level and items at zero stock. | M |
| FR-4.18 | The system must support per-item reorder level and reorder quantity, and must offer to suggest reorder levels from historical sales velocity. | S |
| FR-4.19 | The system must allow a quick stock enquiry from any screen: scan or type an item and see stock, price, cost (owner only), location and last purchase details. | M |
| FR-4.20 | The system must flag slow-moving stock (no sale in *N* days, configurable) and dead stock. | S |
| FR-4.21 | The system must support optional serial-number capture on sale for high-value items (power tools) to support warranty claims. | C |
| FR-4.22 | The system must support optional batch/expiry tracking for items with shelf life (adhesives, sealants, paint). | C |

## FR-5 — Returns, refunds and exchanges

### Linked returns (with the original bill)

| ID | Requirement | Pri |
|---|---|---|
| FR-5.1 | The cashier must be able to start a return by scanning the barcode printed on the original bill, or by typing the bill number, or by searching by date/customer/amount. | M |
| FR-5.2 | On retrieving the bill, the system must display every line with quantity sold, quantity already returned, and quantity still returnable. | M |
| FR-5.3 | The cashier must be able to return whole lines or partial quantities, including fractional quantities for decimal-quantity items. | M |
| FR-5.4 | The system must prevent returning more than was sold, across the cumulative history of all returns against that bill. | M |
| FR-5.5 | The system must refund at the **price actually paid on the original bill**, including any discount applied, not the current shelf price. | M |
| FR-5.6 | The system must enforce a configurable return window (e.g. 7, 14 or 30 days). Returns outside the window must be blocked, or allowed only with owner override and a recorded reason. | M |
| FR-5.7 | The cashier must record a **return reason** per line from a configurable list (faulty, wrong item, wrong size, customer changed mind, damaged in transit, duplicate purchase, other). | M |

### Stock disposition

| ID | Requirement | Pri |
|---|---|---|
| FR-5.8 | For each returned line the operator must explicitly choose the disposition: **return to sellable stock** or **quarantine as damaged/faulty** (not added back to sellable stock). This choice must never be silently defaulted. | M |
| FR-5.9 | Items dispositioned as damaged must post to a damaged-stock account and appear in the shrinkage/damage report. | M |
| FR-5.10 | Certain products must be flaggable as **non-returnable / final sale** (e.g. cut-to-length cable, mixed paint, cut keys, clearance items). Attempting to return them must be blocked, overridable by the owner only. | M |
| FR-5.11 | The system must support a configurable **restocking fee** (percentage or fixed) applied at the operator's discretion for opened or non-defective returns. | C |

### Refund settlement

| ID | Requirement | Pri |
|---|---|---|
| FR-5.12 | The system must support refund methods: **cash**, **store credit / credit note**, **reversal to the customer's account balance** (for credit customers), and **card refund recorded as a note** (physically processed on the card terminal). | M |
| FR-5.13 | The default refund method must be configurable, and cash refunds may be capped at a configurable amount above which owner authorisation is required. | M |
| FR-5.14 | Issuing store credit must generate a uniquely numbered credit note with an expiry date, printable, redeemable in part across multiple future bills, with the remaining balance tracked. | S |
| FR-5.15 | The system must print a **return/credit receipt** referencing the original bill number, clearly headed as a return, showing items, reason, refund method and amount. | M |

### Exchanges

| ID | Requirement | Pri |
|---|---|---|
| FR-5.16 | The system must support an exchange in a single transaction: return line(s) in, new line(s) out, with the net difference calculated automatically. | M |
| FR-5.17 | If the new items cost more, the system must collect the difference through the normal payment flow. If they cost less, it must refund the difference by the configured method. | M |
| FR-5.18 | An exchange must produce one document showing both the returned and the newly issued items with a clear net figure. | M |

### Unlinked returns and controls

| ID | Requirement | Pri |
|---|---|---|
| FR-5.19 | Return without an original bill must be **disabled by default**. Where enabled, it must require owner authorisation, a recorded reason, and ideally customer identification, and it must be listed in a dedicated report — this is the principal return-fraud exposure. | M |
| FR-5.20 | Unlinked returns must default to store credit rather than cash. | S |
| FR-5.21 | Returns must be numbered in their own sequential series, permanently linked to the original bill where one exists. | M |
| FR-5.22 | A return, once completed, must be immutable. | M |
| FR-5.23 | The system must flag repeat-return behaviour where a customer record exists (e.g. more than *N* returns in *N* days) as a soft warning to the operator. | C |

## FR-6 — Customers and suppliers

| ID | Requirement | Pri |
|---|---|---|
| FR-6.1 | The system must maintain customer records: name, phone, address, type (retail/trade), tax number, notes, active flag. | S |
| FR-6.2 | The system must show a customer's purchase history and returns history. | S |
| FR-6.3 | For credit customers the system must track outstanding balance, credit limit, receipts against account, and produce an ageing report. | S |
| FR-6.4 | The system must record customer payments/settlements against outstanding bills and print a payment receipt. | S |
| FR-6.5 | The system must maintain supplier records: name, contact, address, tax number, payment terms, notes. | M |
| FR-6.6 | The system must show purchase history and outstanding payables per supplier. | S |
| FR-6.7 | Customer phone numbers and personal data must be protected under the security requirements in Section 13 and must be exportable/erasable on request. | S |

## FR-7 — Bill and document printing

Detailed layout specification is in Section 10.

| ID | Requirement | Pri |
|---|---|---|
| FR-7.1 | The system must print a sales bill to an 80 mm thermal printer using standard ESC/POS commands, avoiding lock-in to any single printer brand. | M |
| FR-7.2 | The system must also support printing to any Windows-installed printer (A4/A5) for a formal invoice format, for trade customers who need one. | S |
| FR-7.3 | Bill header/footer content (shop name, address, phone, tax number, logo, return-policy text, thank-you line) must be configurable without a code change. | M |
| FR-7.4 | The bill must print a machine-readable barcode or QR of the bill number so returns can be started by scanning the customer's receipt. | M |
| FR-7.5 | The system must support automatic print on completion, plus manual reprint; the number of copies must be configurable. | M |
| FR-7.6 | All reprints must be clearly marked "DUPLICATE" and logged. | M |
| FR-7.7 | The system must be able to open the cash drawer via the printer's kick-out port on cash tender and on demand (owner-authorised "no sale" open, which must be logged). | M |
| FR-7.8 | A printer failure must never block or roll back a sale. The sale must complete, and the bill must be queued for reprint with an on-screen warning. | M |
| FR-7.9 | The system must be able to save any bill as PDF and, optionally, send it to the customer via a share/export action. | C |
| FR-7.10 | The system must print: sales bill, return/credit receipt, credit note, quotation, GRN, purchase order, stock-take sheet, X report, Z report, barcode labels. | M |

## FR-8 — Cash management and day close

| ID | Requirement | Pri |
|---|---|---|
| FR-8.1 | The cashier must open a shift by entering the opening cash float, which is recorded and timestamped. | M |
| FR-8.2 | The system must record cash movements that are not sales: **cash in** (float top-up, owner deposit) and **cash out** (petty expense, supplier payment, banking), each with a reason and optional printed slip. | M |
| FR-8.3 | The system must produce an **X report** on demand: a mid-shift snapshot of sales, tenders, returns and expected drawer contents that does **not** close or clear the shift. | M |
| FR-8.4 | The system must produce a **Z report** at day/shift close: full totals, prompt the cashier to count and enter physical cash, compute the variance (over/short) against expected, record and print it, and close the period. | M |
| FR-8.5 | Once a Z report is taken, that shift must be locked — no new transactions may be posted into it and its figures must be immutable. | M |
| FR-8.6 | Cash variance history must be retained and reportable over time, since a persistent pattern is the shop's main indicator of error or loss. | M |
| FR-8.7 | The system must warn if the application is closed with an open shift, and must recover the open shift cleanly on restart. | M |
| FR-8.8 | The system must never allow a Z report to be deleted or re-run for a closed period. Corrections are made by a documented adjustment in the next period. | M |

## FR-9 — Reports

General requirements; the full catalogue of reports is in Section 9.

| ID | Requirement | Pri |
|---|---|---|
| FR-9.1 | Every report must support a date-range filter with quick presets (today, yesterday, this week, this month, last month, this year, custom). | M |
| FR-9.2 | Every report must be printable and exportable to CSV/Excel and PDF. | M |
| FR-9.3 | Reports must run against the local database and must work with no internet connection. | M |
| FR-9.4 | Reports containing cost price, margin or profit must be visible to the owner role only. | M |
| FR-9.5 | Any report over a period of one year must render in under 10 seconds on the specified hardware. | S |
| FR-9.6 | Report figures must reconcile exactly: sales − returns − discounts + tax must tie to the tender totals and to the Z reports for the same period. This is an acceptance test, not a guideline. | M |
| FR-9.7 | The home screen must show a compact dashboard: today's sales, bill count, average bill value, cash in drawer, low-stock count, last backup status. | M |

## FR-10 — Configuration and settings

| ID | Requirement | Pri |
|---|---|---|
| FR-10.1 | Shop profile: name, address, phone, email, tax registration number, logo. | M |
| FR-10.2 | Financial: currency symbol and position, decimal places, rounding rule, quantity decimal places. | M |
| FR-10.3 | Tax: multiple named tax rates, tax classes assignable per product, and a switch for tax-inclusive or tax-exclusive pricing. | M |
| FR-10.4 | Numbering: prefix and starting number for bills, returns, credit notes, GRNs and POs. | M |
| FR-10.5 | Policy: return window days, unlinked-return on/off, default refund method, cash-refund limit, cashier discount limits, negative-stock policy, restocking fee. | M |
| FR-10.6 | Peripherals: printer selection, paper width, copies, drawer kick command, scanner behaviour, scale settings. | M |
| FR-10.7 | Backup: schedule, local path, USB path, cloud target, credentials, retention, encryption passphrase. | M |
| FR-10.8 | Receipt template editor: header, footer, policy text, which optional fields print. | S |
| FR-10.9 | All settings changes must be written to the audit log. | M |

## FR-11 — Backup and restore (the only use of the web tier)

| ID | Requirement | Pri |
|---|---|---|
| FR-11.1 | The system must take an automatic full backup at least once per day, at a configurable time, plus automatically on shift close. | M |
| FR-11.2 | The owner must be able to trigger a manual backup at any time from the UI. | M |
| FR-11.3 | Each backup must be written first to a local folder, and to an attached USB drive if configured — so that a working backup exists even with no internet at all. | M |
| FR-11.4 | Each backup must be compressed and **encrypted at rest** (AES-256 or equivalent) with a passphrase the owner controls. | M |
| FR-11.5 | The backup file must be uploaded to the configured cloud/web store asynchronously in the background, without blocking or slowing the POS. | M |
| FR-11.6 | If the upload fails (no internet, credentials expired, storage full), the system must retry with backoff, keep the local copy, and surface the failure state without interrupting trading. | M |
| FR-11.7 | The home screen must permanently display last successful local backup and last successful cloud upload, with an escalating warning after a configurable number of days without a successful cloud upload (default 3). | M |
| FR-11.8 | Every backup must store a checksum, and the system must verify integrity after writing and after uploading. | M |
| FR-11.9 | Retention must be configurable and must default to a grandfather-father-son scheme: last 14 daily, last 8 weekly, last 12 monthly. Older files are pruned automatically. | S |
| FR-11.10 | A protected web page must let the owner sign in, list available cloud backups with date, size and integrity status, and download any of them. | M |
| FR-11.11 | The web backup page must be read-only with respect to business data. It must not display product, sales or customer data — only backup file metadata. | M |
| FR-11.12 | The system must provide a guided **restore** function: select a backup (local, USB or downloaded), verify checksum, prompt for the passphrase, show what date the data will be restored to, require explicit typed confirmation, and back up the current database before overwriting it. | M |
| FR-11.13 | Restore must be an owner-only action and must be recorded in the audit log. | M |
| FR-11.14 | The system must include a self-test that restores the latest backup into a scratch location and verifies it opens — run monthly and reported — because an unverified backup is not a backup. | S |
| FR-11.15 | The complete restore procedure must be documented in the user manual in plain language, with screenshots, for a non-technical owner. | M |

---

# 7. Business rules

| ID | Rule |
|---|---|
| BR-01 | Stock on hand is always the sum of the movement ledger. No screen may set a stock figure directly. |
| BR-02 | Bill numbers are sequential and gapless per prefix and year. A cancelled bill keeps its number, marked cancelled. |
| BR-03 | A completed bill or return is immutable. Corrections are made only by same-day cancellation (owner) or by a return. |
| BR-04 | Refund value = price actually paid on the original bill, after original discounts, not current shelf price. |
| BR-05 | Cumulative returns against a bill line may never exceed the quantity sold on that line. |
| BR-06 | Every returned item must be explicitly dispositioned as sellable or damaged. |
| BR-07 | Cost prices, margins and profit figures are visible to the owner role only. |
| BR-08 | Stock is decremented at bill completion, not while the bill is being built; held bills do not reserve stock. |
| BR-09 | Selling stock is always converted to base units through the item's conversion factor before the ledger is written. |
| BR-10 | A closed shift (post-Z) can never receive new transactions. |
| BR-11 | Once a backup is taken it is never overwritten in place; each backup is a new, dated, immutable file. |
| BR-12 | Every privileged override records who authorised it, when and why. |
| BR-13 | A printer, scanner, network or backup failure never prevents a sale from completing. |
| BR-14 | Products with transaction history are deactivated, never deleted. |

---

# 8. Key workflows

## 8.1 Normal sale (target: under 20 seconds for a 5-item bill)

1. Cashier presses **F2 / New Sale**; cursor lands in the scan box.
2. Items are scanned or searched; quantities adjusted where needed; unit switched where needed (e.g. sell by box).
3. Optional: discount applied within limit; customer attached.
4. Cashier presses **F9 / Pay**, chooses tender, enters amount received.
5. System shows change due, allocates the bill number, saves atomically, decrements stock, kicks the drawer, prints.
6. Screen resets to a new empty bill.

## 8.2 Sale with a cut-to-length item

1. Cashier scans the cable/pipe SKU.
2. System recognises it as decimal-quantity and prompts for length.
3. Cashier enters `2.75`; system prices at rate × quantity and, if configured, adds the cutting-charge service line.
4. Line is flagged non-returnable per FR-5.10.
5. Sale completes normally; 2.75 m is deducted from the base-unit stock pool.

## 8.3 Return with receipt

1. Cashier presses **F4 / Return** and scans the barcode on the customer's bill.
2. System shows the bill with returnable quantities per line.
3. Cashier selects lines and quantities, picks a reason per line.
4. Cashier sets disposition per line: back to stock, or damaged.
5. System checks the return window and non-returnable flags; escalates to owner override if needed.
6. Cashier selects refund method; the system computes the refund at original paid prices.
7. System saves the return, adjusts stock per disposition, opens the drawer for a cash refund, prints the return receipt.

## 8.4 Exchange

1. Start as a return (8.3), selecting the incoming items.
2. Press **Add Items** and scan the replacement items onto the same transaction.
3. System shows returned total, new total and the net difference.
4. Collect or refund the difference; print one combined document.

## 8.5 Goods receipt (GRN)

1. Owner opens **Purchases → Goods Receipt**, selects the supplier, enters the invoice number and date.
2. Optionally pulls in an open PO to prefill lines.
3. Scans or selects each item, enters received quantity, receiving unit and cost.
4. System shows new moving-average cost and current margin per line, flagging any item now selling at or below cost.
5. Owner optionally updates selling prices inline.
6. Owner posts the GRN: stock increases, costs update, payable recorded.
7. Owner optionally prints barcode labels for the whole received batch.

## 8.6 Stock take

1. Owner starts a stock take, choosing a scope (whole shop / category / rack / brand).
2. System snapshots system quantities and prints or displays count sheets.
3. Counts are entered by scanning or typing; the take can be paused and resumed.
4. System shows a variance list by quantity and by value, worst variances first.
5. Owner reviews, adds notes, confirms; corrections post as a single batch of Stock-take Correction movements.
6. Variance report is archived permanently.

## 8.7 Day close

1. Cashier takes an **X report** during the day if wanted (no effect on data).
2. At close, cashier selects **Z / Close Shift**.
3. System shows expected cash = opening float + cash sales + cash in − cash out − cash refunds.
4. Cashier counts the drawer and enters the physical total.
5. System computes over/short, requires a note if the variance exceeds a configurable threshold, prints the Z report, locks the shift.
6. System triggers an automatic backup and attempts a cloud upload.

## 8.8 Disaster recovery (the point of the whole backup design)

1. Terminal is lost, stolen or dead.
2. Owner installs the application on a replacement machine.
3. Owner signs in to the backup web page, downloads the most recent verified backup.
4. Owner runs **Restore**, supplies the passphrase, confirms the restore date.
5. System verifies the checksum, restores, and reports the last transaction date recovered.
6. Trading resumes. Only transactions after the last backup are lost — bounded by the daily backup schedule.

---

# 9. Reports specification

| ID | Report | Contents | Filters | Role |
|---|---|---|---|---|
| RPT-01 | Daily sales summary | Bill count, gross, discounts, tax, net, by tender type | Date range | Both |
| RPT-02 | Sales detail | Every bill line: date, bill no, item, qty, unit, price, discount, total | Date, item, category, cashier, customer | Both |
| RPT-03 | Item-wise sales | Qty sold, revenue, cost, gross profit, margin % per SKU | Date, category, brand, supplier | Owner |
| RPT-04 | X report | Live shift snapshot: sales, tenders, returns, cash in/out, expected drawer — non-clearing | Current shift | Both |
| RPT-05 | Z report | Closed shift totals, counted cash, over/short variance, notes | Shift | Both |
| RPT-06 | Category / brand performance | Revenue, units, profit, share of sales, ranked | Date range | Owner |
| RPT-07 | Profit & margin | Revenue, COGS at moving average, gross profit, margin %, by item/category/period | Date range | Owner |
| RPT-08 | Stock on hand | Current qty per SKU with base and alternate units, location, reorder level | Category, brand, supplier, location | Both |
| RPT-09 | Stock valuation | Qty × moving-average cost and × selling price; total stock value | As-at date, category | Owner |
| RPT-10 | Reorder / low stock | Items at or below reorder level with suggested order qty, grouped by supplier | Supplier, category | Both |
| RPT-11 | Stock movement ledger | Every movement for a SKU with running balance and reference document | Item, date, movement type | Owner |
| RPT-12 | Slow-moving & dead stock | Items with no sale in N days, with value tied up | N days, category | Owner |
| RPT-13 | Fast-moving items | Top sellers by units and by value | Date range, top N | Owner |
| RPT-14 | Returns analysis | Returns by item, reason, disposition, refund method, value; return rate % | Date range, reason | Owner |
| RPT-15 | Damage & shrinkage | Write-offs and damaged returns by reason, quantity and value | Date range | Owner |
| RPT-16 | Purchases / GRN | Receipts by supplier, item, value; cost-price movement over time | Date, supplier | Owner |
| RPT-17 | Supplier payables | Outstanding balance and ageing per supplier | As-at date | Owner |
| RPT-18 | Customer sales & receivables | Sales, payments, outstanding balance, ageing for credit customers | Date, customer | Owner |
| RPT-19 | Tax report | Taxable sales, tax collected by rate; tax on returns netted off | Date range, tax rate | Owner |
| RPT-20 | Discounts & overrides | Every discount above threshold, price override and void, with user and reason | Date range, user | Owner |
| RPT-21 | Cash variance history | Over/short per shift across time, with trend | Date range | Owner |
| RPT-22 | Stock take variance | Counted vs system, by quantity and value, per take | Stock take | Owner |
| RPT-23 | Exceptions | Negative-stock sales, unlinked returns, out-of-window returns, cancelled bills, "no sale" drawer opens | Date range | Owner |
| RPT-24 | Backup status | Backup history: date, size, local/cloud status, verification result | Date range | Owner |
| RPT-25 | Audit log | All privileged actions with before/after values | Date, user, action | Owner |

---

# 10. Bill / receipt printing specification

## 10.1 Sales bill — 80 mm thermal specimen

```
        [ SHOP LOGO — optional ]
        ─────────────────────────────
              SHOP NAME
        123 Main Street, Town
        Tel: 000-0000000
        Tax Reg No: XXXXXXXXX
        ─────────────────────────────
Bill No : INV-2026-004312
Date    : 03/09/2026   Time: 14:07
Cashier : Kamal        Customer: Walk-in
─────────────────────────────────────
Item                Qty   Rate    Amount
─────────────────────────────────────
Hex Bolt M10x50 Zn
                  20 pcs  25.00   500.00
PVC Elbow 1" 90deg
                   4 pcs  90.00   360.00
Cable 2.5mm 3-core
                2.75 m   420.00  1155.00
  (cut to length - non returnable)
Cutting charge
                   1 svc  50.00    50.00
─────────────────────────────────────
Sub total                      2065.00
Discount                        -65.00
Taxable value                  2000.00
Tax @ 0%                          0.00
─────────────────────────────────────
TOTAL                          2000.00
─────────────────────────────────────
Cash                           2500.00
CHANGE                          500.00
─────────────────────────────────────
Items: 4     Units: 27.75

     ||| ||||| || |||| ||| ||||
        INV-2026-004312

Returns accepted within 14 days with
this bill. Cut goods & mixed paint are
non-returnable.
        Thank you — please come again
─────────────────────────────────────
```

## 10.2 Printing requirements

| ID | Requirement | Pri |
|---|---|---|
| PRT-01 | Paper widths 80 mm (default) and 58 mm must both be supported by configuration. | M |
| PRT-02 | Long item names must wrap onto a continuation line rather than truncate. | M |
| PRT-03 | The total must print in double-height/double-width for legibility. | M |
| PRT-04 | The bill number must print as a scannable 1D barcode (Code128) or QR. | M |
| PRT-05 | Auto-cut must fire after each document where the printer supports it. | M |
| PRT-06 | Non-returnable lines must be annotated on the bill. | M |
| PRT-07 | Return receipts must be headed clearly, reference the original bill number, and show reason and refund method. | M |
| PRT-08 | Reprints must carry a "DUPLICATE — not a valid original" line. | M |
| PRT-09 | The A4/A5 invoice template must include full shop and customer details, tax breakdown, and a signature area, for trade customers. | S |
| PRT-10 | Printing must be tested against at least two different ESC/POS printer models before acceptance. | M |

---

# 11. Data model

## 11.1 Principal entities

| Entity | Key attributes |
|---|---|
| **User** | id, username, password_hash, role, active, created_at, last_login |
| **Category** | id, name, parent_id, active |
| **Brand** | id, name, active |
| **Supplier** | id, name, contact, phone, address, tax_no, payment_terms, active |
| **Customer** | id, name, phone, address, type (retail/trade), tax_no, credit_limit, balance, active |
| **UnitOfMeasure** | id, name, symbol, decimal_places |
| **Product** | id, code, name, name_alt, category_id, brand_id, base_uom_id, type, tax_class_id, cost_avg, reorder_level, reorder_qty, location, non_returnable, serial_tracked, batch_tracked, active |
| **ProductVariant** | id, product_id, sku, attribute_values (size/length/thread/material/finish/colour), barcode(s), price, active |
| **ProductUom** | id, product_id, uom_id, conversion_factor, selling_price, is_base |
| **Barcode** | id, product_variant_id, barcode, is_primary |
| **PriceTier** | id, product_variant_id, tier (retail/trade), min_qty, price, valid_from, valid_to |
| **StockMovement** | id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, ref_doc_id, balance_after, user_id, timestamp, note |
| **PurchaseOrder / POLine** | id, supplier_id, date, status, expected_date / product_variant_id, qty, uom_id, cost |
| **GoodsReceipt / GRNLine** | id, supplier_id, invoice_no, date, total, user_id / product_variant_id, qty, uom_id, unit_cost, tax |
| **StockTake / StockTakeLine** | id, scope, started_at, completed_at, status, user_id / product_variant_id, system_qty, counted_qty, variance |
| **Sale** | id, bill_no, datetime, customer_id, user_id, shift_id, subtotal, discount, tax, rounding, total, status (completed/cancelled) |
| **SaleLine** | id, sale_id, product_variant_id, description, qty, uom_id, unit_price, discount, tax, line_total, qty_returned, note |
| **Payment** | id, sale_id / return_id, tender_type, amount, reference |
| **Return** | id, return_no, datetime, original_sale_id (nullable), user_id, shift_id, total_refund, refund_method, authorised_by |
| **ReturnLine** | id, return_id, sale_line_id, product_variant_id, qty, unit_price, reason, disposition (sellable/damaged) |
| **CreditNote** | id, number, issued_from_return_id, customer_id, amount_issued, amount_remaining, expiry, status |
| **Shift** | id, user_id, opened_at, opening_float, closed_at, counted_cash, expected_cash, variance, note, status |
| **CashMovement** | id, shift_id, direction (in/out), amount, reason, user_id, timestamp |
| **HeldBill** | id, label, payload, created_at, user_id |
| **AuditLog** | id, timestamp, user_id, action, entity_type, entity_id, before_json, after_json |
| **Setting** | key, value, updated_by, updated_at |
| **BackupRecord** | id, filename, taken_at, size, checksum, local_path, usb_status, cloud_status, verified_at |

## 11.2 Integrity requirements

| ID | Requirement |
|---|---|
| DM-01 | All monetary values stored as fixed-precision decimal, never floating point. |
| DM-02 | All quantities stored in base units as decimal with the configured precision. |
| DM-03 | Sale, return, GRN and shift writes are transactional — all or nothing. |
| DM-04 | Foreign keys enforced at the database level; no orphan lines. |
| DM-05 | StockMovement, Sale, Return, Shift and AuditLog are append-only; no UPDATE or DELETE paths exist in the application. |
| DM-06 | All timestamps stored with time zone or in a single documented local zone consistently. |
| DM-07 | The database must be indexed for the counter's hot paths: barcode lookup, name search, bill-number lookup, date-range scans. |

---

# 12. User interface requirements

| ID | Requirement | Pri |
|---|---|---|
| UI-01 | The sales screen must be fully operable from the keyboard and scanner; the mouse must be optional throughout. | M |
| UI-02 | Function keys must be fixed, visible on screen and printed on a quick-reference card: F1 Help, F2 New Sale, F3 Search Item, F4 Return, F5 Hold, F6 Recall, F7 Discount, F8 Customer, F9 Pay, F10 Reprint, F11 Stock Enquiry, F12 Day Close, Esc Cancel. | M |
| UI-03 | Text must be large and high-contrast; the running total must be the most prominent element on the screen. | M |
| UI-04 | The interface must be usable on a 1366×768 display and above. | M |
| UI-05 | Every destructive action (clear bill, cancel bill, post adjustment, restore) must require an explicit confirmation naming what will happen. | M |
| UI-06 | Error messages must be in plain language with a suggested next step — never a raw exception or error code. | M |
| UI-07 | The UI must support English plus one additional configurable language for item names and printed bills. | S |
| UI-08 | The application must start to a usable sales screen in under 10 seconds. | M |
| UI-09 | The status bar must permanently show: logged-in user, shift status, last backup time, cloud backup status, printer status. | M |
| UI-10 | Numeric entry fields must accept both the keypad and main number row, and must reject invalid characters silently rather than raising dialogs. | M |
| UI-11 | The back office must be visually distinct from the sales screen so the cashier cannot confuse the two. | S |
| UI-12 | An on-screen help panel and a printable one-page cheat sheet must cover the ten most common tasks. | M |

---

# 13. Non-functional requirements

## 13.1 Performance

| ID | Requirement |
|---|---|
| NFR-P1 | Barcode scan to line appearing on the bill: **≤ 300 ms** with 20,000 SKUs loaded. |
| NFR-P2 | Item name search results begin appearing within **500 ms** of typing. |
| NFR-P3 | Bill completion (save + stock update + print command dispatch): **≤ 2 seconds**. |
| NFR-P4 | Bill lookup by number: **≤ 1 second**. |
| NFR-P5 | Any report over a one-year range: **≤ 10 seconds**. |
| NFR-P6 | Application cold start to sales screen: **≤ 10 seconds**. |
| NFR-P7 | Performance must not degrade measurably with 3 years of transaction history (~500,000 bill lines). |

## 13.2 Reliability and availability

| ID | Requirement |
|---|---|
| NFR-R1 | The system must be fully functional with no internet connection, indefinitely. |
| NFR-R2 | An abrupt power loss must not corrupt the database; the last committed transaction must survive and any in-progress bill must be recoverable or cleanly discarded. |
| NFR-R3 | Peripheral failure (printer, scanner, drawer, scale) must degrade gracefully with a clear warning and must never block a sale. |
| NFR-R4 | Target availability during shop hours: **99.5%**, excluding hardware failure. |
| NFR-R5 | Recovery Point Objective: **≤ 24 hours** (daily backup) — improving to the last shift close in practice. Recovery Time Objective: **≤ 4 hours** on replacement hardware. |

## 13.3 Security

| ID | Requirement |
|---|---|
| NFR-S1 | Authentication required for all access; passwords/PINs salted and hashed. |
| NFR-S2 | Role-based authorisation enforced in the business layer, not only by hiding UI elements. |
| NFR-S3 | The local database file must be encrypted at rest, or the host machine must use full-disk encryption as a documented prerequisite. |
| NFR-S4 | Backup files must be encrypted before leaving the machine; the cloud provider must never hold readable business data. |
| NFR-S5 | All cloud communication must use TLS 1.2 or above. |
| NFR-S6 | Cloud credentials must be stored in the OS credential store, never in a plain configuration file. |
| NFR-S7 | The system must not store full card numbers, CVV or any cardholder data. Only tender type, amount and an optional last-4 reference. |
| NFR-S8 | The audit log must be append-only and unreachable by any UI edit or delete path. |
| NFR-S9 | Failed login attempts must be logged and rate-limited after 5 consecutive failures. |

## 13.4 Usability

| ID | Requirement |
|---|---|
| NFR-U1 | A cashier with no prior POS experience must be able to complete a normal sale after **30 minutes** of training. |
| NFR-U2 | The common sale path must require no mouse and no more than 4 keystrokes beyond the item scans. |
| NFR-U3 | Terminology must be the shop's, not the developer's ("Bill", "Return", "Goods received"), and must be configurable per FR-10. |

## 13.5 Maintainability and portability

| ID | Requirement |
|---|---|
| NFR-M1 | Business rules, tax rates, policies and receipt layouts must be configurable without code changes. |
| NFR-M2 | The application must be installable by a non-technical user via a single installer, with no separate database server to configure. |
| NFR-M3 | Updates must be applied without data loss and must include automatic schema migration with a pre-migration backup. |
| NFR-M4 | The system must run on Windows 10/11 (64-bit) as the primary target. |
| NFR-M5 | Source code, database schema, build instructions and an administrator manual must be delivered to the owner. **The owner's data and the means to reach it must not be locked to the vendor.** |
| NFR-M6 | A full data export (products, stock, sales, returns, customers, suppliers) to open formats must be available at any time. |

## 13.6 Capacity

| ID | Requirement |
|---|---|
| NFR-C1 | Products: 50,000 SKUs. |
| NFR-C2 | Transactions: 1,000 bills/day sustained; 5 years retained on-line. |
| NFR-C3 | Customers: 20,000. |
| NFR-C4 | Database growth must stay within the disk provisioned in Section 14 for 5 years. |

## 13.7 Legal and compliance

| ID | Requirement |
|---|---|
| NFR-L1 | Bills must carry all locally mandated details (shop name, address, tax registration number, bill number, date, tax breakdown) — final list to be confirmed by the owner. |
| NFR-L2 | Sales records must be retained for the locally required period and must be tamper-evident. |
| NFR-L3 | The return policy printed on the bill must match the policy configured in the system. |
| NFR-L4 | Customer personal data must be limited to what is needed, and must be exportable and erasable on request. |

---

# 14. Hardware and software requirements

## 14.1 Minimum terminal specification

| Component | Minimum | Recommended |
|---|---|---|
| CPU | Dual-core 2.0 GHz | Quad-core 2.5 GHz+ |
| RAM | 4 GB | 8 GB |
| Storage | 128 GB SSD | 256 GB SSD |
| Display | 1366×768 | 1920×1080, or touch |
| OS | Windows 10 64-bit | Windows 11 64-bit |
| Power | — | **UPS strongly recommended** (protects the database from abrupt power loss) |

## 14.2 Peripherals

| Device | Specification | Necessity |
|---|---|---|
| Barcode scanner | USB, HID keyboard-wedge, 1D minimum (2D preferred for QR on bills) | Required |
| Receipt printer | 80 mm thermal, ESC/POS command set, auto-cutter, USB, with RJ11/RJ12 drawer port | Required |
| Cash drawer | Standard, driven from the printer's kick-out port | Required |
| UPS | 600 VA or above | Strongly recommended |
| USB flash drive | 32 GB, dedicated to backups | Required |
| Label printer | Thermal label printer for shelf/product labels | Optional |
| Weighing scale | Serial/USB with a documented protocol | Optional |
| Internet | Any broadband or mobile connection; **intermittent is acceptable** | Backup only |

---

# 15. Out of scope (this release)

Explicitly excluded, to prevent scope drift:

| ID | Excluded | Note |
|---|---|---|
| OS-01 | Multiple cashiers working simultaneously, or a second till | Core constraint of this project |
| OS-02 | Multi-branch / multi-warehouse, inter-branch transfers | Single shop only |
| OS-03 | E-commerce / online store / marketplace integration | — |
| OS-04 | Integrated card payment processing | Standalone card terminal assumed |
| OS-05 | Full accounting (general ledger, trial balance, P&L, balance sheet) | Export to the accountant instead |
| OS-06 | Payroll and HR | — |
| OS-07 | Loyalty points, gift cards, promotional campaigns | Store credit notes are supported; loyalty is not |
| OS-08 | Mobile app or tablet POS | Web tier is backup only |
| OS-09 | Equipment rental / hire contracts | Common in hardware ERP, not needed here |
| OS-10 | Job costing, contractor project accounts, special-order supplier catalogues | Basic credit accounts only |
| OS-11 | Delivery/logistics management, driver dispatch | — |
| OS-12 | SMS/email marketing | — |
| OS-13 | Live cloud dashboard or remote real-time access to sales data | Explicitly excluded by the "web for backup only" constraint |

Items OS-01, OS-02, OS-08 and OS-13 are architectural exclusions. Others could be added in a later phase.

---

# 16. Acceptance criteria

The system is accepted when all of the following are demonstrated on the shop's actual hardware:

| ID | Criterion |
|---|---|
| AC-01 | All **Must** requirements are implemented and pass their test cases. |
| AC-02 | A 10-line bill including a decimal-quantity item, a unit switch, a discount and split tender completes correctly and prints correctly. |
| AC-03 | A partial return against an existing bill refunds at the original paid price, restocks only the lines marked sellable, and prints a correct return receipt. |
| AC-04 | An exchange with a higher-priced replacement collects the correct difference in one transaction. |
| AC-05 | An attempted return of a non-returnable cut item is blocked and only proceeds with owner override, which is logged. |
| AC-06 | Cumulative over-return against a single bill line is impossible. |
| AC-07 | 100 SKUs are imported from a spreadsheet with a validation report and correct resulting stock and prices. |
| AC-08 | A GRN including a box→piece conversion increases stock in base units correctly and updates moving-average cost correctly. |
| AC-09 | A bulk break (1 coil → 90 m) posts a balanced pair of movements with cost carried across. |
| AC-10 | A stock take across one category produces a correct variance report and posts corrections in one batch. |
| AC-11 | X and Z reports are produced; the Z report cash variance is computed correctly against a deliberately mis-counted drawer, and the shift locks. |
| AC-12 | Report totals reconcile: RPT-01 net sales = sum of tenders in RPT-05 = sum of sales minus returns in RPT-02, for the same period, to the cent. |
| AC-13 | The system operates for a full simulated trading day with the network cable unplugged, with zero functional loss. |
| AC-14 | A backup is taken, uploaded, downloaded from the web page, verified, and restored to a clean machine, and the restored data matches. |
| AC-15 | A simulated power cut mid-bill leaves the database uncorrupted and the last committed bill intact. |
| AC-16 | The printer is disconnected mid-transaction; the sale still completes and the bill is queued for reprint. |
| AC-17 | A cashier account cannot view cost prices, margins or owner-only reports, verified at the business-logic layer as well as the UI. |
| AC-18 | Performance targets NFR-P1 to NFR-P6 are met with a database seeded with 20,000 SKUs and 100,000 historical bill lines. |
| AC-19 | Bill numbering is proven gapless across 500 consecutive bills including cancellations. |
| AC-20 | User manual, admin manual, keyboard cheat sheet, source code and schema documentation are delivered. |

---

# 17. Phased delivery plan

| Phase | Duration (indicative) | Contents | Outcome |
|---|---|---|---|
| **Phase 0 — Discovery** | 1 week | Confirm open questions (§18), collect current price list, return policy, bill format, tax rules | Signed-off SRS |
| **Phase 1 — Core trading** | 4–5 weeks | Products, categories, UOM & variants, barcodes, stock ledger, sales, cash tender, bill printing, users, local backup | Shop can sell and print |
| **Phase 2 — Returns & inventory control** | 3–4 weeks | Returns, exchanges, credit notes, GRN, adjustments, damage, stock take, reorder alerts | Shop can control stock |
| **Phase 3 — Reports & cash discipline** | 2–3 weeks | Full report suite, X/Z reports, shift & cash management, audit log, exceptions | Owner has visibility |
| **Phase 4 — Backup & resilience** | 2 weeks | Cloud backup pipeline, encryption, retention, backup web page, guided restore, restore drill | Business is protected |
| **Phase 5 — Extras & handover** | 2 weeks | Trade pricing, credit customers, quantity breaks, A4 invoices, label printing, data migration, training, documentation | Go-live |
| **Phase 6 — Warranty support** | 3 months | Defect fixes, tuning, minor changes | Stable operation |

Phases 1 and 2 are the minimum viable system. Phase 4 should not be deferred — an unbacked-up POS is the single largest risk the shop carries.

---

# 18. Open questions for the client

These must be answered before development begins. Each has a proposed default so that work is not blocked.

| # | Question | Proposed default |
|---|---|---|
| Q-01 | Country, currency and tax regime? Are tax-inclusive prices used? | Configurable; tax-inclusive assumed |
| Q-02 | What details are legally required on a bill? | Confirm with the shop's accountant |
| Q-03 | Exact return policy: window length, receipt requirement, restocking fee, non-returnable categories? | 14 days, receipt required, no fee, cut goods & mixed paint non-returnable |
| Q-04 | Are card payments accepted, and on what device? | Standalone terminal; POS records tender only |
| Q-05 | Are credit/contractor accounts needed at launch? | Phase 5 |
| Q-06 | Approximate SKU count and daily bill volume? | 20,000 SKUs, 200 bills/day |
| Q-07 | Which items are sold by length/weight, and what precision? | Cable, pipe, rope, chain, loose fasteners; 3 decimals |
| Q-08 | Is existing product/stock data available electronically? | Spreadsheet import assumed |
| Q-09 | Which cloud store for backups — Google Drive, OneDrive, S3-compatible, or vendor-hosted? | Owner's own Google Drive |
| Q-10 | Who holds the backup encryption passphrase, and where is it kept off-site? | Owner; written copy kept off-premises |
| Q-11 | Should the cashier be allowed to sell below stock on hand (negative stock)? | Warn and allow, logged |
| Q-12 | What is the cashier's maximum discount without owner approval? | 5% per line, 5% per bill |
| Q-13 | Is a second language needed on screen and on bills? | English only at launch |
| Q-14 | Existing printer and scanner models, if any? | To be confirmed before Phase 1 |
| Q-15 | Is barcode label printing needed for loose items at launch? | Yes, Phase 1 |
| Q-16 | Preferred bill number format and starting number? | `INV-YYYY-NNNNNN`, starting at 1 |

---

# 19. Glossary

| Term | Meaning |
|---|---|
| **Base unit** | The unit in which stock is physically held and counted (piece, metre, kg) |
| **Alternate unit** | A larger or smaller selling/purchasing unit converted to the base unit by a fixed factor |
| **Bulk break** | Converting stock from one packaging form to another (coil → loose metres) |
| **COGS** | Cost of goods sold, valued here at moving-average cost |
| **Credit note** | A numbered store-credit voucher issued in place of a cash refund |
| **ESC/POS** | The de facto command standard for thermal receipt printers |
| **GRN** | Goods Receipt Note — the document recording stock received from a supplier |
| **Held bill** | An in-progress sale parked so another customer can be served |
| **Linked return** | A return matched to the original bill |
| **Moving average cost** | Stock cost recalculated as a weighted average on each receipt |
| **Offline-first** | Architecture where the local database is authoritative and the network is optional |
| **RPO / RTO** | Recovery Point Objective (how much data you can lose) / Recovery Time Objective (how fast you must be back) |
| **SKU** | Stock Keeping Unit — one uniquely identified, individually counted item |
| **Unlinked return** | A return with no original bill — the main return-fraud exposure |
| **UOM** | Unit of Measure |
| **X report** | Mid-shift, non-clearing sales and cash snapshot |
| **Z report** | End-of-shift report that reconciles counted cash against expected and locks the period |

---

## Sign-off

| Role | Name | Signature | Date |
|---|---|---|---|
| Shop Owner | | | |
| Development Lead | | | |

*End of document.*
