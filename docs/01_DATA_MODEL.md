# Data Model

SQLite 3 (SQLCipher). All identifiers `snake_case`. Every table has `id INTEGER PRIMARY KEY` —
plain, never `AUTOINCREMENT` — apart from three documented exceptions listed in §13.

---

## 1. Storage conventions

| Concept | Storage | Rule |
|---|---|---|
| Money | `INTEGER`, scaled ×10 000 | `12345678` = 1 234.5678 currency units. Never `REAL`. |
| Quantity | `INTEGER`, scaled ×10 000 | Always in the product's **base unit**. |
| Percentage / tax rate | `INTEGER`, scaled ×10 000 | `1500` = 15.00% (i.e. 0.1500) |
| Conversion factor | `INTEGER`, scaled ×10 000 | `1 box = 100 pcs` → `1000000` |
| Timestamp | `TEXT` ISO-8601 with offset | `2026-09-03T14:22:31.123+05:30`. Sortable, unambiguous (DM-06). |
| Date (business day) | `TEXT` `YYYY-MM-DD` | Used for rollups and report grouping |
| Boolean | `INTEGER` 0/1 | |
| Enum | `TEXT` | Uppercase snake, constrained by `CHECK`. Readable in a raw dump — matters for handover. |
| JSON | `TEXT` | Audit before/after, held-bill payload, variant attributes |

Constants in code: `MoneyScale = 10_000`, `QtyScale = 10_000`, `RateScale = 10_000`.

`stock_balance` is the only projection in the schema, and the only one in the skeleton migration
(§13). It is written in the same transaction as the `stock_movement` rows it summarises and is
rebuildable from them (CLAUDE.md invariant 3). The rebuild command, the start-up sample check and
the recompute test belong to **P1-T07** and are deliberately absent from the skeleton: a rebuild
that nothing yet posts to would be untestable ceremony.

---

## 2. Subject areas

The schema splits into six areas. Diagrams below are per area; a table appears in the area that owns it.

```mermaid
flowchart LR
    A["A · Catalogue<br/>& Pricing"] --> B["B · Inventory<br/>& Purchasing"]
    A --> C["C · Sales<br/>& Tender"]
    C --> D["D · Returns<br/>& Credit"]
    B --> C
    C --> E["E · Cash<br/>& Shifts"]
    D --> E
    F["F · System<br/>users · audit · settings · numbering · backup · print"] -.-> A
    F -.-> B
    F -.-> C
    F -.-> D
    F -.-> E
```

---

## 3. Area A — Catalogue and pricing

```mermaid
erDiagram
    CATEGORY ||--o{ CATEGORY : "parent of"
    CATEGORY ||--o{ PRODUCT : classifies
    BRAND    ||--o{ PRODUCT : brands
    TAX_CLASS ||--o{ PRODUCT : taxes
    UOM      ||--o{ PRODUCT : "base unit"
    PRODUCT  ||--|{ PRODUCT_VARIANT : "has SKUs"
    PRODUCT  ||--|{ PRODUCT_UOM : "sells in"
    UOM      ||--o{ PRODUCT_UOM : unit
    PRODUCT_VARIANT ||--o{ BARCODE : "identified by"
    PRODUCT_VARIANT ||--o{ PRICE_TIER : "priced by"
    PRODUCT_VARIANT ||--o{ PRICE_CHANGE_LOG : "history"
    SUPPLIER ||--o{ PRODUCT_SUPPLIER : supplies
    PRODUCT  ||--o{ PRODUCT_SUPPLIER : "sourced from"

    PRODUCT {
        int id PK
        text code UK
        text name
        text name_alt
        int category_id FK
        int brand_id FK
        int base_uom_id FK
        text type "STANDARD|DECIMAL|SERVICE|NON_INVENTORY"
        int tax_class_id FK
        int cost_avg "money"
        int reorder_level "qty"
        int reorder_qty "qty"
        text location
        int non_returnable
        int max_discount_rate
        int active
    }
    PRODUCT_VARIANT {
        int id PK
        int product_id FK
        text sku UK
        text attributes "JSON"
        int price "money, base unit"
        int active
    }
    PRODUCT_UOM {
        int id PK
        int product_id FK
        int uom_id FK
        int conversion_factor
        int selling_price "money"
        int is_base
    }
    BARCODE {
        int id PK
        int product_variant_id FK
        text barcode UK
        int is_primary
    }
    PRICE_TIER {
        int id PK
        int product_variant_id FK
        text tier "RETAIL|TRADE"
        int min_qty
        int price "money"
        text valid_from
        text valid_to
    }
```

### DDL

```sql
CREATE TABLE category (
  id        INTEGER PRIMARY KEY,
  name      TEXT NOT NULL,
  parent_id INTEGER REFERENCES category(id),
  active    INTEGER NOT NULL DEFAULT 1,
  UNIQUE (name, parent_id)
);
-- FR-2.20: two levels only. Enforced by trigger: a category whose
-- parent_id is not null may not itself be a parent.

CREATE TABLE brand (
  id     INTEGER PRIMARY KEY,
  name   TEXT NOT NULL UNIQUE,
  active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE tax_class (
  id       INTEGER PRIMARY KEY,
  name     TEXT NOT NULL UNIQUE,          -- 'Standard 15%', 'Zero rated'
  rate     INTEGER NOT NULL,              -- scaled x10000; 1500 = 15%
  active   INTEGER NOT NULL DEFAULT 1
);
-- Tax-inclusive vs exclusive is a shop-wide setting (FR-10.3), not per class.

CREATE TABLE uom (
  id             INTEGER PRIMARY KEY,
  name           TEXT NOT NULL UNIQUE,    -- 'Metre', 'Piece', 'Coil'
  symbol         TEXT NOT NULL,           -- 'm', 'pc', 'coil'
  decimal_places INTEGER NOT NULL DEFAULT 0 CHECK (decimal_places BETWEEN 0 AND 4)
);

CREATE TABLE product (
  id                INTEGER PRIMARY KEY,
  code              TEXT NOT NULL UNIQUE,
  name              TEXT NOT NULL,
  name_alt          TEXT,
  category_id       INTEGER REFERENCES category(id),
  brand_id          INTEGER REFERENCES brand(id),
  base_uom_id       INTEGER NOT NULL REFERENCES uom(id),
  type              TEXT NOT NULL CHECK (type IN
                      ('STANDARD','DECIMAL','SERVICE','NON_INVENTORY')),
  tax_class_id      INTEGER NOT NULL REFERENCES tax_class(id),
  cost_avg          INTEGER NOT NULL DEFAULT 0,   -- moving average, base unit
  reorder_level     INTEGER NOT NULL DEFAULT 0,
  reorder_qty       INTEGER NOT NULL DEFAULT 0,
  location          TEXT,                          -- rack / bin
  non_returnable    INTEGER NOT NULL DEFAULT 0,    -- FR-5, AC-05
  min_sell_qty      INTEGER NOT NULL DEFAULT 0,
  max_discount_rate INTEGER,                       -- null = use global limit
  warranty_days     INTEGER,
  notes             TEXT,
  image_path        TEXT,
  active            INTEGER NOT NULL DEFAULT 1,
  created_at        TEXT NOT NULL,
  updated_at        TEXT NOT NULL
);
CREATE INDEX ix_product_category ON product(category_id);
CREATE INDEX ix_product_brand    ON product(brand_id);
CREATE INDEX ix_product_active   ON product(active);

-- Every product has at least one variant, even if it has no attributes.
-- Uniform handling everywhere downstream: stock, sales and returns always
-- reference a variant, never a product. This is worth the one extra row.
CREATE TABLE product_variant (
  id         INTEGER PRIMARY KEY,
  product_id INTEGER NOT NULL REFERENCES product(id),
  sku        TEXT NOT NULL UNIQUE,
  attributes TEXT NOT NULL DEFAULT '{}',   -- {"size":"M8","length":"50mm",...}
  price      INTEGER NOT NULL,             -- money, per BASE unit, retail
  active     INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL
);
CREATE INDEX ix_variant_product ON product_variant(product_id);

CREATE TABLE product_uom (
  id                INTEGER PRIMARY KEY,
  product_id        INTEGER NOT NULL REFERENCES product(id),
  uom_id            INTEGER NOT NULL REFERENCES uom(id),
  conversion_factor INTEGER NOT NULL CHECK (conversion_factor > 0),
  selling_price     INTEGER,      -- null = base price x factor (FR-2.5)
  is_base           INTEGER NOT NULL DEFAULT 0,
  UNIQUE (product_id, uom_id)
);
-- Exactly one row per product with is_base = 1 and conversion_factor = 10000.
-- Enforced by trigger.

CREATE TABLE barcode (
  id                 INTEGER PRIMARY KEY,
  product_variant_id INTEGER NOT NULL REFERENCES product_variant(id),
  barcode            TEXT NOT NULL UNIQUE,
  is_primary         INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX ix_barcode_variant ON barcode(product_variant_id);
-- barcode UNIQUE is the hot path index for NFR-P1.

CREATE TABLE price_tier (
  id                 INTEGER PRIMARY KEY,
  product_variant_id INTEGER NOT NULL REFERENCES product_variant(id),
  tier               TEXT NOT NULL CHECK (tier IN ('RETAIL','TRADE')),
  min_qty            INTEGER NOT NULL DEFAULT 0,
  price              INTEGER NOT NULL,
  valid_from         TEXT,
  valid_to           TEXT
);
CREATE INDEX ix_price_tier_lookup
  ON price_tier(product_variant_id, tier, min_qty);

CREATE TABLE price_change_log (          -- FR-2.17
  id                 INTEGER PRIMARY KEY,
  product_variant_id INTEGER NOT NULL REFERENCES product_variant(id),
  old_price          INTEGER NOT NULL,
  new_price          INTEGER NOT NULL,
  changed_at         TEXT NOT NULL,
  user_id            INTEGER NOT NULL REFERENCES app_user(id),
  reason             TEXT
);

CREATE TABLE product_supplier (
  id          INTEGER PRIMARY KEY,
  product_id  INTEGER NOT NULL REFERENCES product(id),
  supplier_id INTEGER NOT NULL REFERENCES supplier(id),
  supplier_ref TEXT,
  last_cost   INTEGER,
  UNIQUE (product_id, supplier_id)
);
```

