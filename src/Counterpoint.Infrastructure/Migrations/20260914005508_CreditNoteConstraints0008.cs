using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <summary>
    /// P2-T05 (docs/04_PHASE_2_returns_inventory.md, docs/01_DATA_MODEL.md §6): two lookup
    /// indexes credit-note redemption needs, and the bound the risk note asks the database to
    /// hold as well as the guarded UPDATE - <c>0 &lt;= amount_remaining &lt;= amount_issued</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>credit_note_redemption.credit_note_id</c> gets a plain index - the reconciliation query
    /// sums redemptions grouped by it, and adding an index to SQLite needs no rebuild, so
    /// <c>migrationBuilder.CreateIndex</c> is exactly right there.
    /// </para>
    /// <para>
    /// <c>credit_note</c> is different: SQLite has no <c>ALTER TABLE … ADD CONSTRAINT</c>, so the
    /// new CHECK rebuilds it - create <c>ef_temp_credit_note</c>, copy, drop, rename - the same
    /// operation <c>PaymentSaleReturnForeignKey0007</c> (P2-T02) performed on <c>payment</c>.
    /// <c>credit_note</c> is not append-only (docs/01_DATA_MODEL.md §6 - CLAUDE.md's append-only
    /// list is <c>sale</c>, <c>sale_line</c>, <c>payment</c>, <c>sale_return</c>,
    /// <c>sale_return_line</c>, <c>stock_movement</c>, <c>shift</c>, <c>cash_movement</c>,
    /// <c>audit_log</c>, and <c>credit_note</c>/<c>credit_note_redemption</c> are not on it:
    /// <c>status</c> and <c>amount_remaining</c> are ordinary mutable columns), so there are no
    /// triggers to lose and re-create here - the trap this rebuild actually carries is the other
    /// one docs/01_DATA_MODEL.md §13 names: EF's SQLite generator writes a rebuilt table's columns
    /// alphabetically rather than in declaration order
    /// (<c>DM_01_EveryTableKeepsItsDocumentedColumnOrder</c> - confirmed with
    /// <c>dotnet ef migrations script</c>, which reordered this table to
    /// <c>id, amount_issued, amount_remaining, customer_id, expires_on, issued_at, number,
    /// sale_return_id, status</c> when scaffolded as <c>AddCheckConstraint</c>). Written by hand,
    /// in docs/01_DATA_MODEL.md §6's order instead, for the same reason a positional
    /// <c>INSERT INTO credit_note VALUES (...)</c> - a repair session, a bulk import - must not
    /// silently put a sale_return_id in customer_id.
    /// </para>
    /// <para>
    /// Unlike the trigger case in <c>PaymentSaleReturnForeignKey0007</c>, the two
    /// <c>CreateIndex</c> operations are left as native <c>migrationBuilder</c> calls rather than
    /// raw SQL: <c>dotnet ef migrations script</c> showed EF's generator recognises a pending
    /// <c>CreateIndex</c> against a table it is about to rebuild and folds it into the rebuild's
    /// own index-recreation step, in the correct final position, regardless of where the
    /// C# call sits relative to <c>AddCheckConstraint</c> - that batching only breaks down for
    /// operations the generator cannot reason about, such as a hand-written <c>CREATE TRIGGER</c>
    /// <c>Sql()</c> call. <c>ux_credit_note_number</c> is recreated by hand here anyway, because
    /// once the CHECK forces the rebuild to be hand-written SQL for the column order fix, the
    /// index that already existed on the table has to come back inside the same hand-written
    /// block or it would be lost with nothing left to recreate it.
    /// </para>
    /// </remarks>
    public partial class CreditNoteConstraints0008 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_redemption_credit_note",
                table: "credit_note_redemption",
                column: "credit_note_id");

            migrationBuilder.Sql(
                """
                CREATE TABLE "ef_temp_credit_note" (
                    "id" INTEGER NOT NULL CONSTRAINT "pk_credit_note" PRIMARY KEY,
                    "number" TEXT NOT NULL,
                    "sale_return_id" INTEGER NOT NULL,
                    "customer_id" INTEGER NULL,
                    "amount_issued" INTEGER NOT NULL,
                    "amount_remaining" INTEGER NOT NULL,
                    "issued_at" TEXT NOT NULL,
                    "expires_on" TEXT NULL,
                    "status" TEXT NOT NULL,
                    CONSTRAINT "ck_credit_note_amount_remaining_bounds" CHECK (amount_remaining >= 0 AND amount_remaining <= amount_issued),
                    CONSTRAINT "ck_credit_note_status" CHECK (status IN ('ACTIVE','SPENT','EXPIRED','VOID')),
                    CONSTRAINT "fk_credit_note_customer_customer_id" FOREIGN KEY ("customer_id") REFERENCES "customer" ("id"),
                    CONSTRAINT "fk_credit_note_sale_return_sale_return_id" FOREIGN KEY ("sale_return_id") REFERENCES "sale_return" ("id")
                );
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "ef_temp_credit_note" ("id", "number", "sale_return_id", "customer_id", "amount_issued", "amount_remaining", "issued_at", "expires_on", "status")
                SELECT "id", "number", "sale_return_id", "customer_id", "amount_issued", "amount_remaining", "issued_at", "expires_on", "status"
                FROM "credit_note";
                """);

            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            migrationBuilder.Sql("""DROP TABLE "credit_note";""");

            migrationBuilder.Sql("""ALTER TABLE "ef_temp_credit_note" RENAME TO "credit_note";""");

            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);

            migrationBuilder.Sql("""CREATE INDEX "ix_credit_note_customer" ON "credit_note" ("customer_id");""");

            migrationBuilder.Sql("""CREATE UNIQUE INDEX "ux_credit_note_number" ON "credit_note" ("number");""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_redemption_credit_note",
                table: "credit_note_redemption");

            migrationBuilder.Sql(
                """
                CREATE TABLE "ef_temp_credit_note" (
                    "id" INTEGER NOT NULL CONSTRAINT "pk_credit_note" PRIMARY KEY,
                    "number" TEXT NOT NULL,
                    "sale_return_id" INTEGER NOT NULL,
                    "customer_id" INTEGER NULL,
                    "amount_issued" INTEGER NOT NULL,
                    "amount_remaining" INTEGER NOT NULL,
                    "issued_at" TEXT NOT NULL,
                    "expires_on" TEXT NULL,
                    "status" TEXT NOT NULL,
                    CONSTRAINT "ck_credit_note_status" CHECK (status IN ('ACTIVE','SPENT','EXPIRED','VOID')),
                    CONSTRAINT "fk_credit_note_customer_customer_id" FOREIGN KEY ("customer_id") REFERENCES "customer" ("id"),
                    CONSTRAINT "fk_credit_note_sale_return_sale_return_id" FOREIGN KEY ("sale_return_id") REFERENCES "sale_return" ("id")
                );
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "ef_temp_credit_note" ("id", "number", "sale_return_id", "customer_id", "amount_issued", "amount_remaining", "issued_at", "expires_on", "status")
                SELECT "id", "number", "sale_return_id", "customer_id", "amount_issued", "amount_remaining", "issued_at", "expires_on", "status"
                FROM "credit_note";
                """);

            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            migrationBuilder.Sql("""DROP TABLE "credit_note";""");

            migrationBuilder.Sql("""ALTER TABLE "ef_temp_credit_note" RENAME TO "credit_note";""");

            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);

            migrationBuilder.Sql("""CREATE UNIQUE INDEX "ux_credit_note_number" ON "credit_note" ("number");""");
        }
    }
}
