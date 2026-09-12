using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <summary>
    /// The last of the four dangling references from <c>Skeleton0001</c> (docs/01_DATA_MODEL.md
    /// §13): gives <c>payment.sale_return_id</c> its foreign key to <c>sale_return(id)</c>, now
    /// that <c>sale_return</c> is a real destination for a refund payment to point at (P2-T02).
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQLite has no <c>ALTER TABLE … ADD CONSTRAINT</c>, so adding this foreign key rebuilds
    /// <c>payment</c> - create <c>ef_temp_payment</c>, copy, drop, rename - and a rebuild silently
    /// drops that table's triggers (§8, §13's "trigger trap"). <c>payment</c> is append-only, so
    /// <c>trg_payment_no_update</c> and <c>trg_payment_no_delete</c> must exist again the moment
    /// this migration finishes, in the same migration - never a later one, or a database that
    /// stops between the two would sit with an unprotected <c>payment</c> table.
    /// </para>
    /// <para>
    /// <b>Written by hand, not with <see cref="MigrationBuilder.AddForeignKey"/>.</b> The rebuild
    /// a structural operation like <c>AddForeignKey</c> triggers on SQLite is deferred by EF's
    /// generator and only flushed once - at the very end of <see cref="Up"/>'s whole operation
    /// list - regardless of where any <see cref="MigrationBuilder.Sql"/> call naming the same
    /// table sits relative to it (verified with <c>dotnet ef migrations script</c>: a
    /// <c>CREATE TRIGGER</c> appended after <c>AddForeignKey</c> here ran <em>before</em> the
    /// rebuild it depends on, which is exactly backwards and would have left a live database with
    /// the foreign key but neither trigger). Every statement below is therefore issued as literal
    /// SQL, in the literal order EF's own generator produces for this exact change - captured with
    /// the same tool - with the two <c>CREATE TRIGGER</c> statements appended after the rename,
    /// so ordering is a property of the C# source order instead of a generator's internal,
    /// unlisted batching rule.
    /// </para>
    /// <para>
    /// <c>PRAGMA foreign_keys</c> is a no-op inside a transaction, which is why the drop/rename
    /// step runs with <c>suppressTransaction: true</c> around it - the same three-command-group
    /// shape <c>ProductForeignKeys0003</c>'s remarks describe for the same reason.
    /// </para>
    /// <para>
    /// <c>ef_temp_payment</c>'s columns are declared in the DDL order of docs/01_DATA_MODEL.md
    /// §5 (<c>id, sale_id, sale_return_id, tender_type, amount, reference, paid_at</c>) - the
    /// same order <c>Payment.cs</c> declares its properties in - rather than the alphabetical
    /// order <c>dotnet ef migrations script</c> generates for an unannotated rebuild. Without
    /// this, <c>DM_01_EveryTableKeepsItsDocumentedColumnOrder</c> would fail the moment this
    /// migration ran: SQLite's type affinity accepts a positional
    /// <c>INSERT INTO payment VALUES (...)</c> written against §5's order without complaint, so a
    /// reordered table would quietly put an amount in <c>sale_id</c> and a tender type in
    /// <c>amount</c> for exactly the write a repair session or a bulk import makes.
    /// </para>
    /// </remarks>
    public partial class PaymentSaleReturnForeignKey0007 : Migration
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

            // Only now that "payment" exists again, with the new foreign key, do its two
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