### Full-text search (FR-2.11, NFR-P2)

```sql
CREATE VIRTUAL TABLE product_search USING fts5(
  name, name_alt, code, sku, brand, category, location,
  content=''            -- contentless; we store the rowid mapping ourselves
);
-- rowid = product_variant.id
-- Maintained by AFTER INSERT/UPDATE/DELETE triggers on product and
-- product_variant. Rebuildable by ReindexSearchCommand.
```

---

## 4. Area B — Inventory and purchasing

```mermaid
erDiagram
    PRODUCT_VARIANT ||--|| STOCK_BALANCE : "current state"
    PRODUCT_VARIANT ||--o{ STOCK_MOVEMENT : "ledger"
    SUPPLIER ||--o{ PURCHASE_ORDER : "ordered from"
    PURCHASE_ORDER ||--|{ PURCHASE_ORDER_LINE : contains
    SUPPLIER ||--o{ GOODS_RECEIPT : "received from"
    GOODS_RECEIPT ||--|{ GOODS_RECEIPT_LINE : contains
    PURCHASE_ORDER ||--o{ GOODS_RECEIPT : fulfils
    STOCK_TAKE ||--|{ STOCK_TAKE_LINE : counts
    GOODS_RECEIPT_LINE ||--o{ STOCK_MOVEMENT : posts
    STOCK_TAKE_LINE ||--o{ STOCK_MOVEMENT : posts

    STOCK_MOVEMENT {
        int id PK
        int product_variant_id FK
        text movement_type
        int qty_base "signed"
        int unit_cost "money"
        text ref_doc_type
        int ref_doc_id
        int balance_after
        int user_id FK
        text occurred_at
        text note
    }
    STOCK_BALANCE {
        int product_variant_id PK
        int qty_base
        int cost_avg "money"
        text updated_at
    }
```

### DDL

```sql
CREATE TABLE supplier (
  id             INTEGER PRIMARY KEY,
  name           TEXT NOT NULL,
  contact        TEXT, phone TEXT, address TEXT, tax_no TEXT,
  payment_terms  TEXT,
  active         INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE stock_balance (
  product_variant_id INTEGER PRIMARY KEY REFERENCES product_variant(id),
  qty_base           INTEGER NOT NULL DEFAULT 0,
  cost_avg           INTEGER NOT NULL DEFAULT 0,
  updated_at         TEXT NOT NULL
);
CREATE INDEX ix_stock_balance_low ON stock_balance(qty_base);

CREATE TABLE stock_movement (            -- APPEND ONLY
  id                 INTEGER PRIMARY KEY,
  product_variant_id INTEGER NOT NULL REFERENCES product_variant(id),
  movement_type      TEXT NOT NULL CHECK (movement_type IN (
                       'GRN','SALE','RETURN_IN','ADJUSTMENT','DAMAGE',
                       'STOCK_TAKE','BULK_BREAK_OUT','BULK_BREAK_IN',
                       'OPENING','TRANSFER_OUT','TRANSFER_IN')),
  qty_base           INTEGER NOT NULL,   -- signed: + increases stock
  unit_cost          INTEGER NOT NULL,   -- money, at the moment of movement
  ref_doc_type       TEXT NOT NULL,      -- 'SALE','GRN','RETURN','STOCK_TAKE',...
  ref_doc_id         INTEGER,
  balance_after      INTEGER NOT NULL,
  user_id            INTEGER NOT NULL REFERENCES app_user(id),
  occurred_at        TEXT NOT NULL,
  note               TEXT
);
CREATE INDEX ix_movement_variant_time
  ON stock_movement(product_variant_id, occurred_at);
CREATE INDEX ix_movement_ref  ON stock_movement(ref_doc_type, ref_doc_id);
CREATE INDEX ix_movement_time ON stock_movement(occurred_at);

CREATE TABLE purchase_order (
  id            INTEGER PRIMARY KEY,
  po_no         TEXT NOT NULL UNIQUE,
  supplier_id   INTEGER NOT NULL REFERENCES supplier(id),
  ordered_at    TEXT NOT NULL,
  expected_at   TEXT,
  status        TEXT NOT NULL CHECK (status IN
                  ('DRAFT','SENT','PARTIAL','RECEIVED','CANCELLED')),
  user_id       INTEGER NOT NULL REFERENCES app_user(id),
  note          TEXT
);

CREATE TABLE purchase_order_line (
  id                 INTEGER PRIMARY KEY,
  purchase_order_id  INTEGER NOT NULL REFERENCES purchase_order(id),
  product_variant_id INTEGER NOT NULL REFERENCES product_variant(id),
  qty                INTEGER NOT NULL,      -- in uom_id, not base
  uom_id             INTEGER NOT NULL REFERENCES uom(id),
  unit_cost          INTEGER NOT NULL,
  qty_received_base  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE goods_receipt (
  id               INTEGER PRIMARY KEY,
  grn_no           TEXT NOT NULL UNIQUE,
  supplier_id      INTEGER NOT NULL REFERENCES supplier(id),
  purchase_order_id INTEGER REFERENCES purchase_order(id),
  supplier_inv_no  TEXT,
  received_at      TEXT NOT NULL,
  subtotal         INTEGER NOT NULL,
  tax              INTEGER NOT NULL DEFAULT 0,
  other_cost       INTEGER NOT NULL DEFAULT 0,   -- freight etc, apportioned
  total            INTEGER NOT NULL,
  user_id          INTEGER NOT NULL REFERENCES app_user(id),
  note             TEXT
);

CREATE TABLE goods_receipt_line (
  id                 INTEGER PRIMARY KEY,
  goods_receipt_id   INTEGER NOT NULL REFERENCES goods_receipt(id),
  product_variant_id INTEGER NOT NULL REFERENCES product_variant(id),
  qty                INTEGER NOT NULL,         -- as entered, in uom_id
  uom_id             INTEGER NOT NULL REFERENCES uom(id),
  qty_base           INTEGER NOT NULL,         -- converted (AC-08)
  unit_cost          INTEGER NOT NULL,         -- per uom_id
  unit_cost_base     INTEGER NOT NULL,         -- per base unit
  tax                INTEGER NOT NULL DEFAULT 0,
  line_total         INTEGER NOT NULL
);
CREATE INDEX ix_grn_line_grn ON goods_receipt_line(goods_receipt_id);

CREATE TABLE stock_take (
  id           INTEGER PRIMARY KEY,
  scope        TEXT NOT NULL,        -- 'ALL' | 'CATEGORY:12' | 'LOCATION:A3'
  started_at   TEXT NOT NULL,
  completed_at TEXT,
  status       TEXT NOT NULL CHECK (status IN ('OPEN','POSTED','ABANDONED')),
  user_id      INTEGER NOT NULL REFERENCES app_user(id)
);

CREATE TABLE stock_take_line (
  id                 INTEGER PRIMARY KEY,
  stock_take_id      INTEGER NOT NULL REFERENCES stock_take(id),
  product_variant_id INTEGER NOT NULL REFERENCES product_variant(id),
  system_qty         INTEGER NOT NULL,   -- frozen when the sheet is generated
  counted_qty        INTEGER,
  variance           INTEGER,
  counted_at         TEXT
);
CREATE INDEX ix_stock_take_line_take ON stock_take_line(stock_take_id);
```

