using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankSystem.Account.Infrastructure.Migrations;

public partial class ExpandAccountNumberForIban : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "AccountNumber",
            table: "Accounts",
            type: "character varying(34)",
            maxLength: 34,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character(10)",
            oldFixedLength: true,
            oldMaxLength: 10);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "AccountNumber",
            table: "Accounts",
            type: "character(10)",
            fixedLength: true,
            maxLength: 10,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(34)",
            oldMaxLength: 34);
    }
}
