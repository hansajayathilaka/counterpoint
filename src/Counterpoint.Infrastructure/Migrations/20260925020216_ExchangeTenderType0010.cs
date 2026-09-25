using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <summary>
    /// Adds <c>'EXCHANGE'</c> to <c>ck_payment_tender_type</c> - the token an exchange's credit
    /// (SRS FR-5 exchange, AC-04, task P2-T04) now settles as on both documents, instead of the
    /// interim <c>sale.bill_discount</c> use <c>CreateExchangeHandler</c>'s own remarks
    /// documented and this migration retires (2026-09-25 review pass, docs/01_DATA_MODEL.md's
    /// hash-chain-adjacent design notes call the same handler's remarks the place to read this
    /// decision's full reasoning). No other column, index or foreign key changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQLite has no <c>ALTER TABLE ... DROP CONSTRAINT</c> or <c>ADD CONSTRAINT</c>, so widening
    /// the CHECK rebuilds <c>payment</c> - create <c>ef_temp_payment</c>, copy, drop, rename - and
    /// a rebuild silently drops that table's triggers (§8, §13's "trigger trap"). <c>payment</c>
    /// is append-only, so <c>trg_payment_no_update</c> and <c>trg_payment_no_delete</c> must exist
    /// again the moment this migration finishes, in the same migration - never a later one, or a
    /// database that stops between the two would sit with an unprotected <c>payment</c> table.
    /// </para>
    /// <para>
    /// <b>Written by hand, not with the <c>DropCheckConstraint</c>/<c>AddCheckConstraint</c> pair
    /// <c>dotnet ef migrations add</c> itself generated.</b> <c>dotnet ef migrations script</c>
    /// against that scaffold (the same verification <c>PaymentSaleReturnForeignKey0007</c>'s own
    /// remarks describe) shows EF's SQLite provider defers to the identical create-copy-drop-
    /// rename rebuild underneath, in alphabetical column order
    /// (<c>id, amount, paid_at, reference, sale_id, sale_return_id, tender_type</c>) rather than
    /// docs/01_DATA_MODEL.md §5's declared order - exactly the silent column-order corruption
    /// <c>PaymentSaleReturnForeignKey0007</c>'s own remarks warn a rebuild risks
    /// (<c>DM_01_EveryTableKeepsItsDocumentedColumnOrder</c> would have failed the moment this
    /// migration ran). Every statement below is <c>ef_temp_payment</c>'s own §5 column order,
    /// literal SQL, with the two <c>CREATE TRIGGER</c> statements appended after the rename - the
    /// identical shape <c>PaymentSaleReturnForeignKey0007</c> established for this exact table.
    /// </para>
    /// <para>
    /// <c>PRAGMA foreign_keys</c> is a no-op inside a transaction, which is why the drop/rename
    /// step runs with <c>suppressTransaction: true</c> around it - the same three-command-group
    /// shape <c>PaymentSaleReturnForeignKey0007</c> uses for the same reason.
    /// </para>
    /// </remarks>
    public partial class ExchangeTenderType0010 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE "ef_temp_payment" (
                    "id" INTEGER NOT NULL CONSTRAINT "pk_payment" PRIMARY KEY,
                    "sale_id" INTEGER NULL,
                    "sale_return_id" INTEGER NULL,
                    "tender_type" TEXT NOT NULL,
                    "amount" INTEGER NOT NULL,
                    "reference" TEXT NULL,
                    "paid_at" TEXT NOT NULL,
                    CONSTRAINT "ck_payment_one_document" CHECK ((sale_id IS NOT NULL) <> (sale_return_id IS NOT NULL)),
                    CONSTRAINT "ck_payment_tender_type" CHECK (tender_type IN ('CASH','CARD','BANK_TRANSFER','CREDIT_NOTE','ON_ACCOUNT','CHEQUE','EXCHANGE')),
                    CONSTRAINT "fk_payment_sale_return_sale_return_id" FOREIGN KEY ("sale_return_id") REFERENCES "sale_return" ("id"),
                    CONSTRAINT "fk_payment_sale_sale_id" FOREIGN KEY ("sale_id") REFERENCES "sale" ("id")
                );
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "ef_temp_payment" ("id", "sale_id", "sale_return_id", "tender_type", "amount", "reference", "paid_at")
                SELECT "id", "sale_id", "sale_return_id", "tender_type", "amount", "reference", "paid_at"
                FROM "payment";
                """);

            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            migrationBuilder.Sql("""DROP TABLE "payment";""");

            migrationBuilder.Sql("""ALTER TABLE "ef_temp_payment" RENAME TO "payment";""");

            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);

            migrationBuilder.Sql("""CREATE INDEX "ix_payment_return" ON "payment" ("sale_return_id");""");

            migrationBuilder.Sql("""CREATE INDEX "ix_payment_sale" ON "payment" ("sale_id");""");

            // Only now that "payment" exists again, with the widened CHECK, do its two
            // append-only triggers come back.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_payment_no_update
                BEFORE UPDATE ON payment
                BEGIN SELECT RAISE(ABORT, 'payment is append-only'); END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_payment_no_delete
                BEFORE DELETE ON payment
                BEGIN SELECT RAISE(ABORT, 'payment is append-only'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE "ef_temp_payment" (
                    "id" INTEGER NOT NULL CONSTRAINT "pk_payment" PRIMARY KEY,
                    "sale_id" INTEGER NULL,
                    "sale_return_id" INTEGER NULL,
                    "tender_type" TEXT NOT NULL,
                    "amount" INTEGER NOT NULL,
                    "reference" TEXT NULL,
                    "paid_at" TEXT NOT NULL,
                    CONSTRAINT "ck_payment_one_document" CHECK ((sale_id IS NOT NULL) <> (sale_return_id IS NOT NULL)),
                    CONSTRAINT "ck_payment_tender_type" CHECK (tender_type IN ('CASH','CARD','BANK_TRANSFER','CREDIT_NOTE','ON_ACCOUNT','CHEQUE')),
                    CONSTRAINT "fk_payment_sale_return_sale_return_id" FOREIGN KEY ("sale_return_id") REFERENCES "sale_return" ("id"),
                    CONSTRAINT "fk_payment_sale_sale_id" FOREIGN KEY ("sale_id") REFERENCES "sale" ("id")
                );
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "ef_temp_payment" ("id", "sale_id", "sale_return_id", "tender_type", "amount", "reference", "paid_at")
                SELECT "id", "sale_id", "sale_return_id", "tender_type", "amount", "reference", "paid_at"
                FROM "payment";
                """);

            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            migrationBuilder.Sql("""DROP TABLE "payment";""");

            migrationBuilder.Sql("""ALTER TABLE "ef_temp_payment" RENAME TO "payment";""");

            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);

            migrationBuilder.Sql("""CREATE INDEX "ix_payment_return" ON "payment" ("sale_return_id");""");

            migrationBuilder.Sql("""CREATE INDEX "ix_payment_sale" ON "payment" ("sale_id");""");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_payment_no_update
                BEFORE UPDATE ON payment
                BEGIN SELECT RAISE(ABORT, 'payment is append-only'); END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_payment_no_delete
                BEFORE DELETE ON payment
                BEGIN SELECT RAISE(ABORT, 'payment is append-only'); END;
                """);
        }
    }
}