---

## 5. Area C — Sales and tender

```mermaid
erDiagram
    CUSTOMER ||--o{ SALE : "buys"
    APP_USER ||--o{ SALE : "rings up"
    SHIFT    ||--o{ SALE : "within"
    SALE     ||--|{ SALE_LINE : contains
    SALE     ||--|{ PAYMENT : "tendered by"
    PRODUCT_VARIANT ||--o{ SALE_LINE : "sold as"
    UOM      ||--o{ SALE_LINE : "sold in"
    SALE_LINE ||--o{ STOCK_MOVEMENT : posts
    APP_USER ||--o{ HELD_BILL : parks

    SALE {
        int id PK
        text bill_no UK
        text sold_at
        int customer_id FK
        int user_id FK
        int shift_id FK
        int subtotal "money"
        int line_discount "money"
        int bill_discount "money"
        int tax "money"
        int rounding "money"
        int total "money"
        text status "COMPLETED|CANCELLED"
        text prev_hash
        text row_hash
    }
    SALE_LINE {
        int id PK
        int sale_id FK
        int product_variant_id FK
        text description
        int qty "in uom_id"
        int qty_base
        int uom_id FK
        int unit_price "money"
        int discount "money"
        int tax_rate
        int tax "money"
        int line_total "money"
        int unit_cost "money, COGS snapshot"
        int qty_returned "base units"
    }
    PAYMENT {
        int id PK
        int sale_id FK
        int sale_return_id FK
        text tender_type
        int amount "money"
        text reference
    }
```

### DDL

```sql
CREATE TABLE customer (
  id           INTEGER PRIMARY KEY,
  name         TEXT NOT NULL,
  phone        TEXT, address TEXT, tax_no TEXT,
  type         TEXT NOT NULL DEFAULT 'RETAIL' CHECK (type IN ('RETAIL','TRADE')),
  credit_limit INTEGER NOT NULL DEFAULT 0,
  balance      INTEGER NOT NULL DEFAULT 0,
  active       INTEGER NOT NULL DEFAULT 1,
  created_at   TEXT NOT NULL
);
CREATE INDEX ix_customer_phone ON customer(phone);

CREATE TABLE sale (                       -- APPEND ONLY (status is the exception)
  id            INTEGER PRIMARY KEY,
  bill_no       TEXT NOT NULL UNIQUE,
  sold_at       TEXT NOT NULL,
  business_date TEXT NOT NULL,            -- YYYY-MM-DD, for rollups
  customer_id   INTEGER REFERENCES customer(id),
  user_id       INTEGER NOT NULL REFERENCES app_user(id),
  shift_id      INTEGER NOT NULL REFERENCES shift(id),
  subtotal      INTEGER NOT NULL,
  line_discount INTEGER NOT NULL DEFAULT 0,
  bill_discount INTEGER NOT NULL DEFAULT 0,
  tax           INTEGER NOT NULL DEFAULT 0,
  rounding      INTEGER NOT NULL DEFAULT 0,
  total         INTEGER NOT NULL,
  cogs          INTEGER NOT NULL DEFAULT 0,
  status        TEXT NOT NULL CHECK (status IN ('COMPLETED','CANCELLED')),
  cancelled_by  INTEGER REFERENCES app_user(id),
  cancelled_at  TEXT,
  note          TEXT,
  prev_hash     TEXT NOT NULL,
  row_hash      TEXT NOT NULL
);
CREATE INDEX ix_sale_date   ON sale(business_date);
CREATE INDEX ix_sale_shift  ON sale(shift_id);
CREATE INDEX ix_sale_cust   ON sale(customer_id);
CREATE INDEX ix_sale_soldat ON sale(sold_at);

CREATE TABLE sale_line (                  -- APPEND ONLY (qty_returned is the exception)
  id                 INTEGER PRIMARY KEY,
  sale_id            INTEGER NOT NULL REFERENCES sale(id),
  line_no            INTEGER NOT NULL,
  product_variant_id INTEGER REFERENCES product_variant(id),  -- null for open item
  description        TEXT NOT NULL,       -- snapshot: the name as sold
  qty                INTEGER NOT NULL,
  uom_id             INTEGER NOT NULL REFERENCES uom(id),
  qty_base           INTEGER NOT NULL,
  unit_price         INTEGER NOT NULL,    -- per uom_id, as charged
  discount           INTEGER NOT NULL DEFAULT 0,
  tax_rate           INTEGER NOT NULL DEFAULT 0,
  tax                INTEGER NOT NULL DEFAULT 0,
  line_total         INTEGER NOT NULL,
  unit_cost          INTEGER NOT NULL DEFAULT 0,   -- COGS snapshot, base unit
  qty_returned       INTEGER NOT NULL DEFAULT 0,   -- base units (AC-06)
  note               TEXT,
  UNIQUE (sale_id, line_no)
);
CREATE INDEX ix_sale_line_sale    ON sale_line(sale_id);
CREATE INDEX ix_sale_line_variant ON sale_line(product_variant_id, sale_id);

CREATE TABLE payment (                    -- APPEND ONLY
  id             INTEGER PRIMARY KEY,
  sale_id        INTEGER REFERENCES sale(id),
  sale_return_id INTEGER REFERENCES sale_return(id),
  tender_type    TEXT NOT NULL CHECK (tender_type IN
                   ('CASH','CARD','BANK_TRANSFER','CREDIT_NOTE','ON_ACCOUNT','CHEQUE')),
  amount         INTEGER NOT NULL,        -- negative for a refund out
  reference      TEXT,                    -- max 20 chars, PAN-rejecting (NFR-S7)
  paid_at        TEXT NOT NULL,
  CHECK ((sale_id IS NOT NULL) <> (sale_return_id IS NOT NULL))
);
CREATE INDEX ix_payment_sale   ON payment(sale_id);
CREATE INDEX ix_payment_return ON payment(sale_return_id);

CREATE TABLE held_bill (
  id         INTEGER PRIMARY KEY,
  label      TEXT NOT NULL,
  payload    TEXT NOT NULL,               -- JSON snapshot of the in-progress bill
  created_at TEXT NOT NULL,
  user_id    INTEGER NOT NULL REFERENCES app_user(id)
);
```

