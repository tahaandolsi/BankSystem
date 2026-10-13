using BankSystem.Account.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankSystem.Account.Infrastructure.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("202610110002_AddAccountTransactionReceiptOutcome")]
public partial class AddAccountTransactionReceiptOutcome : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Outcome",
            table: "AccountTransactionReceipts",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Applied");

        migrationBuilder.AddColumn<string>(
            name: "RejectionCode",
            table: "AccountTransactionReceipts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ProcessedAt",
            table: "AccountTransactionReceipts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE \"AccountTransactionReceipts\" SET \"ProcessedAt\" = \"AppliedAt\"");

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "ProcessedAt",
            table: "AccountTransactionReceipts",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP",
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_AccountTransactionReceipts_OutcomeAndRejectionCode",
            table: "AccountTransactionReceipts",
            sql: "(\"Outcome\" = 'Applied' AND \"RejectionCode\" IS NULL) OR (\"Outcome\" = 'Rejected' AND \"RejectionCode\" IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_AccountTransactionReceipts_OutcomeAndRejectionCode",
            table: "AccountTransactionReceipts");

        migrationBuilder.DropColumn(name: "Outcome", table: "AccountTransactionReceipts");
        migrationBuilder.DropColumn(name: "RejectionCode", table: "AccountTransactionReceipts");
        migrationBuilder.DropColumn(name: "ProcessedAt", table: "AccountTransactionReceipts");
    }
}