**Why `sale_line` snapshots `description`, `unit_price` and `unit_cost`:** a return six months later must refund at the price originally paid (AC-03) and profit reports must use the cost at the time of sale. Neither can be recovered from the catalogue, because the catalogue moves.

---

## 6. Area D — Returns and credit

```mermaid
erDiagram
    SALE ||--o{ SALE_RETURN : "returned against"
    SALE_RETURN ||--|{ SALE_RETURN_LINE : contains
    SALE_LINE ||--o{ SALE_RETURN_LINE : "reverses"
    SALE_RETURN ||--o{ PAYMENT : "refunded by"
    SALE_RETURN ||--o| CREDIT_NOTE : issues
    CREDIT_NOTE ||--o{ CREDIT_NOTE_REDEMPTION : "spent on"
    SALE ||--o{ CREDIT_NOTE_REDEMPTION : "paid with"
    SALE_RETURN_LINE ||--o{ STOCK_MOVEMENT : posts

    SALE_RETURN {
        int id PK
        text return_no UK
        text returned_at
        int original_sale_id FK "nullable = unlinked"
        int user_id FK
        int shift_id FK
        int total_refund "money"
        text refund_method
        int authorised_by FK
        text reason
    }
    SALE_RETURN_LINE {
        int id PK
        int sale_return_id FK
        int sale_line_id FK "nullable"
        int product_variant_id FK
        int qty_base
        int unit_price "money, original"
        text reason
        text disposition "SELLABLE|DAMAGED"
    }
    CREDIT_NOTE {
        int id PK
        text number UK
        int sale_return_id FK
        int customer_id FK
        int amount_issued "money"
        int amount_remaining "money"
        text expires_on
        text status
    }
```

### DDL

```sql
CREATE TABLE sale_return (                -- APPEND ONLY
  id               INTEGER PRIMARY KEY,
  return_no        TEXT NOT NULL UNIQUE,
  returned_at      TEXT NOT NULL,
  business_date    TEXT NOT NULL,
  original_sale_id INTEGER REFERENCES sale(id),   -- NULL = unlinked (FR-5, elevated risk)
  exchange_sale_id INTEGER REFERENCES sale(id),   -- set when part of an exchange
  customer_id      INTEGER REFERENCES customer(id),
  user_id          INTEGER NOT NULL REFERENCES app_user(id),
  shift_id         INTEGER NOT NULL REFERENCES shift(id),
  subtotal         INTEGER NOT NULL,
  tax              INTEGER NOT NULL DEFAULT 0,
  restocking_fee   INTEGER NOT NULL DEFAULT 0,
  total_refund     INTEGER NOT NULL,
  refund_method    TEXT NOT NULL CHECK (refund_method IN
                     ('CASH','CARD','CREDIT_NOTE','EXCHANGE','ON_ACCOUNT')),
  authorised_by    INTEGER REFERENCES app_user(id),   -- owner, for overrides
  reason           TEXT,
  prev_hash        TEXT NOT NULL,
  row_hash         TEXT NOT NULL
);
CREATE INDEX ix_return_date ON sale_return(business_date);
CREATE INDEX ix_return_sale ON sale_return(original_sale_id);

CREATE TABLE sale_return_line (           -- APPEND ONLY
  id                 INTEGER PRIMARY KEY,
  sale_return_id     INTEGER NOT NULL REFERENCES sale_return(id),
  sale_line_id       INTEGER REFERENCES sale_line(id),   -- NULL when unlinked
  product_variant_id INTEGER NOT NULL REFERENCES product_variant(id),
  qty_base           INTEGER NOT NULL CHECK (qty_base > 0),
  unit_price         INTEGER NOT NULL,   -- the price ORIGINALLY paid (AC-03)
  unit_cost          INTEGER NOT NULL,
  tax                INTEGER NOT NULL DEFAULT 0,
  line_refund        INTEGER NOT NULL,
  reason             TEXT NOT NULL,
  disposition        TEXT NOT NULL CHECK (disposition IN ('SELLABLE','DAMAGED'))
);

CREATE TABLE credit_note (
  id               INTEGER PRIMARY KEY,
  number           TEXT NOT NULL UNIQUE,
  sale_return_id   INTEGER NOT NULL REFERENCES sale_return(id),
  customer_id      INTEGER REFERENCES customer(id),
  amount_issued    INTEGER NOT NULL,
  amount_remaining INTEGER NOT NULL,
  issued_at        TEXT NOT NULL,
  expires_on       TEXT,
  status           TEXT NOT NULL CHECK (status IN ('ACTIVE','SPENT','EXPIRED','VOID'))
);

CREATE TABLE credit_note_redemption (
  id             INTEGER PRIMARY KEY,
  credit_note_id INTEGER NOT NULL REFERENCES credit_note(id),
  sale_id        INTEGER NOT NULL REFERENCES sale(id),
  amount         INTEGER NOT NULL,
  redeemed_at    TEXT NOT NULL
);
```

**AC-06 (no cumulative over-return)** is enforced inside the return transaction:
`sale_line.qty_returned + requested_qty_base <= sale_line.qty_base`, checked with the row locked by the write transaction, then incremented. It is the one permitted `UPDATE` on `sale_line`.

---

## 7. Area E — Cash and shifts

```mermaid
erDiagram
    APP_USER ||--o{ SHIFT : opens
    SHIFT ||--o{ SALE : contains
    SHIFT ||--o{ SALE_RETURN : contains
    SHIFT ||--o{ CASH_MOVEMENT : records
    SHIFT ||--o| Z_REPORT : "closed by"

    SHIFT {
        int id PK
        int user_id FK
        text opened_at
        int opening_float "money"
        text closed_at
        int counted_cash "money"
        int expected_cash "money"
        int variance "money"
        text status "OPEN|CLOSED"
    }
    CASH_MOVEMENT {
        int id PK
        int shift_id FK
        text direction "IN|OUT"
        int amount "money"
        text reason
        int user_id FK
        text occurred_at
    }
```

```sql
CREATE TABLE shift (                      -- APPEND ONLY (close fields settable once)
  id             INTEGER PRIMARY KEY,
  shift_no       TEXT NOT NULL UNIQUE,
  user_id        INTEGER NOT NULL REFERENCES app_user(id),
  opened_at      TEXT NOT NULL,
  business_date  TEXT NOT NULL,
  opening_float  INTEGER NOT NULL,
  closed_at      TEXT,
  counted_cash   INTEGER,
  expected_cash  INTEGER,
  variance       INTEGER,
  status         TEXT NOT NULL CHECK (status IN ('OPEN','CLOSED')),
  closed_by      INTEGER REFERENCES app_user(id),
  note           TEXT
);
CREATE UNIQUE INDEX ux_one_open_shift ON shift(status) WHERE status = 'OPEN';
-- C-01: at most one open shift, enforced by the database.

CREATE TABLE cash_movement (              -- APPEND ONLY
  id          INTEGER PRIMARY KEY,
  shift_id    INTEGER NOT NULL REFERENCES shift(id),
  direction   TEXT NOT NULL CHECK (direction IN ('IN','OUT')),
  amount      INTEGER NOT NULL CHECK (amount > 0),
  reason      TEXT NOT NULL,
  user_id     INTEGER NOT NULL REFERENCES app_user(id),
  occurred_at TEXT NOT NULL
);

-- Rollups, written in the same transaction as the Z report (NFR-P5)
CREATE TABLE daily_sales_summary (
  business_date TEXT PRIMARY KEY,
  bill_count    INTEGER NOT NULL,
  gross         INTEGER NOT NULL,
  discount      INTEGER NOT NULL,
  tax           INTEGER NOT NULL,
  net           INTEGER NOT NULL,
  cogs          INTEGER NOT NULL,
  return_count  INTEGER NOT NULL,
  return_value  INTEGER NOT NULL,
  tender_cash   INTEGER NOT NULL,
  tender_card   INTEGER NOT NULL,
  tender_other  INTEGER NOT NULL,
  built_at      TEXT NOT NULL
);

CREATE TABLE daily_product_summary (
  business_date      TEXT NOT NULL,
  product_variant_id INTEGER NOT NULL REFERENCES product_variant(id),
  qty_base           INTEGER NOT NULL,
  net                INTEGER NOT NULL,
  cogs               INTEGER NOT NULL,
  PRIMARY KEY (business_date, product_variant_id)
);
```

---

## 8. Area F — System

```mermaid
erDiagram
    APP_USER ||--o{ AUDIT_LOG : "acts"
    APP_USER ||--o{ APP_SETTING : "changes"
    SALE ||--o{ PRINT_JOB : queues
    NUMBER_SEQUENCE }o--|| SALE : "numbers"
    BACKUP_RECORD

    APP_USER {
        int id PK
        text username UK
        text password_hash
        text role "CASHIER|OWNER"
        int active
        int failed_attempts
        text locked_until
        text last_login
    }
    AUDIT_LOG {
        int id PK
        text occurred_at
        int user_id FK
        text action
        text entity_type
        int entity_id
        text before_json
        text after_json
        text prev_hash
        text row_hash
    }
    PRINT_JOB {
        int id PK
        text doc_type
        int doc_id
        text payload "rendered bytes, base64"
        text status "PENDING|PRINTED|FAILED"
        int attempts
        int is_duplicate
        text created_at
    }
    BACKUP_RECORD {
        int id PK
        text filename
        text taken_at
        int size_bytes
        text checksum
        text local_path
        text usb_status
        text cloud_status
        text verified_at
    }
```

```sql
CREATE TABLE app_user (
  id              INTEGER PRIMARY KEY,
  username        TEXT NOT NULL UNIQUE,
  display_name    TEXT NOT NULL,
  password_hash   TEXT NOT NULL,          -- Argon2id encoded string
  role            TEXT NOT NULL CHECK (role IN ('CASHIER','OWNER')),
  active          INTEGER NOT NULL DEFAULT 1,
  failed_attempts INTEGER NOT NULL DEFAULT 0,
  locked_until    TEXT,
  last_login      TEXT,
  created_at      TEXT NOT NULL
);

CREATE TABLE app_setting (
  key        TEXT PRIMARY KEY,
  value      TEXT NOT NULL,
  value_type TEXT NOT NULL CHECK (value_type IN ('STRING','INT','MONEY','BOOL','JSON')),
  updated_by INTEGER REFERENCES app_user(id),
  updated_at TEXT NOT NULL
);

CREATE TABLE number_sequence (
  doc_type TEXT PRIMARY KEY CHECK (doc_type IN
             ('SALE','RETURN','CREDIT_NOTE','GRN','PO','SHIFT','STOCK_TAKE','QUOTE')),
  prefix   TEXT NOT NULL,          -- e.g. 'INV-'
  pattern  TEXT NOT NULL,          -- e.g. '{prefix}{yyyy}-{n:000000}'
  next_val INTEGER NOT NULL
);

CREATE TABLE audit_log (                  -- APPEND ONLY, hash chained
  id          INTEGER PRIMARY KEY,
  occurred_at TEXT NOT NULL,
  user_id     INTEGER REFERENCES app_user(id),
  action      TEXT NOT NULL,              -- 'SALE_CANCELLED','PRICE_CHANGED',...
  entity_type TEXT NOT NULL,
  entity_id   INTEGER,
  before_json TEXT,
  after_json  TEXT,
  reason      TEXT,
  prev_hash   TEXT NOT NULL,
  row_hash    TEXT NOT NULL
);
CREATE INDEX ix_audit_time   ON audit_log(occurred_at);
CREATE INDEX ix_audit_entity ON audit_log(entity_type, entity_id);

CREATE TABLE print_job (
  id           INTEGER PRIMARY KEY,
  doc_type     TEXT NOT NULL CHECK (doc_type IN
                 ('SALE','RETURN','CREDIT_NOTE','X_REPORT','Z_REPORT',
                  'GRN','PO','STOCK_TAKE','LABEL','CASH_SLIP')),
  doc_id       INTEGER,
  target       TEXT NOT NULL DEFAULT 'RECEIPT',   -- RECEIPT|A4|LABEL
  payload      BLOB NOT NULL,                     -- rendered ESC/POS or PDF path
  copies       INTEGER NOT NULL DEFAULT 1,
  is_duplicate INTEGER NOT NULL DEFAULT 0,        -- FR-7.6
  status       TEXT NOT NULL CHECK (status IN ('PENDING','PRINTED','FAILED')),
  attempts     INTEGER NOT NULL DEFAULT 0,
  last_error   TEXT,
  created_at   TEXT NOT NULL,
  printed_at   TEXT
);
CREATE INDEX ix_print_pending ON print_job(status) WHERE status = 'PENDING';

CREATE TABLE backup_record (
  id          INTEGER PRIMARY KEY,
  filename    TEXT NOT NULL,
  taken_at    TEXT NOT NULL,
  size_bytes  INTEGER NOT NULL,
  checksum    TEXT NOT NULL,               -- SHA-256 of ciphertext
  schema_ver  TEXT NOT NULL,
  local_path  TEXT,
  usb_status  TEXT NOT NULL CHECK (usb_status  IN ('NA','OK','FAILED')),
  cloud_status TEXT NOT NULL CHECK (cloud_status IN ('PENDING','OK','FAILED','SKIPPED')),
  cloud_key   TEXT,
  attempts    INTEGER NOT NULL DEFAULT 0,
  last_error  TEXT,
  verified_at TEXT
);

CREATE TABLE schema_version (
  version    TEXT PRIMARY KEY,
  applied_at TEXT NOT NULL
);
```

### Append-only triggers

The complete set, as created by migration `Skeleton0001`. These are the whole of CLAUDE.md
invariant 5: there is no application code path that can be trusted to enforce it, because a
repair session with `sqlite3` is not application code.

**Trigger recreation rule.** EF Core's SQLite provider rebuilds a table (create-copy-drop-rename)
for almost any alter, and **a rebuild silently drops that table's triggers**. Any migration that
alters an append-only table must re-create its triggers in the same migration, with the trigger
SQL written out literally. `Infrastructure/Data/AppendOnlyTables.cs` holds the expected trigger
*names* only — never the SQL, because migrations are immutable history and a shared constant
edited later would retroactively change what an already-applied migration did. `MigrationRunner`
checks that manifest against `sqlite_schema` **on any run that actually applied a migration** —
a start with nothing pending returns before the check, because nothing can have dropped a trigger
when no DDL ran. `AppendOnlyTriggerTests` checks the manifest too, on every test run.

**`IS NOT`, not `<>`.** Every `WHEN` guard below compares with `IS NOT`. `<>` against a nullable
column yields NULL when either side is NULL, a NULL `WHEN` clause does not fire, and an UPDATE
touching a nullable column of a row where it was NULL would slip straight past the guard — on the
very rows (`note`, `product_variant_id`, `customer_id`) most likely to hold NULLs. `IS NOT` is
NULL-safe and identical for NOT NULL columns.

**`PRAGMA recursive_triggers = ON` is part of the protection, not a tuning knob.** SQLite fires a
`BEFORE DELETE` trigger for the row that `REPLACE` conflict resolution removes *only* when
recursive triggers are enabled, and they are **off by default**. With them off,
`INSERT OR REPLACE INTO sale (id, …) VALUES (1, …)` walks past `trg_sale_no_delete` and rewrites
the bill — new total, new `row_hash`, no error — and the same for `sale_line`, `payment`,
`stock_movement`, `audit_log` and `shift`. The pragma is applied to every connection by
`PosConnectionFactory` (CLAUDE.md invariant 9's list); none of the triggers below write, so there
is no recursion for it to enable. Note the residual limit: this closes the hole for every
connection the application opens, but a `sqlite3` repair session that does not set the pragma
still gets the old behaviour. Closing that would need a `BEFORE INSERT` existence guard on each
append-only table as well.

`cash_movement`, `sale_return` and `sale_return_line` are append-only too; their triggers land
with their tables in P1/P2.

```sql
-- ---- stock_movement: the stock ledger is the truth (invariant 3) --------------------
CREATE TRIGGER trg_stock_movement_no_update
BEFORE UPDATE ON stock_movement
BEGIN SELECT RAISE(ABORT, 'stock_movement is append-only'); END;

CREATE TRIGGER trg_stock_movement_no_delete
BEFORE DELETE ON stock_movement
BEGIN SELECT RAISE(ABORT, 'stock_movement is append-only'); END;

-- ---- payment ------------------------------------------------------------------------
CREATE TRIGGER trg_payment_no_update
BEFORE UPDATE ON payment
BEGIN SELECT RAISE(ABORT, 'payment is append-only'); END;

CREATE TRIGGER trg_payment_no_delete
BEFORE DELETE ON payment
BEGIN SELECT RAISE(ABORT, 'payment is append-only'); END;

-- ---- audit_log: hash chained, so a single edited row breaks the chain ---------------
CREATE TRIGGER trg_audit_log_no_update
BEFORE UPDATE ON audit_log
BEGIN SELECT RAISE(ABORT, 'audit_log is append-only'); END;

CREATE TRIGGER trg_audit_log_no_delete
BEFORE DELETE ON audit_log
BEGIN SELECT RAISE(ABORT, 'audit_log is append-only'); END;

-- ---- sale: append-only except status, cancelled_by, cancelled_at --------------------
CREATE TRIGGER trg_sale_no_delete
BEFORE DELETE ON sale
BEGIN SELECT RAISE(ABORT, 'sale is append-only'); END;

CREATE TRIGGER trg_sale_restricted_update
BEFORE UPDATE ON sale
WHEN  old.id            IS NOT new.id
   OR old.bill_no       IS NOT new.bill_no
   OR old.sold_at       IS NOT new.sold_at
   OR old.business_date IS NOT new.business_date
   OR old.customer_id   IS NOT new.customer_id
   OR old.user_id       IS NOT new.user_id
   OR old.shift_id      IS NOT new.shift_id
   OR old.subtotal      IS NOT new.subtotal
   OR old.line_discount IS NOT new.line_discount
   OR old.bill_discount IS NOT new.bill_discount
   OR old.tax           IS NOT new.tax
   OR old.rounding      IS NOT new.rounding
   OR old.total         IS NOT new.total
   OR old.cogs          IS NOT new.cogs
   OR old.note          IS NOT new.note
   OR old.prev_hash     IS NOT new.prev_hash
   OR old.row_hash      IS NOT new.row_hash
BEGIN SELECT RAISE(ABORT, 'sale: only status, cancelled_by and cancelled_at may be updated'); END;

-- COMPLETED -> CANCELLED, one direction, once. A cancelled bill keeps its number (invariant 4).
CREATE TRIGGER trg_sale_cancel_only_forward
BEFORE UPDATE OF status ON sale
WHEN NOT (old.status = 'COMPLETED' AND new.status = 'CANCELLED')
BEGIN SELECT RAISE(ABORT, 'sale.status may only change from COMPLETED to CANCELLED'); END;

-- Without this, cancelled_at could be rewritten on a live COMPLETED bill: the trigger above
-- only fires when status is named in the SET clause.
CREATE TRIGGER trg_sale_cancel_fields_together
BEFORE UPDATE ON sale
WHEN (old.cancelled_by IS NOT new.cancelled_by OR old.cancelled_at IS NOT new.cancelled_at)
     AND NOT (old.status = 'COMPLETED' AND new.status = 'CANCELLED')
BEGIN SELECT RAISE(ABORT, 'sale: cancellation fields may only be set while cancelling'); END;

-- FR-8.5, AC-11: no sale may be posted into a closed shift.
CREATE TRIGGER trg_sale_shift_open
BEFORE INSERT ON sale
WHEN (SELECT status FROM shift WHERE id = new.shift_id) IS NOT 'OPEN'
BEGIN SELECT RAISE(ABORT, 'cannot post into a closed shift'); END;

-- ---- sale_line: append-only except qty_returned -------------------------------------
CREATE TRIGGER trg_sale_line_no_delete
BEFORE DELETE ON sale_line
BEGIN SELECT RAISE(ABORT, 'sale_line is append-only'); END;

CREATE TRIGGER trg_sale_line_restricted_update
BEFORE UPDATE ON sale_line
WHEN  old.id                 IS NOT new.id
   OR old.sale_id            IS NOT new.sale_id
   OR old.line_no            IS NOT new.line_no
   OR old.product_variant_id IS NOT new.product_variant_id
   OR old.description        IS NOT new.description
   OR old.qty                IS NOT new.qty
   OR old.uom_id             IS NOT new.uom_id
   OR old.qty_base           IS NOT new.qty_base
   OR old.unit_price         IS NOT new.unit_price
   OR old.discount           IS NOT new.discount
   OR old.tax_rate           IS NOT new.tax_rate
   OR old.tax                IS NOT new.tax
   OR old.line_total         IS NOT new.line_total
   OR old.unit_cost          IS NOT new.unit_cost
   OR old.note               IS NOT new.note
BEGIN SELECT RAISE(ABORT, 'sale_line: only qty_returned may be updated'); END;

-- AC-06: no cumulative over-return. The application checks this inside the return
-- transaction; the database is what makes it true. Monotonic, because there is no reversal
-- document for a sale_return: winding qty_returned back down would let the same line be
-- returned twice over and defeat the cumulative bound.
CREATE TRIGGER trg_sale_line_qty_returned_bounds
BEFORE UPDATE OF qty_returned ON sale_line
WHEN new.qty_returned < 0
  OR new.qty_returned > new.qty_base
  OR new.qty_returned < old.qty_returned
BEGIN SELECT RAISE(ABORT, 'sale_line.qty_returned must be between 0 and qty_base and may never decrease'); END;

-- ---- shift: append-only except the close fields, settable once ----------------------
CREATE TRIGGER trg_shift_no_delete
BEFORE DELETE ON shift
BEGIN SELECT RAISE(ABORT, 'shift is append-only'); END;

CREATE TRIGGER trg_shift_restricted_update
BEFORE UPDATE ON shift
WHEN  old.id            IS NOT new.id
   OR old.shift_no      IS NOT new.shift_no
   OR old.user_id       IS NOT new.user_id
   OR old.opened_at     IS NOT new.opened_at
   OR old.business_date IS NOT new.business_date
   OR old.opening_float IS NOT new.opening_float
BEGIN SELECT RAISE(ABORT, 'shift: only the close fields may be updated'); END;

-- "Settable once": a closed shift is frozen, so status can never go CLOSED -> OPEN either.
CREATE TRIGGER trg_shift_closed_is_final
BEFORE UPDATE ON shift
WHEN old.status = 'CLOSED'
BEGIN SELECT RAISE(ABORT, 'a closed shift is immutable'); END;

-- note is a close field, not an immutable one: SRS §8.7 requires a note when the cash variance
-- exceeds the threshold, and RPT-05 prints it on the Z report. It is written by the same UPDATE
-- that closes the shift, so it is guarded here - never settable on a live OPEN shift, never
-- editable afterwards (trg_shift_closed_is_final).
CREATE TRIGGER trg_shift_close_fields_together
BEFORE UPDATE ON shift
WHEN (old.closed_at     IS NOT new.closed_at
   OR old.counted_cash  IS NOT new.counted_cash
   OR old.expected_cash IS NOT new.expected_cash
   OR old.variance      IS NOT new.variance
   OR old.closed_by     IS NOT new.closed_by
   OR old.note          IS NOT new.note)
   AND NOT (old.status = 'OPEN' AND new.status = 'CLOSED')
BEGIN SELECT RAISE(ABORT, 'shift close fields may only be set while closing the shift'); END;
```

An abort reaches the client as `SQLITE_CONSTRAINT` (19) with extended code
`SQLITE_CONSTRAINT_TRIGGER` (1811) and the message above. SQLite does not order triggers on the
same event, so when two of them would both refuse a statement, which message comes back is
unspecified — assert on the code, not the message, in that case.

---

## 9. State machines

### Sale

```mermaid
stateDiagram-v2
    [*] --> Building : F2 New Sale (in memory only)
    Building --> Building : scan / edit / discount
    Building --> Held : F5 Hold
    Held --> Building : F6 Recall
    Building --> [*] : Esc (discarded, nothing persisted)
    Building --> Completed : F9 Pay, tenders balance
    Completed --> Cancelled : owner override, audited
    Completed --> [*]
    Cancelled --> [*]
```

A bill exists in the database only once it is `COMPLETED`. Nothing half-finished is ever persisted, which is why AC-15 (power cut mid-bill) has a trivially correct answer: the in-progress bill is gone, the last committed bill is intact.

### Shift

```mermaid
stateDiagram-v2
    [*] --> Open : open shift, enter float
    Open --> Open : sales, returns, cash in/out, X report
    Open --> Closed : Z report — count cash, record variance
    Closed --> [*] : immutable, backup triggered
```

### Print job

```mermaid
stateDiagram-v2
    [*] --> Pending : written in the sale transaction
    Pending --> Printed : worker succeeds
    Pending --> Failed : 3 attempts exhausted
    Failed --> Pending : manual reprint (F10), marked DUPLICATE
    Printed --> Pending : manual reprint, marked DUPLICATE
```

### Backup

```mermaid
stateDiagram-v2
    [*] --> Snapshot : schedule / shift close / manual
    Snapshot --> Encrypted : zstd + AES-256-GCM
    Encrypted --> LocalWritten : local folder (+ USB)
    LocalWritten --> UploadPending
    UploadPending --> Uploaded : success
    UploadPending --> UploadPending : backoff retry
    Uploaded --> Verified : checksum + monthly restore self-test
```

---

## 10. Enumerations (single source of truth)

Mirror these exactly as C# enums in `Domain/Enums/`. The `CHECK` constraints above and the enums must never drift; an integration test asserts every enum member is accepted by its column and every non-member is rejected.

| Enum | Values |
|---|---|
| `Role` | `CASHIER`, `OWNER` |
| `ProductType` | `STANDARD`, `DECIMAL`, `SERVICE`, `NON_INVENTORY` |
| `MovementType` | `GRN`, `SALE`, `RETURN_IN`, `ADJUSTMENT`, `DAMAGE`, `STOCK_TAKE`, `BULK_BREAK_OUT`, `BULK_BREAK_IN`, `OPENING`, `TRANSFER_OUT`, `TRANSFER_IN` |
| `TenderType` | `CASH`, `CARD`, `BANK_TRANSFER`, `CREDIT_NOTE`, `ON_ACCOUNT`, `CHEQUE` |
| `SaleStatus` | `COMPLETED`, `CANCELLED` |
| `RefundMethod` | `CASH`, `CARD`, `CREDIT_NOTE`, `EXCHANGE`, `ON_ACCOUNT` |
| `Disposition` | `SELLABLE`, `DAMAGED` |
| `ShiftStatus` | `OPEN`, `CLOSED` |
| `PriceTierName` | `RETAIL`, `TRADE` |
| `PrintDocType` | `SALE`, `RETURN`, `CREDIT_NOTE`, `X_REPORT`, `Z_REPORT`, `GRN`, `PO`, `STOCK_TAKE`, `LABEL`, `CASH_SLIP` |
| `PrintStatus` | `PENDING`, `PRINTED`, `FAILED` |
| `CloudStatus` | `PENDING`, `OK`, `FAILED`, `SKIPPED` |

---

## 11. Seed data (created by the first-run wizard)

| Table | Rows |
|---|---|
| `uom` | Piece (pc, 0dp), Metre (m, 3dp), Kilogram (kg, 3dp), Litre (L, 3dp), Box, Coil, Packet, Roll, Bundle |
| `tax_class` | From Q-01. Default: `Standard` at the shop's rate, `Zero rated` at 0. |
| `number_sequence` | `SALE` → `INV-{yyyy}-{n:000000}`, `RETURN` → `RTN-…`, `CREDIT_NOTE` → `CN-…`, `GRN` → `GRN-…`, `PO` → `PO-…`, `SHIFT` → `SH-…`, all `next_val = 1` (Q-16) |
| `app_user` | One `OWNER` account created in the wizard. No default password, ever. |
| `app_setting` | Full defaults per FR-10.1–10.8 (see `Application/Settings/SettingDefaults.cs`) |
| `category` | Plumbing, Electrical, Fasteners, Tools, Paint, Adhesives, Garden, Building — editable |

---

## 12. Index rationale

| Index | Serves |
|---|---|
| `barcode.barcode` UNIQUE | NFR-P1 — the single hottest lookup in the system |
| `product_search` FTS5 | NFR-P2 |
| `sale.bill_no` UNIQUE | NFR-P4, return-by-receipt |
| `sale.business_date` | every date-range report |
| `sale_line.sale_id` | bill recall, reprint, return |
| `sale_line(product_variant_id, sale_id)` | product sales history, return lookup by item |
| `stock_movement(product_variant_id, occurred_at)` | stock card / item history |
| `stock_movement(ref_doc_type, ref_doc_id)` | "show me the movements this GRN posted" |
| `stock_balance.qty_base` | low-stock / reorder report |
| `ux_one_open_shift` partial unique | C-01 enforced by the database |
| `print_job.status` partial | outbox polling stays O(pending) |
| `ix_product_category`, `ix_product_brand` | catalogue browsing and the category/brand filters on every product list |
| `ix_product_active` | every screen and report that excludes discontinued lines, which is most of them |
| `ix_variant_product` | loading a product's SKUs — the join behind every catalogue and label screen |
| `ix_movement_time` | date-range stock movement reports that are not scoped to one item |
| `ix_sale_shift` | X and Z reports, which read a whole shift's bills |
| `ix_sale_soldat` | "what happened between 2 and 3 pm", and audit lookups by clock time rather than business day |
| `ix_payment_sale` | tender breakdown per bill: reprint, refund, Z report |
| `ix_payment_return` | the same for a refund out |
| `ix_audit_time` | the audit log viewer's default ordering and its date filter |
| `ix_audit_entity` | "show me everything that happened to bill 1234" |
| `ix_sale_cust` | customer purchase history. **No foreign key stands behind it yet** — `customer` arrives with P5-T02 (§13). |

**From the skeleton migration onwards, every index in this schema is one somebody chose.** EF
Core's `ForeignKeyIndexConvention` is removed in `PosDbContext.ConfigureConventions`, so a foreign
key does not silently acquire an index that costs a write on the sale path and serves no read.
Anything wanted is declared, named and listed here.

Run `ANALYZE` after bulk import and `PRAGMA optimize` on clean shutdown.

---

## 13. Skeleton subset and migrations

Migration `Skeleton0001` (P0-T04) creates fifteen of the tables above — enough for one product,
one sale, one payment, one stock movement and one printed receipt. The rest of this document
arrives in P1-T01 through the same pipe.

### The fifteen tables and the foreign keys that actually exist

```mermaid
erDiagram
    UOM       ||--o{ PRODUCT : "base unit"
    TAX_CLASS ||--o{ PRODUCT : taxes
    PRODUCT   ||--|{ PRODUCT_VARIANT : "has SKUs"
    PRODUCT_VARIANT ||--|| STOCK_BALANCE : "current state"
    PRODUCT_VARIANT ||--o{ STOCK_MOVEMENT : ledger
    PRODUCT_VARIANT ||--o{ SALE_LINE : "sold as"
    APP_USER  ||--o{ STOCK_MOVEMENT : posts
    APP_USER  ||--o{ SHIFT : "opens and closes"
    APP_USER  ||--o{ SALE : "rings up and cancels"
    APP_USER  ||--o{ AUDIT_LOG : acts
    SHIFT     ||--o{ SALE : within
    SALE      ||--|{ SALE_LINE : contains
    SALE      ||--o{ PAYMENT : "tendered by"
    UOM       ||--o{ SALE_LINE : "sold in"
    NUMBER_SEQUENCE {
        text doc_type PK
    }
    PRINT_JOB {
        int id PK
    }
    SCHEMA_VERSION {
        text version PK
    }
```

`number_sequence`, `print_job` and `schema_version` stand alone by design: a document number must
be allocatable without touching the document, the print outbox must survive the sale it came from,
and the schema version is about the file rather than the business.

### The four dangling references

The DDL above writes these as `REFERENCES`, but the tables they point at do not exist yet. With
`PRAGMA foreign_keys = ON` a reference to a missing table is accepted at `CREATE TABLE` and then
fails at **INSERT** time with "no such table" — a landmine, not a constraint. All four are
therefore plain nullable `INTEGER` columns in the skeleton:

| Column | Points at | Added by |
|---|---|---|
| `product.category_id` | `category(id)` | P1-T01 |
| `product.brand_id` | `brand(id)` | P1-T01 |
| `sale.customer_id` | `customer(id)` | P5-T02 |
| `payment.sale_return_id` | `sale_return(id)` | P2-T02 |

Adding a foreign key to SQLite rebuilds the table, **which drops that table's triggers**. The
migrations that add these constraints to `sale` and `payment` must re-create their append-only
triggers in the same migration — see the trigger recreation rule in §6.

### Migrations

- **Naming:** `Skeleton0001`, `FullSchema0002`, … Never a name starting with a digit: EF sanitises
  `0001_Skeleton` into class `_0001_Skeleton`, which fails this repository's `CA1707` build.
- **Forward only.** `Down` is generated and left alone so `dotnet ef migrations remove` works while
  a migration is being written. It is never run against a till.
- **Never edit an applied migration.** Add a new one.
- **`schema_version` is the documented authority**; `__EFMigrationsHistory` (and its companion
  `__EFMigrationsLock`) is EF's mechanism. `MigrationRunner` writes `schema_version` after the
  chain and reconciles the two on every start, so an upgrade interrupted between them repairs
  itself rather than drifting.
- **Regenerating a migration** needs the design-time package, which is behind an MSBuild flag
  (docs/adr/0004):

  ```
  EfTooling=true dotnet ef migrations add <Name> \
    --project src/Counterpoint.Infrastructure \
    --startup-project src/Counterpoint.Infrastructure
  ```

  Then hand-append any `migrationBuilder.Sql(...)` triggers to `Up()`. Never hand-edit
  `PosDbContextModelSnapshot.cs`.

### EF mapping rules that are now schema contracts

The model is the source EF regenerates a table from, so anything true of the database has to be
true of the model — otherwise the first `ALTER` in a later migration quietly rewrites it.

| Rule | Why |
|---|---|
| No `decimal`, `double` or `float` in the mapped model. Money, quantity and rates are C# `long`. | A bare `decimal` maps to `TEXT` in SQLite with no error to show for it, and money stored as text does not add up (CLAUDE.md invariant 1). |
| No `AUTOINCREMENT`, no `sqlite_sequence`. | Document numbers come from `number_sequence` (invariant 4), and AUTOINCREMENT costs a `sqlite_sequence` write per insert on the sale path. Enforced by `NoAutoincrementAnnotationProvider`, a replaced `IRelationalAnnotationProvider` — not by editing the migration, which a table rebuild would undo. |
| No automatic foreign-key indexes. | See §12. |
| `DeleteBehavior.NoAction` on every relationship. | EF defaults a required FK to `ON DELETE CASCADE`; on `stock_movement` that would let deleting a variant wipe the stock ledger. The DDL above is bare `REFERENCES`, which is `NO ACTION`. |
| `HasDefaultValue(x).ValueGeneratedNever()`, always both. | With `HasDefaultValue` alone, EF treats an explicitly assigned CLR default as "not set" and sends the column's DEFAULT instead — an inactive product would save as active. |
| Unique constraints are named `ux_*` indexes, not inline `UNIQUE`. | The model must name an index the same as the database, or the next migration's diff drops and recreates it. |
| Timestamps are `DateTimeOffset` through `Iso8601TimestampConverter`, set once as a convention. | DM-06, fixed width so a TEXT sort is a chronological sort. Covers `DateTimeOffset?` too. |
| Business dates (`sale.business_date`, `shift.business_date`) are plain `TEXT` `YYYY-MM-DD` strings. | They are the grouping key for every rollup. Routing them through the timestamp converter would corrupt it. |
| Property declaration order is the DDL column order. | EF emits columns in declaration order, so the generated `CREATE TABLE` matches this document column for column and stays reviewable. |

Persistence rows live in `Infrastructure/Data/Schema` and their mapping in
`Infrastructure/Data/Configurations`, one file per table. They are `internal`, hold no behaviour
and know nothing of `Money` or `Quantity`: P1-T05 onward brings the real domain types and P1-T01
moves the mapping onto them.

### Three tables are not keyed on `id`

`stock_balance` (keyed on `product_variant_id`, one row per variant), `number_sequence` (keyed on
`doc_type`) and `schema_version` (keyed on `version`). Every other table is a bare
`id INTEGER PRIMARY KEY`.
